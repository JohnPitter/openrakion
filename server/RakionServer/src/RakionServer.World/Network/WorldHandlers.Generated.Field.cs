using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_FieldGameStart(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_0041fef0: guard via this_00[0x518]/[0x529] (= user+0x1460 InField / user+0x14a4 FieldSecondary).
                    // this_00[0x518]==0 (nao esta no field) OU this_00[0x529]!=0 (ja iniciou/secundario) -> erro 0x1d.
                    if (!u.InField || u.FieldSecondary)
                    {
                        // FUN_0041eb20(this, slot, 0x1d, '\0', 1): erro NAO-fatal (cVar5=0).
                        using var we = new PacketWriter();
                        we.WriteWord(0x1d);
                        we.WriteByte(0);
                        u.SendLobby(we.ToArray());
                        Log.Debug($"[FieldGameStart] slot={u.Slot} rejeitado: estado invalido (InField={u.InField} Secondary={u.FieldSecondary})");
                        return;
                    }

                    // *param_3 = id-alvo (int). Se 0 -> erro 0x1e com cVar5='\x01'.
                    int target = ctx.P.Int32();
                    if (target == 0)
                    {
                        using var we = new PacketWriter();
                        we.WriteWord(0x1e);
                        we.WriteByte(1);
                        u.SendLobby(we.ToArray());
                        Log.Debug($"[FieldGameStart] slot={u.Slot} rejeitado: target id == 0");
                        return;
                    }

                    // Varredura dos 6 slots de membro do usuario (this_00 + idx*0xd8, idx<6), procurando *piVar1 == target.
                    // Ao achar: FUN_0040be30 (carrega stats/char do membro), FUN_0040d3f0 (recalcula derivados a partir
                    // de piVar1+0x356 e piVar1[0xd6]), FUN_0040ac30 (faz o spawn/broadcast in-game com os stats) e copia
                    // piVar1+0xd7 -> user+0x2368 (ready-flag). Esses helpers nao tem corpo neste slice; modelados como
                    // 'membro encontrado -> sucesso' contra a lista de jogadores do field.
                    byte resultCode = 0;       // local_100a
                    bool found = false;
                    var field = ctx.World.GetField(u.FieldId);
                    if (field != null)
                    {
                        foreach (var m in field.Players)
                        {
                            if (m.Slot == target) { found = true; break; }
                        }
                    }
                    // iVar2 == 6 (nenhum dos 6 slots casou) -> resultCode = 2.
                    if (!found) { resultCode = 2; }

                    // FUN_004038e0(handler, slot, 3, &local_1004): resposta lobby subtype 0x14 + byte resultCode (len 3).
                    using var w = new PacketWriter();
                    w.WriteWord(0x14);         // local_1004 subtype
                    w.WriteByte(resultCode);   // local_1002
                    u.SendLobby(w.ToArray());
                    Log.Info($"[FieldGameStart] slot={u.Slot} target={target} result={resultCode} (found={found})");

                    // FUN_0041b8b0(this, slot): notificacao pos-start ao field. Sem corpo neste slice; modelado como
                    // broadcast informando que este jogador entrou em jogo, para os demais membros do field.
                    if (field != null && resultCode == 0)
                    {
                        using var bw = new PacketWriter();
                        bw.WriteWord(0x14);
                        bw.WriteByte((byte)u.Slot);
                        field.Broadcast(bw.ToArray(), u);
                    }
                }


        private static void Op_FieldJoinById(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField)
            {
                u.Disconnect(0x2b);
                return;
            }

            byte type = ctx.P.Byte();
            ushort arg = 0;
            if (type != 0)
                arg = ctx.P.UInt16();

            uint fieldSecondary = (uint)(u.FieldSecondary ? 1 : 0); // user+0x14a4 (local_1008)

            // TODO FUN_0040bd80(this_00, type, arg, &a, &b, &c): resolves the target field/room
            // and fills a/b/c (u32) when type==1. Helper not in this body. Modeled as success.
            byte status = 0;
            uint a = 0, b = 0, c = 0;

            // efeito de FUN_0040bd80 + join: vincula o usuario ao field alvo (type==1 -> arg = fieldId)
            if (type == 0x01)
            {
                var jf = ctx.World.GetField(arg);
                if (jf == null || jf.Count >= jf.MaxPlayers) status = 1;
                else { jf.Add(u); u.FieldId = jf.Id; a = (uint)(jf.Master?.Game?.UserId ?? 0); }
            }

            if (status == 0)
            {
                // SUCCESS -> plain channel (SendMessage), subtype 0x10.
                using var w = new PacketWriter();
                w.WriteUInt32(fieldSecondary); // local_1000
                w.WriteByte(type);             // local_ffc
                if (type == 0x01)
                {
                    w.WriteUInt32(a);          // local_ffb
                    w.WriteUInt32(b);          // local_ff7
                    w.WriteUInt32(c);          // local_ff3
                    w.WriteWord(arg);          // local_fef
                }
                u.SendMessage(0x10, w.ToArray());
            }
            else
            {
                // FAIL -> lobby channel (SendLobby), subtype 0x1b + status byte.
                using var w = new PacketWriter();
                w.WriteWord(0x1b);
                w.WriteByte(status);
                u.SendLobby(w.ToArray());
            }
        }

        private static void Op_FieldJoinByName(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField)
            {
                u.Disconnect(0x2c);
                return;
            }

            uint fieldSecondary = (uint)(u.FieldSecondary ? 1 : 0); // user+0x14a4 (local_1018)

            string name = ctx.P.CString();
            byte type = ctx.P.Byte();
            ushort arg = 0;
            if (type != 0)
                arg = ctx.P.UInt16();

            // TODO FUN_0040bd80(this_00, type, arg, &a, &b, &c): resolves the named field/room
            // and fills a/b/c (u32) when type==1. Helper not in this body. Modeled as success.
            byte status = 0;
            uint a = 0, b = 0, c = 0;

            if (status != 0)
            {
                // FAIL -> lobby channel, subtype 0x1c + status byte.
                using var fw = new PacketWriter();
                fw.WriteWord(0x1c);
                fw.WriteByte(status);
                u.SendLobby(fw.ToArray());
                return;
            }

            // SUCCESS -> plain channel (SendMessage), subtype 0x11.
            using var w = new PacketWriter();
            w.WriteUInt32(fieldSecondary); // uStack_1000
            w.WriteCString(name);          // aCStack_ffc (name + null)
            w.WriteByte(type);             // cVar1
            if (type == 0x01)
            {
                w.WriteUInt32(a);          // uStack_1020
                w.WriteUInt32(b);          // uStack_1024
                w.WriteUInt32(c);          // uStack_1028
                w.WriteWord(arg);          // uVar2
            }
            u.SendMessage(0x11, w.ToArray());
        }

        private static void Op_FieldPlayerList(HandlerContext ctx)
        {
            var u = ctx.User;
            var world = ctx.World;
            // Guards na ordem do binario
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x46); return; }
            if (u.Status != 0x02) { u.Disconnect(0x47); return; }

            // payload: [byte max][u16 startIdx][byte b11][byte b3][byte b2][byte b4][byte b5][byte b6][byte b7]
            byte maxCount = ctx.P.Byte();          // bVar1 = *param_3
            if (maxCount > 10) { u.Disconnect(0x48); return; }
            ushort startIdx = ctx.P.UInt16();      // uVar14 = *(param_3+1)
            if (startIdx >= world.Fields.Count /* DAT_00455824 (num fields) */) { u.Disconnect(0x49); return; }
            byte b11 = ctx.P.Byte();  // param_3[3]
            byte b3  = ctx.P.Byte();  // param_3[4]
            byte b2  = ctx.P.Byte();  // param_3[5]
            byte b4  = ctx.P.Byte();  // param_3[6]
            byte b5  = ctx.P.Byte();  // param_3[7]
            byte b6  = ctx.P.Byte();  // param_3[8]
            byte b7  = ctx.P.Byte();  // param_3[9]

            // Varredura dos fields a partir de startIdx, ate maxCount entradas, serializando cada uma
            // (FUN_0041b830/FUN_00405920/FUN_00405790). Cada entrada = [u16 fieldIdx][Field.SerializeListEntry()].
            // (filtros de FUN_0040b6c0 nao aplicados — lista todos; refinar com b2..b7 quando necessario.)
            byte matched = 0;          // local_102d
            byte[] entries;
            using (var le = new PacketWriter())
            {
                var fields = world.Fields;
                for (int i = startIdx; i < fields.Count && matched < maxCount; i++)
                {
                    var fld = fields[i];
                    le.WriteWord(fld.Id);                 // u16 fieldIdx
                    le.WriteBytes(fld.SerializeListEntry());
                    matched++;
                }
                entries = le.ToArray();
            }

            // Resposta sempre via SendLobby subtype 0x36:
            //   local_1004._0_2_ = 0x36 (subtype, off0)
            //   local_1004._2_1_ = matched (byte, off2) — contador
            //   off3+ : entries (cada: u16 fieldIdx + payload do field via FUN_00405790)
            using var w = new PacketWriter();
            w.WriteWord(0x36);        // subtype
            w.WriteByte(matched);     // contador de entradas
            if (matched != 0) w.WriteBytes(entries);
            u.SendLobby(w.ToArray());
        }

        private static void Op_FieldLeaveGame(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guards na ordem do binario
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x50); return; }
            if (u.Status != 0x03) { u.Disconnect(0x51); return; } // status '\x03' = field/in-game

            // Sem parse de payload e sem struct de resposta.
            // FUN_0040b7d0(user, out fieldIdx(u16), out slot(byte)) — le posicao do user no field.
            ushort fieldIdx = 0;   // param_1 (reaproveitado pela FUN_0040b7d0)
            byte slot = 0;         // local_4 (byte)
            // TODO FUN_0040b7d0(user, out fieldIdx, out slot);
            // TODO FUN_004091e0(field[fieldIdx], slot) — remove/marca saida do jogador no field.
            // TODO FUN_0041b8b0(this, originalSlot) — notifica/atualiza estado pos-saida.
            // remove o jogador do field atual (FUN_004091e0); libera o field se vazio
            _ = fieldIdx; _ = slot;
            ctx.World.LeaveField(u);
        }

        private static void Op_FieldGameReady(HandlerContext ctx)
                {
                    // FUN_00423b70 (opcode 0x3e). user = this+0xd4 + slot*0x23b4.
                    var u = ctx.User;

                    // Guard: InField && FieldSecondary. Senao => Disconnect(0x62).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x62); return; }
                    // Guard: Status == '\x03'. Senao => Disconnect(99 = 0x63).
                    if (u.Status != 0x03) { u.Disconnect(0x63); return; }

                    // FUN_0040b7d0(user, &fieldIndex, &fieldSlot)
                    int fieldIndex = u.FieldId;       // *(user+0x14a0)
                    byte fieldSlot = (byte)u.Slot;    // *(user+0x14a2)

                    // Sem payload neste opcode.

                    // FUN_004075a0(field = this+0xe4 + fieldIndex*0x3c0, fieldSlot, 0):
                    // logica de troca de time/slot (FUN_004075a0 @ 0x4075a0). Estrutura por-slot do field
                    // (field+0x124+i*0x14: u16 userSlot; +0x126 occupied; +0x127 locked) e contadores de
                    // time (+0x116/+0x117) nao estao modelados no dominio atual -> efeito modelado:
                    // broadcast de "ready" (subtype 0x3e, payload local_1004=0x3e) aos jogadores do field.
                    var field = ctx.World.GetField(fieldIndex);
                    if (field == null)
                    {
                        Log.Warn("field", "FieldGameReady: field {0} inexistente (slot {1})", fieldIndex, u.Slot);
                        return;
                    }

                    Log.Info("field", "FieldGameReady: field {0} '{1}' slot {2} pronto", field.Id, field.Name, fieldSlot);

                    using var w = new PacketWriter();
                    w.WriteByte(fieldSlot);   // local_1001 = param_1 (slot de origem)
                    field.Broadcast(WorldHandlers.Prefix(0x3e, w.ToArray()), u);
                }


        private static void Op_FieldGameStart_3f(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: InField e FieldSecondary
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x64); return; } // 100 = 0x64
            // Guard: Status = 0x03 (field)
            if (u.Status != 0x03) { u.Disconnect(0x65); return; }

            // FUN_0040b7d0(user, &fieldIndex, &fieldSlot)
            int fieldIndex = u.FieldId;
            byte fieldSlot = (byte)u.Slot;
            _ = fieldSlot;

            // Guard adicional: SubStatus do usuario precisa ser '4' (0x34) -> caso contrario DISC 0x66.
            // No binario: *(user+0x146c) != '4'. SubStatus mapeia +0x146c.
            if (u.SubStatus != 0x34) { u.Disconnect(0x66); return; }

            // FUN_00405740(field): inicia o jogo no field (state em jogo) e avisa os jogadores.
            var field = ctx.World.GetField(fieldIndex);
            if (field != null)
            {
                field.InGame = true;
                field.State = 2;                 // field+8 = 2 (em jogo)
                using var w = new PacketWriter();
                w.WriteByte((byte)u.Slot);       // quem iniciou
                field.Broadcast(Prefix(0x3f, w.ToArray()));
                Log.Info("field", "field {0} '{1}' iniciou a partida", field.Id, field.Name);
            }
        }

        private static void Op_FieldSetGameMode(HandlerContext ctx)
                {
                    // FUN_00423cc0 (opcode 0x40). user = this+0xd4 + slot*0x23b4; iVar4 = slot*0x23b4.
                    var u = ctx.User;

                    // Guard: InField && FieldSecondary. Senao => Disconnect(0x67).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x67); return; }
                    // Guard: Status == '\x03'. Senao => Disconnect(0x68).
                    if (u.Status != 0x03) { u.Disconnect(0x68); return; }

                    // FUN_0040b7d0(user, &fieldIndex(local_8), &fieldSlot(param_1 low byte)):
                    //   fieldIndex(u16) = *(user+0x14a0); fieldSlot(byte) = *(user+0x14a2).
                    int fieldIndex = u.FieldId;       // local_8[0]
                    byte fieldSlot = (byte)u.Slot;    // (char)param_1

                    var field = ctx.World.GetField(fieldIndex);
                    if (field == null)
                    {
                        Log.Warn("field", "FieldSetGameMode: field {0} inexistente (slot {1})", fieldIndex, u.Slot);
                        return;
                    }

                    // cVar1 = *(this+0xd4 + 0x146c + slot*0x23b4) = SubStatus do usuario.
                    // Se (sub != '4' && sub != '\x01'): exige fieldSlot == *(field+0x121) (master slot),
                    // senao => Disconnect(0x69).
                    byte sub = u.SubStatus;
                    if (sub != 0x34 && sub != 0x01)
                    {
                        byte fieldMasterSlot = (byte)field.MasterSlot; // *(field+0x121)
                        if (fieldSlot != fieldMasterSlot) { u.Disconnect(0x69); return; }
                    }

                    // Payload: 1 byte (bVar2 = *param_3) = novo modo/valor.
                    byte mode = ctx.P.Byte();

                    // FUN_004097c0(field = this+0xe4 + fieldIndex*0x3c0, mode): aplica o modo no field.
                    // Corpo nao presente no decompile -> efeito modelado: grava Field.Mode e propaga.
                    field.Mode = mode;
                    Log.Info("field", "FieldSetGameMode: field {0} '{1}' slot {2} mode=0x{3:x2}", field.Id, field.Name, fieldSlot, mode);

                    using var w = new PacketWriter();
                    w.WriteByte(fieldSlot);   // quem alterou
                    w.WriteByte(mode);        // novo modo
                    field.Broadcast(WorldHandlers.Prefix(0x40, w.ToArray()));
                }


        private static void Op_FieldCreateRoomEntry(HandlerContext ctx)
        {
            var u = ctx.User;
            // local_4 = DAT_00454928 -> guarda de stack/SEH (FUN_00435e83). Sem efeito de protocolo.

            // Guard: InField e FieldSecondary
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x6a); return; }
            // Guard: Status = 0x03 (field)
            if (u.Status != 0x03) { u.Disconnect(0x6b); return; }

            // FUN_0040b7d0(user, &fieldIndex, &fieldFlag): fieldIndex(u16) + um byte (local_11d)
            int fieldIndex = u.FieldId;
            byte fieldFlag = (byte)u.Slot; // local_11d resolvido por FUN_0040b7d0 (byte do usuario)

            var field = ctx.World.GetField(fieldIndex);
            // GetField pode devolver null (FieldId inconsistente); o binario assume um field-object valido
            // neste indice. Sem o guard, field.State/SlotEnabled abaixo lancariam NRE -> disconnect 0x6c.
            if (field == null) { u.Disconnect(0x6c); return; }

            // field+8 == '\x02' -> field ocupado/em-jogo -> DISC 0x6c.
            // TODO field+8 = estado do field; modelado como field.State.
            if (field.State == 0x02) { u.Disconnect(0x6c); return; }
            // field+0x119 == 0 -> slot/sala nao habilitada -> DISC 0x6d.
            if (field.SlotEnabled == 0) { u.Disconnect(0x6d); return; }
            // local_11d != field+0x121 (master slot) -> DISC 0x6e.
            if (fieldFlag != (byte)field.MasterSlot) { u.Disconnect(0x6e); return; }

            // ---- Parse do payload (param_3): 3 strings nul-terminated + bytes/word ----
            // String 1: roomName, len < 0x29 (40), buffer 44; senao DISC 0x6f.
            string roomName = ctx.P.CString();
            if (roomName.Length >= 0x29) { u.Disconnect(0x6f); return; }

            // String 2: password, len < 9; senao DISC 0x70. buffer 12.
            string password = ctx.P.CString();
            if (password.Length >= 9) { u.Disconnect(0x70); return; }

            // String 3: descricao/extra, len < 0xc9 (201); senao DISC 0x71. buffer 204.
            string desc = ctx.P.CString();
            if (desc.Length >= 0xc9) { u.Disconnect(0x71); return; }

            // Campos apos as 3 strings (na ordem do binario):
            byte field0 = ctx.P.Byte();        // param_3[pCVar1+0]  -> arg 'flags' passado a FUN_004077c0
            byte maxOrType = ctx.P.Byte();     // bVar2 = param_3[pCVar1+1] (limite < 0x16)
            ushort mapId = ctx.P.UInt16();     // uVar7 = param_3[pCVar1+2..3]
            byte level = ctx.P.Byte();         // bVar3 = param_3[pCVar1+4]
            byte mode  = ctx.P.Byte();         // bVar4 = param_3[pCVar1+5]

            // Validacao (mesma ordem/codigos do binario). uVar7 = codigo de erro default.
            ushort err;
            if (maxOrType < 0x16)
            {
                // bVar2 < 0x16
                if (mapId < 0x122 || mapId > 0x4ba)
                {
                    err = 0xce;
                }
                else if (mode == 2)
                {
                    // 0x0c < level < 0x1f -> OK
                    if (level > 0x0c && level < 0x1f) { ApplyCreate(); return; }
                    err = 0xcf;
                }
                else if (mode == 3)
                {
                    // 0x13 < level < 0x33 -> OK
                    if (level > 0x13 && level < 0x33) { ApplyCreate(); return; }
                    err = 0xcf;
                }
                else
                {
                    // mode != 0 && mode < 5 -> OK; senao 0x72
                    if (mode != 0 && mode < 5) { ApplyCreate(); return; }
                    err = 0x72;
                }
            }
            else
            {
                err = 0xcd; // bVar2 >= 0x16
            }

            u.Disconnect(err);
            return;

            // LAB_0042406b: caminho de sucesso.
            void ApplyCreate()
            {
                // TODO FUN_004077c0(field, roomName, password, desc, field0, maxOrType, mapId, level, mode):
                // cria/registra a entrada de sala/jogo no field. Sem corpo -> efeito modelado (assume sucesso).
                // FUN_004077c0(field, roomName, password, desc, field0, maxOrType, mapId, level, mode);
                _ = field0; _ = maxOrType; _ = mapId; _ = level; _ = mode;
                _ = roomName; _ = password; _ = desc;
            }
        }

        private static void Op_DisconnectNotText(HandlerContext ctx)
        {
            // FUN_00422270: CWorld::NetworkMessageDisconnect -> loga e desconecta o usuario com reason 1.
            // FUN_0042f280("...") = log; FUN_0041eb20(this,slot,1,'\0',0) = Disconnect(reason=1).
            // O ultimo arg ('\0',0) difere do Disconnect padrao ('\x01',1) usado nos guards, mas a
            // convencao do projeto mapeia ClientSession.Disconnect(reason) para FUN_0041eb20(this,slot,reason,..).
            Log.Info("[RW] ### CWorld::NetworkMessageDisconnect # Disconnect 1 Not Text");
            ctx.User.Disconnect(0x01);
        }
    }
}
