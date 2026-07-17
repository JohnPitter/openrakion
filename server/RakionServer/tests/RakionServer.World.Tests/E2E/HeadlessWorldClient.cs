using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>Cria sala competitiva (0x3b). Layout:
        /// `[cstr name][cstr pass][cstr desc][u8 map][u8 mode][u8 rounds][u16 dur][u8 frag][u8 minLvl][u8 maxLvl][u8 rangeCode]`.
        /// mode 1 = Golem War (sem restrição de fragLimit).</summary>
        public void CreateGolemRoom(string name, byte map = 0)
        {
            using var w = new PacketWriter();
            w.WriteCString(name);   // ≤ 0x28
            w.WriteCString("");     // senha ≤ 8
            w.WriteCString("");     // descrição ≤ 0xc8
            w.WriteByte(map);       // mapId
            w.WriteByte(1);         // mode = Golem
            w.WriteByte(1);         // rounds
            w.WriteWord(432);       // duração (0x1b0, dentro de 0x122..0x4ba)
            w.WriteByte(0);         // fragLimit (ignorado no Golem)
            w.WriteByte(1);         // minLevel
            w.WriteByte(99);        // maxLevel
            w.WriteByte(0);         // levelRangeCode
            Send(0x3b, w.ToArray());
        }

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

        /// <summary>Master inicia a partida (0x43): sem payload.</summary>
        public void StartMatch() => Send(0x43, Array.Empty<byte>());

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
            await Task.CompletedTask;
        }
    }
}
