using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Cliente World headless para validação dinâmica via backend. Fala o mesmo
    /// transporte do cliente real: frame `[u16 size][AES(content)]`, cifra AES-128 do
    /// canal lobby/field (<see cref="PacketCrypto.EnableWorldDefault"/>) e o plaintext
    /// cliente->servidor `[u16 opcode][u16 seq][data]`.
    ///
    /// Do lado servidor->cliente o conteúdo decifrado é `[u16 msgType][data]` (canais
    /// SendMessage/SendLobby) ou o frame de login já pronto começando pelo próprio
    /// opcode (SendEncryptedFrame: 0x0C/0x0D/0x10...). O leitor guarda o conteúdo bruto
    /// decifrado; o teste classifica pelo primeiro byte/word conforme o caso.
    /// </summary>
    public sealed class HeadlessWorldClient : IAsyncDisposable
    {
        private readonly Socket _sock;
        private readonly PacketCrypto _crypto = new();
        private readonly BlockingCollection<byte[]> _rx = new(new ConcurrentQueue<byte[]>());
        private readonly CancellationTokenSource _cts = new();
        private int _seq;

        public string Name { get; }
        public IReadOnlyList<byte[]> Received => _receivedLog;
        private readonly List<byte[]> _receivedLog = new();

        private HeadlessWorldClient(Socket sock, string name)
        {
            _sock = sock;
            Name = name;
            _crypto.EnableWorldDefault();
        }

        public static async Task<HeadlessWorldClient> ConnectAsync(string host, int port, string name)
        {
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await sock.ConnectAsync(host, port);
            var client = new HeadlessWorldClient(sock, name);
            client._sock.NoDelay = true;
            _ = Task.Run(() => client.ReceiveLoopAsync(client._cts.Token));
            return client;
        }

        /// <summary>Envia um frame cliente->servidor: monta `[u16 opcode][u16 seq][payload]`,
        /// cifra e enquadra com `[u16 size]`. Login (0x0C) usa seq 0 e reseta o contador.</summary>
        public void Send(ushort opcode, byte[] payload)
        {
            ushort seq;
            if (opcode == 0x0C) { _seq = 0; seq = 0; }
            else if (opcode == 0x0F) { seq = 0; }
            else { _seq++; if (_seq > 65000) _seq = 0; seq = (ushort)_seq; }

            byte[] content = new byte[4 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(0), opcode);
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(2), seq);
            Array.Copy(payload, 0, content, 4, payload.Length);

            byte[] body = _crypto.Encrypt(content);
            int size = 2 + body.Length;
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)size);
            Array.Copy(body, 0, frame, 2, body.Length);
            _sock.Send(frame);
        }

        /// <summary>Login v258: `[u8 verifyMode][cstr md5][cstr account][cstr password][u16 tail]`.
        /// verifyMode 0x04 pula o MD5 no login (LoginBypass), suficiente com hash não forçado.</summary>
        public void Login(string account, string password)
        {
            using var w = new PacketWriter();
            w.WriteByte(0x04);       // verifyMode = LoginBypass
            w.WriteCString("");      // md5 (ignorado no bypass)
            w.WriteCString(account);
            w.WriteCString(password);
            w.WriteWord(0);          // tail u16
            Send(0x0C, w.ToArray());
        }

        /// <summary>Char-select 0x14: `[int32 characterId]`. Promove a sessão a FieldLobby e
        /// entra no channel-lobby (o servidor responde ack + 0x1f/0x1e).</summary>
        public void SelectCharacter(int characterId)
        {
            byte[] payload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(payload, characterId);
            Send(0x14, payload);
        }

        /// <summary>Parâmetros de criação de sala (0x3b).</summary>
        public readonly record struct RoomSpec(
            string Name, byte Map, byte Mode, byte Rounds, ushort DurationSec,
            byte FragLimit, byte MinLevel, byte MaxLevel)
        {
            // Enum de domínio: Golem=1, Deathmatch=2, TeamDeath=3, Boss=4.
            // fragLimit: Deathmatch (2) em 13..30; TeamDeath (3) em 20..50.
            public static RoomSpec Golem(string name) => new(name, 0, 1, 1, 432, 0, 1, 99);
            public static RoomSpec Deathmatch(string name) => new(name, 0, 2, 1, 432, 20, 1, 99);
            public static RoomSpec TeamDeath(string name) => new(name, 0, 3, 1, 432, 25, 1, 99);
            public static RoomSpec Boss(string name) => new(name, 0, 4, 1, 432, 0, 1, 99);
        }

        /// <summary>Cria sala competitiva (0x3b). Layout:
        /// `[cstr name][cstr pass][cstr desc][u8 map][u8 mode][u8 rounds][u16 dur][u8 frag][u8 minLvl][u8 maxLvl][u8 rangeCode]`.</summary>
        public void CreateRoom(RoomSpec spec)
        {
            using var w = new PacketWriter();
            w.WriteCString(spec.Name);   // ≤ 0x28
            w.WriteCString("");          // senha ≤ 8
            w.WriteCString("");          // descrição ≤ 0xc8
            w.WriteByte(spec.Map);
            w.WriteByte(spec.Mode);
            w.WriteByte(spec.Rounds);
            w.WriteWord(spec.DurationSec);
            w.WriteByte(spec.FragLimit);
            w.WriteByte(spec.MinLevel);
            w.WriteByte(spec.MaxLevel);
            w.WriteByte(0);              // levelRangeCode
            Send(0x3b, w.ToArray());
        }

        public void CreateGolemRoom(string name) => CreateRoom(RoomSpec.Golem(name));

        /// <summary>Entra numa sala (0x38): `[u16 fieldId][cstr password]`.</summary>
        public void JoinRoom(ushort fieldId, string password = "")
        {
            using var w = new PacketWriter();
            w.WriteWord(fieldId);
            w.WriteCString(password);
            Send(0x38, w.ToArray());
        }

        /// <summary>Marca ready/not-ready na sala (0x3d): `[u8 ready]`.</summary>
        public void SetReady(bool ready) => Send(0x3d, new[] { (byte)(ready ? 1 : 0) });

        /// <summary>Troca de time na sala (0x3e): sem payload; move para o bloco de 10 assentos oposto.</summary>
        public void ChangeTeam() => Send(0x3e, Array.Empty<byte>());

        /// <summary>Master inicia a partida (0x43): sem payload.</summary>
        public void StartMatch() => Send(0x43, Array.Empty<byte>());

        /// <summary>Spawn no stage (0x4b): primeiro 0x4b da entrada inicia o relógio da partida.
        /// Payload real do cliente é grande; o servidor só usa o gatilho, então mandamos vazio.</summary>
        public void SpawnField() => Send(0x4b, new byte[72]);

        /// <summary>Resultado de stage solo (0x53). Layout:
        /// `[u8 stage][u8 rank][u8 count][count×u16 slots][u32 exp][u32 gold][u32 cell0..2]`.</summary>
        public void SendStageResult(byte stage, byte rank, uint exp, uint gold)
        {
            using var w = new PacketWriter();
            w.WriteByte(stage);
            w.WriteByte(rank);
            w.WriteByte(0);          // count de cells = 0
            w.WriteUInt32(exp);
            w.WriteUInt32(gold);
            w.WriteUInt32(0).WriteUInt32(0).WriteUInt32(0); // cell exp 0..2
            Send(0x53, w.ToArray());
        }

        // ---- UDP de gameplay ------------------------------------------------

        private Socket? _udp;
        private readonly BlockingCollection<byte[]> _udpRx = new(new ConcurrentQueue<byte[]>());

        public IPEndPoint UdpLocalEndpoint => (IPEndPoint)_udp!.LocalEndPoint!;

        /// <summary>Abre o socket UDP local do cliente (porta efêmera de loopback).</summary>
        public void OpenUdp()
        {
            _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _ = Task.Run(() => UdpReceiveLoopAsync(_cts.Token));
        }

        /// <summary>Handshake da porta de gameplay (0x0202, 23 bytes): registra o endpoint UDP
        /// do jogador no servidor. slot e sessionKey vêm do estado da sessão (o cliente real os
        /// lê do 0x0C; aqui o teste os fornece do servidor).</summary>
        public void UdpHandshake(int serverGamePort, ushort slot, uint sessionKey)
        {
            byte[] p = new byte[GameplayUdpHandshakeSize];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), 0x0202); // Port2Type
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(7), slot);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(9), sessionKey);
            IPEndPoint local = UdpLocalEndpoint;
            local.Address.MapToIPv4().GetAddressBytes().CopyTo(p, 13);
            p[17] = (byte)(local.Port >> 8);
            p[18] = (byte)local.Port;            // porta big-endian
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(19), 0x12345678); // echoData
            _udp!.SendTo(p, new IPEndPoint(IPAddress.Loopback, serverGamePort));
        }

        /// <summary>Datagrama de movimento 0x030A (26 bytes) com o assento de origem no offset 6.</summary>
        public byte[] SendMove(int serverGamePort, byte sourceSeat, short x, short y, short z)
        {
            byte[] p = new byte[26];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), 0x030a);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), 1); // sequence
            p[6] = sourceSeat;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(7), 16); // deltaMs
            p[9] = 0;   // state/echo empacotados
            p[10] = 0;  // actionCode
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(11), x);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(13), y);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(15), z);
            _udp!.SendTo(p, new IPEndPoint(IPAddress.Loopback, serverGamePort));
            return p;
        }

        /// <summary>Datagrama de ANIMAÇÃO/ataque 0x0311 (10 bytes): kind 1 = Attack.</summary>
        public byte[] SendAttack(int serverGamePort, byte sourceSeat, byte kind = 1, byte arg0 = 0)
        {
            byte[] p = new byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), 0x0311);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), 2); // sequence
            p[6] = sourceSeat;
            p[7] = 0;      // sourceEcho
            p[8] = kind;   // 0=Normal, 1=Attack, 2=Damage(precisa estendido)
            p[9] = arg0;
            _udp!.SendTo(p, new IPEndPoint(IPAddress.Loopback, serverGamePort));
            return p;
        }

        /// <summary>Datagrama de SYNC de estado 0x030F (14 bytes).</summary>
        public byte[] SendSync(int serverGamePort, byte sourceSeat, byte lifeState = 1)
        {
            byte[] p = new byte[14];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), 0x030f);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), 3); // sequence
            p[6] = sourceSeat;
            p[7] = 0;           // sourceEcho
            p[8] = lifeState;
            _udp!.SendTo(p, new IPEndPoint(IPAddress.Loopback, serverGamePort));
            return p;
        }

        public byte[] WaitForUdp(Func<byte[], bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
                if (_udpRx.TryTake(out byte[]? pkt, 200) && predicate(pkt))
                    return pkt;
            throw new TimeoutException($"[{Name}] nenhum datagrama UDP casou o predicado em {timeout.TotalSeconds:0.#}s");
        }

        private const int GameplayUdpHandshakeSize = 23;

        private async Task UdpReceiveLoopAsync(CancellationToken ct)
        {
            byte[] buf = new byte[2048];
            var any = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (!ct.IsCancellationRequested && _udp != null)
                {
                    SocketReceiveFromResult r = await _udp.ReceiveFromAsync(buf, SocketFlags.None, any, ct);
                    if (r.ReceivedBytes <= 0) continue;
                    byte[] pkt = new byte[r.ReceivedBytes];
                    Array.Copy(buf, pkt, r.ReceivedBytes);
                    _udpRx.Add(pkt, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Espera um frame decifrado cujo primeiro byte casa com <paramref name="firstByte"/>
        /// (ex.: 0x0C login, 0x0D tabela, 0x10 GameGuard).</summary>
        public byte[] WaitForFirstByte(byte firstByte, TimeSpan timeout)
            => WaitFor(f => f.Length > 0 && f[0] == firstByte, timeout);

        public byte[] WaitFor(Func<byte[], bool> predicate, TimeSpan timeout)
        {
            lock (_receivedLog)
                foreach (byte[] earlier in _receivedLog)
                    if (predicate(earlier)) return earlier;

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_rx.TryTake(out byte[]? frame, 200))
                {
                    lock (_receivedLog) _receivedLog.Add(frame);
                    if (predicate(frame)) return frame;
                }
            }
            throw new TimeoutException($"[{Name}] nenhum frame casou o predicado em {timeout.TotalSeconds:0.#}s " +
                $"(recebidos {_receivedLog.Count})");
        }

        public int DrainReceived()
        {
            while (_rx.TryTake(out byte[]? frame, 50))
                lock (_receivedLog) _receivedLog.Add(frame);
            return _receivedLog.Count;
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[32768];
            int have = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await _sock.ReceiveAsync(
                        new ArraySegment<byte>(buffer, have, buffer.Length - have), SocketFlags.None, ct);
                    if (n <= 0) break;
                    have += n;

                    int consumed = 0;
                    while (have - consumed >= 2)
                    {
                        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(consumed));
                        if (size < 4 || size > buffer.Length) return;
                        if (have - consumed < size) break;

                        int contentLen = size - 2;
                        byte[] content = (contentLen >= 16 && contentLen % 16 == 0)
                            ? _crypto.Decrypt(buffer.AsSpan(consumed + 2, contentLen))
                            : buffer.AsSpan(consumed + 2, contentLen).ToArray();
                        consumed += size;
                        _rx.Add(content, ct);
                    }

                    if (consumed > 0)
                    {
                        Array.Copy(buffer, consumed, buffer, 0, have - consumed);
                        have -= consumed;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { }
            try { _sock.Shutdown(SocketShutdown.Both); } catch { }
            try { _sock.Close(); } catch { }
            try { _udp?.Close(); } catch { }
            await Task.CompletedTask;
        }
    }
}
