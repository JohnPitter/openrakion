using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Grupo combate-B: corpos reconstruidos FIELMENTE dos FUN_xxxx do worldserv.exe
    /// (handlers.out.txt / fieldhelpers.out.txt / roomflow.out.txt / combat_callees.out.txt).
    ///
    /// Opcodes: 0x3b (FieldCreate sala), 0x3d (troca arma A<->B), 0x3e (re-spawn/assento),
    /// 0x3f (start-vote/kick), 0x40 (destroy/leave-object), 0x41 (config-in-field),
    /// 0x42 (ready/equip-lock), 0x4a (charge/postura-shift), 0x4b (MOVE relay exclui sender),
    /// 0x4c (action direcionada), 0x4d (rotacao/facing 2 eixos).
    ///
    /// CANAIS (confirmados pela fundacao):
    ///   FIELD  = ClientSession.SendMessage(msgType, body)  -> [u16 serverSeq][u16 msgType][body]
    ///            (= FUN_0041b940 / FUN_0041b8a0). data passado = SO o corpo apos msgType.
    ///   LOBBY  = ClientSession.SendEncryptedFrame([u16 msgType][...]) (= FUN_004038e0).
    ///   broadcast FIELD a todos do field = field.BroadcastField(msgType, body[, except]) (= FUN_004061f0).
    ///   broadcast FIELD aos state==4     = field.BroadcastFieldPlaying(msgType, body[, except]).
    /// O "len" do decompile inclui os 2 bytes do msgType; aqui passamos apenas (len-2) bytes de corpo.
    /// </summary>
    public static partial class WorldHandlers
    {
        // ===================== 0x3b  FUN_00423580  FieldCreate(sala) =====================
        // Gate: InField (user+0x1460) && FieldSecondary (user+0x14a4) && Status==2.
        // parse: name(<0x29)\0  senha(<9)\0  desc(<0xc9)\0  b1(map<100) b2(mode 1..4)
        //        b3 b4(u16 mapSlot) b5 b6 b7 b8. Valida mapa habilitado/modo/ranges, acha
        //        fieldSlot livre, inicializa e ack LOBBY 0x3b [result/playerSlot].
        internal static void Op_0x3B_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            // gate (disc 0x52 = sem field; 0x53 = status != 2). Status de sala = 2 (FieldLobby/LoggedIn)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x52); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x53); return; }

            // parse das 3 strings nul-terminadas
            string name = ctx.P.CString(0x29);
            if (name.Length >= 0x29) { u.Disconnect(0x54); return; }
            string pass = ctx.P.CString(9);
            if (pass.Length >= 9) { u.Disconnect(0x55); return; }
            string desc = ctx.P.CString(0xc9);
            if (desc.Length >= 0xc9) { u.Disconnect(0x56); return; }

            // bytes de configuracao (bVar1=map, bVar2=mode, byte, u16 mapSlot, b3, b4, b5)
            if (ctx.P.Remaining < 9) { u.Disconnect(0x5b); return; }
            byte map = ctx.P.Byte();          // bVar1 (uStack_2124)
            byte mode = ctx.P.Byte();         // bVar2 (uStack_210c)
            byte timeFlag = ctx.P.Byte();     // param_3[uVar8+2] (compara <0x16 quando mode!=0)
            ushort mapSlot = ctx.P.UInt16();  // uVar10 (uStack_2128) — valida 0x122..0x4ba
            byte b3 = ctx.P.Byte();           // bVar3 (uStack_2120) — sub-time/level p/ mode 2
            byte b4 = ctx.P.Byte();           // bVar4 (local_212c) — minLevel
            byte b5 = ctx.P.Byte();           // bVar5 (uStack_2114) — maxLevel

            ushort err = 0;
            // FUN_00423580: se mode==0 -> mapa simples (map<100 && habilitado)
            if (mode == 0)
            {
                if (map >= 100) { u.Disconnect(0x57); return; }
                // mapa habilitado? (this+0xe8 tabela). Sem a tabela aqui assumimos habilitado.
                // ack parcial FIELD 0x25 (confirma criacao) + ack LOBBY 0x3b.
            }
            else if (mode > 4) { u.Disconnect(0x5b); return; }
            else if (timeFlag >= 0x16) { u.Disconnect(0xca); return; }
            else if (mapSlot < 0x122 || mapSlot > 0x4ba) { err = 0xcb; }
            else if (mode == 2 && (b3 < 0xd || b3 > 0x1e)) { err = 0xcc; }
            else if (mode == 3 && b3 > 0x13 && b3 < 0x33) { err = 0xcc; }
            else if (b4 == 0 || b5 > 99) { err = 0x59; }
            else if (!(u.FieldCashCost >= b4 && u.FieldCashCost <= b5)) { err = 0x5a; }

            if (err != 0) { u.Disconnect(err); return; }

            // acha fieldSlot livre e inicializa (FUN_00405440) — ponte da fundacao
            var field = ctx.World.EnsureFieldForSession(u);
            if (field != null)
            {
                field.Name = name;
                field.Mode = mode;
                field.MapId = map;
                field.MaxRounds = 1;
                if (mapSlot >= 0x122 && mapSlot <= 0x4ba) field.MapId = (byte)(mapSlot & 0xff);
                field.MinLevel = b4;
                field.MaxLevel = b5;
                field.MasterSlot = field.FindRec(u)?.Slot ?? -1;
                Log.Ok("field", "[{0}] criou sala '{1}' (mode={2} map={3})", u.Slot, name, mode, map);
            }

            // ack LOBBY 0x3b  (FUN_004038e0 ..., len=5): [0x3b 00][result/playerSlot u16... ]
            byte playerSlot = (byte)(field?.FindRec(u)?.Slot ?? 0);
            using var w = new PacketWriter();
            w.WriteWord(0x3b).WriteByte(0).WriteByte(playerSlot);
            u.SendEncryptedFrame(w.ToArray());
        }

        // ===================== 0x3d  FUN_00423ad0 -> FUN_00407520  troca de arma A<->B ===========
        // Gate: InField && FieldSecondary (disc 0x60) ; Status==3 (disc 0x61).
        // parse: param_3[0]=dir (0 ou 1). FUN_00407520: state@+0x126 do player:
        //   dir==0 && state==2 -> state=1 ; dir==1 && state==1 -> state=2 ; senao ignora.
        //   Broadcast FUN_004061f0 len=4 msgType 0x3d body=[playerSlot][dir].
        internal static void Op_0x3D_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x60); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x61); return; }

            byte dir = ctx.P.Byte();
            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            // FUN_00407520
            if (dir == 0)
            {
                if (rec.WeaponState != 2) return;
                rec.WeaponState = 1;
            }
            else
            {
                if (rec.WeaponState != 1) return;
                rec.WeaponState = 2;
            }
            byte[] body = { (byte)rec.Slot, dir };
            field.BroadcastField(0x3d, body);
            Log.Ok("field", "[{0}] troca de arma dir={1} -> weapon={2} (slot {3}, state={4})", u.Slot, dir, rec.WeaponState, rec.Slot, rec.State);
        }

        // ===================== 0x3e  FUN_00423b70 -> FUN_004075a0  re-spawn / troca de assento =====
        // Gate: InField && FieldSecondary (disc 0x62) ; Status==3 (disc 0x63).
        // FUN_004075a0(field, playerSlot, 0): tenta mover o jogador do bloco fila p/ o de jogo
        //   (10..0x13 <-> 0..9 conforme team/contagem +0x116/+0x117). Falha => FIELD 0x3e [result=2]
        //   so ao proprio (len=3). OK => move o registro (+0x124..+0x134), atualiza host +0x121 e
        //   broadcast FUN_004061f0 len=5 msgType 0x3e body=[oldSlot][newSlot].
        internal static void Op_0x3E_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x62); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x63); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            // host-lock (+0x127): nao pode mover -> result=2 ao proprio
            // (representado por rec.State==5 "locked" na fundacao)
            byte result; // local_1002: 0=ok, 2=falha
            int target = -1;
            if (rec.State == 5)
            {
                result = 2;
            }
            else if (rec.Slot < 10)
            {
                // procura vaga no bloco 10..0x13
                result = 2;
                for (int i = 10; i < 0x14; i++)
                    if (field.Slots[i].State == 0) { target = i; result = 0; break; }
            }
            else
            {
                // procura vaga no bloco 0..9
                result = 2;
                for (int i = 0; i < 10; i++)
                    if (field.Slots[i].State == 0) { target = i; result = 0; break; }
            }

            if (result != 0)
            {
                // FIELD 0x3e [result] so ao proprio (FUN_0041b8a0 len=3)
                u.SendMessage(0x3e, new byte[] { result });
                Log.Info("field", "[{0}] re-spawn negado (sem vaga, slot {1})", u.Slot, rec.Slot);
                return;
            }

            // move o player-record (FUN_0040b7b0 + copia +0x124..+0x134)
            var dst = field.Slots[target];
            byte oldSlot = (byte)rec.Slot;
            dst.Session = rec.Session;
            dst.State = rec.State;
            dst.WeaponState = rec.WeaponState;
            dst.Dead = rec.Dead;
            dst.Score = rec.Score;
            dst.Cause = rec.Cause;
            rec.Session = null; rec.State = 0; rec.WeaponState = 1; rec.Dead = false; rec.Score = 0; rec.Cause = 0;

            if (field.MasterSlot == oldSlot) field.MasterSlot = target;

            byte[] body = { oldSlot, (byte)target };
            field.BroadcastField(0x3e, body); // FUN_004061f0 len=5 (2 msgType + 3 body? -> [old][new]+1)
            Log.Ok("field", "[{0}] re-spawn slot {1} -> {2}", u.Slot, oldSlot, target);
        }

        // ===================== 0x3f  FUN_00423c00 -> FUN_00405740  start-vote / desfazer sala =====
        // Gate: InField && FieldSecondary (disc 0x64) ; Status==3 (disc 0x65) ; subStatus=='4' (disc 0x66).
        // FUN_00405740(field): zera todos os player-records ocupados (state!=0 e !=5 -> state=0),
        //   notifica cada um (FUN_0041b8b0) e seta field+8=0 (desfaz/libera o field).
        internal static void Op_0x3F_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x64); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x65); return; }
            if (u.SubStatus != '4') { u.Disconnect(0x66); return; }

            var field = ReconField(ctx);
            if (field == null) return;

            // FUN_00405740: limpa todos e libera o field
            foreach (var r in field.Slots)
            {
                if (r.State != 0 && r.State != 5)
                {
                    r.State = 0;
                    // FUN_0041b8b0(target) = notificacao curta de cleanup ao user
                    // (sem payload extra no decompile; nada a enviar aqui)
                }
            }
            field.State = 0; // field+8 = 0
            Log.Ok("field", "[{0}] field {1} liberado (start-vote/desfaz)", u.Slot, field.Id);
        }

        // ===================== 0x40  FUN_00423cc0 -> FUN_004097c0  destroy / leave-object =========
        // Gate: InField && FieldSecondary (disc 0x67) ; Status==3 (disc 0x68).
        // Se subStatus(+0x146c) NAO e' '4' nem '\x01' (nao-host): exige (char)param_1==field+0x121
        //   (so o host derruba outro), senao disc 0x69. parse param_3[0]=playerSlot alvo.
        // FUN_004097c0(field, targetSlot): se state!=0/5 e nao host-locked(+0x127) ->
        //   FUN_004091e0(remove o objeto) + FUN_0041b8b0(notifica o user-alvo).
        internal static void Op_0x40_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x67); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x68); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            // checagem de host (FUN_00423cc0): se nao-host (subStatus != '4' e != 0x01)
            // exige que o sender seja o host do field (seu seat == field.MasterSlot).
            if (u.SubStatus != '4' && u.SubStatus != 0x01)
            {
                if (rec.Slot != field.MasterSlot) { u.Disconnect(0x69); return; }
            }

            byte targetSlot = ctx.P.Byte();
            var t = field.RecAt(targetSlot);
            if (t == null) return;

            // FUN_004097c0: state != 0 e != 5 e nao host-locked
            if (t.State != 0 && t.State != 5)
            {
                var victim = t.Session;
                // FUN_004091e0(field, targetSlot) = remove o player-record do campo
                t.State = 0; t.Dead = false; t.Score = 0; t.Session = null;
                // FUN_0041b8b0(victim) = notificacao de cleanup ao user-alvo (sem payload extra)
                Log.Ok("field", "[{0}] destroy slot {1} (victim {2})", u.Slot, targetSlot, victim?.Slot ?? 0xffff);
            }
        }

        // ===================== 0x41  FUN_00423dd0 -> FUN_004077c0  config-in-field (host) =========
        // Gate: InField && FieldSecondary (disc 0x6a) ; Status==3 (disc 0x6b) ; field+8 != 2 (disc 0x6c,
        //   nao pode em match); field+0x119 != 0 (disc 0x6d); playerSlot==host +0x121 (disc 0x6e).
        // parse: name(<0x29)\0 sub(<9)\0 desc(<0xc9)\0 + b(timeFlag<0x16) u16(map 0x122..0x4ba)
        //   maptime(b) mode(2/3). FUN_004077c0: grava name/sub/desc + 0x118/0x11a/0x11c/0x11e/0x119
        //   e broadcast FUN_004061f0 len=uVar1+6 msgType 0x41 com tudo concatenado.
        internal static void Op_0x41_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x6a); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x6b); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            if (field.State == 2) { u.Disconnect(0x6c); return; }           // em match
            if (field.Mode == 0) { u.Disconnect(0x6d); return; }            // field+0x119 == 0
            if (rec.Slot != field.MasterSlot) { u.Disconnect(0x6e); return; } // nao e' o host

            string name = ctx.P.CString(0x29);
            if (name.Length >= 0x29) { u.Disconnect(0x6f); return; }
            string sub = ctx.P.CString(9);
            if (sub.Length >= 9) { u.Disconnect(0x70); return; }
            string desc = ctx.P.CString(0xc9);
            if (desc.Length >= 0xc9) { u.Disconnect(0x71); return; }

            if (ctx.P.Remaining < 6) { u.Disconnect(0x72); return; }
            byte timeFlag = ctx.P.Byte(); // bVar2 (<0x16) — param_3[pCVar1] tambem grava +0x118 (map)
            byte mapByte = timeFlag;      // FUN_004077c0 param_4 = param_3[pCVar1] (mesmo byte) -> +0x118
            ushort mapSlot = ctx.P.UInt16(); // uVar7 (0x122..0x4ba)
            byte maxRounds = ctx.P.Byte();   // bVar3 (param_5) -> +0x11a
            byte mode = ctx.P.Byte();        // bVar4 (param_8) -> +0x119

            if (timeFlag >= 0x16) { u.Disconnect(0xcd); return; }
            if (mapSlot < 0x122 || mapSlot > 0x4ba) { u.Disconnect(0xce); return; }
            if (mode == 2) { if (!(maxRounds > 0xc && maxRounds < 0x1f)) { u.Disconnect(0xcf); return; } }
            else if (mode == 3) { if (!(maxRounds > 0x13 && maxRounds < 0x33)) { u.Disconnect(0xcf); return; } }
            else if (!(mode != 0 && mode < 5)) { u.Disconnect(0x72); return; }

            // FUN_004077c0: grava no field-record
            field.Name = name;
            field.MapId = mapByte;
            field.MaxRounds = maxRounds;
            field.RoundDurationSec = mapSlot;   // +0x11c (u16) = uVar7 (param_6)
            field.FragLimit = timeFlag;         // +0x11e (param_7 = param_3[pCVar1])
            field.Mode = mode;                  // +0x119

            // broadcast FUN_004061f0 msgType 0x41:
            //   body = name\0 sub\0 desc\0 [mapByte][maxRounds][u16 dur][fragLimit][mode]
            using var w = new PacketWriter();
            w.WriteCString(name).WriteCString(sub).WriteCString(desc)
             .WriteByte(field.MapId).WriteByte(field.MaxRounds)
             .WriteWord(field.RoundDurationSec).WriteByte(field.FragLimit).WriteByte(field.Mode);
            field.BroadcastField(0x41, w.ToArray());
            Log.Ok("field", "[{0}] config sala '{1}' mode={2} map={3} rounds={4} dur={5}",
                u.Slot, name, mode, field.MapId, maxRounds, field.RoundDurationSec);
        }

        // ===================== 0x42  FUN_00424100 -> FUN_00407910  ready / equip-lock toggle ======
        // Gate: InField && FieldSecondary (disc 0x73) ; Status==3 (disc 0x74) ;
        //   (char)param_1==field+0x121 host (disc 0x75). parse b0=val (<0x14 && !=8,9,0x12,0x13 -> disc 0x76)
        //   b1=arg. FUN_00407910(field, val, arg): requer field+0x119 != 0; arg==0 && state==0 -> state=5
        //   (lock, decrementa contagem do time +0x114/+0x115); arg!=0 && state==5 -> state=0 (unlock,
        //   incrementa). Broadcast FUN_004061f0 len=4 msgType 0x42 body=[val][arg].
        internal static void Op_0x42_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x73); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x74); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;
            if (rec.Slot != field.MasterSlot) { u.Disconnect(0x75); return; } // (char)param_1==host

            byte val = ctx.P.Byte();   // bVar2 (slot alvo do lock)
            if (!(val < 0x14 && val != 8 && val != 9 && val != 0x12 && val != 0x13))
            {
                u.Disconnect(0x76);
                return;
            }
            byte arg = ctx.P.Byte();

            // FUN_00407910: requer field+0x119 != 0
            if (field.Mode == 0) return;

            var target = field.RecAt(val);
            if (target == null) return;
            if (arg == 0)
            {
                if (target.State != 0) return; // so trava slot vazio
                target.State = 5;              // locked/spectator
            }
            else
            {
                if (target.State != 5) return; // so destrava slot trancado
                target.State = 0;
            }
            byte[] body = { val, arg };
            field.BroadcastField(0x42, body);
            Log.Ok("field", "[{0}] equip-lock slot={1} arg={2} -> state={3}", u.Slot, val, arg, target.State);
        }

        // ===================== 0x4a  FUN_004246e0 -> FUN_00405a90  charge / postura-shift =========
        // Gate: InField && FieldSecondary (disc 0x83) ; Status==3 (disc 0x84) ;
        //   playerSlot==field+0x121 host (disc 0x85). parse param_3[0]=dir (2 ou 3).
        // FUN_00405a90(field, dir): requer field+8==2 && field+0x119==0. dir==2 -> +0x2c0++ (Wins0),
        //   +0x2bf=1 (WinnerSide), incrementa kills time0 dos playing. dir==3 -> +0x2c1++ (Wins1),
        //   +0x2bf=0, kills time1. seta +0x2bd=dir, +0x2b4=2 (RoundEnd), +0x2b8=now+15000.
        //   Broadcast FUN_0041b8a0 a cada state==4 len=6 msgType 0x4a body=[cause/2bd][2bf][2c0][2c1].
        internal static void Op_0x4A_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x83); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x84); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;
            if (rec.Slot != field.MasterSlot) { u.Disconnect(0x85); return; }

            byte dir = ctx.P.Byte();

            // FUN_00405a90: requer field+8==2 && field+0x119==0
            if (field.State != 2 || field.Mode != 0) return;

            if (dir == 2)
            {
                field.Wins0++;
                field.WinnerSide = 1;
                foreach (var r in field.Slots) if (r.Playing && r.Team == 0) r.Score++;
            }
            else if (dir == 3)
            {
                field.Wins1++;
                field.WinnerSide = 0;
                foreach (var r in field.Slots) if (r.Playing && r.Team == 1) r.Score++;
            }
            else return;

            field.LastRoundWinner = dir;   // +0x2bd
            field.Phase = MatchPhase.RoundEnd; // +0x2b4 = 2
            field.DeadlineMs = Environment.TickCount64 + 15000;

            // corpo [lastWinner/2bd][winnerSide/2bf][wins0/2c0][wins1/2c1] — golden source em Field.Build0x4a()
            field.BroadcastFieldPlaying(0x4a, field.Build0x4a());
            Log.Ok("field", "[{0}] charge dir={1} (w0={2} w1={3})", u.Slot, dir, field.Wins0, field.Wins1);
        }

        // ===================== 0x4b  FUN_004247b0 -> FUN_00405c00  MOVE relay (EXCLUI sender) =====
        // Gate: InField && FieldSecondary (disc 0x86) ; Status==3 (disc 0x87).
        // parse: u16 len (<=200, senao disc 0x88) + blob[len]. FUN_00405c00(field, senderSlot, len, blob):
        //   requer field+8==2 && sender state==4. Broadcast FUN_0041b8a0 a cada state==4 E slot!=sender,
        //   msgType 0x4b body=[senderSlot][u16 len][blob] (len total = blob+5).
        internal static void Op_0x4B_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x86); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x87); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            ushort len = ctx.P.UInt16();
            if (len > 200) { u.Disconnect(0x88); return; }

            // FUN_00405c00: field+8==2 && sender state==4
            if (field.State != 2 || rec.State != 4) return;
            if (ctx.P.Remaining < len) return;
            byte[] blob = ctx.P.Bytes(len);

            // body = [senderSlot][u16 len][blob]
            using var w = new PacketWriter();
            w.WriteByte((byte)rec.Slot).WriteWord(len).WriteBytes(blob);
            field.BroadcastFieldPlaying(0x4b, w.ToArray(), except: u); // EXCLUI o proprio sender
            Log.Debug("field", "[{0}] move relay len={1} (slot {2})", u.Slot, len, rec.Slot);
        }

        // ===================== 0x4c  FUN_00424880 -> FUN_00405cc0  action direcionada a 1 alvo ====
        // Gate: InField && FieldSecondary (disc 0x89) ; Status==3 (disc 0x8a).
        // parse: b0=targetSlot (<0x14, senao disc 0x8b) + u16 len (<=200, senao disc 0x8c) + blob.
        // FUN_00405cc0(field, senderSlot, targetSlot, len, blob): requer field+8==2 && sender state==4
        //   && target state==4. Envia SO ao target (FUN_0041b8a0) msgType 0x4b body=[senderSlot][u16 len][blob].
        internal static void Op_0x4C_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x89); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x8a); return; }

            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            byte targetSlot = ctx.P.Byte();
            if (targetSlot > 0x13) { u.Disconnect(0x8b); return; }
            ushort len = ctx.P.UInt16();
            if (len > 200) { u.Disconnect(0x8c); return; }

            // FUN_00405cc0: field+8==2 && sender state==4 && target state==4
            if (field.State != 2 || rec.State != 4) return;
            var target = field.RecAt(targetSlot);
            if (target == null || target.State != 4 || target.Session == null) return;
            if (ctx.P.Remaining < len) return;
            byte[] blob = ctx.P.Bytes(len);

            // SO ao target: msgType 0x4b body=[senderSlot][u16 len][blob]
            using var w = new PacketWriter();
            w.WriteByte((byte)rec.Slot).WriteWord(len).WriteBytes(blob);
            target.Session.SendMessage(0x4b, w.ToArray());
            Log.Debug("field", "[{0}] action -> slot {1} len={2}", u.Slot, targetSlot, len);
        }

        // ===================== 0x4d  FUN_00424980 -> FUN_00405d70  rotacao / facing 2 eixos =======
        // Gate: InField && FieldSecondary (disc 0x8d) ; Status==3 (disc 0x8e).
        // parse: short x=param_3[0], short y=param_3[1]. FUN_00405d70(field, x, y): requer field+8==2
        //   && field+0x2b4==1 (Playing). grava +0x2c4=x, +0x2c6=y. Se x==0 -> +0x2bf=0, +0x2c1++ (Wins1),
        //   kills time1. Senao se y==0 -> +0x2c0++ (Wins0), +0x2bf=1, kills time0. Em ambos: +0x2bd=2,
        //   +0x2b4=2 (RoundEnd), +0x2b8=now+15000 e broadcast 0x4a a cada state==4 len=6
        //   body=[cause=2][winnerSide][wins0][wins1].
        internal static void Op_0x4D_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x8d); return; }
            if (u.Status != UserStatus.InField) { u.Disconnect(0x8e); return; }

            short x = ctx.P.Int16();
            short y = ctx.P.Int16();
            var rec = ReconRec(ctx, out var field);
            if (rec == null || field == null) return;

            // FUN_00405d70: field+8==2 && field+0x2b4==1
            if (field.State != 2 || field.Phase != MatchPhase.Playing) return;

            // +0x2c4/+0x2c6 sao do FIELD-record (alvo/objetivo do round); guardados na sessao do host.
            u.FieldPairA = x; // field+0x2c4
            u.FieldPairB = y; // field+0x2c6
            if (x == 0)
            {
                field.WinnerSide = 0;       // +0x2bf
                field.Wins1++;              // +0x2c1
                foreach (var r in field.Slots) if (r.Playing && r.Team == 1) r.Score++;
            }
            else if (y == 0)
            {
                field.Wins0++;              // +0x2c0
                field.WinnerSide = 1;       // +0x2bf
                foreach (var r in field.Slots) if (r.Playing && r.Team == 0) r.Score++;
            }
            else
            {
                return; // x!=0 && y!=0 -> LAB_00405ed6 (nada)
            }

            field.LastRoundWinner = 2;          // +0x2bd = 2 (cause)
            field.Phase = MatchPhase.RoundEnd;  // +0x2b4 = 2
            field.DeadlineMs = Environment.TickCount64 + 15000;

            byte[] body = { 2, field.WinnerSide, field.Wins0, field.Wins1 };
            field.BroadcastFieldPlaying(0x4a, body);
            Log.Ok("field", "[{0}] facing x={1} y={2} (w0={3} w1={4})", u.Slot, x, y, field.Wins0, field.Wins1);
        }
    }
}
