using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Grupo "combate-A" dos handlers in-field reconstruidos FIELMENTE do worldserv.exe:
    ///   0x43 ATTACK / match-start engage   (FUN_00424210 -> FUN_004079d0)
    ///   0x45 spawn / join-into-field       (FUN_004242c0 -> FUN_00407c70)
    ///   0x46 saída/morte própria           (FUN_00424350 -> FUN_0041b860/00405950/0040b900/00407e00)
    ///
    /// Canais (confirmado pela fundacao):
    ///   FIELD  = ctx.User.SendMessage / field.BroadcastField  (header [serverSeq][msgType])
    ///   LOBBY  = ctx.User.SendEncryptedFrame(Prefix(msgType,...))
    /// Gate fiel: InField(user+0x1460) != 0 && FieldSecondary(user+0x14a4) != 0 && Status(user+0x1440)==3.
    /// </summary>
    public static partial class WorldHandlers
    {
        // ===================================================================
        // 0x43 ATTACK / match-start engage  (FUN_00424210 -> FUN_004079d0)
        // ===================================================================
        /// <summary>
        /// FUN_00424210: gate (0x1460!=0 && 0x14a4!=0 -> DISC 0x77; Status==3 -> DISC 0x78);
        /// FUN_0040b7d0 resolve (fieldSlot,playerSlot); exige playerSlot == field+0x121 (host) -> DISC 0x79;
        /// chama FUN_004079d0 (engage do round). Ponto central de "match start / liberar combate".
        /// </summary>
        internal static void Op_0x43_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // FUN_00424210: if (0x1460==0 || 0x14a4==0) DISC 0x77
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x77); return; }
            // if (0x1440 != 3) DISC 0x78
            if (u.Status != UserStatus.InField) { u.Disconnect(0x78); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null) { u.Disconnect(0x79); return; }

            // if ((char)playerSlot != field+0x121) DISC 0x79  -> so o host dispara o engage
            if (rec.Slot != field.MasterSlot) { u.Disconnect(0x79); return; }

            Log.Info("combat", "[{0}] 0x43 engage (field {1} seat {2} host)", u.Slot, field.Id, rec.Slot);
            lock (field.SyncRoot) Combat_0x43_Engage(ctx, field);
        }

        /// <summary>
        /// FUN_004079d0 (engage do round). Se o field nao estiver em estado 2/0:
        ///   - se o registro do host (field+host*0x14+0x127) == 1 (host-locked): START forcado;
        ///   - senao avalia por modo (field+0x119) se ha condicao p/ iniciar; se nao ha,
        ///     envia ao host FIELD 0x43 [cause] (len 3) e aborta.
        /// No START: marca field+8=2, todos os players state 1/2 -> 3 (engage), zera contadores de
        /// tiro do user (user+0x2395/+0x2399), chama FUN_004065e0 (inicia round). Caso OK do laco
        /// "switch" (cVar4==0): broadcast FUN_004061f0 0x43 [0] a TODOS, seta field+0x2b4=0,
        /// field+0x2b8=GetTick+40000 e zera estado de combate (+0x3ac..+0x3bf).
        /// </summary>
        private static void Combat_0x43_Engage(HandlerContext ctx, Field field)
        {
            // FUN_004079d0: if (field+8 == 2 || field+8 == 0) return;  (so age se field+8 estiver "armado")
            if (field.State == 2 || field.State == 0) return;

            sbyte cVar4 = 0; // causa de abort (0 = pode iniciar)
            var hostRec = field.RecAt(field.MasterSlot);
            bool forcedStart = hostRec != null && hostRec.State == 1 /* +0x127 host-locked flag */;

            if (!forcedStart)
            {
                // switch (field+0x119) — avalia se ha condicao de inicio; cVar4 != 0 = abort
                switch (field.Mode)
                {
                    case 1:
                    case 3:
                    case 4:
                        // compara contadores de time (+0x116/+0x117 e +0x114/+0x115)
                        if (field.MinLevel == field.MaxLevel /* +0x116 == +0x117 (alias) */)
                        {
                            // (+0x114 == +0x115) -> cai no default (pode iniciar)
                            cVar4 = 2;
                        }
                        else
                        {
                            cVar4 = 1;
                        }
                        break;
                    case 2:
                        // if (1 < (+0x117 + +0x116)) pode iniciar; senao abort cause 1
                        cVar4 = (CountTeam(field, 0) + CountTeam(field, 1)) > 1 ? (sbyte)0 : (sbyte)1;
                        break;
                    default:
                        cVar4 = 0; // default: pode iniciar
                        break;
                }

                // default/empate cai no laco que procura outro player pronto (state 1/3/4 != host).
                if (cVar4 == 0 || cVar4 == 2)
                {
                    bool another = false;
                    for (int b = 0; b < 0x12; b++)
                    {
                        if (b == field.MasterSlot) continue;
                        var r = field.RecAt(b);
                        byte st = r?.State ?? 0;
                        if (st == 1 || st == 4 || st == 3) { another = true; break; }
                    }
                    if (!another) forcedStart = true; // ninguem mais pronto -> START (LAB_00407ab0)
                    else cVar4 = 3;                   // ha outro -> aborta com cause 3
                }
            }

            if (forcedStart)
            {
                field.ArmMatch(Environment.TickCount64);
                cVar4 = 0; // segue para o broadcast de start (LAB_00407b16, cVar4==0)
            }
            else if (cVar4 != 0)
            {
                // abort: FUN_0041b8a0(host, 0x43, [cause]) len 3 -> so ao host
                ctx.User.SendMessage(0x43, new[] { (byte)cVar4 });
                Log.Info("combat", "[{0}] 0x43 engage abortado (cause {1})", ctx.User.Slot, cVar4);
                return;
            }

            // LAB_00407b16 cVar4==0: broadcast 0x43 [0] a TODOS (FUN_004061f0); reset de match.
            field.BroadcastField(0x43, new byte[] { 0x00 });
            ctx.World.Bots.PublishMatchSpawns(field);
            field.Phase = MatchPhase.Pre;
            field.DeadlineMs = Environment.TickCount64 + 40000;
            field.Warned30 = 0;
            Log.Ok("combat", "[{0}] 0x43 START broadcast (field {1}, fase=Pre, deadline +40s)", ctx.User.Slot, field.Id);
        }

        // ===================================================================
        // 0x45 spawn / join-into-field  (FUN_004242c0 -> FUN_00407c70)
        // ===================================================================
        /// <summary>
        /// FUN_004242c0: gate (0x1460!=0 && 0x14a4!=0 -> DISC 0x7a; Status==3 -> DISC 0x7b);
        /// FUN_0040b7d0 resolve (fieldSlot,playerSlot); chama FUN_00407c70 (spawn do player no campo).
        /// </summary>
        internal static void Op_0x45_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // if (0x1460==0 || 0x14a4==0) DISC 0x7a
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7a); return; }
            // if (0x1440 != 3) DISC 0x7b
            if (u.Status != UserStatus.InField) { u.Disconnect(0x7b); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null) return;

            Log.Info("combat", "[{0}] 0x45 spawn (field {1} seat {2})", u.Slot, field.Id, rec.Slot);
            if (Combat_0x45_SpawnInto(field, rec.Slot, ctx.World.Config.ForceTunneling))
            {
                u.PlayerSpawnMatchId = field.MatchId;
                ctx.World.Bots.SendMatchSpawnsTo(u, field);
            }
        }

        /// <summary>
        /// FUN_00407c70: spawn de 1 player no campo.
        ///   - se field+8 != 2: result = 1 (falha) -> FIELD 0x45 [result] so ao proprio.
        ///   - se field+0x119 == 2 (survival): state = 3 direto (sem time).
        ///   - senao escolhe time pela contagem dos blocos 0..9 (bVar2) vs 10..0x13 (bVar3),
        ///     respeitando host-lock (+0x127); se o time do player nao for o menos cheio -> result=2.
        ///   - OK (result 0): state=3, zera kills (+0x12d/+0x12e) e score (+0x130),
        ///     broadcast FUN_004061f0 0x45 [result=0][seat] a TODOS + FUN_004066c0 (housekeeping).
        /// </summary>
        internal static bool Combat_0x45_SpawnInto(
            Field field, int seat, bool forceTunneling)
        {
            var rec = field.RecAt(seat);
            if (rec == null) return false;

            sbyte result;
            if (field.State != 2)
            {
                result = 1; // field nao armado
            }
            else if (rec.Playing)
            {
                result = 0; // cliente gráfico confirma 0x48 antes do 0x4B; não regride para spawning
            }
            else if (field.Mode == 2)
            {
                rec.State = 3; // survival: spawn direto
                result = 0;
            }
            else
            {
                // conta jogadores ocupados (state 3/4, nao morto +1) nos blocos 0..9 e 10..0x13
                int bVar2 = 0, bVar3 = 0;
                for (int i = 0; i < 10; i++)
                {
                    var r = field.RecAt(i);
                    if (r != null && (r.State == 3 || r.State == 4) && !r.Dead) bVar2++;
                }
                for (int i = 10; i < 0x14; i++)
                {
                    var r = field.RecAt(i);
                    if (r != null && (r.State == 3 || r.State == 4) && !r.Dead) bVar3++;
                }

                bool hostLocked = rec.State == 1; // +0x127 host-locked flag do registro
                if (seat < 10)
                {
                    if (!hostLocked && bVar3 < bVar2) result = 2; // time 0 ja mais cheio -> recusa
                    else { rec.State = 3; result = 0; }
                }
                else
                {
                    if (!hostLocked && bVar2 < bVar3) result = 2; // time 1 ja mais cheio -> recusa
                    else { rec.State = 3; result = 0; }
                }
            }

            if (result != 0)
            {
                // FUN_0041b8a0(self, 0x45, [result,seat]) len 4 — so ao proprio
                rec.Session?.SendMessage(0x45,
                    FieldLifecycleFrames.SpawnRejected((byte)seat, (byte)result));
                Log.Info("field", "spawn recusado (field {0} seat {1} result {2})", field.Id, seat, result);
                return false;
            }

            // OK: zera kills/score do registro e broadcast 0x45 [result=0][seat] a TODOS.
            rec.Dead = false;
            rec.RoundScore = 0;
            field.BroadcastField(0x45, FieldLifecycleFrames.Spawn((byte)seat));
            PublishTunnelingPresenceOnSpawn(field, rec, forceTunneling);
            Log.Ok("field", "spawn OK (field {0} seat {1} team {2})", field.Id, seat, rec.Team);
            return true;
        }

        private static void PublishTunnelingPresenceOnSpawn(
            Field field, PlayerRec record, bool forceTunneling)
        {
            switch (field.RegisterTunnelingPresence(
                (byte)record.Slot,
                record.Session != null &&
                    (forceTunneling || record.Session.UdpObservedEndpoint == null)))
            {
                case TunnelingPresenceChange.Enabled:
                    field.BroadcastField(0x54, Array.Empty<byte>());
                    break;
                case TunnelingPresenceChange.SyncEnabled:
                    record.Session?.SendMessage(0x54, Array.Empty<byte>());
                    break;
            }
        }

        // ===================================================================
        // 0x46 saida/morte de combate do PROPRIO sender  (FUN_00424350)
        // ===================================================================
        /// <summary>
        /// FUN_00424350: gate (0x1460!=0 && 0x14a4!=0 -> DISC 0x7c; Status==3 -> DISC 0x7d);
        /// FUN_0040b7d0 resolve (fieldSlot, mySlot) — o slot usado em TUDO e' o do PROPRIO
        /// sender; param_3[0] e' so um FLAG (&lt;2). O cliente manda 0x46 ao SAIR do stage /
        /// cair de combate. Se flag&lt;2 e o sender e' valido (FUN_0041b860): ammo (FUN_00405950)
        /// + aplica penalidade de EXP (FUN_0040b900) e responde 0x58 AO PROPRIO. O comando DB 0xc
        /// entra na fila FUN_0041b940 e FUN_004138b0 persiste CharacterInfo.exp; o callback 0xc
        /// de confirmacao nao possui consumer em FUN_004295c0.
        /// Sempre:
        /// FUN_00407e00(mySlot) processa a morte/saida do sender. SEM broadcast de 0x46 (a 1a
        /// leitura inventava um targetSlot, dava dano no golem e broadcastava -> crash na saida).
        /// </summary>
        internal static void Op_0x46_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // if (0x1460==0 || 0x14a4==0) DISC 0x7c
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7c); return; }
            // if (0x1440 != 3) DISC 0x7d
            if (u.Status != UserStatus.InField) { u.Disconnect(0x7d); return; }

            var rec = ReconRec(ctx, out var field);
            if (field == null || rec == null) return;

            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0xff;
            lock (field.SyncRoot)
            {
                if (flag < 2 && Combat_CanHit(field, rec.Slot))
                {
                    int ammo = Combat_KillDiff(field, rec.Slot);
                    int remainingExperience = Combat_ApplyExitExperiencePenalty(u, (byte)ammo);
                    _ = ctx.World.Db.UpdateCharacterExperienceAsync(
                        u.ActiveCharId, unchecked((uint)remainingExperience));
                    u.SendMessage(0x58, FieldLifecycleFrames.ExitExperience(remainingExperience));
                }
                bool roundEnded = field.OnPlayerExit(rec.Slot, cause: 0);
                if (field.UnregisterTunnelingPresence((byte)rec.Slot) == TunnelingPresenceChange.Disabled)
                    field.BroadcastField(0x55, Array.Empty<byte>());
                field.BroadcastField(0x46, FieldLifecycleFrames.Exit((byte)rec.Slot));
                if (roundEnded) field.BroadcastFieldPlaying(0x4a, field.Build0x4a());
                if (field.CountAlive(0) + field.CountAlive(1) == 0)
                {
                    field.EndMatch(0);
                    field.BroadcastLobby(field.BuildMatchEnd(0));
                    _ = ctx.World.SettleEndedMatchAsync(field);
                }
            }
            u.Status = UserStatus.FieldLobby;
            Log.Info("combat", "[{0}] 0x46 saiu da partida para o game room " +
                "(field {1} seat {2} flag {3}, status=2)", u.Slot, field.Id, rec.Slot, flag);
        }

        // ===================== helpers do grupo combate-A =====================

        /// <summary>FUN_0041b860: alvo pode ser atingido? field+8==2 && fase!=0 && alvo state==4 && modo!=0.</summary>
        private static bool Combat_CanHit(Field field, int targetSlot)
        {
            var t = field.RecAt(targetSlot);
            return field.State == 2 && field.Phase != MatchPhase.Pre && t != null && t.State == 4 && field.Mode != 0;
        }

        /// <summary>FUN_00405950: diff de kills do alvo (+0x12d - +0x12e), 0 se nao positivo.</summary>
        private static int Combat_KillDiff(Field field, int targetSlot)
        {
            var t = field.RecAt(targetSlot);
            if (t == null) return 0;
            return t.CounterA > t.CounterB ? t.CounterA - t.CounterB : 0;
        }

        /// <summary>FUN_0040B900: desconta da EXP em +0x1534 usando nível +0x1531 e o diferencial.</summary>
        private static int Combat_ApplyExitExperiencePenalty(ClientSession u, byte differential)
        {
            uint remaining = CharacterProgression.ApplyCombatExitPenalty(
                u.CharExp, u.CharLevel, differential);
            u.CharExp = remaining;
            return unchecked((int)remaining);
        }

        /// <summary>Conta jogadores ocupados (state 3/4, vivos) de um time (slots 0..9 / 10..0x13).</summary>
        private static int CountTeam(Field field, int team)
        {
            int n = 0;
            foreach (var r in field.Slots)
                if ((r.State == 3 || r.State == 4) && !r.Dead && r.Team == team) n++;
            return n;
        }
    }
}
