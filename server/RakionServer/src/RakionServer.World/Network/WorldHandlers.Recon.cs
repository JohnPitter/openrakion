using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Declaracoes dos handlers de COMBATE/SALA a reconstruir (fase Handlers). Cada metodo
    /// "Op_0xNN_Recon" e o ponto de entrada apontado pela tabela Build() em WorldHandlers.cs.
    ///
    /// CONVENCAO (para os agentes de handler):
    ///   internal static void Op_0xNN_Recon(HandlerContext ctx)
    ///   - ctx.User   = ClientSession (user-obj)
    ///   - ctx.World  = WorldServer
    ///   - ctx.Opcode = opcode (0xNN)
    ///   - ctx.P      = PacketReader do payload (apos [opcode][seq])
    ///   - ctx.Raw    = payload cru
    ///   Substitua o corpo (hoje = ReconStub) pela reconstrucao fiel do FUN_xxxx citado.
    ///   NAO altere a assinatura nem a entrada na tabela. Use os HELPERS abaixo p/ resolver
    ///   o Field/player-record e broadcastar.
    ///
    /// HELPERS COMPARTILHADOS (golden source — nao duplicar):
    ///   - Field?      ReconField(ctx)            -> o Field atual da sessao (ou null)
    ///   - PlayerRec?  ReconRec(ctx, out Field f) -> player-record do sender + o Field
    ///   - bool        ReconGate(ctx, byte disc)  -> exige InField && FieldSecondary && Status==3
    ///   - SendField(ctx, msgType, body)          -> canal FIELD ao proprio (SendMessage)
    ///   - SendLobbyMsg(ctx, msgType, body)       -> canal LOBBY ao proprio (SendEncryptedFrame [msgType][...])
    ///   - field.BroadcastField(msgType, body, except)         -> FIELD a todos ocupados (FUN_004061f0)
    ///   - field.BroadcastFieldPlaying(msgType, body, except)  -> FIELD aos state==4
    ///   - field.BroadcastLobby(payload, except)               -> LOBBY a todos ocupados
    /// </summary>
    public static partial class WorldHandlers
    {
        // ---- handlers de COMBATE / GAMEPLAY in-field (Status==3) ----
        // Op_0x3D/0x3E/0x3F/0x40/0x41/0x42_Recon implementados em WorldHandlers.ReconCombatB.cs
        // Op_0x43_Recon implementado em WorldHandlers.ReconCombatA.cs (ATTACK / match-start engage)
        // Op_0x45_Recon implementado em WorldHandlers.ReconCombatA.cs (spawn/join-into-field)
        // Op_0x46_Recon implementado em WorldHandlers.ReconCombatA.cs (HIT / aplicar dano)
        // Op_0x4A/0x4B/0x4C/0x4D_Recon implementados em WorldHandlers.ReconCombatB.cs
        // Op_0x4F_Recon implementado em WorldHandlers.ReconRoomB.cs (DIE / morte)
        // Op_0x50_Recon implementado em WorldHandlers.ReconRoomB.cs (GameResult/scoring)
        // Op_0x53_Recon implementado em WorldHandlers.ReconRoomB.cs (FieldCreate result)
        // Op_0x61_Recon implementado em WorldHandlers.ReconRoomB.cs (FieldReady ack)

        // ---- handlers de SALA / lista (Status==2) ----
        internal static void Op_0x2D_Recon(HandlerContext ctx) => ReconStub(ctx); // RoomMemberList
        internal static void Op_0x2E_Recon(HandlerContext ctx) => ReconStub(ctx); // RoomShopBuy — handler REAL = Op_RoomMemberQuery (WorldHandlers.Generated.cs)
        internal static void Op_0x2F_Recon(HandlerContext ctx) => ReconStub(ctx); // RoomShopList
        internal static void Op_0x34_Recon(HandlerContext ctx) => ReconStub(ctx); // RoomBuyB
        internal static void Op_0x36_Recon(HandlerContext ctx) => ReconStub(ctx); // RoomList/FieldSearch
        // Op_0x3A_Recon implementado em WorldHandlers.ReconRoomB.cs (FieldStart/Leave3D)
        // Op_0x3B_Recon implementado em WorldHandlers.ReconCombatB.cs (FieldCreate sala)

        // ===================== HELPERS COMPARTILHADOS =====================

        /// <summary>Placeholder ate o agente de handler reconstruir o corpo (aplica o gate + loga).</summary>
        private static void ReconStub(HandlerContext ctx)
        {
            var e = Table[ctx.Opcode];
            if (StubGates.TryGetValue(ctx.Opcode, out var g))
            {
                bool ok = (!g.InF || ctx.User.InField) && (!g.FSec || ctx.User.FieldSecondary);
                if (!ok) { if (g.Disc != 0) ctx.User.Disconnect(g.Disc); return; }
            }
            Log.Debug("op", "[{0}] {1} (FUN_{2:x}) — RECON pendente ({3} bytes)",
                ctx.User.Slot, e.Name, e.Addr, ctx.Raw.Length);
        }

        /// <summary>Field atual da sessao (resolve user.FieldId -> World.Fields). Null se fora.</summary>
        internal static Field? ReconField(HandlerContext ctx) => ctx.World.GetField(ctx.User.FieldId);

        /// <summary>
        /// Player-record do sender + o Field (FUN_0040b7d0 -> seat). Devolve null se a sessao
        /// nao esta num field ou nao tem seat alocado.
        /// </summary>
        internal static PlayerRec? ReconRec(HandlerContext ctx, out Field? field)
        {
            field = ctx.World.GetField(ctx.User.FieldId);
            return field?.FindRec(ctx.User);
        }

        /// <summary>
        /// Gate fiel dos handlers in-field de combate: exige InField && FieldSecondary && Status==3.
        /// Desconecta com 'disc' (0 = sem desconexao) e devolve false se falhar.
        /// </summary>
        internal static bool ReconGate(HandlerContext ctx, byte disc)
        {
            var u = ctx.User;
            if (u.InField && u.FieldSecondary && u.Status == UserStatus.InField) return true;
            if (disc != 0) u.Disconnect(disc);
            return false;
        }

        /// <summary>Envia ao proprio sender pelo canal FIELD ([serverSeq][msgType][body]).</summary>
        internal static void SendField(HandlerContext ctx, ushort msgType, byte[] body)
            => ctx.User.SendMessage(msgType, body);

        /// <summary>Envia ao proprio sender pelo canal LOBBY (payload = [u16 msgType][body]).</summary>
        internal static void SendLobbyMsg(HandlerContext ctx, ushort msgType, byte[] body)
            => ctx.User.SendEncryptedFrame(Prefix(msgType, body));
    }
}
