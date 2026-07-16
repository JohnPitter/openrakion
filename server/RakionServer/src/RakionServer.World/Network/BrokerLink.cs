using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Canal IPC (UDP) do world com o(s) broker(s). Reconstruido a partir do
    /// BrokenServer (Program.OnIPC + Servers.IPCServer):
    ///   - O world anuncia-se com um pacote "ServerInfo" (opcode 257) que o broker
    ///     le e marca o servidor como online ("Server: X change to online").
    ///   - O broker pode pedir RequestServerInfo (cmd 0) -> respondemos com 257.
    ///
    /// O broker casa o servidor pela PORTA DE ORIGEM do pacote UDP contra o
    /// `ipcport` configurado, entao ligamos o socket na porta IPC do world.
    /// </summary>
    public sealed class BrokerLink
    {
        public readonly record struct Stats(int MaxUser, int CurUser, int MaxRoom, int CurRoom);

        private readonly WorldConfig _cfg;
        private readonly Func<Stats> _stats;
        private readonly Func<IPEndPoint, byte[], byte[]?>? _udpPort1Handshake;
        private readonly string _code; // cifra IPC (vazia por padrao)
        private readonly Random _rnd = new();

        private Socket? _udp;
        private CancellationTokenSource? _cts;
        private readonly byte[] _rx = new byte[8192];

        public BrokerLink(
            WorldConfig cfg,
            Func<Stats> stats,
            Func<IPEndPoint, byte[], byte[]?>? udpPort1Handshake = null,
            string code = "")
        {
            _cfg = cfg;
            _stats = stats;
            _udpPort1Handshake = udpPort1Handshake;
            _code = code;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // liga na porta IPC do world (o broker casa pela porta de origem)
            _udp.Bind(new IPEndPoint(IPAddress.Any, _cfg.Port));
            Log.Ok("broker", "IPC UDP ligado na porta {0}", _cfg.Port);

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token));

            AnnounceAll();
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _udp?.Close(); } catch { }
        }

        /// <summary>Envia o heartbeat de info para todos os brokers configurados.</summary>
        public void AnnounceAll()
        {
            foreach (var (ip, port) in _cfg.Brokers)
                SendServerInfo(ip, port);
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            // broker expira o servidor apos 5 min; reanunciamos a cada 60s
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(60), ct); }
                catch (OperationCanceledException) { break; }
                AnnounceAll();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested && _udp != null)
            {
                SocketReceiveFromResult res;
                try
                {
                    res = await _udp.ReceiveFromAsync(_rx, SocketFlags.None, any, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { break; }

                if (res.ReceivedBytes < 4)
                    continue;

                byte[] data = new byte[res.ReceivedBytes];
                Buffer.BlockCopy(_rx, 0, data, 0, res.ReceivedBytes);

                // O cliente manda gameplay UDP (port1, marker 0x0201) NESTA porta tambem
                // (40708). Roteia p/ echo de gameplay (result byte 0 = port1) antes do IPC.
                if (data.Length >= GameplayUdpHandshake.PacketSize &&
                    data[0] == 0x01 && data[1] == 0x02)
                {
                    EchoGameplayUdp(data, (IPEndPoint)res.RemoteEndPoint);
                    continue;
                }

                if (!string.IsNullOrEmpty(_code))
                    IpcCodec.Decode(data, _code);

                try { HandlePacket(data, (IPEndPoint)res.RemoteEndPoint); }
                catch (Exception ex) { Log.Error("broker", "IPC recv: {0}", ex.Message); }
            }
        }

        /// <summary>
        /// Echo do ping UDP de gameplay (FUN_00425d80/fa0): responde 0x0201 com o byte de
        /// resultado por porta (0=port1/40708, 1=port2/40709), p/ OnRecvSuccessUDP registrar
        /// os 2 endpoints. O dispatcher remove 7 bytes; echoData = body+0xc = pkt[19:23].
        /// </summary>
        private void EchoGameplayUdp(byte[] packet, IPEndPoint from)
        {
            byte[]? echo = _udpPort1Handshake?.Invoke(from, packet);
            if (echo == null) return;
            try { _udp!.SendTo(echo, from); }
            catch (Exception ex) { Log.Debug("broker", "echo udp port1: {0}", ex.Message); }
        }

        private void HandlePacket(byte[] data, IPEndPoint from)
        {
            // GUARD: a porta de IPC (40708) tambem recebe o gameplay UDP port1 do CLIENTE. O 0x0201 ja' foi
            // roteado p/ echo antes daqui; QUALQUER outro UDP do cliente (ex.: 127.0.0.1:2301) caia neste
            // parser e era mis-interpretado como IPC — gerava ServerInfo espurio (byte[3]==0), "cmd
            // desconhecido" (2/3) e ate over-read do login (byte[3]==1). Validamos a CRC do protocolo
            // (BCRC = XOR de todos os bytes; com o proprio CRC no fim, o XOR total e' 0) p/ aceitar SO'
            // pacote bem-formado do broker. IPC minimo = [u16 port][u8 rnd][u8 cmd][u16 len][u8 crc] = 7B
            // (= o RequestServerInfo). Gameplay/forjado tem XOR != 0 -> descartado em silencio.
            if (data.Length < 7 || !IpcCodec.VerifyCrc(data))
            {
                Log.Debug("broker", "UDP nao-IPC descartado ({0}B de {1})", data.Length, from);
                return;
            }

            // formato broker->world: [u16 port][u8 rnd][u8 cmd][u16 len][payload][u8 crc]
            var r = new PacketReader(data);
            r.UInt16();          // porta do broker (ignorada)
            r.Byte();            // rnd
            byte cmd = r.Byte();

            switch (cmd)
            {
                case Protocol.Ipc.RequestServerInfo:
                    Log.Debug("broker", "RequestServerInfo de {0} — respondendo", from);
                    SendServerInfo(from.Address.ToString(), from.Port);
                    break;

                default:
                    Log.Debug("broker", "IPC cmd desconhecido {0} de {1}", cmd, from);
                    break;
            }
        }

        private void SendServerInfo(string ip, int port)
        {
            Stats s = _stats();
            using var p = new PacketWriter();
            p.WriteWord(Protocol.Ipc.OpServerInfo);     // [u16 257]
            p.WriteByte(_rnd.Next(1, 250));             // rnd  -.
            p.WriteByte(Protocol.Ipc.ResponseServerInfo);//cmd 2 |-> broker faz Skip(4)
            p.WriteWord(9);                             // len -'
            p.WriteByte(_cfg.ServerId);                 // [u8 serverId]
            p.WriteWord(s.MaxRoom);                     // maxSalas
            p.WriteWord(s.CurRoom);                     // usedSala
            p.WriteWord(s.MaxUser);                     // maxSlots
            p.WriteWord(s.CurUser);                     // usedSlots
            p.AddCrc();
            SendRaw(ip, port, p.ToArray());
            Log.Debug("broker", "ServerInfo -> {0}:{1} (users {2}/{3}, salas {4}/{5})",
                ip, port, s.CurUser, s.MaxUser, s.CurRoom, s.MaxRoom);
        }

        private void SendRaw(string ip, int port, byte[] data)
        {
            if (!string.IsNullOrEmpty(_code))
                IpcCodec.Encode(data, _code);
            try
            {
                _udp?.SendTo(data, new IPEndPoint(IPAddress.Parse(ip), port));
            }
            catch (Exception ex)
            {
                Log.Error("broker", "SendRaw {0}:{1}: {2}", ip, port, ex.Message);
            }
        }
    }
}
