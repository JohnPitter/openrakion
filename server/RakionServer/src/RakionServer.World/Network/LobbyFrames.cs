using System.Text;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Síntese dos frames W->C da cadeia LOBBY -> CANAL -> SALA -> STAGE. Funções PURAS
    /// (parâmetros -> bytes): sem estado de sessão e sem I/O, montadas com <see cref="PacketWriter"/>
    /// a partir da ESTRUTURA documentada + CONSTANTES DE PROTOCOLO + DADO DE SESSÃO por parâmetro.
    /// Não há blob de captura: nenhum byte é replay de uma sessão gravada.
    ///
    /// Classificação dos bytes — DERIVADA POR DIFF DE 3 SESSÕES REAIS (mitm A/B/C, capture_field_entry):
    ///  - CONSTANTE DE PROTOCOLO (igual em todas as sessões): opcodes, "dchannel01", o nonce 0x10 e o
    ///    token do 0x36b (idênticos A=B), e o framing (00/01/markers). Ficam fixos aqui.
    ///  - DADO DE SESSÃO: userid e nome (do domínio); tempo restante do stage (duração da sala, dur+3).
    ///  - HANDLE AUTORAL DO SERVIDOR: os campos que VARIARAM entre as sessões A/B/C são PONTEIROS do
    ///    worldserv original (ex.: 0x14/0x36 = `648c0509`/`648ce806`/`3c8c5607` ~ 0x07xxxxxx) que o
    ///    cliente apenas ECOA (nunca dereferencia — é memória de outro processo). Por isso NÃO se
    ///    replicam: entram pelo <paramref name="fieldHandle"/> — o handle do NOSSO field-objeto, gerado
    ///    por sessão (<see cref="ClientSession"/>). As demais regiões de token derivam dele (<see cref="Fill"/>).
    ///
    /// POS-CLEAR (0x4a/1f_clear/1e_clear/36_clear): só há 1 sessão capturada (sem stage-clear nos diffs),
    /// então a fronteira handle×constante segue por ANALOGIA à cadeia de entrada — confirmar com captura
    /// de um stage-clear no original.
    /// </summary>
    public static class LobbyFrames
    {
        /// <summary>Canal único do mundo offline (0x1e/0x1e_clear). Texto C terminado em nul.</summary>
        public const string ChannelName = "dchannel01";

        /// <summary>0x10 GameGuard challenge (16B). IDÊNTICO em A=B=C -> constante (cliente GG-neutralizado
        /// não valida o conteúdo; é um nonce de handshake de forma fixa).</summary>
        private static readonly byte[] GameGuardChallenge =
            { 0x4e, 0x95, 0xdd, 0x29, 0xce, 0x3a, 0x55, 0xdb, 0x20, 0xb6, 0xad, 0x97, 0xa6, 0x5c, 0xc0, 0x1c };

        /// <summary>Token do 0x36b (9B): arma a lista de games. IDÊNTICO em A=B -> constante de protocolo.</summary>
        private static readonly byte[] GameListArmToken = { 0x6a, 0x4d, 0xca, 0xaf, 0x2b, 0x4b, 0x3d, 0x9f, 0xa5 };

        private static readonly byte[] FramePad7 = { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };

        // ---- BUILDERS ----------------------------------------------------------------------------------

        /// <summary>0x10 GameGuard challenge: [10 00][nonce 16B][00 x6]. Tudo constante.</summary>
        public static byte[] GameGuard()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x10);
            w.WriteBytes(GameGuardChallenge);
            w.WriteBytes(new byte[6]);
            return w.ToArray();
        }

        /// <summary>0x14 spawn/start-ack. RE FUN_0041fef0 (@0x41fef0, linha 755): a resposta REAL tem LEN=3
        /// = [14 00][status=0]. O scoring é armado no HANDLER (FUN_0040ac30), NÃO neste frame; o
        /// [20000000][handle] do blob antigo era LIXO DE STACK (padding do bloco de 12B). 3 reais + zero-pad.</summary>
        public static byte[] SpawnAck()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x14);
            w.WriteByte(0);                 // status (0 = sucesso)
            w.WriteBytes(new byte[9]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x36 game-list (FUN_00422c90 FieldPlayerList): no original é a LISTA DE SALAS, de tamanho
        /// VARIÁVEL. Nosso STUB mantém a forma capturada que destrava a game-list/botão Create no cliente
        /// (lista vazia p/ solo); a cauda (handle) é inerte. Reimplementar a serialização da lista seria
        /// grande e sem ganho funcional aqui — fica stub.</summary>
        public static byte[] GameListArm(byte[] fieldHandle)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteWord(0);
            w.WriteUInt32(0x20);
            w.WriteBytes(Fill(fieldHandle, 4));
            return w.ToArray();
        }

        /// <summary>0x36b: [36 00][00][token 9B]. Arma a lista de games (1x; remandar reabre o polling).</summary>
        public static byte[] GameListArmExtra()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteByte(0);
            w.WriteBytes(GameListArmToken);
            return w.ToArray();
        }

        /// <summary>0x1f info de sessão. Entrada e SAÍDA pós-clear divergem só na cauda. O userid (do
        /// domínio) casa com o 0x0C; a cauda variável é handle do field (Fill do fieldHandle).</summary>
        public static byte[] SessionInfo(ushort userId, string name, byte[] fieldHandle, bool clear)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1f);
            w.WriteWord(0);
            w.WriteWord(userId);
            WriteName(w, name);
            w.WriteBytes(FramePad7);
            if (clear)
            {
                w.WriteByte(0x02);                  // marker da saída
                w.WriteByte(0);
                w.WriteBytes(Fill(fieldHandle, 7)); // handle (era ptr do original)
            }
            else
            {
                w.WriteByte(fieldHandle[0]);        // byte de sessão (era 08/06 por sessão)
                w.WriteWord(userId);
                w.WriteBytes(new byte[6]);
            }
            return w.ToArray();
        }

        /// <summary>0x1e lista de canais ("dchannel01"). userid e nome do domínio; o token de entrada (8B)
        /// é handle do field. A saída pós-clear não tem handle (só const + userid).</summary>
        public static byte[] ChannelList(ushort userId, string name, byte[] fieldHandle, bool clear)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1e);
            w.WriteWord(0x0100);
            w.WriteCString(ChannelName);
            w.WriteWord(0);
            w.WriteWord(userId);
            WriteName(w, name);
            w.WriteBytes(FramePad7);
            if (clear)
            {
                w.WriteWord(0x0030);
                w.WriteWord(0x0325);
                w.WriteWord(userId);
                w.WriteWord(0);
            }
            else w.WriteBytes(Fill(fieldHandle, 8)); // token de entrada = handle do field
            return w.ToArray();
        }

        /// <summary>0x3b ack de criação de sala. RE FUN_00423580 (@0x423580, linha 264): LEN=5 =
        /// [3b 00][status=0][seat:u16] (seat = slot do field-objeto, 0 no solo). O [538b003600007f] do blob
        /// antigo era LIXO DE STACK. 5 reais + zero-pad.</summary>
        public static byte[] RoomCreateAck()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3b);
            w.WriteByte(0);                 // status (0 = sucesso)
            w.WriteWord(0);                 // seat:u16 (slot do field-objeto; 0 no solo)
            w.WriteBytes(new byte[7]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x43 ack de start do match: [43 00][00][handle 5B][3b 00 00 00]. A cauda 3b = opcode da
        /// sala ecoado (constante A=B).</summary>
        public static byte[] MatchStartAck(byte[] fieldHandle)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x43);
            w.WriteByte(0);
            w.WriteBytes(Fill(fieldHandle, 5));
            w.WriteByte(0x3b); w.WriteBytes(new byte[3]);
            return w.ToArray();
        }

        /// <summary>0x48 tempo restante do stage. RE FUN_00408440 (@0x408440, linha 1696): LEN=9 =
        /// [48 00][01][RemainingSec=dur+3 u16][this+0x2c0=0][this+0x2c1=0][this+0x122][this+0x123]. Os 2
        /// últimos são índices de best-player (14 14 na captura). O [a0 0f] final era LIXO DE STACK.
        /// RemainingSec vem do domínio (duração da sala). Referência: 432s -> 435 (0x01b3). 9 reais + zero-pad.</summary>
        public static byte[] RemainingTime(int durationSec)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x48);
            w.WriteByte(1);
            w.WriteWord(durationSec + 3);
            w.WriteWord(0);                         // this+0x2c0 / this+0x2c1
            w.WriteByte(0x14); w.WriteByte(0x14);   // this+0x122 / this+0x123 = índices best-player (LEN=9 acaba aqui)
            w.WriteBytes(new byte[3]);              // padding do bloco de 12B (era lixo de stack: 00 a0 0f)
            return w.ToArray();
        }

        /// <summary>0x4a resultado do StageClear (tela de Rank). RE do builder FUN_00405a90 (@0x405a90):
        /// a mensagem REAL tem 6 bytes (uVar6=6) — [4a 00][tipo=this+0x2bd, eco do request=0x02]
        /// [this+0x2bf=1][this+0x2c0=contador de clears][this+0x2c1=0]. Os 6 bytes seguintes do bloco
        /// cifrado de 12B eram LIXO DE STACK na captura antiga (uStack além dos 6 escritos), NÃO handle —
        /// por isso zero-pad determinístico em vez do `737624007c04` capturado.</summary>
        public static byte[] StageClearResult()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x4a);
            w.WriteByte(0x02);                                     // tipo StageClear (eco do param_1 do request)
            w.WriteByte(0x01); w.WriteByte(0x01); w.WriteByte(0);  // estado do field (this+0x2bf/0x2c0/0x2c1)
            w.WriteBytes(new byte[6]);                             // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x36 refresh da lista de games (pós-clear / FieldLeaveGame): [36 00][00][handle 4B][00][02][00 00 00].</summary>
        public static byte[] GameListRefresh(byte[] fieldHandle)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteByte(0);
            w.WriteBytes(Fill(fieldHandle, 4));
            w.WriteByte(0); w.WriteByte(0x02); w.WriteBytes(new byte[3]);
            return w.ToArray();
        }

        /// <summary>0x44 fim de partida (volta ao game room): [44 00][reason][00][01 00 00 00][nome da sala].
        /// O nome vem do domínio (último campo, tamanho variável seguro).</summary>
        public static byte[] MatchEnd(byte reason, string roomName)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x44);
            w.WriteByte(reason);
            w.WriteByte(0);
            w.WriteUInt32(1);
            w.WriteBytes(Encoding.ASCII.GetBytes(roomName ?? ""));
            return w.ToArray();
        }

        /// <summary>0x0e OnRecvSuccessUDP: ecoa o ENDPOINT DO CLIENTE (ip+porta big-endian) nos dois slots
        /// + trailer zeros. Modo local-scored (suprime o combo se mandarmos as portas do server).</summary>
        public static byte[] Endpoints(byte[] ip, ushort port)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x0e);
            w.WriteByte(0);
            for (int slot = 0; slot < 2; slot++)
            {
                w.WriteBytes(ip);
                w.WriteByte((byte)(port >> 8));     // big-endian
                w.WriteByte((byte)(port & 0xff));
            }
            w.WriteBytes(new byte[9]);              // trailer zeros
            return w.ToArray();
        }

        /// <summary>Expande o handle (ponteiro autoral do servidor, 4B) para n bytes — preenche as regiões
        /// de token que o cliente apenas ecoa. O valor é irrelevante p/ o cliente; só precisa ser estável
        /// na sessão (mesmo handle no 0x14 e 0x36).</summary>
        private static byte[] Fill(byte[] handle, int n)
        {
            byte[] o = new byte[n];
            for (int i = 0; i < n; i++) o[i] = handle[i % handle.Length];
            return o;
        }

        private static void WriteName(PacketWriter w, string name)
        {
            byte[] nb = Encoding.ASCII.GetBytes(name ?? "");
            w.WriteByte(nb.Length > 0 ? nb[0] : 0);
            w.WriteByte(nb.Length > 1 ? nb[1] : 0);
        }
    }
}
