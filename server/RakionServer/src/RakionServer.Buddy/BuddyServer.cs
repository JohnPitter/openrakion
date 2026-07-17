using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Buddy Server (messenger F9). Fala o frame [u16 size][u16 CD][payload] do Buddy2.dll. Reconstruido
    /// por RE COMPLETA do dispatcher CBuddy2::OnMsg (FUN_10007420) e auxiliares. Messenger é P2P PURO
    /// (SEM relay): as mensagens correm UDP direto cliente-a-cliente (cifradas pelo Buddy2.dll); o servidor
    /// só faz o BROKERING de endereços.
    ///  - Handshake: PRECREDENTIAL (8B) -> LOGIN. Login cifrado -> IDENTIDADE por IP (messenger_session do
    ///    world, via <see cref="BuddyDatabase"/>). Chave de rede = NICK do char.
    ///  - Lista de amigos: RET_LOGIN, 1 registro 0x94 por amigo (<see cref="BuddyFrames.BuddyRecord"/>).
    ///  - Brokering P2P: no RET_LOGIN vai um TOKEN (body[2:6]); o cliente o ECOA via UDP (sendto @100759e),
    ///    e o servidor aprende o endpoint UDP de origem. A presença (NTF_USER_STATE) leva esse ip:porta ->
    ///    SetUserOnline ativa o P2P. As PMs nunca passam pelo servidor.
    /// </summary>
    public sealed class BuddyServer
    {
        private readonly int[] _ports;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Socket> _listeners = new();
        private readonly List<Socket> _udpSockets = new();
        private readonly BuddyDatabase _db = new();
        private readonly ConcurrentDictionary<Socket, BuddyConn> _conns = new();
        // token (body[2:6] do RET_LOGIN) -> conexao: correlaciona o eco UDP do cliente com a sessao TCP.
        private readonly ConcurrentDictionary<uint, BuddyConn> _byToken = new();
        private int _tokenSeq;

        public BuddyServer(params int[] ports) => _ports = ports;

        public void Start()
        {
            foreach (int port in _ports)
            {
                var l = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                l.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                l.Bind(new IPEndPoint(IPAddress.Any, port));
                l.Listen(64);
                _listeners.Add(l);
                _ = Task.Run(() => AcceptLoopAsync(l, port, _cts.Token));

                // UDP na MESMA porta: o cliente faz sendto do token p/ getpeername(tcp) = ip:porta do servidor.
                var u = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                u.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.Bind(new IPEndPoint(IPAddress.Any, port));
                _udpSockets.Add(u);
                _ = Task.Run(() => UdpLoopAsync(u, port, _cts.Token));
                Log.Ok("buddy", "ouvindo na porta {0} (TCP+UDP)", port);
            }
        }

        public void Stop()
        {
            try { _cts.Cancel(); } catch { }
            foreach (var l in _listeners) { try { l.Close(); } catch { } }
            foreach (var u in _udpSockets) { try { u.Close(); } catch { } }
        }

        private async Task AcceptLoopAsync(Socket l, int port, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Socket sock;
                try { sock = await l.AcceptAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log.Error("buddy", "accept {0}: {1}", port, ex.Message); continue; }
                _ = Task.Run(() => HandleAsync(sock, port, ct));
            }
        }

        // ---- brokering P2P: aprende o endpoint UDP do cliente pelo eco do token ----------

        private async Task UdpLoopAsync(Socket u, int port, CancellationToken ct)
        {
            byte[] buf = new byte[2048];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested)
            {
                SocketReceiveFromResult r;
                try { r = await u.ReceiveFromAsync(buf.AsMemory(), SocketFlags.None, any, ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log.Error("buddy", "udp {0}: {1}", port, ex.Message); continue; }
                if (r.ReceivedBytes >= 4 && r.RemoteEndPoint is IPEndPoint ep)
                    OnUdpRegister(BinaryPrimitives.ReadUInt32LittleEndian(buf), ep);
            }
        }

        /// <summary>Eco do token (4B) via UDP = o cliente registrando seu endpoint P2P. Aprende o ip:porta de
        /// origem e anuncia a presença (agora com endereço real) aos amigos online.</summary>
        private void OnUdpRegister(uint token, IPEndPoint ep)
        {
            if (!_byToken.TryGetValue(token, out var conn)) return;   // token desconhecido (ruido UDP)
            if (conn.UdpEndpoint != null) return;                     // ja' registrado
            conn.UdpEndpoint = ep;
            Log.Ok("buddy", "[{0}] endpoint P2P registrado: {1} (nick {2})", conn.Ip, ep, conn.Nick ?? "?");
            AnnouncePresence(conn, online: true);
        }

        private async Task HandleAsync(Socket sock, int port, CancellationToken ct)
        {
            string ip = (sock.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
            var conn = new BuddyConn(sock, ip);
            _conns[sock] = conn;
            Log.Info("buddy", "[{0}] conectado em :{1}", ip, port);
            byte[] buf = new byte[65536];
            int have = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await sock.ReceiveAsync(new ArraySegment<byte>(buf, have, buf.Length - have), SocketFlags.None, ct);
                    if (n <= 0) break;
                    have += n;

                    int consumed = 0;
                    while (have - consumed >= BuddyProtocol.HeaderSize)
                    {
                        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(consumed));
                        if (size < BuddyProtocol.HeaderSize) { Log.Warn("buddy", "[{0}] size invalido {1}", ip, size); return; }
                        if (have - consumed < size) break;

                        ushort cd = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(consumed + 2));
                        byte[] payload = new byte[size - BuddyProtocol.HeaderSize];
                        Array.Copy(buf, consumed + 4, payload, 0, payload.Length);
                        consumed += size;
                        Dispatch(conn, cd, payload);
                    }
                    if (consumed > 0) { Array.Copy(buf, consumed, buf, 0, have - consumed); have -= consumed; }
                    else if (have == buf.Length) { Log.Warn("buddy", "[{0}] buffer cheio", ip); break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (Exception ex) { Log.Error("buddy", "[{0}] {1}", ip, ex.Message); }
            finally { OnDisconnect(conn); try { sock.Close(); } catch { } Log.Info("buddy", "[{0}] desconectado", ip); }
        }

        private void Dispatch(BuddyConn conn, ushort cd, byte[] payload)
        {
            Log.Debug("buddy", "[{0}] RECV CD=0x{1:x4} ({2}) len={3}", conn.Ip, cd, BuddyProtocol.Name(cd), payload.Length);
            switch (cd)
            {
                case BuddyProtocol.SVC_PRECREDENTIAL: SendPrecredential(conn.Sock, conn.Ip); break;
                case BuddyProtocol.SVC_LOGIN:         _ = HandleLoginAsync(conn); break;

                // confirmacoes que destravam fluxos do cliente (wRtc=0 = sucesso). SET_NICK sincroniza o
                // nick do messenger (sem o RET_SET_NICK o jogo trava a VENDA no shop). Ver BuddyProtocol.
                case BuddyProtocol.SVC_ADD_BUDDY:     ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_ADD_BUDDY); break;
                case BuddyProtocol.SVC_REMOVE_BUDDY:  ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_REMOVE_BUDDY); break;
                case BuddyProtocol.SVC_GROUP_BUDDY:   ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_GROUP_BUDDY); break;
                case BuddyProtocol.SVC_RENAME_GROUP:  ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_RENAME_GROUP); break;
                case BuddyProtocol.SVC_GROUP_DEL:     ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_GROUP_DEL); break;
                case BuddyProtocol.SVC_GROUP_CHG:     ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_GROUP_CHG); break;
                case BuddyProtocol.SVC_SMS_SEND:      ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_SMS_SEND); break;
                case BuddyProtocol.SVC_SET_NICK:      ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_SET_NICK); break;
                case BuddyProtocol.SVC_SET_EXTUSER:   ReplyOk(conn.Sock, conn.Ip, BuddyProtocol.RET_SET_EXTUSER); break;
                case BuddyProtocol.SVC_GROUP_GETLIST: SendGroupListEmpty(conn.Sock, conn.Ip); break;

                default:
                    Log.Debug("buddy", "[{0}] CD 0x{1:x4} ({2}) — sem handler (stub)", conn.Ip, cd, BuddyProtocol.Name(cd));
                    break;
            }
        }

        // ---- login: identidade + lista de amigos + token P2P ----------------------------

        /// <summary>RET_LOGIN: resolve a identidade (account pelo IP), carrega a buddylist e monta a lista,
        /// embutindo o token P2P (body[2:6]). A presença NÃO sai aqui: espera o cliente ecoar o token via UDP
        /// (OnUdpRegister) p/ ter o endpoint real. Sem identidade (sem sessao world p/ o IP) = lista vazia.</summary>
        private async Task HandleLoginAsync(BuddyConn conn)
        {
            string? account = await _db.ResolveAccountByIpAsync(conn.Ip);
            conn.Account = account;
            conn.Buddies = account != null ? await _db.LoadBuddiesAsync(account) : new List<BuddyEntry>();
            conn.Nick = account != null ? (await _db.GetNickByAccountAsync(account) ?? account) : null;
            conn.LoggedIn = true;
            conn.Token = (uint)Interlocked.Increment(ref _tokenSeq);
            _byToken[conn.Token] = conn;

            Send(conn.Sock, BuddyProtocol.RET_LOGIN, BuddyFrames.LoginList(conn.Buddies, conn.Token));
            Log.Ok("buddy", "[{0}] RET_LOGIN: conta='{1}' nick='{2}' token={3} — {4} amigo(s): [{5}]",
                conn.Ip, account ?? "?", conn.Nick ?? "?", conn.Token, conn.Buddies.Count,
                string.Join(", ", conn.Buddies.ConvertAll(b => b.Nick)));
        }

        private void OnDisconnect(BuddyConn conn)
        {
            _conns.TryRemove(conn.Sock, out _);
            if (conn.Token != 0) _byToken.TryRemove(conn.Token, out _);
            if (conn.LoggedIn && conn.Nick != null && conn.UdpEndpoint != null) AnnouncePresence(conn, online: false);
        }

        /// <summary>Brokering de presença (NTF_USER_STATE). Quando <paramref name="conn"/> registra o endpoint
        /// (entra) ou cai (sai): avisa cada amigo online que tem conn na lista, com o ip:porta P2P de conn; e,
        /// na entrada, conta a conn os amigos online que já têm endpoint. Casa amizade por NICK.</summary>
        private void AnnouncePresence(BuddyConn conn, bool online)
        {
            if (conn.Nick == null) return;
            byte[]? ip4 = conn.UdpEndpoint?.Address.GetAddressBytes();
            ushort port = (ushort)(conn.UdpEndpoint?.Port ?? 0);
            foreach (var other in _conns.Values)
            {
                if (ReferenceEquals(other, conn) || !other.LoggedIn || other.Nick == null) continue;
                if (other.HasBuddy(conn.Nick))
                    Send(other.Sock, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(conn.Nick, online, ip4, port));
                if (online && conn.HasBuddy(other.Nick) && other.UdpEndpoint != null)
                    Send(conn.Sock, BuddyProtocol.NTF_USER_STATE, BuddyFrames.UserState(
                        other.Nick, true, other.UdpEndpoint.Address.GetAddressBytes(), (ushort)other.UdpEndpoint.Port));
            }
        }

        // ---- handshake / respostas simples ---------------------------------------------

        private static void SendPrecredential(Socket sock, string ip)
        {
            var ep = sock.RemoteEndPoint as IPEndPoint;
            using var p = new PacketWriter();
            uint addr = ep != null ? BitConverter.ToUInt32(ep.Address.MapToIPv4().GetAddressBytes(), 0) : 0;
            // RET_PRECREDENTIAL EXIGE 8 bytes (`if (len == 8)`), senao o cliente nao manda o SVC_LOGIN.
            p.WriteUInt32(addr);
            p.WriteUInt32((uint)(ep?.Port ?? 0));
            Send(sock, BuddyProtocol.RET_PRECREDENTIAL, p.ToArray());
            Log.Info("buddy", "[{0}] RET_PRECREDENTIAL enviado (8B)", ip);
        }

        /// <summary>RET_GROUP_GETLIST (0x3151): [u16 wRtc=0][u16 count=0] — lista de grupos vazia.</summary>
        private static void SendGroupListEmpty(Socket sock, string ip)
        {
            using var p = new PacketWriter();
            p.WriteWord(0);
            p.WriteWord(0);
            Send(sock, BuddyProtocol.RET_GROUP_GETLIST, p.ToArray());
            Log.Debug("buddy", "[{0}] RET_GROUP_GETLIST (vazio)", ip);
        }

        /// <summary>Resposta generica de sucesso: RET com [u16 wRtc=0] (o client trata !=0 como falha).</summary>
        private static void ReplyOk(Socket sock, string ip, ushort retCd)
        {
            using var p = new PacketWriter();
            p.WriteWord(0);
            Send(sock, retCd, p.ToArray());
            Log.Debug("buddy", "[{0}] {1} OK", ip, BuddyProtocol.Name(retCd));
        }

        /// <summary>Envia um frame [u16 size][u16 CD][payload], size = total.</summary>
        private static void Send(Socket sock, ushort cd, byte[] payload)
        {
            int size = BuddyProtocol.HeaderSize + payload.Length;
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)size);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), cd);
            Array.Copy(payload, 0, frame, 4, payload.Length);
            try { sock.Send(frame); } catch (Exception ex) { Log.Error("buddy", "send: {0}", ex.Message); }
        }
    }

    /// <summary>Estado de uma conexao do messenger. Chave de rede = <see cref="Nick"/>; <see cref="Token"/>
    /// correlaciona o eco UDP -> <see cref="UdpEndpoint"/> (endpoint P2P aprendido).</summary>
    internal sealed class BuddyConn
    {
        public BuddyConn(Socket sock, string ip) { Sock = sock; Ip = ip; }
        public Socket Sock { get; }
        public string Ip { get; }
        public string? Account { get; set; }
        public string? Nick { get; set; }
        public bool LoggedIn { get; set; }
        public uint Token { get; set; }
        public IPEndPoint? UdpEndpoint { get; set; }
        public List<BuddyEntry> Buddies { get; set; } = new();
        public bool HasBuddy(string? nick) =>
            nick != null && Buddies.Exists(b => string.Equals(b.Nick, nick, StringComparison.OrdinalIgnoreCase));
    }
}
