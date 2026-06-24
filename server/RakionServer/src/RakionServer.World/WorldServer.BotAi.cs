using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Motor de IA + simulação de combate dos bots (server-side, parte do FieldEngine). Roda por-field
    /// durante a partida: anuncia o spawn do bot (0x45 — frame conhecido), escolhe alvo, e — quando o
    /// codec de movimento/ação estiver decodificado (task #6) — SINTETIZA o movimento e os ataques do
    /// bot (estado de IA -> bytes), nunca relay. A morte do bot é sintetizada pelo servidor (o bot não
    /// tem cliente p/ reportar a própria morte); a morte do humano é nativa (o cliente reporta 0x4f).
    /// </summary>
    public sealed partial class WorldServer
    {
        private const long BotDecisionIntervalMs = 250;
        private const long BotMoveIntervalMs = 150;
        private const long BotAttackCooldownMs = 800;

        /// <summary>Um tick de IA de todos os bots de um field (chamado pelo MatchTick na fase Playing).</summary>
        internal void BotTick(Domain.Field f)
        {
            if (f.BotCount == 0 || f.Phase != MatchPhase.Playing) return;
            long now = Environment.TickCount64;

            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot!;

                // 1) SPAWN: marca o bot como playing (domínio) e — se os frames do bot estiverem ligados —
                //    anuncia aos clientes (FIELD 0x45 [seat]). Gated: mandar 0x45 de um seat cujo info o
                //    cliente não tem (roster não validado) pode travar o cliente (lição-mestra).
                if (!bot.SpawnedThisRound)
                {
                    bot.SpawnedThisRound = true;
                    rec.State = 4; rec.Dead = false; bot.Dead = false;
                    if (BotMovement.ClientFramesEnabled) f.BroadcastField(0x45, new[] { (byte)rec.Slot });
                    Log.Ok("bot", "field {0}: '{1}' spawn (seat {2} time {3}){4}", f.Id, bot.Name, rec.Slot,
                        rec.Team, BotMovement.ClientFramesEnabled ? "" : " [server-side; frames off]");
                    continue;
                }
                if (bot.Dead || rec.Dead) continue;

                // 2) DECISÃO: alvo + (futuro) intenção de ataque, em cadência própria.
                if (now >= bot.NextDecisionMs)
                {
                    bot.NextDecisionMs = now + BotDecisionIntervalMs;
                    bot.TargetSeat = PickTargetSeat(f, rec);
                    if (bot.TargetSeat >= 0 && now >= bot.NextAttackMs)
                    {
                        bot.NextAttackMs = now + BotAttackCooldownMs;
                        EmitBotAttack(f, rec, bot);
                    }
                }

                // 3) MOVIMENTO: emite o frame de move sintetizado em cadência fixa.
                if (now >= bot.NextMoveMs)
                {
                    bot.NextMoveMs = now + BotMoveIntervalMs;
                    EmitBotMovement(f, rec, bot);
                }
            }
        }

        /// <summary>Alvo do bot: 1º humano VIVO do time inimigo. (Quando houver posições do codec de
        /// movimento, trocar por "humano vivo mais próximo".)</summary>
        private static int PickTargetSeat(Domain.Field f, PlayerRec self)
        {
            foreach (var r in f.Slots)
                if (r.Session != null && r.Playing && !r.Dead && r.Team != self.Team)
                    return r.Slot;
            return -1;
        }

        /// <summary>
        /// SÍNTESE do movimento+ação do bot = CNetMessage 0x30a por **UDP unreliable** a cada humano em
        /// jogo (espelha CNet::SendToOtherRelayClient/SendData_Unreliable: por-peer, state==3, ≠ si). O
        /// corpo (pos/aim) está cravado em BotMovement.EncodeActionBody; o datagrama completo fica gated
        /// no wrapper UDP (task #6). Até lá, no-op (o bot existe no spawn). NUNCA relay: pacote montado do
        /// estado do bot.
        /// </summary>
        private void EmitBotMovement(Domain.Field f, PlayerRec rec, BotPlayer bot)
        {
            foreach (var r in f.Slots)
            {
                var s = r.Session;
                if (s == null || !r.Playing || s.UdpEndpoint == null) continue; // só peers humanos em jogo
                byte[]? pkt = BotMovement.TryBuildActionDatagram(bot, rec.Slot);
                if (pkt == null) return;                                          // gated no wrapper UDP
                SendGameplayRaw(s.UdpEndpoint, pkt);
            }
        }

        /// <summary>
        /// Intenção de ATAQUE do bot: aponta a mira ao alvo. O ataque é o MESMO 0x30a (com o action-vec /
        /// actState setados) que EmitBotMovement serializa — o cliente do alvo processa o golpe e reporta
        /// a própria morte (0x4f killer=botSeat), nativo. Sem posições do codec ainda, registra a intenção.
        /// </summary>
        private static void EmitBotAttack(Domain.Field f, PlayerRec rec, BotPlayer bot)
        {
            var t = f.RecAt(bot.TargetSeat);
            if (t == null || !t.Occupied) return;
            // Com posições (codec de movimento): aim = direção normalizada até o alvo. Por ora, intenção.
            bot.AimX = 0; bot.AimY = 0; bot.AimZ = 0;
        }

        /// <summary>
        /// O HUMANO acertou o bot: o servidor SINTETIZA o dano (o bot não tem cliente p/ reportar morte).
        /// Ao zerar o HP, marca morto, credita o killer/placar (OnPlayerDeath) e broadcasta 0x4f
        /// (victim=botSeat) — o MESMO frame de morte de um jogador. Ponto de integração da detecção de
        /// hit (task #6: parsear a ação de ataque do humano que o servidor já vê no UDP).
        /// </summary>
        public void BotTakeDamage(Domain.Field f, PlayerRec rec, int killerSeat, ushort dmg, byte cause)
        {
            var bot = rec.Bot;
            if (bot == null || bot.Dead || rec.Dead) return;
            bot.Hp = (ushort)Math.Max(0, bot.Hp - dmg);
            if (bot.Hp > 0) return;

            bot.Dead = true;
            f.OnPlayerDeath(rec.Slot, killerSeat, cause);   // dead + crédito ao killer + placar/round (domínio)
            if (BotMovement.ClientFramesEnabled)
            {
                f.BroadcastFieldPlaying(0x4f,
                    new byte[] { (byte)rec.Slot, cause, (byte)killerSeat, f.Score0, f.Score1 });
                if (f.Phase == MatchPhase.RoundEnd)
                    f.BroadcastFieldPlaying(0x4a, f.Build0x4a());
            }
            Log.Ok("bot", "field {0}: '{1}' morto por seat {2} (cause {3})", f.Id, bot.Name, killerSeat, cause);
        }
    }
}
