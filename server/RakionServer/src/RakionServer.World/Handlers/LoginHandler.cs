using System;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World.Handlers
{
    /// <summary>
    /// Handler de login do world, reconstruido 1:1 de FUN_0041f6c0 @ 0x41f6c0
    /// (worldserv.exe). E o destino do opcode 0x0C quando a sessao ainda nao
    /// esta autenticada (this+0x5b18 == 0).
    ///
    /// Pacote: [u8 verifyMode][cstr md5][cstr account][cstr password][u16 tail].
    /// O modo 1 seleciona MD5_2; os demais selecionam MD5_1; modo 4 pula o MD5 no login.
    /// </summary>
    public static class LoginHandler
    {
        public static async Task HandleAsync(WorldServer server, ClientSession s, byte[] payload)
        {
            // (1) this+0x50 != 0 -> servidor travado
            if (server.Locked)
            {
                Log.Warn("login", "[{0}] servidor travado -> DISC {1}", s.Slot, Protocol.DiscReason.ServerLocked);
                s.Disconnect(Protocol.DiscReason.ServerLocked);
                return;
            }

            // (2) slot ja ativo (user+0x1460 / +0x14a4) -> login duplicado
            if (s.SlotActive || s.SecondActive)
            {
                Log.Warn("login", "[{0}] slot ja em uso -> DISC {1}", s.Slot, Protocol.DiscReason.SlotInUse);
                s.Disconnect(Protocol.DiscReason.SlotInUse);
                return;
            }

            // (3) capacidade (this+0x536c <= curUsers) -> erro servidor cheio
            if (server.CurrentUsers >= server.MaxUser)
            {
                Log.Warn("login", "[{0}] servidor cheio ({1}/{2})", s.Slot, server.CurrentUsers, server.MaxUser);
                s.SendLoginError(Protocol.LoginError.SubServerFull);
                return;
            }

            // parse do pacote
            var r = new PacketReader(payload);
            byte verifyMode = r.Byte();
            string clientHash = r.CString(Protocol.LoginLimits.UserIdMax + 1);
            s.VerifyMode = verifyMode;

            Log.Info("login", "[{0}] login verifyMode={1}", s.Slot, verifyMode);

            if (!Domain.ClientHashPolicy.LoginAccepted(
                verifyMode, clientHash, server.Config.ClientHashes))
            {
                Log.Warn("integrity", "[{0}] MD5 do login recusado (mode={1})", s.Slot, verifyMode);
                s.SendLoginError(Protocol.LoginError.SubHashMismatch);
                return;
            }

            // (5)/(6) field2/field3. FUN_0041b810 grava no objeto do usuario. O ORIGINAL NAO
            // desconecta por tamanho — a reconstrucao havia adicionado um DISC 20 a mais (o mesmo
            // que o STATUS.md patchou no RakionWorldServ.exe @0x41f8c9). O cliente GG-removido,
            // quando lancado direto, manda o caminho do exe nesse campo (longo). Aceitamos e
            // apenas CLAMPamos p/ caber no objeto do usuario; o login nao valida esses campos
            // (auth real foi no broker/web). Mantemos o fluxo (a síntese do 0x0C cuida da resposta).
            string field2 = r.CString(Protocol.LoginLimits.Field2Max + 2);
            if (field2.Length > Protocol.LoginLimits.Field2Max)
            {
                Log.Warn("login", "[{0}] field2 longo ({1}) — clamp p/ {2} (sem DISC, igual ao patch do original)",
                    s.Slot, field2.Length, Protocol.LoginLimits.Field2Max);
                field2 = field2.Substring(0, Protocol.LoginLimits.Field2Max);
            }

            string field3 = r.CString(Protocol.LoginLimits.Field3Max + 2);
            if (field3.Length > Protocol.LoginLimits.Field3Max)
            {
                Log.Warn("login", "[{0}] field3 longo ({1}) — clamp p/ {2}", s.Slot, field3.Length, Protocol.LoginLimits.Field3Max);
                field3 = field3.Substring(0, Protocol.LoginLimits.Field3Max);
            }

            // (7) tail u16
            ushort tail = r.CanRead(2) ? r.UInt16() : (ushort)0;

            // sucesso — promove a sessao
            await server.OnLoginSuccessAsync(s, field2, field3, tail);
        }
    }
}
