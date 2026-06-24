using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_FieldChatBroadcast(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: deve estar no field (in-field + secondary)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x86); return; }
            // Guard: status precisa ser 0x03 (field)
            if (u.Status != 0x03) { u.Disconnect(0x87); return; }

            // TODO FUN_0040b7d0: resolve o slot do jogador dentro do field e um byte de flag.
            // Efeito minimo: assume o proprio slot do usuario e flag 0.
            ushort fieldSlot = u.Slot;
            byte fieldFlag = 0;

            // Payload: [u16 length][byte[length] data]
            ushort length = ctx.P.UInt16();
            if (length > 200) { u.Disconnect(0x88); return; }
            byte[] data = ctx.P.Bytes(length);

            // TODO FUN_00405c00(fieldObj=(this+0xe4 + fieldSlot*0x3c0), fieldFlag, length, data):
            // dispatch de chat/broadcast no nivel do field. A resposta e emitida internamente.
            var field = ctx.World.GetField(u.FieldId);
            if (field != null)
            {
                // TODO FUN_00405c00: broadcast do conteudo aos jogadores do field.
                using var w = new PacketWriter();
                w.WriteWord(length);
                w.WriteBytes(data);
                field.Broadcast(w.ToArray());
            }
            _ = fieldSlot; _ = fieldFlag;
        }

        private static void Op_FieldTaggedBroadcast(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x89); return; }
            if (u.Status != 0x03) { u.Disconnect(0x8a); return; }

            // TODO FUN_0040b7d0: resolve fieldSlot + flag. Efeito minimo: proprio slot, flag 0.
            ushort fieldSlot = u.Slot;
            byte fieldFlag = 0;

            // Payload: [byte tag][u16 length][byte[length] data]
            byte tag = ctx.P.Byte();
            if (tag > 0x13) { u.Disconnect(0x8b); return; }
            ushort length = ctx.P.UInt16();
            if (length > 200) { u.Disconnect(0x8c); return; }
            byte[] data = ctx.P.Bytes(length);

            // TODO FUN_00405cc0(fieldObj, fieldFlag, tag, length, data): dispatch tagueado no field.
            var field = ctx.World.GetField(u.FieldId);
            if (field != null)
            {
                using var w = new PacketWriter();
                w.WriteByte(tag);
                w.WriteWord(length);
                w.WriteBytes(data);
                field.Broadcast(w.ToArray());
            }
            _ = fieldSlot; _ = fieldFlag;
        }

        private static void Op_GameChat(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa1); return; }
                    // Guard 2: status field (3); se nao, retorna sem disconnect
                    if (u.Status != 0x03) { return; }

                    // FUN_0040b7d0: fieldIdx + slot do remetente
                    // TODO FUN_0040b7d0(this_00, out fieldIdx, out mySlot)
                    int fieldIdx = u.FieldId;
                    byte mySlot = (byte)u.Slot;
                    var field = ctx.World.GetField(fieldIdx);

                    // Parse: u16 = tamanho do conteudo
                    ushort len = ctx.P.UInt16();
                    if (len > 1000) { u.Disconnect(0xa2); return; }
                    byte[] body = ctx.P.Bytes(len);

                    // Comando de bot ("/addbot", "/removebot") — o servidor lê o texto C->S em claro.
                    // Se consumido, NAO broadcasta como chat normal.
                    if (TryHandleBotChatCommand(ctx, body)) return;

                    // FUN_00405f30: faz broadcast do chat no field para todos os jogadores
                    // TODO FUN_00405f30(field, mySlot, len, body)
                    using (var w = new PacketWriter())
                    {
                        w.WriteByte(mySlot);
                        w.WriteWord(len);
                        w.WriteBytes(body);
                        field?.Broadcast(Prefix(0x56, w.ToArray()), u);
                    }
                }

        private static void Op_GameVoiceChat(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa3); return; }
                    // Guard 2
                    if (u.Status != 0x03) { return; }

                    // FUN_0040b7d0: fieldIdx + mySlot
                    // TODO FUN_0040b7d0(this_00, out fieldIdx, out mySlot)
                    int fieldIdx = u.FieldId;
                    byte mySlot = (byte)u.Slot;
                    var field = ctx.World.GetField(fieldIdx);

                    // Parse: u8 canal/indice (<= 0x13)
                    byte channel = ctx.P.Byte();
                    if (channel > 0x13) { u.Disconnect(0xa4); return; }
                    // u16 tamanho (<= 1000)
                    ushort len = ctx.P.UInt16();
                    if (len > 1000) { u.Disconnect(0xa5); return; }
                    byte[] body = ctx.P.Bytes(len);

                    // FUN_004060a0: broadcast do chat/voz no field considerando o canal
                    // TODO FUN_004060a0(field, mySlot, channel, len, body)
                    using (var w = new PacketWriter())
                    {
                        w.WriteByte(mySlot);
                        w.WriteByte(channel);
                        w.WriteWord(len);
                        w.WriteBytes(body);
                        field?.Broadcast(Prefix(0x57, w.ToArray()), u);
                    }
                }

        private static void Op_GameEmoteAction(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa6); return; }
                    // Guard 2
                    if (u.Status != 0x03) { return; }

                    // Parse (na ordem do binario, ANTES do FUN_0040b7d0)
                    byte action = ctx.P.Byte();
                    if (action > 0x13) { u.Disconnect(0xa7); return; }
                    uint data = ctx.P.UInt32();

                    // FUN_0040b7d0: fieldIdx + mySlot
                    // TODO FUN_0040b7d0(this_00, out fieldIdx, out mySlot)
                    int fieldIdx = u.FieldId;
                    byte mySlot = (byte)u.Slot;
                    var field = ctx.World.GetField(fieldIdx);

                    // FUN_004062c0: executa/transmite a acao no field. 3o arg = slot do user (uVar3 = (ushort)param_1 original).
                    // TODO FUN_004062c0(field, action, u.Slot, data)
                    using (var w = new PacketWriter())
                    {
                        w.WriteByte((byte)u.Slot);
                        w.WriteByte(action);
                        w.WriteUInt32(data);
                        field?.Broadcast(Prefix(0x59, w.ToArray()), u);
                    }
                }

        private static void Op_GameWhisperToSlot(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa8); return; }
                    // Guard 2: status field (3); se nao, apenas retorna
                    if (u.Status != 0x03) { return; }

                    // Parse: slot alvo (local_1008/uVar2)
                    ushort targetSlot = ctx.P.UInt16();
                    if (targetSlot >= ctx.World.MaxUser) { u.Disconnect(0xa9); return; }

                    // FUN_0040b7d0: meu seat (local_1008) + meu slot (local_100a)
                    // TODO FUN_0040b7d0(this_00, out mySeat, out mySlot)
                    ushort mySlot = u.Slot;     // local_100a -> local_1002 (undefined2 = 2 bytes)
                    uint data = ctx.P.UInt32(); // local_1000 = *(param_3+1)

                    // Localiza a sessao do alvo e confirma que esta em field (status==3)
                    ClientSession? target = null;
                    foreach (var s in ctx.World.Sessions)
                    {
                        if (s.Slot == targetSlot) { target = s; break; }
                    }
                    if (target == null || target.Status != 0x03) { return; }

                    // Resposta enviada AO ALVO via SendLobby (canal lobby), len 8: 0x5a { u16 mySlot, u32 data }
                    using (var w = new PacketWriter())
                    {
                        w.WriteWord(mySlot);   // local_1002 (undefined2 = 2 bytes)
                        w.WriteUInt32(data);   // local_1000
                        target.SendLobby(Prefix(0x5a, w.ToArray()));
                    }
                }

        private static void Op_FieldChatNamed(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: must be in field + secondary field flag set -> DISC 0xad
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xad); return; }
            // Guard: status must be 3 (field) -> DISC 0xae
            if (u.Status != 0x03) { u.Disconnect(0xae); return; }

            // Payload: [byte targetCode][cstring text]
            byte targetCode = ctx.P.Byte();          // local_108d = *param_3
            string text = ctx.P.CString();           // lstrcpyA(buf, param_3+1)
            // local_108f = '\0' -> a leading category/flag byte passed to FUN_0040a420
            byte category = 0x00;

            // FUN_0040b7d0(userObj, out fieldId, out slotInField)
            // TODO FUN_0040b7d0: resolve the user's field and slot.
            int fieldId = u.FieldId;                  // auStack_108c[0]
            byte slotInField = (byte)u.Slot;          // bStack_108e

            var field = ctx.World.GetField(fieldId);

            // FUN_0040a420(fieldObj, &category, &slotInField, &targetCode, textBuf)
            // performs the chat/whisper delivery within the field; returns nonzero on success.
            // TODO FUN_0040a420: deliver the field chat; assume success (true) to preserve
            // the response structure.
            bool ok = field != null; // model: success when field resolved
            _ = category; _ = slotInField; _ = targetCode; _ = text;

            if (ok)
            {
                // On success: lobby ack, subtype 0x5f, total len 3 (subtype only).
                u.SendLobby(BuildSubtypeOnly(0x5f));
            }
        }

        // helper: lobby payload carrying only the [u16 subtype] (matches FUN_004038e0 len=3)
        private static byte[] BuildSubtypeOnly(int subtype)
        {
            using var w = new PacketWriter();
            w.WriteWord(subtype);
            return w.ToArray();
        }

        private static void Op_FieldChatCode(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: must be in field + secondary field flag set -> DISC 0xaf
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xaf); return; }
            // Guard: status must be 3 (field) -> DISC 0xb0
            if (u.Status != 0x03) { u.Disconnect(0xb0); return; }

            // Payload: [byte code]
            byte code = ctx.P.Byte();                 // local_100b = *param_3
            byte category = 0x00;                     // local_100a = 0

            // FUN_0040b7d0(userObj, out fieldId, out slotInField)
            // TODO FUN_0040b7d0: resolve the user's field and slot.
            int fieldId = u.FieldId;                  // local_1008[0]
            byte slotInField = (byte)u.Slot;          // local_1009

            var field = ctx.World.GetField(fieldId);

            // FUN_0040a420(fieldObj, &code, &slotInField, &category, NULL)
            // same delivery routine as 0x5d but with no text buffer (last arg NULL).
            // TODO FUN_0040a420: deliver the field signal/emote; assume success.
            bool ok = field != null;
            _ = code; _ = slotInField; _ = category;

            if (ok)
            {
                // On success: lobby ack, subtype 0x5f, total len 3 (subtype only).
                using var w = new PacketWriter();
                w.WriteWord(0x5f);
                u.SendLobby(w.ToArray());
            }
        }

        private static void Op_FieldEmoteEcho(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00428a10. iVar1 = user[slot]. local_1000 = user+0x1460 (handle de field, cru).
                    // Guard: user+0x1460 != 0 && user+0x14a4 != 0, senao DISC 0xc4.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xc4); return; }

                    // Payload (undefined4* param_3): [u32 echo]. local_ffc = *param_3.
                    int echo = ctx.P.Int32();

                    // Resposta via FUN_0041b940(this, slot, len=0xc, &local_1004):
                    //   local_1004 = *(user+0x1488) (serverSeq) -> injetado por SendMessage
                    //   local_1002 = 0x20                       -> subtype/msgType
                    //   local_1000 = *(user+0x1460)             -> handle de field (cru), reenviado no corpo
                    //   local_ffc  = echo                       -> dword ecoado
                    // len 0xc = [u16 seq][u16 subtype][u32 handle][u32 echo].
                    using var w = new PacketWriter();
                    w.WriteInt32(u.FieldHandleRaw); // local_1000 (= cru de user+0x1460)
                    w.WriteInt32(echo);             // local_ffc
                    u.SendMessage(0x20, w.ToArray());
                }
    }
}
