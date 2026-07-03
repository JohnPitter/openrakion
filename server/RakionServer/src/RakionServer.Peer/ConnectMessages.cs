using System;

namespace RakionServer.Peer
{
    /// <summary>
    /// Builders dos corpos das CNetworkMessages do CONNECT REMOTE (handshake do peer, Start_AtClient_t §4/§5
    /// do blueprint). SÍNTESE pura do domínio + constantes de protocolo NOMEADAS (nunca replay de blob): o
    /// frame é montado do estado, validado byte-a-byte contra a spec nos golden tests.
    ///
    /// REQ_CONNECTREMOTESESSIONSTATE (type 7) — layout CRAVADO pelo DISASM da engine.dll do Rakion
    /// (ConnectRemoteSessionState @0x36105f30) e reconciliado com a fonte SE1 (Start_AtClient_t):
    ///   [INDEX 'VTAG'=0x56544147][INDEX version=10000][INDEX op=6][CTString modName][CTString pasw]
    ///   [INDEX nLocalPlayers][CSessionSocketParams = 3×INDEX]
    /// Os 3 primeiros campos são os GATES de aceitação do host, TODOS comparados como DWORD (4B) no disasm:
    ///   @0x36105ffa  cmp [ebp-0x18],0x56544147   (magic)
    ///   @0x36106021  cmp [ebp-0x20],0x2710       (version = 10000)
    ///   @0x36106031  cmp edi,0x6                 (op = 6 — comparação de 32 bits, logo INDEX, não u8)
    /// (Fontes: tagv_connect_decode.out.txt §2 + p2p_handshake_decode.out.txt — disasm cravado.)
    ///
    /// DIVERGÊNCIA vs SE1 STOCK — RESOLVIDA (FATO): a SE1 aberta manda [INDEX 'VTAG'][INDEX iMajor][INDEX iMinor]
    /// (SessionState.cpp:318, build 10000/10). O RAKION É FORK: o 3º campo NÃO é iMinor=10, é op=6 (connect) —
    /// o disasm compara `cmp edi,0x6` (DWORD) e Start_AtServer_t @0x3610a020 confirma `and ecx,0x3f; cmp cl,0x6`.
    /// Logo o layout REAL do Rakion = 3 INDEX de 4B (magic/version/op). version=10000 coincide com _SE_BUILD_MAJOR
    /// stock; o iMinor stock (10) foi substituído pelo op=6 no fork.
    ///
    /// A CAPTURA NÃO PODE CONFIRMAR ISTO NO FIO (e não precisa): o REQ_CONNECTREMOTE é corpo de STREAM RELIABLE da
    /// engine, que roda em LOOPBACK dentro de cada cliente (CServer P2P local). O magic/version/op NUNCA trafega em
    /// datagrama (0 hits em 6275+3707 frames; max 31B/frame). A fonte da verdade aqui é o DISASM, não a captura.
    /// </summary>
    public static class ConnectMessages
    {
        /// <summary>
        /// Magic INDEX('VTAG') = 0x56544147. O char-literal 'VTAG' do MSVC x86 é o ULONG 0x56544147; escrito
        /// por &lt;&lt; (memcpy LE) sai no fio como bytes 47 54 41 56. O disasm compara o u32 já lido contra
        /// 0x56544147 (@0x36105ffa) — então escrevemos o VALOR 0x56544147 como INDEX/u32 LE.
        /// </summary>
        public const uint VtagMagic = 0x56544147;

        /// <summary>version: INDEX(4B) = 10000 (disasm cmp [ebp-0x20],0x2710). Coincide com _SE_BUILD_MAJOR stock.</summary>
        public const int ProtocolVersion = 10000;

        /// <summary>op: INDEX(4B) = 6 (disasm cmp edi,0x6, DWORD). 3º campo após magic+version; substitui o iMinor stock.</summary>
        public const int ConnectOp = 6;

        /// <summary>
        /// Corpo do REQ_CONNECTREMOTESESSIONSTATE (type 7) que o PEER manda ao host (§4.1). O bot é 1 jogador
        /// local. <paramref name="modName"/> = o MESMO mod do servidor (self-host: o mod do worldserv; se vazio,
        /// ""). <paramref name="password"/> = senha da sessão ("" se none). <paramref name="sockParams"/> =
        /// CSessionSocketParams (loopback por default). Devolve o frame completo (1º byte = tipo 7).
        /// </summary>
        public static byte[] BuildConnectRemoteRequest(
            string modName,
            string password,
            SessionSocketParams sockParams,
            int localPlayers = 1)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.ReqConnectRemoteSessionState);
            w.WriteIndex(unchecked((int)VtagMagic));   // [INDEX 'VTAG'] gate 1 (magic, DWORD)
            w.WriteIndex(ProtocolVersion);              // [INDEX version=10000] gate 2 (DWORD)
            w.WriteIndex(ConnectOp);                    // [INDEX op=6] gate 3 (DWORD; cmp edi,0x6)
            w.WriteCString(modName ?? "");              // [CTString modName] gate 4 (== mod do host)
            w.WriteCString(password ?? "");             // [CTString pasw]
            w.WriteIndex(localPlayers);                 // [INDEX nLocalPlayers] (1 p/ o bot)
            sockParams.WriteTo(w);                      // [CSessionSocketParams 3×INDEX]
            return w.ToArray();
        }

        /// <summary>
        /// Corpo de uma mensagem que carrega só o byte de tipo (REQ_STATEDELTA 9, REQ_CRCLIST 11): o peer as
        /// manda vazias e o host responde (§5, passos S4/S7). Reuso p/ não duplicar o "begin+toarray".
        /// </summary>
        public static byte[] BuildTypeOnly(NetworkMessageType type)
        {
            using var w = NetMessage.BeginWrite(type);
            return w.ToArray();
        }

        /// <summary>
        /// Corpo do REP_CRCCHECK (type 13) que o peer responde ao host (§5.5): [ULONG crc][INDEX lastSeq].
        /// crc = CRCT_MakeCRCForFiles_t dos arquivos que o host listou (em self-host == os do bot → bate por
        /// construção). lastSeq = ses_iLastProcessedSequence (começa em -1/0: nada processado ainda).
        /// O CÁLCULO do crc (CrcEngine) e a leitura da lista do host são da fase do SessionHandshake (P5).
        /// </summary>
        public static byte[] BuildCrcCheckReply(uint crc, int lastProcessedSequence)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.RepCrcCheck);
            w.WriteU32(crc);                       // [ULONG crc]
            w.WriteIndex(lastProcessedSequence);   // [INDEX ses_iLastProcessedSequence]
            return w.ToArray();
        }
    }
}
