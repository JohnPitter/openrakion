using System;
using System.Globalization;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Motor de IA + simulação de combate dos bots (server-side, parte do FieldEngine). Roda por-field
    /// durante a partida: anuncia o spawn do bot (0x45 — frame conhecido), escolhe alvo, persegue o
    /// alvo mais próximo e — quando o codec de movimento/ação estiver decodificado — SINTETIZA o
    /// movimento e os ataques do bot (estado de IA -> bytes), nunca relay. A morte do bot é sintetizada
    /// pelo servidor (o bot não tem cliente p/ reportar a própria morte); a morte do humano é nativa
    /// (o cliente reporta 0x4f).
    /// </summary>
    public sealed partial class BotManager
    {
        private const long BotMoveIntervalMs = 100;   // taxa real da captura (~100ms entre 0x30a do mesmo sender)

        /// <summary>Anel de LUTA (coord) que o bot mantém no melee — logo FORA da colisão do avatar (~2.6), pra o
        /// humano NÃO precisar encostar (encostar dispara o empurrão de colisão do cliente). Bem dentro do alcance
        /// de golpe (4.5), então o hit conecta daqui sem sobreposição. NÃO foge quando o humano avança (correção
        /// radial suave em <see cref="BotPlayer.StrafeAround"/>) — só segura o espaçamento.</summary>
        private const float BotStrafeRing = 2.6f;

        /// <summary>Quanto à frente (ms) a IA projeta o alvo ao liderar a perseguição/mira (escalado pelo perfil).</summary>
        private const float BotLeadMs = 260f;

        /// <summary>Velocidade de CORRIDA do humano medida na captura: eixo dominante caiu ~0.86 coord/128ms
        /// ≈ 0.67 coord por tick de 100ms (worldserver.log, humano correndo em linha). A animação de corrida do
        /// cliente (keystate 00 01) "pisa" nessa cadência; mover MAIS rápido que isso faz o modelo DESLIZAR
        /// (patinação). O chase corre nesta MESMA velocidade — casa o passo com a animação (sem deslize) e mantém
        /// o ritmo do humano (fecha distância cortando ângulo/prevendo, não com boost). No melee usa o footwork.</summary>
        private const float RunSpeedPerTick = 0.66f;

        /// <summary>Padrões de combo do bot (sequências de actionId do 0x0311) — encadeia golpes como um jogador
        /// real em vez de repetir 1 golpe "sem parar". actionIds REAIS capturados do humano in-game (worldserver.log):
        /// a CADEIA de M1 é a faixa CONSECUTIVA 0x0400→0x0C00 (os passos do encadeamento); 0x14/0x19/0x1A = movimentos
        /// de TIER ALTO (pulo/rotação/skill). Rotaciona a cada combo p/ variar: cadeia curta/média/longa + especiais.</summary>
        private static readonly ushort[][] BotComboPatterns =
        {
            new ushort[] { 0x0400, 0x0500, 0x0600, 0x0700 },           // cadeia de M1 de 4 (a mais comum no log)
            new ushort[] { 0x0700, 0x0800, 0x0900, 0x0A00, 0x0B00 },   // cadeia média→forte de 5
            new ushort[] { 0x0400, 0x0500, 0x0600 },                   // cadeia curta de 3
            new ushort[] { 0x0400, 0x0500, 0x1400 },                   // básicos → ESPECIAL (skill/rotação)
            new ushort[] { 0x0800, 0x0900, 0x1900, 0x0C00 },           // combo → skill (pulo) → finisher
        };

        /// <summary>Espaçamento entre golpes DENTRO de um combo (ms) — rápido/encadeado.</summary>
        private const long BotComboHitGapMs = 300;

        /// <summary>Descanso após terminar um combo (ms) — recuperação p/ não golpear "sem parar".</summary>
        private const long BotComboRecoveryMs = 1600;

        /// <summary>Atraso (ms) entre o spawn e o INÍCIO do movimento do bot. O gate (0x319/lockstep) do host abre
        /// ~2s após o spawn; sem o atraso, o bot já andou nesse meio-tempo e "pula" pro lugar atual quando o gate
        /// abre ("apareceu do nada"). Segurando o bot no ponto de spawn até o gate abrir, o cliente o vê chegar.</summary>
        private const long BotMoveStartDelayMs = 2200;

        /// <summary>Delay de respawn do bot dentro do round (ms). Placeholder ~5s até a RE do timing real (§11).</summary>
        private const long BotRespawnDelayMs = 5000;

        /// <summary>Distância máxima para o bot atacar (coord do mundo). Acima disso, só persegue.</summary>
        private const float BotAttackRange = 8.0f;

        /// <summary>Distância mínima para o bot considerar que "chegou perto" do alvo.</summary>
        private const float BotEngageRange = 2.0f;

        /// <summary>
        /// Posições padrão dos humanos quando não há dados de posição do 0x30a (fallback).
        /// Time 0 no lado +Z (centro ≈ +2), time 1 no lado -Z (centro ≈ -2).
        /// </summary>
        private static float DefaultHumanZ(int team) => team == 0 ? 10f : -10f;

        /// <summary>Um tick de IA de todos os bots de um field (chamado pelo MatchTick na fase Playing).</summary>
        internal void BotTick(Domain.Field f)
        {
            if (f.BotCount == 0 || f.Phase != MatchPhase.Playing) return;
            long now = Environment.TickCount64;

            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot!;
                // Colisão de parede do bot: UNIÃO do box estático do mapa com a envoltória empírica dos humanos.
                // O hull só EXPANDE (chão comprovadamente pisado além da geometria estática); nunca encolhe o box —
                // preferir o hull sozinho clampava o bot pro quadradinho do spawn do humano no início do round
                // (hull minúsculo) = teleporte pro lado do humano + bot preso.
                var mapWalk = Domain.GolemWarLayouts.WalkFor(f.MapId);
                bot.Walk = mapWalk ?? f.HumanHull(2f);

                // 1) SPAWN (fallback p/ bot só visto pelo BotTick; o caminho primário é SpawnFieldBotsInStage
                //    no load do host): marca playing e, com os frames ligados, instancia o avatar via 0x4b
                //    AddPlayer — o frame DECODIFICADO da captura, a forma que o cliente já viu (lição-mestra).
                if (!bot.SpawnedThisRound)
                {
                    SpawnBotIntoRound(f, rec, bot, now);
                    Log.Ok("bot", "field {0}: '{1}' spawn (seat {2} time {3})", f.Id, bot.Name, rec.Slot, bot.Team);
                    continue;
                }
                // 1a) MORTO: respawn no round após o delay (o bot não tem cliente p/ pedir o próprio respawn).
                //     Renasce o domínio e re-anuncia o avatar via 0x45 [seat] — o frame de spawn-into-round que o
                //     cliente já viu (mesma forma do respawn de um humano). Os 0x30a voltam a sair no tick seguinte.
                if (bot.Dead || rec.Dead)
                {
                    if (f.Phase == MatchPhase.Playing && bot.DueForRespawn(now, BotRespawnDelayMs))
                    {
                        bot.Respawn();
                        bot.SpawnGen++;               // respawn = nova entidade -> a ponte invalida o ponteiro velho e re-acha (anti-crash)
                        LinkOf(bot).ResetPresence();
                        rec.Dead = false; rec.State = 4;
                        if (BotMovement.UseNpcAvatar)
                            SpawnBotAsNpc(f, rec, bot);   // 0x307 NPC (descartado)
                        else
                            f.BroadcastField(0x45, new[] { (byte)rec.Slot });   // 0x45 fantasma — a ponte re-acha e segue movendo
                        Log.Ok("bot", "field {0}: '{1}' RESPAWN seat {2} (apos {3}ms)", f.Id, bot.Name, rec.Slot, BotRespawnDelayMs);
                    }
                    continue;
                }

                // 1b) O bot participa do canal reliable apenas com cada humano. O anti-sequestro permanece no
                //     UdpGameplay: tráfego humano↔humano nunca é relayado e 0x319 nunca registra seat humano.
                EmitBotCombatPresence(f, rec, bot, now);

                // 2) DECISÃO: alvo mais próximo + intenção de ataque, em cadência própria.
                if (now >= bot.NextDecisionMs)
                {
                    bot.NextDecisionMs = now + bot.Profile.DecisionIntervalMs;
                    bot.TargetSeat = PickTargetSeat(f, rec, bot);
                    bool obj = IsObjectiveMode(f);
                    bool eng = obj ? IsEngagingHuman(f, bot) : HasLiveTarget(f, bot);
                    Log.Ok("bot", "decisão field {0} seat {1} [{8}]: {2} (alvo seat {3}, rota wp {4}/3, map {5}, pos {6:F1},{7:F1})",
                        f.Id, rec.Slot, eng ? "LUTAR" : (obj ? "OBJETIVO" : "patrulha"),
                        bot.TargetSeat, bot.RouteIndex, f.MapId, bot.X, bot.Z, bot.Difficulty);
                }

                // Segura no ponto de spawn até o gate (0x319/lockstep) abrir (~2s): o bot emite 0x30a PARADO p/ o
                // cliente vê-lo no spawn; só começa a andar/atacar depois — senão "aparece do nada" pulando.
                bool gateOpenedVisually = now - bot.SpawnedMs >= BotMoveStartDelayMs;

                // Prioridade: combate com humano inimigo (Golem = só se PRÓXIMO; Deathmatch = persegue sempre);
                // senão, avança o OBJETIVO (rota Golem War); senão (deathmatch sem alvo vivo), patrulha.
                bool objective = IsObjectiveMode(f);
                bool engage = objective ? IsEngagingHuman(f, bot) : HasLiveTarget(f, bot);
                bool stunned = bot.IsStunned(now);   // atordoado ao apanhar: não ataca nem se move (reação de hit)

                // 3) MOVIMENTO.
                if (now >= bot.NextMoveMs)
                {
                    bot.NextMoveMs = now + BotMoveIntervalMs;
                    float px = bot.X, pz = bot.Z;

                    if (gateOpenedVisually && !stunned)
                    {
                        bot.MaybeFlipStrafe(now);   // troca o lado da orbitação periodicamente
                        if (engage) ChaseTarget(f, bot, now);
                        else if (objective) UpdateBotObjective(f, rec, bot, now);
                        else bot.PatrolStep();
                    }

                    // keystate = movimento REAL (posição mudou), NÃO "tem alvo" — senão o cliente toca a
                    // animação de caminhada PARADO no standoff de combate ("anda na mesma posição"). Em stagger,
                    // não se move mas EMITE a posição (recuo) parado.
                    bool moved = MathF.Abs(bot.X - px) > 0.01f || MathF.Abs(bot.Z - pz) > 0.01f;
                    if (BotMovement.UseNpcAvatar) EmitBotNpcMove(bot);   // 0x30b move (chave estável do NPC)
                    else if (!BotMovement.UseClientBridge) EmitBotMovement(f, rec, bot, moved);  // ponte move por SetPlacement
                }

                // 4) ATAQUE ao humano: combo em rajada (self-gated por alcance). O golem é atacado dentro
                //    de UpdateBotObjective. Só quando engajado num humano inimigo E fora do stagger.
                if (gateOpenedVisually && engage && !stunned)
                    UpdateBotCombat(f, rec, bot, now);
            }

            // Ponte de injeção no cliente: publica as posições dos bots vivos p/ a DLL reposicionar
            // (SetPlacement nativo = o bot ANDA na tela). Só no modo ponte (a DLL acha CPlayer, não NPC).
            if (BotMovement.UseClientBridge) WriteBotBridgePositions(f);

            // Abridor do canal reliable (HIT×N nativo): publica os slots dos bots vivos deste stage p/ a
            // bot_reliable.dll setar player[slot]+0x1d8=1 no cliente do humano. Complementa o movimento
            // server-side (0x30a via +0x1e8); NÃO dirige posição. Ver docs/pvp-stage-re.md §12.
            PublishBotReliableSlots(f);
            PublishBotNpc(f);
        }

        /// <summary>FASE 1b/2: publica os bots vivos como NPC de colisão p/ a bot_reliable.dll criar/mover no cliente.
        /// Linha "owner sub classId x y z heading" (owner=slot, sub=0, class=0x468 Golem). Coords de mundo invariantes.</summary>
        private static void PublishBotNpc(Domain.Field f)
        {
            var sb = new StringBuilder(96);
            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot;
                if (bot == null || rec.Dead || bot.Dead || !rec.Playing) continue;
                sb.Append(rec.Slot).Append(" 0 1128 ")   // owner=slot, sub=0, class=0x468(=1128) Golem
                  .Append(bot.X.ToString("F2", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bot.Y.ToString("F2", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bot.Z.ToString("F2", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bot.Yaw.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            }
            BridgeIo.WriteBotNpc(sb);
        }

        /// <summary>Publica os slots dos bots vivos do field p/ o abridor do canal reliable (<c>bot_slots.txt</c>).
        /// A DLL injetada seta <c>player[slot]+0x1d8=1</c> → o create-com-colisão que o servidor emite passa pelo
        /// caminho nativo → bot acertável → HIT×N. Só slots de bots VIVOS jogando (morto sai; respawn re-entra).</summary>
        private static void PublishBotReliableSlots(Domain.Field f)
        {
            var sb = new StringBuilder(48);
            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot;
                if (bot == null || rec.Dead || bot.Dead || !rec.Playing) continue;
                sb.Append(rec.Slot).Append(' ');
            }
            BridgeIo.WriteBotSlots(sb.ToString());
        }

        /// <summary>Publica as posições dos bots vivos do field p/ a ponte de injeção (bot_pos.txt) — a DLL no
        /// cliente reposiciona cada CPlayer pelo slot. Linha "slot x z yaw" em coords de mundo da engine, com
        /// ponto-decimal invariante (a ponte faz sscanf %f, locale C — vírgula quebraria o parse).</summary>
        private static void WriteBotBridgePositions(Domain.Field f)
        {
            var sb = new StringBuilder(160);
            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot;
                if (bot == null || rec.Dead || bot.Dead || !rec.Playing) continue;
                sb.Append(rec.Slot).Append(' ')
                  .Append(bot.X.ToString("F3", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bot.Z.ToString("F3", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bot.Yaw.ToString("F3", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bot.SpawnGen).Append('\n');   // 5º campo: geração (a ponte re-acha a entidade quando muda)
            }
            BridgeIo.WriteBotPositions(sb);
        }

        /// <summary>
        /// Combate do bot em RAJADA: encadeia um COMBO (vários 0x0311 com actionIds variados) quando o alvo
        /// está em alcance e descansa após terminar (não golpeia "sem parar"). Espelha o jogador real — 3
        /// cliques no M1 = sequência, básico→forte, básico→finisher. Continua o combo em andamento; senão
        /// inicia um novo se há alvo VIVO em alcance e fora do descanso.
        /// </summary>
        private void UpdateBotCombat(Domain.Field f, PlayerRec rec, BotPlayer bot, long now)
        {
            // 1) combo em andamento: emite o próximo golpe da sequência no ritmo do combo. O conjunto de combos
            //    permitido vem do perfil (Easy só cadeias curtas; Hard cadeias longas + especiais).
            int[] pool = bot.Profile.ComboPool;
            if (bot.ComboStep > 0)
            {
                if (now < bot.NextComboHitMs) return;
                ushort[] pat = BotComboPatterns[pool[bot.ComboVariant % pool.Length]];
                ushort actionId = pat[bot.ComboStep - 1];
                EmitBotAttack(rec, bot, actionId);
                // Golpe do bot é relayado (0x0311) ao cliente do humano; o acerto é só ANUNCIADO (chat), nunca
                // sintetiza a morte do humano server-side — isso desincronizava o estado e travava o HIT×N de
                // todos (world log 2026-07-08). Morte do humano é client-authoritative (a vítima reporta 0x4f).
                f.ResolveHumanHitByBot(rec, actionId);
                bot.ComboStep++;
                if (bot.ComboStep > pat.Length)   // combo terminou -> descansa
                {
                    bot.ComboStep = 0;
                    bot.ComboVariant++;
                    bot.NextAttackMs = now + bot.Profile.ComboRecoveryMs;
                }
                else
                {
                    bot.NextComboHitMs = now + bot.Profile.ComboHitGapMs;
                }
                return;
            }

            // 2) sem combo ativo: inicia um se há alvo VIVO em alcance e fora do descanso.
            if (bot.TargetSeat < 0 || now < bot.NextAttackMs) return;
            var target = f.RecAt(bot.TargetSeat);
            if (target == null || !target.Playing || target.Dead) return;
            float tx = target.LastX, tz = target.LastZ;
            if (target.LastPositionMs == 0) { tx = 3.75f; tz = DefaultHumanZ(target.Team); }
            if (bot.Walk is { } walk && !walk.Contains(tx, tz))
            {
                bot.ComboStep = 0;
                return;
            }
            float dx = tx - bot.X, dz = tz - bot.Z;
            if (dx * dx + dz * dz > BotAttackRange * BotAttackRange) return;
            bot.ComboStep = 1;                                       // arma o combo
            bot.NextComboHitMs = now + bot.Profile.ReactionDelayMs;  // atraso de reação humano antes do 1º golpe
        }

        /// <summary>Persegue o alvo humano: ANTECIPA a posição (lidera pelo vetor de velocidade, escala do perfil),
        /// aproxima até o anel de melee e então ORBITA o alvo (não fica parado — humano circula o oponente).
        /// Fallback p/ a posição padrão do time se o alvo ainda não enviou 0x30a.</summary>
        private static void ChaseTarget(Domain.Field f, BotPlayer bot, long now)
        {
            var target = f.RecAt(bot.TargetSeat);
            if (target == null) return;

            // Posição REAL do humano (a mesma que a arbitragem de hit usa) — o espaçamento do melee gira em torno
            // DELA, senão o lead afasta o bot do ponto onde o golpe é medido (bot "longe" na hora do hit).
            // ANTI-FLUTUAR: herda o Y do humano (a IA server-side não tem o relevo do mapa).
            float ax = target.LastX, az = target.LastZ;
            if (target.LastPositionMs != 0) bot.Y = target.LastY;
            if (target.LastPositionMs == 0) { ax = 3.75f; az = DefaultHumanZ(target.Team); }
            if (bot.Walk is { } walk)
            {
                ax = walk.ClampX(ax);
                az = walk.ClampZ(az);
            }

            // Alvo de APROXIMAÇÃO/MIRA com antecipação (lead) — só p/ fechar distância e mirar, NÃO p/ o espaçamento
            // colado (Hard prevê mais; Easy não prevê, lead 0).
            float tx = ax, tz = az;
            if (target.LastPositionMs != 0)
            {
                float lead = BotLeadMs * bot.Profile.LeadFactor;
                tx += target.VelX * lead;
                tz += target.VelZ * lead;
            }
            bot.SetAimToward(tx, target.LastY, tz);

            float dx = ax - bot.X, dz = az - bot.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);   // distância à posição REAL do humano

            // HISTERESE approach↔melee: entra no melee ao alcançar o anel; sai só quando o alvo foge BEM além
            // (re-commit ao chase). Sem isto o bot "dançava" na fronteira (~3.5) fazendo footwork sem nunca fechar.
            if (!bot.InMelee && dist <= BotStrafeRing + 0.3f)
            {
                bot.InMelee = true;
                Log.Ok("bot", "field {0}: bot ENTROU no melee (dist {1:F1})", f.Id, dist);
            }
            else if (bot.InMelee && dist > BotStrafeRing + 2.6f)
            {
                bot.InMelee = false;
                Log.Ok("bot", "field {0}: bot SAIU do melee -> re-chase (dist {1:F1})", f.Id, dist);
            }

            // FORA do melee: COMMIT ao chase — corre reto pra posição REAL, MAIS RÁPIDO que o humano (boost), até
            // fechar. Nada de footwork aqui (senão dança sem alcançar o humano que corre a ~0.6/tick).
            if (!bot.InMelee)
            {
                bot.MoveToward(ax, az, BotStrafeRing, RunSpeedPerTick);
                return;
            }

            // EM melee: FOOTWORK humano em torno da posição REAL — circula / segura o chão / recua tático, em
            // cadência jittered (não orbita igual a máquina). O recuo é backpedal DELIBERADO encarando o alvo.
            if (now >= bot.NextFootMs) PickFootwork(bot, now);
            switch (bot.Foot)
            {
                case BotPlayer.Footwork.Backstep: bot.RetreatFrom(ax, az, bot.Profile.MoveSpeed * 0.5f); break;
                case BotPlayer.Footwork.Hold:     bot.HoldGround(ax, az); break;
                default:                          bot.StrafeAround(ax, az, bot.StrafeDir, BotStrafeRing); break;
            }
        }

        /// <summary>
        /// Escolhe o próximo footwork de combate com distribuição HUMANA: circula na maior parte do tempo (~50%),
        /// segura o chão lendo o oponente às vezes (~32%), e recua TÁTICO de leve e ocasional (~18%) — nunca foge
        /// por reflexo. Duração jittered pelo RNG do bot (cadência viva, dessincronizada entre bots).
        /// </summary>
        private static void PickFootwork(BotPlayer bot, long now)
        {
            // Durações LONGAS (menos trocas = menos stutter): o footwork muda a cada ~0.6–1.5s, não a cada 0.3s.
            int roll = bot.NextRoll();   // [0,100)
            long dur;
            if (roll < 62) { bot.Foot = BotPlayer.Footwork.Strafe; dur = 800 + roll * 12; }             // circula ~0.8–1.5s
            else if (roll < 85) { bot.Foot = BotPlayer.Footwork.Hold; dur = 600 + (roll - 62) * 12; }   // settle/drift ~0.6–0.9s
            else                                                                                         // recuo tático ~0.4–0.6s
            {
                bot.Foot = BotPlayer.Footwork.Backstep;
                dur = 400 + (roll - 85) * 12;
                bot.StrafeDir = (sbyte)-bot.StrafeDir;   // ao voltar do recuo, circula pro outro lado (finta)
            }
            bot.NextFootMs = now + dur;
        }

        /// <summary>
        /// Alvo do bot: humano VIVO mais próximo do time inimigo. Usa posição do 0x30a quando disponível;
        /// fallback para posição padrão do time. Retorna -1 se nenhum humano vivo.
        /// </summary>
        private static int PickTargetSeat(Domain.Field f, PlayerRec self, BotPlayer bot)
        {
            int bestSeat = -1;
            float bestDist = float.MaxValue;

            foreach (var r in f.Slots)
            {
                if (r.Session == null || !r.Playing || r.Dead || r.Team == self.Team) continue;

                float tx = r.LastX;
                float tz = r.LastZ;
                if (r.LastPositionMs == 0)
                {
                    tx = 3.75f;
                    tz = DefaultHumanZ(r.Team);
                }
                float dx = tx - bot.X;
                float dz = tz - bot.Z;
                float dist = dx * dx + dz * dz;   // dist² é suficiente p/ comparar
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestSeat = r.Slot;
                }
            }
            return bestSeat;
        }

        /// <summary>Entra o bot no round: posição de spawn (rota Golem usa o spawn REAL do mapa), estado
        /// playing, socket UDP fresco e o frame de spawn que o cliente já viu (0x4b AddPlayer / 0x307 NPC).
        /// Golden source dos DOIS caminhos de spawn (primário SpawnFieldBotsInStage + fallback BotTick).</summary>
        private void SpawnBotIntoRound(Domain.Field f, PlayerRec rec, BotPlayer bot, long now)
        {
            bot.SpawnedThisRound = true;
            bot.SpawnedMs = now;
            bot.InitStagePosition();          // socket/canais NÃO resetam por round: um cliente humano mantém os dele a partida toda
            if (IsObjectiveMode(f))
            {
                var sp = GolemWarLayouts.For(f.MapId).SpawnFor(bot.Team);
                bot.X = sp.X; bot.Z = sp.Z;   // spawn REAL do mapa (Y no chão); a rota assume daqui
            }
            rec.State = 4; rec.Dead = false; bot.Dead = false;
            bot.SpawnGen++;                   // nova entidade na engine -> a ponte invalida o cache e re-acha
            LinkOf(bot).ResetPresence();
            if (BotMovement.UseNpcAvatar)
                SpawnBotAsNpc(f, rec, bot);                   // 0x307 CreateNpc (desligado por default: UseNpcAvatar=false)
            else if (BotMovement.ClientFramesEnabled)
            {
                NotifyBotAddPlayer(f, rec.Slot, bot);         // 0x4b: instancia o avatar type-7 no stage do host
                if (!BotMovement.UseClientBridge)
                    EnsureBotUdpSocket(bot);                  // origem dos 0x30a sintetizados (via relay do servidor)
            }
        }

        /// <summary>
        /// SÍNTESE do movimento+ação do bot = PAR CNetMessage 0x30a (posição) + 0x030f (keystate/animação).
        /// Emitido do socket DEDICADO do bot ao SERVIDOR (loopback), que relaya ao host do socket do
        /// UdpGameplay — a MESMA origem que o 0x319 registrou no cliente, então o 0x30a passa no
        /// IsValidUDP_ForPlayer. O host lê o slot do corpo e aplica a ação ao avatar do bot
        /// (GetPlayer(slot)!=NULL, garantido pelo 0x4b AddPlayer prévio). NUNCA enviar direto ao cliente
        /// (regressão do mini-peer — ver BotManager.Peer). Datagramas cravados da captura (BotMovement.Build*).
        /// </summary>
        private void EmitBotMovement(Domain.Field f, PlayerRec rec, BotPlayer bot, bool moving)
        {
            var host = f.Master;
            if (host == null) return;

            EnsureBotUdpSocket(bot);          // robustez: recria após round-reset/descartes fora de ordem
            var sock = LinkOf(bot).UdpSocket;
            if (sock == null) return;

            byte[] action = BotMovement.BuildActionDatagram(bot, rec.Slot, moving);
            byte[] key = BotMovement.BuildKeystateDatagram(bot, rec.Slot, moving, Environment.TickCount64);

            // Envia para o servidor (loopback:40708) — o UdpGameplay.Process recebe e relaya ao host
            var serverEp = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, _gameplayPort());
            try { sock.SendTo(action, serverEp); } catch { }
            try { sock.SendTo(key, serverEp); } catch { }

            // Sonda de espaço de coordenadas (throttled): MapId + pos 3D do bot E do alvo humano — usado p/
            // casar o espaço do 0x30a com a geometria do .wld (rota de objetivo Golem War) e identificar o mapa.
            if (bot.UdpSeq++ % 40 < 2)
            {
                var tgt = bot.TargetSeat >= 0 ? f.RecAt(bot.TargetSeat) : null;
                Log.Ok("bot", "map {0} | bot seat {1} pos=({2:F1},{3:F1},{4:F1}) | alvo seat {5} pos=({6:F1},{7:F1},{8:F1})",
                    f.MapId, rec.Slot, bot.X, bot.Y, bot.Z, bot.TargetSeat,
                    tgt?.LastX ?? 0f, tgt?.LastY ?? 0f, tgt?.LastZ ?? 0f);
            }
        }

        /// <summary>
        /// ATAQUE do bot: emite um golpe 0x0311 com o <paramref name="actionId"/> (combo ou golem) ao host. O
        /// aim-vec já segue nos 0x30a (EmitBotMovement) apontando ao alvo — espelha a captura. Contra humano, o
        /// cliente do alvo prevê o hit e reporta 0x4f se morrer; contra golem, o dano é server-authoritative.
        /// </summary>
        private void EmitBotAttack(PlayerRec rec, BotPlayer bot, ushort actionId)
        {
            EnsureBotUdpSocket(bot);
            var sock = LinkOf(bot).UdpSocket;
            if (sock == null) return;
            var serverEp = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, _gameplayPort());
            try { sock.SendTo(BotMovement.BuildAttackDatagram(bot, rec.Slot, actionId), serverEp); } catch { }
            // Abre a janela de swing: o tail dos 0x30f seguintes vira (00, high-byte da ação) enquanto o
            // golpe dura — o driver da animação de ataque no cliente remoto (captura, janelas do 0x0311).
            bot.LastActionHigh = (byte)(actionId >> 8);
            bot.LastActionMs = Environment.TickCount64;
        }
    }
}
