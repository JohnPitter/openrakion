using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World
{
    /// <summary>
    /// Navegação de OBJETIVO do bot no Golem War (server-side): segue a rota de waypoints do mapa
    /// (golem dourado → pad de teleporte → golem inimigo) quando não há humano inimigo por perto —
    /// o combate com humano tem prioridade. Destruir o golem INIMIGO dispara
    /// <see cref="Domain.Field.EndRoundObjective"/> = vitória do round (= "ganhar o mapa"). Geometria
    /// por mapa em <see cref="GolemWarLayouts"/> (coords do .wld; calibração pela sonda do BotAi).
    /// </summary>
    public sealed partial class BotManager
    {
        /// <summary>Distância XZ (coord) p/ um waypoint de passagem/pad ser "alcançado".</summary>
        private const float WaypointReachDist = 2.5f;

        /// <summary>Standoff baixo p/ a navegação de objetivo ALCANÇAR o waypoint (o combate usa 3.0 p/ não
        /// prensar o humano; com 3.0 > <see cref="WaypointReachDist"/> o bot parava antes e travava).</summary>
        private const float ObjectiveMoveStandoff = 1.0f;

        /// <summary>Dano por golpe do bot ao golem (placeholder até a RE da fórmula, §11/task #18).</summary>
        private const ushort GolemHitDmg = 10;

        /// <summary>Modo com golem-objetivo (Golem/Boss)? Só nesses a rota de objetivo se aplica.</summary>
        private static bool IsObjectiveMode(Domain.Field f) =>
            f.Mode == (byte)GameMode.Golem || f.Mode == (byte)GameMode.Boss;

        /// <summary>O bot tem um humano inimigo VIVO como alvo (qualquer distância)?</summary>
        private static bool HasLiveTarget(Domain.Field f, BotPlayer bot)
        {
            if (bot.TargetSeat < 0) return false;
            var t = f.RecAt(bot.TargetSeat);
            return t != null && t.Playing && !t.Dead && !t.IsBot;
        }

        /// <summary>O bot deve ENGAJAR o humano agora? (modo Golem: só se PRÓXIMO; senão larga p/ o objetivo).
        /// Sem posição conhecida do alvo → não engaja (avança o objetivo).</summary>
        private static bool IsEngagingHuman(Domain.Field f, BotPlayer bot)
        {
            if (!HasLiveTarget(f, bot)) return false;
            var t = f.RecAt(bot.TargetSeat)!;
            if (t.LastPositionMs == 0) return false;
            float dx = t.LastX - bot.X, dz = t.LastZ - bot.Z;
            float range = bot.Profile.EngageRange;   // agressividade por dificuldade (Easy hesita; Hard caça de longe)
            return dx * dx + dz * dz <= range * range;
        }

        /// <summary>
        /// Um passo da rota de objetivo: avança ao waypoint atual, ataca golems no alcance e resolve os pads
        /// de teleporte (snap). O chamador (BotTick) já garante o modo-objetivo e a ausência de humano próximo.
        /// </summary>
        private void UpdateBotObjective(Domain.Field f, PlayerRec rec, BotPlayer bot, long now)
        {
            var route = GolemWarLayouts.For(f.MapId).RouteFor(bot.Team);
            if (route.Count == 0) return;
            if (bot.RouteIndex >= route.Count) bot.RouteIndex = route.Count - 1;   // fica no golem inimigo
            var wp = route[bot.RouteIndex];

            float dx = wp.Pos.X - bot.X, dz = wp.Pos.Z - bot.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);

            switch (wp.Kind)
            {
                case WaypointKind.Nav:
                    if (dist <= WaypointReachDist) AdvanceRoute(bot, route.Count);
                    else bot.MoveToward(wp.Pos.X, wp.Pos.Z, ObjectiveMoveStandoff, RunSpeedPerTick);
                    break;

                case WaypointKind.Teleport:
                    if (dist <= WaypointReachDist)
                    {
                        bot.X = wp.Target.X; bot.Z = wp.Target.Z;   // touch-pad snapa o bot (só XZ; Y = plano do chão)
                        Log.Ok("bot", "field {0}: '{1}' teleporte -> ({2:F1},{3:F1})", f.Id, bot.Name, bot.X, bot.Z);
                        AdvanceRoute(bot, route.Count);
                    }
                    else bot.MoveToward(wp.Pos.X, wp.Pos.Z, ObjectiveMoveStandoff, RunSpeedPerTick);
                    break;

                case WaypointKind.Golem:
                    if (dist <= BotAttackRange)
                    {
                        // NÃO injeta o Y do .wld (golem em plataforma) na posição do bot — o espaço Y vivo não
                        // está calibrado e o chão vivo é Y=0; usar o Y do waypoint erguia o bot ("voando").
                        bot.SetAimToward(wp.Pos.X, bot.Y, wp.Pos.Z);       // mira o golem no plano do bot (aim-vec)
                        if (AttackGolem(f, rec, bot, wp.Param, now)) AdvanceRoute(bot, route.Count);
                    }
                    else bot.MoveToward(wp.Pos.X, wp.Pos.Z, ObjectiveMoveStandoff, RunSpeedPerTick);
                    break;
            }
        }

        /// <summary>Ataca o golem-alvo do waypoint na cadência do combo (golpe visual + dano
        /// server-authoritative). Devolve true ao derrubá-lo (a rota avança). Master INIMIGO derrubado já
        /// dispara <see cref="Domain.Field.EndRoundObjective"/> dentro de <see cref="Domain.Field.DamageGolemTarget"/>.</summary>
        private bool AttackGolem(Domain.Field f, PlayerRec rec, BotPlayer bot, int golemTarget, long now)
        {
            if (now < bot.NextAttackMs) return false;
            bot.NextAttackMs = now + bot.Profile.ComboRecoveryMs;
            EmitBotAttack(rec, bot, 0x0001);                       // swing visível no golem (aim já aponta)
            // passa o seat do bot: ao derrubar o golem dourado (target 2) ele GANHA a Gold Sword; só então dana o Master.
            bool destroyed = f.DamageGolemTarget(golemTarget, GolemHitDmg, rec.Slot);
            if (destroyed)
                Log.Ok("bot", "field {0}: '{1}' DERROTOU golem alvo {2} (rota avança)", f.Id, bot.Name, golemTarget);
            return destroyed;
        }

        /// <summary>Avança ao próximo waypoint da rota (clampa no último = golem inimigo).</summary>
        private static void AdvanceRoute(BotPlayer bot, int routeCount)
        {
            if (bot.RouteIndex < routeCount - 1) bot.RouteIndex++;
        }
    }
}
