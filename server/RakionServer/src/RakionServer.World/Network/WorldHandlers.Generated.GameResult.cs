using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_GameResultReport(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // FUN_00425010. Guard 1: InField (0x1460) && FieldSecondary (0x14a4) -> senao DISC 0x98.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x98); return; }
                    // Guard 2: Status (0x1440) == '\x03' -> senao DISC 0x99.
                    if (u.Status != 0x03) { u.Disconnect(0x99); return; }

                    // FUN_0040b7d0(this_00, &local_30f4(u16), &local_30e5(byte)): le user+0x14a0 (indice do field-object)
                    // e user+0x14a2 (seat byte deste user).
                    ushort fieldObjIdx = u.FieldObjectIndex;  // local_30f4[0]
                    byte mySeat = u.FieldSeat;                // local_30e5
                    var field = ctx.World.GetField(u.FieldId);
                    if (field == null) { u.Disconnect(0x9a); return; }

                    // iVar9 = field-object[fieldObjIdx]. Guard 3: *(iVar9+0x119)==0 -> senao DISC 0x9a.
                    if (field.FieldFlag119 != 0) { u.Disconnect(0x9a); return; }
                    // Guard 4: *(iVar9+8)=='\x02' (StateB) && *(iVar9+0x2b4)=='\x02' (StateMode) -> senao DISC 0x9b.
                    if (!(field.StateB == 0x02 && field.StateMode == 0x02)) { u.Disconnect(0x9b); return; }

                    // Parse cabecalho: b0(<100 -> 0x9c), b1(<6 -> 0x9d), count(<5 -> 0x9e).
                    byte b0 = ctx.P.Byte();   // *param_3 -> local_30d4
                    if (b0 >= 100) { u.Disconnect(0x9c); return; }
                    byte b1 = ctx.P.Byte();   // param_3[1] -> local_30d8
                    if (b1 >= 6) { u.Disconnect(0x9d); return; }
                    byte count = ctx.P.Byte(); // param_3[2] -> bVar2 / local_2fd2
                    if (count >= 5) { u.Disconnect(0x9e); return; }

                    // Lê 'count' seats (u16). Cada um deve ser < MaxUser (this+0x108); senao DISC 0x9f.
                    ushort[] seats = new ushort[count];
                    for (int i = 0; i < count; i++)
                    {
                        ushort sw = ctx.P.UInt16();
                        seats[i] = sw;
                        if (sw >= ctx.World.MaxUser) { u.Disconnect(0x9f); return; }
                    }

                    // FUN_0040bae0(userObj, b0, b1, &gamePoint(local_30bc), &rank(local_30f5)): computa resultado p/ este
                    // user. Retorno (bVar5): >1 -> cleanup sem resposta; ==1 -> reply curto de erro; ==0 -> sucesso.
                    uint gamePoint = 0;   // local_30bc
                    byte rank = mySeat;   // local_30f5
                    byte result = 0;      // (byte)uVar7
                    if (result > 1) { return; } // LAB_00425602: cleanup

                    if (result != 0)
                    {
                        // FUN_004038e0(handler, slot, 3, &local_2010): lobby 0x53 [u8 result] (len 3).
                        using var wErr = new PacketWriter();
                        wErr.WriteWord(0x53);     // local_2010
                        wErr.WriteByte(result);   // local_200e
                        u.SendLobby(wErr.ToArray());
                        Log.Info($"[GameResultReport] slot={u.Slot} result={result} (rejeitado)");
                        return;
                    }

                    // (A) reply de sucesso: FUN_004038e0(handler, slot, len, &local_2010) -> lobby 0x53.
                    // local_200e=result(0), local_200d=CONCAT11(b1,b0) => bytes b0,b1; se count!=0 copia os 'count' seats
                    // (local_3098 -> local_200a) e len = count*2 + 6; senao len = 6. local_200b = count.
                    using (var wOk = new PacketWriter())
                    {
                        wOk.WriteWord(0x53);   // local_2010 subtype
                        wOk.WriteByte(0);      // local_200e (result)
                        wOk.WriteByte(b0);     // local_200d byte0 (local_30d4)
                        wOk.WriteByte(b1);     // local_200d byte1 (local_30d8)
                        wOk.WriteByte(count);  // local_200b
                        for (int i = 0; i < count; i++) wOk.WriteWord(seats[i]); // local_200a[]
                        u.SendLobby(wOk.ToArray());
                    }
                    Log.Info($"[GameResultReport] slot={u.Slot} obj={fieldObjIdx} seat={mySeat} b0={b0} b1={b1} count={count}");

                    // exp/gold do payload em offset (3 + 2*count); cursor ja esta nessa posicao.
                    uint exp = ctx.P.UInt32();    // local_30ec
                    uint gold = ctx.P.UInt32();   // local_30dc
                    // Bonus de PU (pu_config): exp/gold multiplicados quando o PU está ativo.
                    exp = u.BonusExp(exp); gold = u.BonusGold(gold);

                    // FUN_0041cf80(this, &slot, &fieldObjIdx, &rank, &exp, &gold): anti-cheat. 0 = ok, !=0 = invalido.
                    int validate = 0;
                    if (validate != 0)
                    {
                        // FUN_0041d380(this, "Wrong Game Point! Exp : %u, Gold : %u", group) + DISC 0xa0.
                        Log.Warn($"[GameResultReport] slot={u.Slot} WRONG GAME POINT exp={exp} gold={gold}");
                        u.Disconnect(0xa0);
                        return;
                    }

                    // (B) FUN_0040d300(userObj, exp, &gold, &newLevel(local_30f6), &levelAux(local_30c4), &levelExtra
                    // (local_30e4)): aplica exp/gold e calcula level-up. Retorno (local_30e0) != 0 -> lobby 0x51.
                    byte newLevel = rank;   // local_30f6
                    uint levelAux = 0;      // local_30c4
                    ushort levelExtra = 0;  // local_30e4[0]
                    bool leveled = false;   // local_30e0 != 0
                    if (leveled)
                    {
                        using var wLvl = new PacketWriter();
                        wLvl.WriteWord(0x51);       // local_2010
                        wLvl.WriteByte(newLevel);   // local_200e (local_30f6)
                        wLvl.WriteByte((byte)levelExtra); // local_200d (local_30e4[0])
                        u.SendLobby(wLvl.ToArray());
                    }

                    // drops em param_3 + (cursor+8): 3 u32 (local_30b0/30ac/30a8).
                    uint dropA = ctx.P.UInt32();
                    uint dropB = ctx.P.UInt32();
                    uint dropC = ctx.P.UInt32();

                    // (C) FUN_0040b940(userObj, &dropStruct, &local_30d0, local_30f0, &local_30a4): processa itens/quest.
                    // Retorno (uVar8) != 0 (ou leveled) -> FUN_004061f0(fieldObject, 7, &local_1010) broadcast 0x52.
                    ushort questQ0 = 0;   // local_30f0._0_2_
                    byte questQ1 = 0;     // local_30f0[2]
                    uint questD0 = 0;     // local_30d0
                    uint questA = 0;      // local_30a4
                    bool questUpdated = false; // uVar8 != 0
                    if (leveled || questUpdated)
                    {
                        using var wq = new PacketWriter();
                        wq.WriteWord(0x52);     // local_1010
                        wq.WriteByte(mySeat);   // local_100e (local_30e5)
                        wq.WriteByte(newLevel); // local_100d (local_30f6)
                        wq.WriteWord(questQ0);  // local_100c (local_30f0._0_2_)
                        wq.WriteByte(questQ1);  // local_100a (local_30f0[2])
                        field.Broadcast(wq.ToArray());
                    }

                    // FUN_0040bb60(userObj, '\x03', &valA(local_30b4), &valB(local_30b8), &valC(local_30c0)): stats finais.
                    uint valA = 0; // local_30b4
                    uint valB = 0; // local_30b8
                    uint valC = 0; // local_30c0

                    // (D) snapshot final: FUN_0041b940(this, slot, len, local_3010) -> SendMessage subtype 0x0a (10).
                    // Base len 0x3f; se count!=0 copia seats (local_3098 -> local_2fd1) e len += count*2.
                    // Apos isso grava b1 e, se b1!='\0', um bloco com id do field-object + b0 + gamePoint + rank.
                    using (var wState = new PacketWriter())
                    {
                        wState.WriteUInt32((uint)u.FieldHandleRaw);     // local_300c (field-obj +0x1460 / user+0x1460)
                        wState.WriteWord((ushort)(u.GroupId & 0xffff));  // local_3010 (+0x1488 sessao) — header local
                        wState.WriteUInt32(gold);                        // local_3008 (local_30dc)
                        wState.WriteUInt32((uint)u.FieldSecondaryRaw);   // local_3004 (user+0x14a4)
                        wState.WriteByte(newLevel);                      // local_3000 (local_30f6)
                        wState.WriteUInt32(levelAux);                    // local_2fff (local_30c4)
                        wState.WriteUInt32(valA);                        // local_2ffb (local_30b4)
                        wState.WriteUInt32(valB);                        // local_2ff7 (local_30b8)
                        wState.WriteUInt32(valC);                        // local_2ff3 (local_30c0)
                        wState.WriteWord(levelExtra);                    // local_2fef (local_30e4[0])
                        wState.WriteUInt32(questD0);                     // local_2fed (local_30d0)
                        wState.WriteUInt32(0);                           // local_2fe9 (local_30cc)
                        wState.WriteUInt32(0);                           // local_2fe5 (local_30c8)
                        wState.WriteWord(questQ0);                       // local_2fe1 (local_30f0._0_2_)
                        wState.WriteByte(questQ1);                       // local_2fdf (local_30f0[2])
                        wState.WriteUInt32(questA);                      // local_2fde (local_30a4)
                        wState.WriteUInt32(0);                           // local_2fda (local_30a0)
                        wState.WriteUInt32(0);                           // local_2fd6 (local_309c)
                        wState.WriteByte(count);                         // local_2fd2
                        for (int i = 0; i < count; i++) wState.WriteWord(seats[i]); // local_2fd1[]
                        wState.WriteByte(b1);                            // local_3010[iVar9] = (char)local_30d8
                        if (b1 != 0)
                        {
                            // *(field-obj[fieldObjIdx] + 4): id do field-object.
                            wState.WriteUInt32((uint)field.Id);          // local_3010+iVar9+1
                            wState.WriteByte(b0);                        // local_300c[..] (local_30d4)
                            wState.WriteUInt32(gamePoint);               // local_30bc
                            wState.WriteByte(rank);                      // local_30f5
                        }
                        u.SendMessage(0x0a, wState.ToArray());
                    }

                    _ = dropA; _ = dropB; _ = dropC;
                }
    }
}
