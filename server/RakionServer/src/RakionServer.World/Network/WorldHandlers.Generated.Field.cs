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

        private static void Op_FieldUnitCommand(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424100. userObj = this+0xd4 + slot*0x23b4.
                    // Guard 0x73: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x73); return; }
                    // Guard 0x74: Status (0x1440) == '\x03' (em campo).
                    if (u.Status != 0x03) { u.Disconnect(0x74); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &ownerByte):
                    //   targetIndex = *(u16)(userObj+0x14a0);  ownerByte = *(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex;  // local_8[0]
                    byte ownerByte = u.FieldTargetOwner;      // (byte)param_1 apos b7d0

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0; owner guard: ownerByte == *(byte)(fieldObj+0x121) (MasterSlot)
                    var field = ctx.World.GetField(targetIndex);
                    byte targetOwner = (byte)(field != null && field.MasterSlot >= 0 ? field.MasterSlot : 0);
                    if (ownerByte != targetOwner) { u.Disconnect(0x75); return; }

                    // Payload: [byte cmd]. Faixa valida: cmd<0x14 && cmd!=8 && cmd!=9 && cmd!=0x12 && cmd!=0x13.
                    if (!ctx.P.CanRead(1)) { u.Disconnect(0x76); return; }
                    byte cmd = ctx.P.Byte();
                    if (cmd < 0x14 && cmd != 8 && cmd != 9 && cmd != 0x12 && cmd != 0x13)
                    {
                        // Faixa valida: le tambem [byte value] e aplica FUN_00407910(fieldObj, cmd, value).
                        if (!ctx.P.CanRead(1)) { u.Disconnect(0x76); return; }
                        byte value = ctx.P.Byte();
                        Log.Debug("field", "[{0}] UnitCommand field={1} cmd={2} value={3}", u.Slot, targetIndex, cmd, value);

                        // FUN_00407910: aplica o comando de unidade no field-objeto e replica aos demais
                        // jogadores. Sem corpo no decompile disponivel -> efeito de rede modelado:
                        // relay do comando (opcode 0x42 [cmd][value]) ao field, exceto o emissor.
                        if (field != null)
                        {
                            using var w = new PacketWriter();
                            w.WriteByte(cmd);
                            w.WriteByte(value);
                            field.Broadcast(WorldHandlers.Prefix(0x42, w.ToArray()), u);
                        }
                        return;
                    }
                    // cmd fora da faixa permitida.
                    u.Disconnect(0x76);
                }


        private static void Op_FieldUnitStop(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424210. userObj = this+0xd4 + slot*0x23b4.
                    // Guard 0x77: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x77); return; }
                    // Guard 0x78: Status (0x1440) == '\x03'.
                    if (u.Status != 0x03) { u.Disconnect(0x78); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &ownerByte): targetIndex=*(u16)(userObj+0x14a0), ownerByte=*(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex;  // local_4 (u16)
                    byte ownerByte = u.FieldTargetOwner;      // (byte)param_1

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0; owner guard 0x79 contra *(byte)(fieldObj+0x121)
                    var field = ctx.World.GetField(targetIndex);
                    byte targetOwner = (byte)(field != null && field.MasterSlot >= 0 ? field.MasterSlot : 0);
                    if (ownerByte != targetOwner) { u.Disconnect(0x79); return; }

                    // FUN_004079d0(fieldObj): comando "stop" da unidade. Sem payload e sem resposta direta;
                    // helper indisponivel no decompile -> efeito de rede modelado: relay do opcode 0x43 (stop)
                    // aos demais jogadores do field para que repliquem a parada da unidade.
                    Log.Debug("field", "[{0}] UnitStop field={1}", u.Slot, targetIndex);
                    if (field != null)
                    {
                        field.Broadcast(WorldHandlers.Prefix(0x43, System.Array.Empty<byte>()), u);
                    }
                }


        private static void Op_FieldUnitByteAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_004242c0. userObj = this+0xd4 + slot*0x23b4.
                    // No decompile a ordem dos guards e: primeiro InField/FieldSecondary (DISC 0x7a),
                    // depois Status==3 (DISC 0x7b).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7a); return; }
                    if (u.Status != 0x03) { u.Disconnect(0x7b); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &actionByte): targetIndex=*(u16)(userObj+0x14a0),
                    //   actionByte=*(byte)(userObj+0x14a2). NAO ha checagem de owner (+0x121) aqui.
                    ushort targetIndex = u.FieldTargetIndex; // param_1 (u16)
                    byte actionByte = u.FieldTargetOwner;    // (byte)local_4

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0.
                    // FUN_00407c70(fieldObj, actionByte): aplica a acao de byte. Sem resposta no fluxo;
                    // helper indisponivel -> efeito de rede modelado: relay do opcode 0x45 [actionByte] ao field.
                    Log.Debug("field", "[{0}] UnitByteAction field={1} action={2}", u.Slot, targetIndex, actionByte);
                    var field = ctx.World.GetField(targetIndex);
                    if (field != null)
                    {
                        using var w = new PacketWriter();
                        w.WriteByte(actionByte);
                        field.Broadcast(WorldHandlers.Prefix(0x45, w.ToArray()), u);
                    }
                }


        private static void Op_FieldUnitCharAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_004246e0. userObj = this+0xd4 + slot*0x23b4.
                    // Guard 0x83: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x83); return; }
                    // Guard 0x84: Status (0x1440) == '\x03'.
                    if (u.Status != 0x03) { u.Disconnect(0x84); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &ownerByte): targetIndex=*(u16)(userObj+0x14a0), ownerByte=*(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex; // local_4 (u16)
                    byte ownerByte = u.FieldTargetOwner;     // (byte)param_1

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0; owner guard 0x85 contra *(byte)(fieldObj+0x121) (MasterSlot)
                    var field = ctx.World.GetField(targetIndex);
                    byte targetOwner = (byte)(field != null && field.MasterSlot >= 0 ? field.MasterSlot : 0);
                    if (ownerByte != targetOwner) { u.Disconnect(0x85); return; }

                    // Payload: [byte action].
                    if (!ctx.P.CanRead(1)) { return; }
                    byte action = ctx.P.Byte();

                    // FUN_00405a90(fieldObj, action): comando de personagem no field. Quando o estado do field permite
                    //   (field+8=='2' && field+0x119=='0') e action e 2 ou 3, ajusta contadores de time/lado
                    //   (field+0x2bf/0x2c0/0x2c1), arma um timer de 15s (field+0x2b8) e faz broadcast do opcode 0x4a
                    //   (struct [0x4a][bid][bdir/flag][cnt...]) aos jogadores ativos do field (slot status '\x04').
                    //   Estado interno por-unidade nao modelado -> efeito de rede modelado: relay do opcode 0x4a [action].
                    Log.Debug("field", "[{0}] UnitCharAction field={1} action={2}", u.Slot, targetIndex, action);
                    if (field != null)
                    {
                        using var w = new PacketWriter();
                        w.WriteByte(action);
                        field.Broadcast(WorldHandlers.Prefix(0x4a, w.ToArray()), u);
                    }
                }


        private static void Op_FieldPlayerAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00425990. pvVar2 = user[slot] (this+0xd4, stride 0x23b4).
                    // Guard 1: user+0x1460 && user+0x14a4 != 0, senao DISC 0xaa.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xaa); return; }
                    // Guard 2: user+0x1440 (Status) == 3, senao DISC 0xab.
                    if (u.Status != 0x03) { u.Disconnect(0xab); return; }
                    // Guard 3: user+0x146c (SubStatus) == 1, senao DISC 0xac.
                    if (u.SubStatus != 0x01) { u.Disconnect(0xac); return; }

                    // FUN_0040b7d0(user, out fieldId(local_4,u16), out slotInField(param_1,byte)).
                    int fieldId = u.FieldId;          // local_4 (u16)
                    byte slotInField = (byte)u.Slot;  // (char)param_1

                    var field = ctx.World.GetField(fieldId); // base = *(this+0xe4)+fieldId*0x3c0
                    if (field == null) return;

                    // Gate do exe (le o byte de acao SOMENTE quando entra):
                    //   playerRecord+8 != 2  (jogador nao no estado proibido),
                    //   slotInField == playerRecord+0x121 (slot deste jogador no field),
                    //   acao < 0x12 (faixa valida).
                    // Modelado: playerRecord+8 == FieldPlayInactive(2). O slot do jogador no field e o proprio.
                    if (u.FieldRecordState == 0x02) return;
                    if (slotInField != (byte)u.Slot) return;

                    byte action = ctx.P.Byte();  // bVar1 = *param_3
                    if (action >= 0x12) return;

                    // FUN_00409080(playerRecord, action): aplica a acao do jogador dentro do field
                    // (animacao/estado) e a propaga aos demais membros do field. Sem pacote de resposta direto.
                    // Modelado: repassa a acao aos outros jogadores do field, subtype 0x5b.
                    using var w = new PacketWriter();
                    w.WriteByte(slotInField); // identifica o autor da acao no field
                    w.WriteByte(action);
                    field.Broadcast(WorldHandlers.Prefix(0x5b, w.ToArray()), u);
                }


        private static void Op_FieldTargetCommand(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00425cc0. pvVar2 = user[slot].
                    // Guard 1: user+0x1460 && user+0x14a4 != 0, senao DISC 0xb1.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xb1); return; }
                    // Guard 2: user+0x1440 (Status) == 3, senao DISC 0xb2.
                    if (u.Status != 0x03) { u.Disconnect(0xb2); return; }

                    // FUN_0040b7d0(user, out fieldId(param_1,u16), out slotInField(local_4,byte)).
                    int fieldId = u.FieldId;          // param_1 (u16)
                    byte slotInField = (byte)u.Slot;  // (char)local_4

                    var field = ctx.World.GetField(fieldId); // base = *(this+0xe4)+fieldId*0x3c0
                    if (field == null) return;

                    // Gate do exe: slotInField precisa bater com um dos dois bytes de papel do field
                    //   playerRecord+0x122 (lider time A)  OU  playerRecord+0x123 (lider time B).
                    // So entao o comando e aplicado. Modelado via Field.LeaderSlotA/LeaderSlotB.
                    if (slotInField != field.LeaderSlotA && slotInField != field.LeaderSlotB) return;

                    // Payload (lido dentro do gate, como no binario): [byte arg0][u16 arg1].
                    byte arg0 = ctx.P.Byte();      // *param_3
                    ushort arg1 = ctx.P.UInt16();  // *(u16*)(param_3+1)

                    // FUN_00405ef0(playerRecord, arg0, arg1): aplica o comando de alvo/objetivo do field
                    // (so atua quando record+8==2 && record+0x2b4==1 && record+0x119==4; arg0<10 grava em
                    //  record+0x2c8, senao em record+0x2ca). Sem pacote de resposta. Repassa o estado ao field.
                    if (arg0 < 10) u.FieldTargetA = arg1; // playerRecord+0x2c8
                    else u.FieldTargetB = arg1;           // playerRecord+0x2ca

                    using var w = new PacketWriter();
                    w.WriteByte(slotInField);
                    w.WriteByte(arg0);
                    w.WriteWord(arg1);
                    field.Broadcast(WorldHandlers.Prefix(0x60, w.ToArray()), u);
                }


        private static void Op_SetUserPing(HandlerContext ctx)
        {
            var u = ctx.User;
            // No state guards in the binary.

            // Payload: [int value]
            int value = ctx.P.Int32();        // iVar1 = *param_3

            // Store at user+0x2380 (per-slot field, e.g. measured ping/latency value).
            // TODO: expose this as a typed property on ClientSession (e.g. u.Ping = value).
            u.Ping = value;

            // If it matches the server-wide reference value (this+0x51b4, a WorldServer
            // counter/threshold), bump the match counter (this+0x51bc).
            // TODO: map this+0x51b4 / this+0x51bc to WorldServer fields once identified.
            if (value == ctx.World.PingReference)
            {
                ctx.World.PingMatchCount++;
            }
        }

        private static void Op_FieldRelayAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_0041c2b0. this_00 = user[slot].
                    // Guard (forma combinada do exe, sem disconnect): user+0x1460 && user+0x14a4 != 0 && Status==3.
                    // Se qualquer condicao falhar o handler simplesmente retorna (nenhum DISC neste opcode).
                    if (!(u.InField && u.FieldSecondary && u.Status == 0x03)) return;

                    if (!ctx.P.CanRead(1)) return;
                    byte src = ctx.P.Byte(); // *param_3 -> reempacotado em (byte)param_1 e usado como source

                    // FUN_0040b7d0(user, out fieldId(local_4,u16), out param_3(byte: indice de membro alvo)).
                    // local_4 indexa o array this+0xe4 (passo 0x3c0): o registro de campo do proprio jogador.
                    int fieldId = u.FieldId;
                    var field = ctx.World.GetField(fieldId);
                    if (field == null) return;

                    // FUN_00406930(playerRecord, &param_3, &src):
                    //   uVar1 = *(playerRecord + (*param_3)*0x14 + 0x124)  -> slot global do membro de indice (*param_3)
                    //   monta [u16 0x62][byte src] e envia (FUN_0041b8a0) APENAS para esse membro.
                    // O segundo byte do payload (param_3 apos FUN_0040b7d0) e o indice do membro destino no field.
                    byte memberIndex = (byte)u.Slot; // param_3 (byte) resolvido por FUN_0040b7d0

                    // Modelado: relay direcionado. Sem o mapa indice->slot do field, repassa o byte 'src'
                    // (subtype 0x62) aos demais membros do field, preservando o relay observavel.
                    _ = memberIndex;
                    using var w = new PacketWriter();
                    w.WriteByte(src); // local_1002 = *param_1
                    field.Broadcast(WorldHandlers.Prefix(0x62, w.ToArray()), u);
                }


        private static void Op_RequestFieldTick(HandlerContext ctx)
                {
                    // FUN_004286a0 (opcode 0x6b). iVar1 = slot*0x23b4 + *(this+0xd4).
                    var u = ctx.User;

                    // Guard: (*(iVar1+0x1460) != 0) && (*(iVar1+0x14a4) != 0) -> InField && FieldSecondary.
                    // Senao => FUN_0041eb20(this, slot, 0xc2, 1, 1) -> Disconnect(0xc2).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xc2); return; }

                    // Resposta plana (FUN_0041b940 = SendMessage), layout a partir de &local_1004:
                    //   local_1004 = *(u16)(iVar1+0x1488) -> seq slot (escrito pelo SendMessage em C#).
                    //   local_1002 = 0x1e                -> subtype.
                    //   local_1000 = *(int)(iVar1+0x1460) -> payload de 4 bytes (field handle / InField marker).
                    // len = 8 = seq(2)+subtype(2)+payload(4). Em C#, SendMessage ja escreve seq+subtype,
                    // entao envio apenas o payload (os 4 bytes de local_1000).
                    int fieldHandle = u.FieldHandleRaw; // *(int)(user+0x1460) = local_1000

                    Log.Info("field", "RequestFieldTick: slot {0} field {1} handle=0x{2:x8}", u.Slot, u.FieldId, fieldHandle);

                    using var w = new PacketWriter();
                    w.WriteInt32(fieldHandle); // local_1000 (unico campo apos o subtype)
                    u.SendMessage(0x1e, w.ToArray());
                }


        private static void Op_RequestFieldSnapshot(HandlerContext ctx)
                {
                    // FUN_00428750 (opcode 0x6c). iVar7 = slot*0x23b4; this_00 = *(this+0xd4) + iVar7.
                    var u = ctx.User;

                    // Guard: (*(this_00+0x1460) != 0) && (local_12f0 = *(this_00+0x14a4)) != 0
                    //        -> InField && FieldSecondary. Senao => Disconnect(0xc3).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xc3); return; }
                    int fieldSecondary = u.FieldSecondaryRaw; // local_12f0 = *(int)(user+0x14a4)

                    // Le do payload (param_3 = undefined4*): uVar3 = *param_3 (u32); uVar2 = *(u16)(param_3+1).
                    if (!ctx.P.CanRead(6)) { u.Disconnect(0xc3); return; }
                    uint reqA = (uint)ctx.P.Int32();   // uVar3 = *param_3
                    ushort reqB = ctx.P.UInt16();      // uVar2 = *(u16)(param_3+1)

                    // FUN_0040ca50(this_00, &cntA(local_12f2), listA(local_12e8), listA2(local_109c),
                    //   &cntB(local_12f3), listB(local_1298), listB2(local_1088), &flagC(local_12f1),
                    //   &extra(local_10b4)):
                    // coleta o snapshot do field do usuario em tres blocos opcionais:
                    //   - bloco A: cntA entradas; por entrada 0x13 bytes (listA) seguidos de 4 bytes (listA2).
                    //   - bloco B: cntB entradas; por entrada 4 bytes (listB) seguidos de 1 byte (listB2).
                    //   - bloco C: flag flagC; se != 0, anexa o cabecalho estendido (~0x21 bytes):
                    //       *(int)(iVar1+0x1460), *(int)(iVar1+0x14a4), local_10b4..local_10a0,
                    //       *(int)(iVar1+0x1460) de novo e *(u16)(iVar1+0x2370).
                    // Corpo de FUN_0040ca50 nao presente no decompile -> efeito modelado: snapshot vazio,
                    // preservando o formato exato da resposta.
                    byte cntA = 0, cntB = 0;
                    byte flagC = 0;
                    byte[] blockA = System.Array.Empty<byte>();   // cntA * (0x13 + 4)
                    byte[] blockB = System.Array.Empty<byte>();   // cntB * (4 + 1)
                    byte[] blockC = System.Array.Empty<byte>();   // cabecalho estendido quando flagC != 0

                    // Cabecalho fixo da resposta (FUN_0041b940 = SendMessage), layout a partir de &local_1010:
                    //   local_1010._0_2_ : *(u16)(user+0x1488)  -> seq slot (escrito pelo SendMessage em C#).
                    //   local_1010._2_2_ : 0x1f (subtype).
                    //   local_100c[0]    : *(int)(iVar1+0x1460)  (field handle).
                    //   local_100c[1]    : reqA (echo do u32 recebido).
                    //   local_1004       : reqB (echo do u16 recebido).
                    //   local_1002       : fieldSecondary (int, *(user+0x14a4) = local_12f0).
                    //   local_ffe        : cntA.
                    //   ...em seguida: bloco A; byte cntB; bloco B; byte flagC; bloco C (se flagC != 0).
                    // Em C#, SendMessage ja escreve seq+subtype(0x1f); o payload comeca no field handle.
                    int fieldHandle = u.FieldHandleRaw;    // *(int)(user+0x1460) = local_100c[0]

                    Log.Info("field", "RequestFieldSnapshot: slot {0} field {1} reqA=0x{2:x8} reqB=0x{3:x4} cntA={4} cntB={5} flagC={6}", u.Slot, u.FieldId, reqA, reqB, cntA, cntB, flagC);

                    using var w = new PacketWriter();
                    w.WriteInt32(fieldHandle);         // local_100c[0]
                    w.WriteUInt32(reqA);               // local_100c[1] (echo)
                    w.WriteWord(reqB);                 // local_1004 (echo)
                    w.WriteInt32(fieldSecondary);      // local_1002
                    w.WriteByte(cntA);                 // local_ffe
                    w.WriteBytes(blockA);              // entradas do bloco A (0x13 + 4 por entrada)
                    w.WriteByte(cntB);                 // byte de contagem do bloco B
                    w.WriteBytes(blockB);              // entradas do bloco B (4 + 1 por entrada)
                    w.WriteByte(flagC);                // flag do bloco C
                    if (flagC != 0)
                        w.WriteBytes(blockC);          // cabecalho estendido do field
                    u.SendMessage(0x1f, w.ToArray());
                }


        private static void Op_FieldUseItem(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00428c90. Guard: this_00+0x1460 (InField) && this_00+0x14a4 (FieldSecondary) != 0 -> senao DISC 0xd0.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd0); return; }
                    // this_00+0x1440 (Status) deve ser '\x03' -> senao DISC 0xd1.
                    if (u.Status != 0x03) { u.Disconnect(0xd1); return; }

                    // Payload: [u8 itemType][s16 itemArg]. (bVar1 = *param_3, sVar2 = *(param_3+1)).
                    byte itemType = ctx.P.Byte();   // bVar1
                    short itemArg = ctx.P.Int16();  // sVar2

                    // FUN_0040b7d0(this_00, &local(u16), &local(byte)): le user+0x14a0 (indice do field-object) e
                    // user+0x14a2. O 4o argumento de FUN_0040e5f0 e *(field-object[idx] + 0x119) = a flag/modo do field.
                    ushort fieldObjIdx = u.FieldObjectIndex;
                    var field = ctx.World.GetField(u.FieldId);
                    byte fieldModeFlag = field != null ? field.Mode : (byte)0; // *(fieldObj[idx]+0x119)

                    // FUN_0040e5f0(userObj, itemType, itemArg, fieldModeFlag): aplica o uso de item no field. Retorna
                    // 0 em falha -> DISC 0xd2. Helper sem corpo neste slice; modelado como sucesso (!=0) e broadcast
                    // do uso de item aos demais jogadores do field.
                    int useResult = 1;
                    if (useResult == 0) { u.Disconnect(0xd2); return; }

                    Log.Info($"[FieldUseItem] slot={u.Slot} obj={fieldObjIdx} type={itemType} arg={itemArg} mode={fieldModeFlag}");
                    if (field != null)
                    {
                        using var bw = new PacketWriter();
                        bw.WriteWord(0x6e);
                        bw.WriteWord(fieldObjIdx);
                        bw.WriteByte(itemType);
                        bw.WriteInt16(itemArg);
                        field.Broadcast(bw.ToArray(), u);
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
