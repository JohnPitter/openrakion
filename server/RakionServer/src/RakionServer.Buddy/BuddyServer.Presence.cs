using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Presença + brokering P2P do messenger. A presença (NTF_USER_STATE) é casada por NICK: quando A loga, os
    /// amigos online de A recebem "A online" e A recebe a presença deles. O PM é P2P PURO — o servidor só ensina
    /// os endereços (NADA de relay): o cliente ecoa o token do RET_LOGIN num datagrama UDP, o Buddy aprende o
    /// endpoint UDP real (NAT/porta) e o repassa nos 2 pares IGUAIS do NTF_USER_STATE p/ o cliente abrir o P2P.
    /// </summary>
    public sealed partial class BuddyServer
    {
        private readonly List<UdpClient> _udp = new();

        // ---- UDP brokering (token echo) -------------------------------------------------------------

        private void StartUdpListener(int port)
        {
            try
            {
                var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                _udp.Add(udp);
                Log.Ok("buddy", "ouvindo (UDP) na porta {0} — brokering P2P", port);
                _ = Task.Run(() => UdpLoopAsync(udp, _cts.Token));
            }
            catch (Exception ex) { Log.Error("buddy", "UDP bind {0}: {1}", port, ex.Message); }
        }

        private void StopUdp()
        {
            foreach (var u in _udp) { try { u.Close(); } catch { } }
        }

        /// <summary>Recebe o token ecoado pelo cliente (os 4 bytes iniciais do RET_LOGIN; o token u16 está em [+2],
        /// pois [+0] é o result=0). Correlaciona token->conexão, aprende o endpoint UDP de origem (= endereço P2P
        /// efetivo do cliente) e re-anuncia a presença com o endereço, habilitando o P2P. Ver disasm @1000759e
        /// (o cliente faz sendto dos 4 bytes do payload do RET_LOGIN).</summary>
        private async Task UdpLoopAsync(UdpClient udp, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log.Debug("buddy", "UDP recv: {0}", ex.Message); continue; }

                if (r.Buffer.Length < 4) continue;
                // O RET_LOGIN NÃO carrega token (o cliente lia o token como count → lista-lixo). Correlaciona o
                // echo pela ORIGEM: a conexão buddy LOGADA com o mesmo IP (localhost = 1 cliente/IP no caso comum;
                // multi-cliente no mesmo IP cai na mais recente).
                var conn = FindLoggedInByIp(r.RemoteEndPoint.Address.ToString());
                if (conn == null) continue;
                bool isNew = conn.UdpEndpoint == null;
                conn.UdpEndpoint = r.RemoteEndPoint;
                if (isNew)
                {
                    Log.Ok("buddy", "[{0}] endpoint P2P aprendido: {1}", conn.Ip, r.RemoteEndPoint);
                    RefreshPresence(conn);   // re-anuncia com o endereço (habilita o P2P do PM)
                }
            }
        }

        // ---- presença (NTF_USER_STATE) --------------------------------------------------------------

        /// <summary>Login: manda a presença de TODOS os amigos (offline também) p/ a UI EXIBI-LOS. O RET_LOGIN só
        /// REGISTRA os amigos internamente; é o NTF_USER_STATE que ACENDE a lista do messenger (bring-up: após o
        /// RET_LOGIN vai o USER_STATE com a lista, acendendo online/offline). E avisa os amigos online de ESTE.</summary>
        private void AnnounceOnline(BuddyConn conn)
        {
            var roster = new List<UserPresence>();
            int online = 0;
            foreach (var bn in conn.BuddyNicks)
            {
                bool up = _byNick.TryGetValue(bn, out var f) && f.LoggedIn && f != conn;
                roster.Add(up ? Presence(f!) : new UserPresence(bn, false, null, 0));
                if (up) { online++; Send(f!, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(new[] { Presence(conn) })); }
            }
            if (roster.Count > 0)
                Send(conn, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(roster));  // ELE vê TODOS (online+offline)
            Log.Debug("buddy", "[{0}] presença: {1} amigo(s) ({2} online)", conn.Ip, roster.Count, online);
        }

        /// <summary>Re-anuncia ESTE online aos amigos online após aprender o endpoint UDP (agora com endereço P2P).</summary>
        private void RefreshPresence(BuddyConn conn)
        {
            foreach (var bn in conn.BuddyNicks)
                if (_byNick.TryGetValue(bn, out var f) && f.LoggedIn && f != conn)
                    Send(f, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(new[] { Presence(conn) }));
        }

        /// <summary>Logout: avisa os amigos online que ESTE ficou offline.</summary>
        private void AnnounceOffline(BuddyConn conn)
        {
            byte[] frame = BuddyFrames.UserState(new[] { new UserPresence(conn.Nick, false, null, 0) });
            foreach (var bn in conn.BuddyNicks)
                if (_byNick.TryGetValue(bn, out var f) && f.LoggedIn && f != conn)
                    Send(f, BuddyProtocol.NTF_USER_STATE, frame);
        }

        /// <summary>SVC_USER_STATE (0x3010): o cliente pergunta o status de um id -> responde a presença dele.</summary>
        private void HandleUserStateQuery(BuddyConn conn, byte[] payload)
        {
            string nick = AsciiZ(payload);
            if (nick.Length == 0) return;
            bool online = _byNick.TryGetValue(nick, out var target) && target!.LoggedIn;
            var pres = online ? Presence(target!) : new UserPresence(nick, false, null, 0);
            Send(conn, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(new[] { pres }));
            Log.Debug("buddy", "[{0}] USER_STATE query '{1}' -> {2}", conn.Ip, nick, online ? "online" : "offline");
        }

        /// <summary>Presença de uma conexão: online + endereço P2P (4B network-order + porta) se o endpoint UDP já
        /// foi aprendido; senão online com endereço 0 (o cliente ainda não pode abrir o P2P; reanunciamos no register).</summary>
        private static UserPresence Presence(BuddyConn c)
        {
            if (c.UdpEndpoint != null)
                return new UserPresence(c.Nick, true, c.UdpEndpoint.Address.MapToIPv4().GetAddressBytes(), (ushort)c.UdpEndpoint.Port);
            return new UserPresence(c.Nick, true, null, 0);
        }

        /// <summary>Fim de conexão: tira dos registros (só se ainda for ESTA — um relog não derruba a nova conexão)
        /// e avisa os amigos que ficou offline.</summary>
        private void OnConnClosed(BuddyConn conn)
        {
            if (!conn.LoggedIn) return;
            if (_byNick.TryGetValue(conn.Nick, out var cur) && cur == conn) _byNick.TryRemove(conn.Nick, out _);
            if (_byAccount.TryGetValue(conn.Account, out var ca) && ca == conn) _byAccount.TryRemove(conn.Account, out _);
            AnnounceOffline(conn);
        }

        /// <summary>Conexão buddy LOGADA com o IP dado (correlação do echo UDP do P2P — o RET_LOGIN não carrega
        /// token). Caso comum (localhost, 1 cliente/IP) é inequívoco; multi-cliente no mesmo IP cai na mais recente.</summary>
        private BuddyConn? FindLoggedInByIp(string ip)
        {
            BuddyConn? found = null;
            foreach (var c in _byAccount.Values)
                if (c.LoggedIn && c.Ip == ip) found = c;
            return found;
        }
    }
}
