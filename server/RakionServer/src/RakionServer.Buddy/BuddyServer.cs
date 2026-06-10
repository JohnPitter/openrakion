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
    /// Buddy Server. Escuta nas portas de BuddyServer (8500) e BuddyCenter (8504),
    /// fala o frame [u16 size][u16 CD][payload] do Buddy2.dll e completa o
    /// handshake de login (PRECREDENTIAL -> LOGIN) com lista de amigos vazia, de
    /// modo que o client prossiga normalmente. Demais comandos sao logados (stub).
    /// </summary>
    public sealed class BuddyServer
    {
        private readonly int[] _ports;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Socket> _listeners = new();

        // Registro de presenca (P2P): clients online -> seu endpoint. O buddy server
        // faz o brokering de enderecos (NTF_VIP_IPPORT/NTF_USER_STATE); o P2P em si
        // (P2P_SVC_*) corre direto client-a-client por UDP, fora do servidor.
        private readonly ConcurrentDictionary<Socket, IPEndPoint> _online = new();

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
                Log.Ok("buddy", "ouvindo na porta {0}", port);
                _ = Task.Run(() => AcceptLoopAsync(l, port, _cts.Token));
            }
        }

        public void Stop()
        {
            try { _cts.Cancel(); } catch { }
            foreach (var l in _listeners) { try { l.Close(); } catch { } }
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
                        // size e u16 (sempre <= 65535 < MaxPacket 0x13880), entao so o limite inferior importa
                        if (size < BuddyProtocol.HeaderSize) { Log.Warn("buddy", "[{0}] size invalido {1}", ip, size); return; }
                        if (have - consumed < size) break;

                        ushort cd = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(consumed + 2));
                        byte[] payload = new byte[size - BuddyProtocol.HeaderSize];
                        Array.Copy(buf, consumed + 4, payload, 0, payload.Length);
                        consumed += size;

                        Dispatch(sock, ip, cd, payload);
                    }
                    if (consumed > 0) { Array.Copy(buf, consumed, buf, 0, have - consumed); have -= consumed; }
                    else if (have == buf.Length) { Log.Warn("buddy", "[{0}] buffer cheio", ip); break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (Exception ex) { Log.Error("buddy", "[{0}] {1}", ip, ex.Message); }
            finally { OnPeerOffline(sock); try { sock.Close(); } catch { } Log.Info("buddy", "[{0}] desconectado", ip); }
        }

        // ---- presenca / P2P (NTF_VIP_IPPORT / NTF_USER_STATE) ----

        /// <summary>Registra o client como online e troca enderecos P2P com os demais.</summary>
        private void OnPeerOnline(Socket sock)
        {
            if (sock.RemoteEndPoint is not IPEndPoint ep) return;
            _online[sock] = ep;
            // envia ao novo client o endereco de cada peer online, e notifica os peers do novo.
            foreach (var kv in _online)
            {
                if (kv.Key == sock) continue;
                Send(sock, BuddyProtocol.NTF_VIP_IPPORT, VipPayload(kv.Value));   // peers -> novo
                Send(kv.Key, BuddyProtocol.NTF_VIP_IPPORT, VipPayload(ep));       // novo -> peers
                Send(kv.Key, BuddyProtocol.NTF_USER_STATE, StatePayload(1));      // online
            }
            Log.Info("buddy", "[{0}] online ({1} peers)", ep, _online.Count);
        }

        /// <summary>Remove o client e notifica os demais (NTF_USER_STATE offline).</summary>
        private void OnPeerOffline(Socket sock)
        {
            if (!_online.TryRemove(sock, out _)) return;
            foreach (var kv in _online)
                Send(kv.Key, BuddyProtocol.NTF_USER_STATE, StatePayload(0)); // offline
        }

        /// <summary>NTF_VIP_IPPORT: [u32 ip][u16 port] do peer (p/ o client abrir P2P direto).</summary>
        private static byte[] VipPayload(IPEndPoint ep)
        {
            using var p = new PacketWriter();
            p.WriteUInt32(BitConverter.ToUInt32(ep.Address.MapToIPv4().GetAddressBytes(), 0));
            p.WriteWord(ep.Port);
            return p.ToArray();
        }

        private static byte[] StatePayload(byte state)
        {
            using var p = new PacketWriter();
            p.WriteByte(state); // 1=online, 0=offline
            return p.ToArray();
        }

        private void Dispatch(Socket sock, string ip, ushort cd, byte[] payload)
        {
            Log.Debug("buddy", "[{0}] RECV CD=0x{1:x4} ({2}) len={3}", ip, cd, BuddyProtocol.Name(cd), payload.Length);
            switch (cd)
            {
                case BuddyProtocol.SVC_PRECREDENTIAL:
                    // RET_PRECREDENTIAL: devolve o endereco visto do client (p/ P2P).
                    SendPrecredential(sock, ip);
                    break;

                case BuddyProtocol.SVC_LOGIN:
                    // RET_LOGIN sucesso: result=0, 0 amigos (payload > 7 bytes -> caminho de sucesso).
                    SendLoginOk(sock, ip);
                    OnPeerOnline(sock);   // registra presenca + brokering de enderecos P2P
                    break;

                // comandos de buddy/grupo: respondem o RET correspondente com wRtc=0 (sucesso).
                // A persistencia (tabela buddylist / usergameinfo.buddyname) e RE incremental.
                case BuddyProtocol.SVC_ADD_BUDDY:    ReplyOk(sock, ip, BuddyProtocol.RET_ADD_BUDDY); break;
                case BuddyProtocol.SVC_REMOVE_BUDDY: ReplyOk(sock, ip, BuddyProtocol.RET_REMOVE_BUDDY); break;
                case BuddyProtocol.SVC_GROUP_BUDDY:  ReplyOk(sock, ip, BuddyProtocol.RET_GROUP_BUDDY); break;
                case BuddyProtocol.SVC_RENAME_GROUP: ReplyOk(sock, ip, BuddyProtocol.RET_RENAME_GROUP); break;
                case BuddyProtocol.SVC_GROUP_DEL:    ReplyOk(sock, ip, BuddyProtocol.RET_GROUP_DEL); break;
                case BuddyProtocol.SVC_GROUP_CHG:    ReplyOk(sock, ip, BuddyProtocol.RET_GROUP_CHG); break;
                case BuddyProtocol.SVC_SMS_SEND:     ReplyOk(sock, ip, BuddyProtocol.RET_SMS_SEND); break;

                default:
                    Log.Debug("buddy", "[{0}] CD 0x{1:x4} ({2}) — handler nao reconstruido (stub)", ip, cd, BuddyProtocol.Name(cd));
                    break;
            }
        }

        private static void SendPrecredential(Socket sock, string ip)
        {
            var ep = sock.RemoteEndPoint as IPEndPoint;
            using var p = new PacketWriter();
            uint addr = ep != null ? BitConverter.ToUInt32(ep.Address.MapToIPv4().GetAddressBytes(), 0) : 0;
            p.WriteUInt32(addr);
            p.WriteWord(ep?.Port ?? 0);
            Send(sock, BuddyProtocol.RET_PRECREDENTIAL, p.ToArray());
            Log.Info("buddy", "[{0}] RET_PRECREDENTIAL enviado", ip);
        }

        private static void SendLoginOk(Socket sock, string ip)
        {
            using var p = new PacketWriter();
            p.WriteWord(0); // result = 0 (sucesso)
            p.WriteWord(0); // reservado
            p.WriteWord(0); // buddyCount = 0 (lista vazia -> pula o loop de amigos)
            p.WriteWord(0); // padding (payload > 7 bytes p/ entrar no caminho de sucesso)
            Send(sock, BuddyProtocol.RET_LOGIN, p.ToArray());
            Log.Ok("buddy", "[{0}] RET_LOGIN OK (0 amigos)", ip);
        }

        /// <summary>Resposta generica de sucesso: RET com [u16 wRtc=0] (o client trata !=0 como falha).</summary>
        private static void ReplyOk(Socket sock, string ip, ushort retCd)
        {
            using var p = new PacketWriter();
            p.WriteWord(0); // wRtc = 0 (sucesso)
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
}
