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
    /// Pacote (payload): [u8 connType][userID\0][field2\0][field3\0][u16 tail]
    ///   connType 4 (Normal) pula a checagem de nome de sessao;
    ///   connType 1 (PK) / outros comparam o userID com o nome da sessao.
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
            byte connType = r.Byte();
            string userId = r.CString(Protocol.LoginLimits.UserIdMax + 1);
            s.ConnType = connType;

            Log.Info("login", "[{0}] login userID='{1}' connType={2}", s.Slot, userId, connType);

            // (4) checagem de nome de sessao (pulada quando connType == Normal/4).
            // O nome esperado vem da sessao validada pelo broker (RequestLogin).
            // NOTA: o login original (FUN_0041f6c0) NAO valida userID/sessao aqui — ele
            // so faz parse (connType, userID, field2, field3, tail) e responde sucesso
            // (FUN_0041b940). A autenticacao real ocorre no broker/web ANTES. Removida a
            // checagem de "nome de sessao" que a reconstrucao havia adicionado a mais.

            // (5)/(6) field2/field3. FUN_0041b810 grava no objeto do usuario. O ORIGINAL NAO
            // desconecta por tamanho — a reconstrucao havia adicionado um DISC 20 a mais (o mesmo
            // que o STATUS.md patchou no RakionWorldServ.exe @0x41f8c9). O cliente GG-removido,
            // quando lancado direto, manda o caminho do exe nesse campo (longo). Aceitamos e
            // apenas CLAMPamos p/ caber no objeto do usuario; o login nao valida esses campos
            // (auth real foi no broker/web). Mantemos o fluxo (replay do 0x0C cuida da resposta).
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
            await server.OnLoginSuccessAsync(s, userId, field2, field3, tail);
        }
    }
}
