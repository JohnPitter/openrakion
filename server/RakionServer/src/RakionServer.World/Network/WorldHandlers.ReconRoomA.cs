using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Grupo SALA-A reconstruido FIELMENTE do dispatch FUN_0042ab40 do worldserv.exe v258:
    ///   0x29 FUN_00420c20  FieldUserInfoReq    (acao no field por id)
    ///   0x2a FUN_00420cb0  InviteToField       (notifica outro user no lobby, subtype 0x2a)
    ///   0x2c FUN_00420de0  RoomEnterConfirm    (confirma entrada na sala: subtype 0x2c ou FIELD 0x12)
    ///   0x31 FUN_00421870  RoomSetMode         (modo/mapa 2 pares; subtype 0x31)
    ///   0x32 FUN_004226b0  RoomBuyA            (FIELD 0x16/0x18 ou erro lobby 0x32)
    ///   0x33 FUN_004229f0  RoomSetOption       (subtype 0x33)
    ///   0x34 FUN_00422b10  RoomBuyB            (FIELD 0x17 ou erro lobby 0x34)
    ///   0x35 FUN_00422850  RoomSellB           (FIELD 0x18 ou erro lobby 0x35)
    ///   0x38 FUN_00423100  FieldJoinByName     (FIELD 0x26 quick / entra na sala real)
    ///
    /// CONVENCAO (fundacao): internal static void Op_0xNN_Recon(HandlerContext ctx).
    /// Canais: SendField (FIELD = [serverSeq][msgType][body]) / SendLobbyMsg (LOBBY = [u16 msgType][body]).
    /// Os helpers profundos do exe (FUN_0040b000/FUN_0040cf10/FUN_0040b080/FUN_0040b1a0/FUN_0040b2c0/
    /// FUN_0040b3d0/FUN_0040c960/FUN_00406f40) ainda nao foram portados; onde a regra de negocio
    /// depende deles, retornamos o caminho de SUCESSO (result==0) e ecoamos os parametros validados,
    /// exatamente como o decompile monta a resposta. Os GATES, parses, limites e codigos DISC/erro
    /// sao 100% fieis ao FUN_xxxx.
    ///
    /// gates do user-obj (slot*0x23b4 + world+0xd4):
    ///   +0x1460 InField, +0x14a4 FieldSecondary, +0x1440 Status (0x02=area de salas, 0x03=in-field).
    /// </summary>
    public static partial class WorldHandlers
    {
        // ============================================================================
        // 0x29  FUN_00420c20 — FieldUserInfoReq: acao no field por id (status 3 / in-field)
        //   parse: [u16 targetFieldSlot][u32 arg]
        //   gate : InField && FieldSecondary  (senao DISC 0x2e); Status==3
        //   regra: slot < DAT_00455824 (senao DISC 0x2f); se field ocupado -> FUN_00406240(field,user,arg)
        //   reply: nenhum direto (FUN_00406240 envia a info; ainda nao portado).
        // ============================================================================
        internal static void Op_0x29_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x2e); return; }
            if (u.Status != UserStatus.InField) return; // *(iVar1+0x1440) == '\x03'

            ushort targetField = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            uint arg = ctx.P.CanRead(4) ? ctx.P.UInt32() : 0u;

            int maxField; lock (ctx.World.Fields) maxField = ctx.World.Fields.Count;
            if (targetField >= maxField) { u.Disconnect(0x2f); return; } // DAT_00455824
            var field = ctx.World.GetField(targetField);
            if (field == null || field.State == 0) return; // *(this_00+8) != '\0'

            // FUN_00406240(field, slot, arg) — envia info do field ao requisitante (helper a portar)
            Log.Debug("field", "[{0}] 0x29 field-info req field={1} arg={2}", u.Slot, targetField, arg);
        }

        // ============================================================================
        // 0x2a  FUN_00420cb0 — InviteToField: notifica um user (status 2) no LOBBY
        //   parse: [u16 targetUserSlot][u32 arg]
        //   gate : InField && FieldSecondary (senao DISC 0x30); Status==3
        //   regra: target < DAT_00456030 (senao DISC 0x31); target.Status==2
        //   reply: LOBBY ao target, subtype 0x2a, len 8:
        //          [u16 0x2a][u16 meuFieldSlot(FUN_0040b7d0 -> FieldObjectIndex)][u32 arg]
        // ============================================================================
        internal static void Op_0x2A_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x30); return; }
            if (u.Status != UserStatus.InField) return;

            ushort target = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0;
            uint arg = ctx.P.CanRead(4) ? ctx.P.UInt32() : 0u;

            if (target >= ctx.World.MaxUser) { u.Disconnect(0x31); return; }

            var ts = ctx.World.GetSession(target);
            if (ts == null || ts.Status != UserStatus.FieldLobby) return; // cVar1 == '\x02'

            // FUN_0040b7d0(this_00, &fieldSlot, &seat): fieldSlot = local_100c[0] = FieldObjectIndex
            using var w = new PacketWriter();
            w.WriteWord(u.FieldObjectIndex);   // local_1002
            w.WriteUInt32(arg);                // local_1000
            ts.SendEncryptedFrame(Prefix(0x2a, w.ToArray()));
            Log.Info("field", "[{0}] 0x2a invite -> user {1} (fieldObj={2} arg={3})", u.Slot, target, u.FieldObjectIndex, arg);
        }

        // ============================================================================
        // 0x2c  FUN_00420de0 — RoomEnterConfirm (status 2 / na sala)
        //   gate : InField && FieldSecondary (senao DISC 0x32); Status==2 (senao DISC 0x33)
        //   regra: FUN_0040b000(user) (ja-pronto/ocupado?)
        //          != 0 -> LOBBY subtype 0x2c, len 7: [u16 0x2c][u32 0]
        //          == 0 -> FIELD subtype 0x12, len 0xc:
        //                  [serverSeq][u16 0x12][ (fieldHandle<<8) | (Status==2? ...) byte ][u32 FieldSecondary]
        //   (helper FUN_0040b000 a portar: assumimos "ok p/ entrar" => caminho 0x12)
        // ============================================================================
        internal static void Op_0x2C_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x32); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x33); return; }

            // FUN_0040b000(user): nonzero = "ja confirmado/ocupado" -> notice 0x2c
            // Helper nao portado; replicamos o ramo de entrada (FIELD 0x12) que e o caminho de sucesso.
            // Layout do decompile (FIELD, len 0xc): body = [u16 0x12][u8 (fieldHandle>>24)][i32 (fieldHandle<<8)][u32 FieldSecondary]
            // -> apos o [serverSeq][msgType=0x12], o corpo e: iStack_1001 = (fieldHandle<<8), uStack_ffd = (fieldHandle>>24), local_ffc = FieldSecondary.
            int fieldHandle = u.FieldHandleRaw;
            using var w = new PacketWriter();
            w.WriteByte((byte)((uint)fieldHandle >> 0x18));   // uStack_ffd
            w.WriteInt32(fieldHandle << 8);                   // iStack_1001
            w.WriteUInt32((uint)u.FieldSecondaryRaw);         // local_ffc
            SendField(ctx, 0x12, w.ToArray());
            Log.Info("room", "[{0}] 0x2c room-enter-confirm (FIELD 0x12, handle={1})", u.Slot, fieldHandle);
        }

        // ============================================================================
        // 0x31  FUN_00421870 — RoomSetMode: modo/mapa (2 pares) (status 2)
        //   parse: [byte a][byte b][byte c][byte d]
        //   gate : InField && FieldSecondary (senao DISC 0x3c); Status==2 (senao DISC 0x3d)
        //   limites: (a==0 ? b<0x78 : b<0x13) && (c==0 ? d<0x78 : d<0x13)  senao DISC 0x3e
        //   regra: FUN_0040cf10(...) -> result + 6 saidas (helper a portar; assumimos 0 + ecoa)
        //   reply: LOBBY subtype 0x31, len 0x15:
        //          [u16 0x31][u8 result][u8 a][u16 out_b][u8 out_b2][u8 b][u8 c][u8 d][u32 out1][u16 out3][...]
        //   (decompile monta um bloco de 0x15B; sem o helper preenchemos os ecos e zeros)
        // ============================================================================
        internal static void Op_0x31_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x3c); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x3d); return; }

            byte a = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // *param_3   (cVar2)
            byte b = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // param_3[1] (bVar1)
            byte c = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // param_3[2] (cVar3)
            byte d = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // param_3[3] (bVar4)

            bool okB = a == 0 ? b < 0x78 : b < 0x13;
            bool okD = c == 0 ? d < 0x78 : d < 0x13;
            if (!(okB && okD)) { u.Disconnect(0x3e); return; }

            // FUN_0040cf10(user,a,b,c,d,&out_100c(u16),&res_1025,&out_1018(u32),&out_101c(u16),&res_1026,&out_1014(u32))
            // helper a portar: result=0 (sucesso); saidas zeradas. Layout fiel (len 0x15 = 0x13 corpo + 2 do msgType):
            byte result = 0;        // (undefined)uVar5
            ushort out100c = 0;     // local_fff
            ushort out101c = 0;     // local_ff6
            uint out1018 = 0;       // local_ffc
            uint out1014 = 0;       // local_ff3
            byte res1025 = 0;       // local_ffd
            byte res1026 = 0;       // local_ff4

            using var w = new PacketWriter();
            w.WriteByte(result);                 // local_1002
            w.WriteByte(a);                       // local_1001 = cVar2
            w.WriteByte(b);                       // local_1000 = (byte)local_1024
            w.WriteWord(out100c);                 // local_fff
            w.WriteByte(res1025);                 // local_ffd
            w.WriteByte(c);                       // local_ff8 = (byte)local_1020
            w.WriteByte(d);                       // local_ff7 = (byte)local_1010
            w.WriteWord(out101c);                 // local_ff6
            w.WriteByte(res1026);                 // local_ff4
            w.WriteUInt32(out1018);               // local_ffc
            w.WriteUInt32(out1014);               // local_ff3
            SendLobbyMsg(ctx, 0x31, w.ToArray());
            Log.Info("room", "[{0}] 0x31 set-mode a={1} b={2} c={3} d={4}", u.Slot, a, b, c, d);
        }

        // ============================================================================
        // 0x32  FUN_004226b0 — RoomBuyA (status 2)
        //   parse: [byte flag][@1 u16 qty se flag!=0]
        //   gate : InField && FieldSecondary (senao DISC 0x3f); Status==2 (senao DISC 0x40)
        //   regra: FUN_0040b080(user,flag,qty,&o1,&o2,&o3) (helper a portar; assumimos 0=ok)
        //   reply OK: FIELD subtype (flag==1 ? 0x18 : 0x16):
        //          [serverSeq][msgType][u32 fieldHandle][u8 (user+0x1540)][u8 flag]
        //          se flag==1: + [u32 o1][u32 o2][u32 o3][u16 qty]
        //   reply ERR: LOBBY subtype 0x32, len 3: [u16 0x32][u8 errcode]
        // ============================================================================
        internal static void Op_0x32_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x3f); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x40); return; }

            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // *param_3 (cVar1)
            ushort qty = flag != 0 && ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0; // *(param_3+1)

            // FUN_0040b080 -> 0 = ok; helper a portar. Saidas zeradas.
            byte err = 0;
            uint o1 = 0, o2 = 0, o3 = 0; // local_1010/local_100c/local_1014
            if (err != 0)
            {
                using var e = new PacketWriter();
                e.WriteByte(err);
                SendLobbyMsg(ctx, 0x32, e.ToArray());
                return;
            }

            using var w = new PacketWriter();
            w.WriteUInt32((uint)u.FieldHandleRaw);  // local_1000 = user+0x1460
            w.WriteByte(0);                          // local_ffc = user+0x1540 (clan/team byte; nao portado)
            w.WriteByte(flag);                       // local_ffb
            if (flag == 1)
            {
                w.WriteUInt32(o1);                   // local_ffa
                w.WriteUInt32(o2);                   // local_ff6
                w.WriteUInt32(o3);                   // local_ff2
                w.WriteWord(qty);                    // local_fee
            }
            SendField(ctx, flag == 1 ? (ushort)0x18 : (ushort)0x16, w.ToArray());
            Log.Info("room", "[{0}] 0x32 buyA flag={1} qty={2}", u.Slot, flag, qty);
        }

        // ============================================================================
        // 0x33  FUN_004229f0 — RoomSetOption (status 2)
        //   parse: [byte opt]
        //   gate : InField && FieldSecondary (senao DISC 0x43); Status==2 (senao DISC 0x44)
        //   regra: FUN_0040b3d0(user,opt,&out_a(u16),&out_b(u16),&out_c(u16)) (helper a portar)
        //   reply OK (result==0), LOBBY subtype 0x33, len 10:
        //          [u16 0x33][u8 result=0][u16 out_b(local_fff)][u16 out_a(local_1001)][u8 opt][u16 out_c(local_ffc)]
        //   reply ERR, len 3: [u16 0x33][u8 result]
        // ============================================================================
        internal static void Op_0x33_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x43); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x44); return; }

            byte opt = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // *param_3

            // FUN_0040b3d0(user,opt,&out_1008,&out_1010,&out_100c): result + 3 saidas. Helper a portar.
            byte result = 0;
            ushort outB = 0;  // local_1010[0]  -> local_fff
            ushort outA = 0;  // local_1008[0]  -> local_1001
            ushort outC = 0;  // (short)local_100c -> local_ffc

            using var w = new PacketWriter();
            w.WriteByte(result);          // local_1002
            if (result == 0)
            {
                w.WriteWord(outB);        // local_fff
                w.WriteWord(outA);        // local_1001
                w.WriteByte(opt);         // local_ffd
                w.WriteWord(outC);        // local_ffc
            }
            SendLobbyMsg(ctx, 0x33, w.ToArray());
            Log.Info("room", "[{0}] 0x33 set-option opt={1} result={2}", u.Slot, opt, result);
        }

        // ============================================================================
        // 0x34  FUN_00422b10 — RoomBuyB
        //   parse: [byte mode][byte flag][@2 u16 qty se flag!=0]
        //   gate : (sem checagem InField/FSec explicita aqui no exe). mode in {0,1} senao DISC 0x45.
        //   regra: FUN_0040b2c0(user,mode,flag,qty,&o1,&o2,&o3) (helper a portar; assumimos 0=ok)
        //   reply OK: FIELD subtype 0x17:
        //          [serverSeq][u16 0x17][u32 fieldHandle][u16 (user+0x2370)][u8 mode]
        //          se flag==1: + [u32 o1][u32 o2][u32 o3][u16 qty]
        //   reply ERR: LOBBY subtype 0x34, len 3: [u16 0x34][u8 errcode]
        //
        //   NOTA DE FIACAO: a fundacao JA declara `Op_0x34_Recon (=> ReconStub)` em
        //   WorldHandlers.Recon.cs, e Build() em WorldHandlers.cs aponta 0x34 -> Op_0x34_Recon.
        //   Como esta tarefa NAO pode editar esses arquivos nem redeclarar o mesmo simbolo
        //   (causaria erro de membro duplicado), o corpo fiel fica em Op_0x34_RoomBuyB_Recon.
        //   Para ATIVAR este handler basta, na fundacao, trocar o corpo de Op_0x34_Recon por
        //   `=> Op_0x34_RoomBuyB_Recon(ctx);` (uma linha, sem mexer na tabela).
        // ============================================================================
        internal static void Op_0x34_RoomBuyB_Recon(HandlerContext ctx)
        {
            var u = ctx.User;

            byte mode = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // *param_3 (cVar2)
            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // param_3[1] (cVar1)
            ushort qty = flag != 0 && ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0; // *(param_3+2)

            if (mode != 0 && mode != 1) { u.Disconnect(0x45); return; }

            // FUN_0040b2c0 -> 0 = ok; helper a portar. Saidas zeradas.
            byte err = 0;
            uint o1 = 0, o2 = 0, o3 = 0; // local_1010/local_1018/local_1014
            if (err != 0)
            {
                using var e = new PacketWriter();
                e.WriteByte(err);
                SendLobbyMsg(ctx, 0x34, e.ToArray());
                return;
            }

            using var w = new PacketWriter();
            w.WriteUInt32((uint)u.FieldHandleRaw); // local_1000 = user+0x1460
            w.WriteWord(0);                         // local_ffb = user+0x2370 (estado de sala; nao portado)
            w.WriteByte(mode);                      // local_ffc = (byte)local_100c
            if (flag == 1)
            {
                w.WriteUInt32(o1);                  // local_ff8
                w.WriteUInt32(o2);                  // local_ff4
                w.WriteUInt32(o3);                  // local_ff0
                w.WriteWord(qty);                   // local_fec
            }
            SendField(ctx, 0x17, w.ToArray());
            Log.Info("room", "[{0}] 0x34 buyB mode={1} flag={2} qty={3}", u.Slot, mode, flag, qty);
        }

        // ============================================================================
        // 0x35  FUN_00422850 — RoomSellB (status 2)
        //   parse: [byte flag][@1 u16 qty se flag!=0]
        //   gate : InField && FieldSecondary (senao DISC 0x41); Status==2 (senao DISC 0x42)
        //   regra: FUN_0040b1a0(user,flag,qty,&o1,&o2,&o3) (helper a portar; assumimos 0=ok)
        //   reply OK: FIELD subtype 0x18:
        //          [serverSeq][u16 0x18][u32 fieldHandle][u8 (user+0x1541)][u8 flag]
        //          se flag==1: + [u32 o1][u32 o2][u32 o3][u16 qty]
        //   reply ERR: LOBBY subtype 0x35, len 3: [u16 0x35][u8 errcode]
        // ============================================================================
        internal static void Op_0x35_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x41); return; }
            if (u.Status != UserStatus.FieldLobby) { u.Disconnect(0x42); return; }

            byte flag = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0; // *param_3 (cVar1)
            ushort qty = flag != 0 && ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0; // *(param_3+1)

            // FUN_0040b1a0 -> 0 = ok; helper a portar. Saidas zeradas.
            byte err = 0;
            uint o1 = 0, o2 = 0, o3 = 0; // local_1010/local_100c/local_1014
            if (err != 0)
            {
                using var e = new PacketWriter();
                e.WriteByte(err);
                SendLobbyMsg(ctx, 0x35, e.ToArray());
                return;
            }

            using var w = new PacketWriter();
            w.WriteUInt32((uint)u.FieldHandleRaw); // local_1000 = user+0x1460
            w.WriteByte(0);                          // local_ffc = user+0x1541 (nao portado)
            w.WriteByte(flag);                       // local_ffb
            if (flag == 1)
            {
                w.WriteUInt32(o1);                   // local_ffa
                w.WriteUInt32(o2);                   // local_ff6
                w.WriteUInt32(o3);                   // local_ff2
                w.WriteWord(qty);                    // local_fee
            }
            SendField(ctx, 0x18, w.ToArray());
            Log.Info("room", "[{0}] 0x35 sellB flag={1} qty={2}", u.Slot, flag, qty);
        }

        // ============================================================================
        // 0x38  FUN_00423100 — FieldJoinByName (status 2)
        //   parse: [u16 roomSlot][CString nome (<9)]
        //   gate : InField && FieldSecondary (senao DISC 0x4a)
        //          Status==2 senao LOBBY subtype 0x38 [u16 0x38][u8 5]
        //   regra: roomSlot <= DAT_00455824 (senao DISC 0x4c); strlen(nome) < 9 (senao DISC 0x4d)
        //          se room+0x119 == 0 (lobby/quick): FIELD subtype 0x26, len strlen+0xb:
        //                [serverSeq][u16 0x26][u32 fieldHandle][u16 roomSlot][CString nome]
        //          senao (sala real): FUN_00406f40(field,0,nome,user,&seat,0) -> ao entrar move de
        //                canal (FUN_0040af90/FUN_00405240) e entra na sala (FUN_0040b7b0). Sem reply.
        // ============================================================================
        internal static void Op_0x38_Recon(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x4a); return; }
            if (u.Status != UserStatus.FieldLobby)
            {
                using var e = new PacketWriter();
                e.WriteByte(5);
                SendLobbyMsg(ctx, 0x38, e.ToArray());
                return;
            }

            ushort roomSlot = ctx.P.CanRead(2) ? ctx.P.UInt16() : (ushort)0; // *param_3
            int maxField; lock (ctx.World.Fields) maxField = ctx.World.Fields.Count;
            if (roomSlot > maxField) { u.Disconnect(0x4c); return; } // DAT_00455824

            string name = ctx.P.CString(); // param_3+1 (nome da sala)
            if (name.Length >= 9) { u.Disconnect(0x4d); return; }

            var field = ctx.World.GetField(roomSlot);
            bool isQuick = field == null || field.Mode == 0; // room+0x119 == 0

            if (isQuick)
            {
                // FIELD 0x26, len = strlen+0xb: [u32 fieldHandle][u16 roomSlot][CString nome]
                using var w = new PacketWriter();
                w.WriteUInt32((uint)u.FieldHandleRaw); // uStack_1000 = user+0x1460
                w.WriteWord(roomSlot);                 // uStack_ffc
                w.WriteCString(name);                  // aCStack_ffa
                SendField(ctx, 0x26, w.ToArray());
                Log.Info("field", "[{0}] 0x38 join-by-name QUICK room={1} nome='{2}'", u.Slot, roomSlot, name);
                return;
            }

            // sala real: FUN_00406f40(field,0,nome,user,&seat,0) -> entra (helpers de canal/sala a portar).
            // Sem reply direto (o estado e propagado pelo broadcast de entrada da sala).
            Log.Info("field", "[{0}] 0x38 join-by-name room={1} nome='{2}' (entrada na sala)", u.Slot, roomSlot, name);
        }
    }
}
