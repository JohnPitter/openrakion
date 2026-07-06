using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Bot = participante de partida SINTETIZADO pelo servidor, sem <see cref="ClientSession"/>.
    /// Ocupa um <see cref="PlayerRec"/> como um jogador de verdade (entra na contagem de time, no
    /// motor de round, no placar), mas todo o comportamento — entrada no campo, movimento, ataque,
    /// morte — é GERADO do domínio (estado de IA -> DTO -> bytes), nunca por relay/replay de pacote
    /// capturado. É a regra-mestra do projeto: implementação de raiz.
    ///
    /// Identidade é EFÊMERA: o bot só existe enquanto a sala/partida vive. Ao fim do match, ou quando
    /// o último humano sai da sala, todos os bots são descartados — não voltam ao roster nem persistem
    /// no DB. Por isso não há characterid/usergameinfo: o bot nunca toca persistência.
    /// </summary>
    public sealed class BotPlayer
    {
        public BotPlayer(int id, string name, byte level, byte charClass, byte team,
                         BotDifficulty difficulty = BotDifficulty.Normal)
        {
            Id = id;
            Name = name;
            Level = level;
            CharClass = charClass;
            Team = team;
            Difficulty = difficulty;
            Profile = BotProfile.For(difficulty);
            MaxHp = Profile.MaxHp;
            Hp = Profile.MaxHp;
        }

        /// <summary>Nível de inteligência do bot (Easy/Normal/Hard). Define o <see cref="Profile"/>.</summary>
        public BotDifficulty Difficulty;

        /// <summary>Perfil de IA derivado da <see cref="Difficulty"/> — fonte única dos parâmetros do motor
        /// (velocidade, cadência, alcance, combos, HP). O motor (BotAi) lê daqui a cada tick.</summary>
        public BotProfile Profile;

        /// <summary>Id efêmero por field (apenas p/ logs e correlação; não é id de DB).</summary>
        public int Id;

        /// <summary>Nome exibido no slot/roster do cliente do host.</summary>
        public string Name;

        /// <summary>Nível exibido no card do slot.</summary>
        public byte Level;

        /// <summary>Classe do char (modelo do avatar que o cliente renderiza no spawn 0x45).</summary>
        public byte CharClass;

        /// <summary>Time alvo do bot (0/1). O seat real é derivado deste time ao alocar o slot.</summary>
        public byte Team;

        // ---- estado de combate/IA (server-side; dirigido pelo motor de IA, task #5) ----

        /// <summary>Energia máxima do bot — vem do <see cref="Profile"/> (durabilidade por dificuldade). A morte
        /// é sintetizada server-side ao zerar (0x4f victim=bot).</summary>
        public ushort MaxHp;

        /// <summary>Energia atual; ao zerar, o servidor SINTETIZA a morte do bot (0x4f victim=bot).</summary>
        public ushort Hp;

        /// <summary>Marcado morto no round atual (aguardando respawn/fim de round).</summary>
        public bool Dead;

        /// <summary>Último golpe sofrido (Environment.TickCount64) — throttle anti-instakill de 0x311 repetidos.</summary>
        public long LastHitMs;

        /// <summary>Instante até o qual o bot está em STAGGER (atordoado ao apanhar): não ataca nem se move,
        /// só emite a posição (recuo) parado. Dá feedback de hit visível sem a faísca nativa (type-7).</summary>
        public long StunUntilMs;

        /// <summary>Atordoa o bot por <paramref name="durationMs"/> a partir de <paramref name="now"/> e
        /// INTERROMPE o combo em andamento (reação de "levou hit"). Acompanha o recuo (<see cref="ApplyKnockback"/>).</summary>
        public void Stagger(long now, long durationMs)
        {
            StunUntilMs = now + durationMs;
            ComboStep = 0;   // combo interrompido pelo golpe
            VelX = 0f; VelZ = 0f;   // o recuo (ApplyKnockback) dá o deslocamento; a velocidade de avanço zera
        }

        /// <summary>True se o bot está atordoado (em stagger) no instante <paramref name="now"/>.</summary>
        public bool IsStunned(long now) => now < StunUntilMs;

        /// <summary>Instante agendado do respawn no round (Environment.TickCount64); 0 = não agendado.</summary>
        public long RespawnAtMs;

        /// <summary>
        /// O bot deve RENASCER agora? Agenda o respawn (1x) ao detectar a morte e devolve true quando o
        /// <paramref name="delayMs"/> passou — o bot não tem cliente p/ pedir o próprio respawn, o servidor o faz.
        /// </summary>
        public bool DueForRespawn(long now, long delayMs)
        {
            if (!Dead) return false;
            if (RespawnAtMs == 0) { RespawnAtMs = now + delayMs; return false; }
            return now >= RespawnAtMs;
        }

        /// <summary>Renasce no round: energia cheia, vivo. NÃO re-posiciona (revive NO LUGAR onde morreu) — a morte
        /// não renderiza no cliente ainda, então teleportar p/ o spawn parecia o bot "voando" pelo mapa. Mantém a
        /// posição p/ continuidade visual. Limpa o agendamento. (Quando a morte renderizar, volta a respawnar no
        /// spawn do time com sequência própria.)</summary>
        public void Respawn()
        {
            Hp = MaxHp;
            Dead = false;
            RespawnAtMs = 0;
            ComboStep = 0;
            NextComboHitMs = 0;
        }

        /// <summary>
        /// Aplica <paramref name="dmg"/> de energia. Devolve true SÓ se ESTE golpe levou o HP a 0 (morte) — o
        /// chamador então sintetiza a morte do bot (0x4f), já que o bot não tem cliente p/ reportá-la. Idempotente
        /// após morto.
        /// </summary>
        public bool TakeDamage(ushort dmg)
        {
            if (Dead || Hp == 0) return false;
            Hp = (ushort)Math.Max(0, Hp - dmg);
            if (Hp != 0) return false;
            Dead = true;
            return true;
        }

        /// <summary>O spawn (0x45) já foi anunciado aos clientes neste round? (evita re-anúncio por tick).</summary>
        public bool SpawnedThisRound;

        /// <summary>Seat do alvo atual (humano) escolhido pela IA; -1 = sem alvo.</summary>
        public int TargetSeat = -1;

        /// <summary>Índice do waypoint atual na rota de objetivo Golem War (golem dourado → pad → golem inimigo).
        /// Avança ao alcançar/destruir cada waypoint. Persiste no respawn-no-lugar; zera por round.</summary>
        public int RouteIndex;

        /// <summary>Próximo tick de decisão da IA (Environment.TickCount64).</summary>
        public long NextDecisionMs;

        /// <summary>Cooldown do próximo ataque (Environment.TickCount64). Após um combo, recebe o descanso de
        /// recuperação (o bot não golpeia "sem parar").</summary>
        public long NextAttackMs;

        /// <summary>Passo do combo em andamento: 0 = sem combo; 1..N = índice (1-based) do próximo golpe da
        /// sequência. Encadeia vários 0x0311 como um jogador real (3 cliques = rajada).</summary>
        public int ComboStep;

        /// <summary>Instante do próximo golpe DENTRO do combo atual (Environment.TickCount64).</summary>
        public long NextComboHitMs;

        /// <summary>Índice rotativo do padrão de combo — varia a sequência a cada combo (não repete o mesmo golpe).</summary>
        public int ComboVariant;

        /// <summary>Próximo envio de movimento sintetizado (Environment.TickCount64).</summary>
        public long NextMoveMs;

        /// <summary>Timestamp do spawn do bot no round (p/ fallback de gate).</summary>
        public long SpawnedMs;

        // ---- posição/orientação server-side (dirigida pelo motor de IA; serializada pelo BotMovement) ----
        public float X, Y, Z;
        public float Yaw;

        /// <summary>AABB caminhável da arena do mapa (colisão de parede do bot server-side). Setado no spawn a
        /// partir da topologia do mapa (<see cref="GolemWarLayouts.WalkFor"/>); <c>null</c> em mapa sem geometria
        /// verificada (aí não clampa). Aplicado em <see cref="Integrate"/> — impede o bot de atravessar paredes.</summary>
        public WalkBounds? Walk;

        // ---- chave da entidade NPC (avatar 0x307/0x30b) ----

        /// <summary>Owner do NPC (grupo) — a entidade é endereçada por <c>owner*9+sub</c> na tabela do host
        /// (<c>+0x1d70</c>). Setado no spawn = seat do bot (cada jogador é dono de até 9 unidades).</summary>
        public byte NpcOwner;

        /// <summary>Sub-slot do NPC dentro do owner (0..8). O bot usa 0 (uma unidade por bot).</summary>
        public byte NpcSub;

        /// <summary>A chave (owner,sub) do NPC já foi atribuída? É atribuída UMA vez (1º spawn) e NUNCA muda —
        /// o create e TODOS os moves usam a mesma chave (senão o move endereça o slot errado e o bot fica preso).</summary>
        public bool NpcKeyAssigned;

        /// <summary>Próximo instante (TickCount64) p/ re-emitir o create 0x307 do NPC durante a janela pós-spawn.
        /// O create é 1 pacote; se o cliente ainda carregava o stage no spawn, ele o descarta e o Golem não
        /// renderiza. Re-emitir por alguns segundos (idempotente: o host ignora se a entidade já existe) garante
        /// o render assim que o cliente fica pronto — resolve o render INCONSISTENTE.</summary>
        public long NextNpcCreateMs;

        /// <summary>Class id da entidade NPC do avatar (0 = usar o default configurado). Permite escolher a
        /// classe humanoide pelo comando "/addbot arqueiro|mago|golem..." sem recompilar.</summary>
        public ushort NpcClassId;

        /// <summary>Geração do avatar — incrementa a cada spawn/respawn (= nova entidade CPlayer na engine). A
        /// ponte de injeção lê isto no <c>bot_pos.txt</c>: ao mudar, invalida o ponteiro cacheado e re-acha a
        /// entidade nova → mata o crash de ponteiro-velho (SetPlacement em memória liberada no respawn).</summary>
        public int SpawnGen;

        // ---- patrulha (placeholder de IA até a perseguição por posição do alvo) ----
        private float _patrolMinZ, _patrolMaxZ;
        private float _patrolDir;   // +1 ou -1 no eixo Z

        /// <summary>
        /// Posiciona o bot no extremo do SEU time e prepara a patrulha no eixo Z, no lado do próprio time.
        /// Faixas/extremos da captura real (x≈3.75, z:[-24.5..24.8]; um time por extremo). Direção inicial
        /// andando em direção ao centro; Yaw apontando ao centro. Chamado no spawn (BotTick fallback e
        /// SpawnFieldBotsInStage primário).
        /// </summary>
        public void InitStagePosition()
        {
            X = 3.75f;
            Y = 0f;
            if (Team == 0)        // time 0 patrulha o lado +Z, do extremo (+24) ao centro (+2)
            {
                _patrolMinZ = 2f; _patrolMaxZ = 24f;
                Z = _patrolMaxZ; _patrolDir = -1f; Yaw = 180f;   // do extremo + em direção ao centro (-Z)
            }
            else                  // time 1 patrulha o lado -Z, do extremo (-24) ao centro (-2)
            {
                _patrolMinZ = -24f; _patrolMaxZ = -2f;
                Z = _patrolMinZ; _patrolDir = 1f; Yaw = 0f;      // do extremo - em direção ao centro (+Z)
            }
        }

        /// <summary>
        /// Um passo da patrulha vai-e-volta no eixo Z (~0.75 coord/tick da captura). Ao atingir o limite,
        /// clampa, inverte a direção e gira o Yaw 180°. X/Y fixos. Placeholder até a perseguição por posição
        /// do alvo.
        /// </summary>
        public void PatrolStep()
        {
            Z += _patrolDir * 0.75f;
            if (Z >= _patrolMaxZ) { Z = _patrolMaxZ; _patrolDir = -1f; Yaw += 180f; }
            else if (Z <= _patrolMinZ) { Z = _patrolMinZ; _patrolDir = 1f; Yaw += 180f; }
        }

        /// <summary>Velocidade atual no plano XZ (coord/tick). Integra a aceleração do perfil p/ partidas/paradas
        /// HUMANAS (arranca e freia, não teleporta à velocidade plena). Zerada no stagger e no reset de round.</summary>
        public float VelX, VelZ;

        /// <summary>Sentido da orbitação no melee (+1/-1). Inverte a cada ~2s p/ o bot não girar sempre pro mesmo
        /// lado (humano troca de lado). Fase por <see cref="Id"/> p/ dessincronizar múltiplos bots.</summary>
        public sbyte StrafeDir = 1;
        private long _nextStrafeFlipMs;

        /// <summary>Inverte o sentido da orbitação periodicamente (cadência levemente variada por bot).</summary>
        public void MaybeFlipStrafe(long now)
        {
            if (_nextStrafeFlipMs == 0) { _nextStrafeFlipMs = now + 1200 + (Id % 5) * 300; return; }
            if (now >= _nextStrafeFlipMs)
            {
                StrafeDir = (sbyte)-StrafeDir;
                _nextStrafeFlipMs = now + 1600 + (Id % 7) * 200;
            }
        }

        /// <summary>
        /// Move o bot em direção a (tx, tz) com modelo de VELOCIDADE: acelera a velocidade atual rumo à
        /// desejada (do <see cref="Profile"/>) em vez de saltar à máxima — arranque/freada suaves de humano.
        /// Encara sempre o alvo. Dentro do <paramref name="standoff"/> a velocidade desejada é zero (desacelera).
        /// </summary>
        public void MoveToward(float tx, float tz, float standoff = 3.0f)
        {
            float dx = tx - X, dz = tz - Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist > 0.01f)
                Yaw = MathF.Atan2(dx, dz) * (180f / MathF.PI);   // SEMPRE encara o alvo (mesmo colado no melee)

            float desVx = 0f, desVz = 0f;
            if (dist >= standoff)
            {
                float inv = Profile.MoveSpeed / dist;            // normaliza e escala p/ a velocidade do perfil
                desVx = dx * inv; desVz = dz * inv;
            }
            Integrate(desVx, desVz);
        }

        /// <summary>
        /// Orbita o alvo (tx, tz) no melee em vez de ficar PARADO — o humano circula o oponente. Move-se
        /// TANGENCIALMENTE (perpendicular à linha até o alvo) na velocidade do perfil escalada por
        /// <see cref="BotProfile.StrafeBias"/>, com correção radial p/ manter ~<paramref name="ringRadius"/>.
        /// <paramref name="dir"/> = +1/-1 dá o sentido do giro. Encara sempre o alvo.
        /// </summary>
        public void StrafeAround(float tx, float tz, float dir, float ringRadius)
        {
            float dx = tx - X, dz = tz - Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist > 0.01f)
                Yaw = MathF.Atan2(dx, dz) * (180f / MathF.PI);
            if (dist < 0.01f) { Integrate(0f, 0f); return; }

            float ux = dx / dist, uz = dz / dist;                // unitário até o alvo
            float tanx = -uz * dir, tanz = ux * dir;             // perpendicular (sentido dir)
            float radial = dist - ringRadius;                    // >0 longe (aproxima); <0 perto (afasta)
            float spd = Profile.MoveSpeed * Math.Clamp(Profile.StrafeBias, 0f, 1f);
            Integrate(tanx * spd + ux * radial * 0.5f, tanz * spd + uz * radial * 0.5f);
        }

        /// <summary>Integra a velocidade atual rumo à desejada, limitada pela aceleração do perfil, e aplica à
        /// posição. Colisão de parede: clampa a posição ao AABB caminhável da arena (<see cref="Walk"/>) e zera
        /// a componente de velocidade que empurra contra a parede — o bot desliza pela parede em vez de
        /// atravessá-la (o movimento é 0x30a direto, sem física do cliente, então a colisão é aqui).</summary>
        private void Integrate(float desVx, float desVz)
        {
            float a = Profile.Acceleration;
            VelX = Approach(VelX, desVx, a);
            VelZ = Approach(VelZ, desVz, a);
            X += VelX; Z += VelZ;

            if (Walk is { } wb)
            {
                float cx = wb.ClampX(X), cz = wb.ClampZ(Z);
                if (cx != X) VelX = 0f;   // bateu na parede X: mata a velocidade normal (não "gruda" acumulando)
                if (cz != Z) VelZ = 0f;   // idem Z
                X = cx; Z = cz;
            }
        }

        /// <summary>Aproxima <paramref name="cur"/> de <paramref name="target"/> em no máx. <paramref name="maxStep"/>.</summary>
        private static float Approach(float cur, float target, float maxStep)
        {
            float d = target - cur;
            if (d > maxStep) return cur + maxStep;
            if (d < -maxStep) return cur - maxStep;
            return target;
        }

        /// <summary>
        /// Recuo VISÍVEL ao levar um golpe: empurra o bot para LONGE de (<paramref name="fromX"/>,
        /// <paramref name="fromZ"/>) por <paramref name="dist"/> coord no plano XZ e o faz encarar o atacante.
        /// O próximo 0x30a carrega a nova posição → o cliente RENDERIZA o bot cambaleando para trás. É o único
        /// feedback de hit possível sem o type-7 (que mura a faísca/dano nativos); o dano em si é server-side.
        /// </summary>
        public void ApplyKnockback(float fromX, float fromZ, float dist)
        {
            float dx = X - fromX, dz = Z - fromZ;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) { dx = 0f; dz = -1f; len = 1f; }       // degenerado (colado): empurra p/ -Z
            X += dx / len * dist;
            Z += dz / len * dist;
            Yaw = MathF.Atan2(fromX - X, fromZ - Z) * (180f / MathF.PI);   // encara o atacante
        }

        /// <summary>
        /// Calcula o vetor de mira (aim-vec) apontando ao alvo (tx, ty, tz). O aim-vec
        /// é a direção do golpe/movimento no CNetMessage 0x30a (packed *0.01).
        /// </summary>
        public void SetAimToward(float tx, float ty, float tz)
        {
            float dx = tx - X;
            float dy = ty - Y;
            float dz = tz - Z;
            float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.01f) { AimX = 0; AimY = 0; AimZ = -5.99f; return; }
            // Normaliza e escala para o alcance do golpe (~6.0 coord, captura: aimZ=-599)
            const float aimScale = 6.0f;
            AimX = dx / len * aimScale;
            AimY = dy / len * aimScale;
            AimZ = dz / len * aimScale;
        }

        /// <summary>Vetor de ação/mira (direção do golpe/movimento) — campo action-vec do CNetMessage 0x30a.</summary>
        public float AimX, AimY, AimZ;

        /// <summary>Contador de sequência do datagrama UDP de gameplay (= *(CNet+4)++ do cliente original). Serve
        /// tanto os moves (0x30a/0x30b) quanto o create NPC (0x0307) — todos unreliable, sem exigência de seq
        /// contígua (o canal reliable 0x8307 foi abandonado: exigia um peer-host que o cliente único não hospeda).</summary>
        public uint UdpSeq;

        // O estado de REDE do bot (socket UDP + lockstep de sessão) NÃO mora aqui: é infra e vive num
        // RakionServer.World.Network.BotNetLink dono do BotManager (mapa keyed pelo bot). Assim o domínio
        // BotPlayer fica puro (posição/HP/IA/protocolo-de-sequência), sem depender de Socket/IPEndPoint.

        /// <summary>High-byte da ÚLTIMA ação (0x0311) do bot — ecoado no tail do 0x30f (driver da animação de
        /// golpe no cliente; captura: persiste até a próxima ação). 0x01 = neutro (default do peer real).</summary>
        public byte LastActionHigh = 0x01;

        /// <summary>Os 2 opens do lockstep de sessão já foram enviados NESTA PARTIDA (o canal com o host abre
        /// UMA vez por partida — na captura não há re-open por round; re-abrir é suspeito de "unknown error").</summary>
        public bool LockstepOpenedMatch;

        /// <summary>O alive-flag 0x830c já foi baixado (payload 0) p/ a morte corrente — evita re-envio por tick.</summary>
        public bool AliveFlagDown;

        /// <summary>Nonce do subFrame (off+0x0c do corpo 0x30a): varia a cada frame; o cliente só exige que mude.</summary>
        private byte _sub;

        /// <summary>Próximo subFrame nonce (incrementa e devolve; wraps em 255→0).</summary>
        public byte NextSubFrame() => unchecked(_sub++);

        /// <summary>Reset por ROUND: energia cheia, vivo, sem alvo (espelha o reset do round do field). O estado de
        /// REDE (socket/peer) é infra e o BotManager o descarta no re-spawn do round (DisposeLink) — não aqui.</summary>
        public void ResetForRound()
        {
            Hp = MaxHp;
            Dead = false;
            VelX = 0f; VelZ = 0f;
            StrafeDir = 1; _nextStrafeFlipMs = 0;
            TargetSeat = -1;
            RouteIndex = 0;
            NextAttackMs = 0;
            ComboStep = 0;
            NextComboHitMs = 0;
            ComboVariant = 0;
            RespawnAtMs = 0;
            LastHitMs = 0;
            StunUntilMs = 0;
            SpawnedThisRound = false;
            SpawnedMs = 0;
            LastActionHigh = 0x01;
            AliveFlagDown = false;
            // LockstepOpenedMatch NÃO reseta: o canal com o host persiste entre rounds (captura).
        }
    }
}
