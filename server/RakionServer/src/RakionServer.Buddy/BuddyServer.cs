using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Buddy Server (messenger F9). Escuta TCP nas portas BuddyServer (8500) e BuddyCenter (8504), fala o frame
    /// [u16 size][u16 CD][payload] do Buddy2.dll e implementa o messenger:
    ///   - HANDSHAKE   : PRECREDENTIAL -> LOGIN (abre a janela);
    ///   - IDENTIDADE  : o login é cifrado/opaco -> resolve a conexão por IP (messenger_session que o World grava);
    ///   - LISTA       : RET_LOGIN com a buddylist real (registros 0x94);
    ///   - PRESENÇA    : NTF_USER_STATE aos amigos online, casado por nick (ver BuddyServer.Presence);
    ///   - DELETE      : SVC_REMOVE_BUDDY persiste a remoção recíproca;
    ///   - PM          : brokering P2P PURO (token UDP -> NTF_USER_STATE com endereço; a msg corre UDP direto, SEM relay).
    /// O add da amizade é MUDO no cliente -> nasce no World (handler 0x19), não aqui.
    /// </summary>
    public sealed partial class BuddyServer
    {
        private readonly int[] _ports;
        private readonly BuddyDatabase _db;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Socket> _listeners = new();

        // conexões logadas: por nick (id de rede do messenger) p/ presença + PM, por token p/ o UDP register, e
        // por account p/ distinguir 2+ clientes do mesmo IP (cada um pega a conta ainda não atrelada a uma conexão).
        private readonly ConcurrentDictionary<string, BuddyConn> _byNick = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<ushort, BuddyConn> _byToken = new();
        private readonly ConcurrentDictionary<string, BuddyConn> _byAccount = new(StringComparer.OrdinalIgnoreCase);
        private int _nextToken;

        public BuddyServer(BuddyDatabase db, params int[] ports) { _db = db; _ports = ports; }

        public void Start()
        {
            foreach (int port in _ports)
            {
                var l = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                l.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                l.Bind(new IPEndPoint(IPAddress.Any, port));
                l.Listen(64);
                _listeners.Add(l);
                Log.Ok("buddy", "ouvindo (TCP) na porta {0}", port);
                _ = Task.Run(() => AcceptLoopAsync(l, port, _cts.Token));
                StartUdpListener(port);   // brokering P2P na MESMA porta UDP (ver BuddyServer.Presence)
            }
            StartBuddyListSync();   // refresh VIVO da lista (o add nasce no World; ver BuddyServer.Sync)
        }

        public void Stop()
        {
            try { _cts.Cancel(); } catch { }
            foreach (var l in _listeners) { try { l.Close(); } catch { } }
            StopUdp();
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

        private async Task HandleAsync(Socket sock, int port, CancellationToken ct)
        {
            string ip = (sock.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
            var conn = new BuddyConn(sock, ip);
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

                        await DispatchAsync(conn, cd, payload);
                    }
                    if (consumed > 0) { Array.Copy(buf, consumed, buf, 0, have - consumed); have -= consumed; }
                    else if (have == buf.Length) { Log.Warn("buddy", "[{0}] buffer cheio", ip); break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (Exception ex) { Log.Error("buddy", "[{0}] {1}", ip, ex.Message); }
            finally { OnConnClosed(conn); try { sock.Close(); } catch { } Log.Info("buddy", "[{0}] desconectado", ip); }
        }

        private async Task DispatchAsync(BuddyConn conn, ushort cd, byte[] payload)
        {
            Log.Debug("buddy", "[{0}] RECV CD=0x{1:x4} ({2}) len={3}", conn.Ip, cd, BuddyProtocol.Name(cd), payload.Length);
            switch (cd)
            {
                case BuddyProtocol.SVC_PRECREDENTIAL: SendPrecredential(conn); break;
                case BuddyProtocol.SVC_LOGIN:         await HandleLoginAsync(conn); break;
                case BuddyProtocol.SVC_REMOVE_BUDDY:  await HandleRemoveAsync(conn, payload); break;
                case BuddyProtocol.SVC_USER_STATE:    HandleUserStateQuery(conn, payload); break;

                // add é MUDO no cliente (nasce no World 0x19); se vier, confirma p/ não travar a UI.
                case BuddyProtocol.SVC_ADD_BUDDY:     ReplyOk(conn, BuddyProtocol.RET_ADD_BUDDY); break;
                case BuddyProtocol.SVC_GROUP_BUDDY:   ReplyOk(conn, BuddyProtocol.RET_GROUP_BUDDY); break;
                case BuddyProtocol.SVC_RENAME_GROUP:  ReplyOk(conn, BuddyProtocol.RET_RENAME_GROUP); break;
                case BuddyProtocol.SVC_GROUP_DEL:     ReplyOk(conn, BuddyProtocol.RET_GROUP_DEL); break;
                case BuddyProtocol.SVC_GROUP_CHG:     ReplyOk(conn, BuddyProtocol.RET_GROUP_CHG); break;
                case BuddyProtocol.SVC_SMS_SEND:      ReplyOk(conn, BuddyProtocol.RET_SMS_SEND); break;

                // SET_NICK/SET_EXTUSER: o nick do messenger é trocado pelo WORLD (0x15); aqui só confirma (a venda
                // no shop trava sem o RET_SET_NICK). GROUP_GETLIST: sem grupos.
                case BuddyProtocol.SVC_SET_NICK:      ReplyOk(conn, BuddyProtocol.RET_SET_NICK); break;
                case BuddyProtocol.SVC_SET_EXTUSER:   ReplyOk(conn, BuddyProtocol.RET_SET_EXTUSER); break;
                // SET_EXTLIST (0x3110): NÃO responder. O ACK 0x3111 síncrono realimenta o dispatcher do
                // buddy2.dll (recv ACK -> manda 0x3110 -> recv ACK ...) num loop APERTADO; com os frames de
                // ~80KB do dispatcher, ~12 níveis estouram a pilha (crash __chkstk 0xc00000fd @buddy2+0x17845,
                // char-select). Sem o ACK o loop apertado quebra (o cliente cai no ping por-frame, benigno).
                // Consumir e ignorar. TODO: handshake correto p/ o cliente limpar o bit4 de +0x140d4 e parar.
                case BuddyProtocol.SVC_SET_EXTLIST:   break;
                case BuddyProtocol.SVC_GROUP_GETLIST: SendGroupListEmpty(conn); break;

                // PM é P2P PURO -> o tunnel TCP (relay) NÃO é usado. Se o cliente cair no fallback, ignora.
                case BuddyProtocol.SVC_TUNNEL_PACKET:
                    Log.Debug("buddy", "[{0}] SVC_TUNNEL_PACKET ignorado (PM é P2P direto, sem relay)", conn.Ip);
                    break;

                default:
                    Log.Debug("buddy", "[{0}] CD 0x{1:x4} ({2}) — sem handler (stub)", conn.Ip, cd, BuddyProtocol.Name(cd));
                    break;
            }
        }

        /// <summary>SVC_LOGIN: resolve a identidade por IP (messenger_session do World), carrega a buddylist e
        /// responde RET_LOGIN com a lista real + token de brokering. Registra a conexão e dispara a presença. Sem
        /// identidade (cliente não logado no world / DB down) -> RET_LOGIN vazio (a janela abre, sem amigos).</summary>
        private async Task HandleLoginAsync(BuddyConn conn)
        {
            var sessions = await _db.ResolveSessionsByIpAsync(conn.Ip);
            // 2+ clientes no MESMO IP (sem 2º PC): cada conexão pega a conta ainda NÃO atrelada a uma conexão buddy
            // ativa; se todas já estão atreladas, cai na mais recente.
            var pick = sessions.Find(s => !_byAccount.ContainsKey(s.Account));
            if (pick.Account == null && sessions.Count > 0) pick = sessions[0];
            if (pick.Account == null)
            {
                Send(conn, BuddyProtocol.RET_LOGIN, BuddyFrames.LoginList(0, Array.Empty<BuddyEntry>()));
                Log.Warn("buddy", "[{0}] LOGIN sem identidade (sem messenger_session) -> lista vazia", conn.Ip);
                return;
            }
            conn.Account = pick.Account;
            conn.Nick = pick.Nick.Length > 0 ? pick.Nick : pick.Account;
            var buddies = await _db.LoadBuddyListAsync(conn.Account);
            conn.BuddyNicks = buddies.ConvertAll(b => b.Nick);
            conn.Token = NextToken();
            conn.LoggedIn = true;
            _byNick[conn.Nick] = conn;
            _byToken[conn.Token] = conn;
            _byAccount[conn.Account] = conn;

            byte[] loginList = BuddyFrames.LoginList(conn.Token, buddies);
            // TRACE byte-a-byte do RET_LOGIN: sem servidor original p/ capturar (openrakion-server = nossa
            // reconstrução), o ground truth é a RE do Buddy2.dll; logar o que EMITIMOS crava o offset do
            // nome truncado ('He'/'roi2') vs o parse do cliente (buddyrec_out: id@0, nome UTF-16@0x14).
            Log.Info("buddy", "[{0}] RET_LOGIN {1}B nicks=[{2}] hex={3}", conn.Ip, loginList.Length,
                string.Join(",", conn.BuddyNicks), Convert.ToHexString(loginList));
            Send(conn, BuddyProtocol.RET_LOGIN, loginList);
            Log.Ok("buddy", "[{0}] LOGIN '{1}' (nick '{2}') — {3} amigo(s), token={4}",
                conn.Ip, conn.Account, conn.Nick, buddies.Count, conn.Token);
            AnnounceOnline(conn);
        }

        /// <summary>SVC_REMOVE_BUDDY (0x3002): o cliente manda o nick do amigo a remover (ASCII null-terminated).
        /// Persiste a remoção RECÍPROCA (buddylist) e confirma. RE: P2P_SVC_REMOVEBUDDY (0xc043) -> FUN_10001190
        /// envia 0x3002 ao servidor (buddyfull_out l.445).</summary>
        private async Task HandleRemoveAsync(BuddyConn conn, byte[] payload)
        {
            Log.Info("buddy", "[{0}] SVC_REMOVE payload {1}B hex={2}", conn.Ip, payload.Length, Convert.ToHexString(payload));
            string nick = AsciiZ(payload);
            if (conn.Account.Length > 0 && nick.Length > 0)
            {
                await _db.RemoveBuddyByNickAsync(conn.Account, nick);
                var fresh = await _db.LoadBuddyListAsync(conn.Account);   // recarrega p/ a presença não ver o ex-amigo
                conn.BuddyNicks = fresh.ConvertAll(b => b.Nick);
            }
            ReplyOk(conn, BuddyProtocol.RET_REMOVE_BUDDY);
            Log.Ok("buddy", "[{0}] REMOVE_BUDDY '{1}' (dono '{2}')", conn.Ip, nick, conn.Account);
        }

        // ---- envio / helpers ----------------------------------------------------------------------

        private static void SendPrecredential(BuddyConn conn)
        {
            var ep = conn.Sock.RemoteEndPoint as IPEndPoint;
            using var p = new PacketWriter();
            // O cliente (RET_PRECREDENTIAL) exige payload de exatamente 8 bytes [u32 ip][u32 port], senão não
            // manda o SVC_LOGIN e o messenger nunca loga (trava a venda no shop, "name for messenger changed").
            uint addr = ep != null ? BitConverter.ToUInt32(ep.Address.MapToIPv4().GetAddressBytes(), 0) : 0;
            p.WriteUInt32(addr);
            p.WriteUInt32((uint)(ep?.Port ?? 0));
            Send(conn, BuddyProtocol.RET_PRECREDENTIAL, p.ToArray());
            Log.Info("buddy", "[{0}] RET_PRECREDENTIAL (8B)", conn.Ip);
        }

        /// <summary>RET_GROUP_GETLIST (0x3151): [u16 wRtc=0][u16 count=0] — sem grupos.</summary>
        private static void SendGroupListEmpty(BuddyConn conn)
        {
            using var p = new PacketWriter();
            p.WriteWord(0);
            p.WriteWord(0);
            Send(conn, BuddyProtocol.RET_GROUP_GETLIST, p.ToArray());
        }

        /// <summary>Resposta genérica de sucesso: RET com [u16 wRtc=0] (o cliente trata !=0 como falha).</summary>
        private static void ReplyOk(BuddyConn conn, ushort retCd)
        {
            using var p = new PacketWriter();
            p.WriteWord(0);
            Send(conn, retCd, p.ToArray());
            Log.Debug("buddy", "[{0}] {1} OK", conn.Ip, BuddyProtocol.Name(retCd));
        }

        /// <summary>Envia um frame [u16 size][u16 CD][payload]. Serializa por conexão (a presença, disparada por
        /// OUTRA conexão, concorre com a resposta do próprio loop — sends concorrentes corromperiam o framing).</summary>
        private static void Send(BuddyConn conn, ushort cd, byte[] payload)
        {
            int size = BuddyProtocol.HeaderSize + payload.Length;
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)size);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), cd);
            Array.Copy(payload, 0, frame, 4, payload.Length);
            try { lock (conn.SendLock) { conn.Sock.Send(frame); } }
            catch (Exception ex) { Log.Error("buddy", "[{0}] send: {1}", conn.Ip, ex.Message); }
        }

        /// <summary>Lê uma string ASCII null-terminated do início de um payload (resto = lixo de stack do cliente).</summary>
        private static string AsciiZ(byte[] data)
        {
            int nul = Array.IndexOf(data, (byte)0);
            int len = nul >= 0 ? nul : data.Length;
            return len > 0 ? Encoding.ASCII.GetString(data, 0, len) : "";
        }

        /// <summary>Token de brokering único por conexão (!= 0). Wrap em 16 bits (colisão exigiria 65535 sessões).</summary>
        private ushort NextToken()
        {
            ushort t;
            do { t = (ushort)Interlocked.Increment(ref _nextToken); } while (t == 0);
            return t;
        }
    }
}
