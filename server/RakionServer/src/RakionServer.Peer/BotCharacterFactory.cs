using System;
using System.Security.Cryptography;
using System.Text;

namespace RakionServer.Peer
{
    /// <summary>
    /// Traduz a identidade de DOMÍNIO do bot (nome/classe/level/time) para um <see cref="PlayerCharacter"/>
    /// serializável (a borda DTO→bytes). Mantém a regra de negócio FORA do wire: o domínio não conhece o
    /// formato do appearance.
    ///
    /// FATO (fonte SE1): name/team/GUID são triviais — GUID é só 16 bytes estáveis (a SE1 usa CoCreateGuid; aqui
    /// derivamos um GUID DETERMINÍSTICO do nome p/ ser estável entre rounds sem tocar COM/ole32).
    ///
    /// GAP DO APPEARANCE — CRAVADO COMO NÃO-CAPTURÁVEL (mineração 2026-06-28 de tagv_connect_decode.out.txt +
    /// p2p_handshake_decode.out.txt + cli_rx_addplayer.out.txt + p2p_loopback.pcapng): o <see cref="PlayerCharacter.Appearance"/>
    /// de 32B é PROPRIETÁRIO do Rakion E é OFF-WIRE por construção. O CPlayerCharacter viaja no corpo do STREAM
    /// RELIABLE de sessão (REQ_CONNECTPLAYER/SEQ_ADDPLAYER), que NUNCA trafega em datagrama — cada cliente roda um
    /// CServer P2P em LOOPBACK e monta/consome esse corpo na memória local. PROVA: max 31B/frame em 6275+3707
    /// datagramas capturados (UDP relay + P2P-direto); 0 hits do magic/connect no fio. Logo NENHUMA captura de fio
    /// dá os 32B reais — nem a de 2 clientes que o coordenador supôs existir. As únicas vias do appearance real são:
    ///   (1) hook de RUNTIME no operator&gt;&gt;(CPlayerCharacter) injetado no rakion.exe vivo (lê o blob desserializado
    ///       da memória — NÃO há esse dump nos arquivos atuais; cli_rx_addplayer.out.txt é decompile estático), OU
    ///   (2) a 2ª engine REAL (modelo headless §9-12 headless-engine-re.md) entregar o appearance pelo handshake.
    ///
    /// Construímos um appearance NÃO-ZERADO (classe/level no byte 0/1) só para o GOLDEN TEST e para evitar o
    /// modelo-NULL→AV de appearance zerado. RESSALVA DURA (lição-mestra): este placeholder NÃO é forma que o
    /// cliente já viu — NÃO mandar ao cliente humano de produção sem o blob real (cravar via via 1 ou 2 acima).
    /// </summary>
    public static class BotCharacterFactory
    {
        /// <summary>
        /// Monta o CPlayerCharacter do bot a partir do domínio. <paramref name="team"/> vira pc_strTeam ("0"/"1"
        /// por convenção; "" se sem time). O appearance é o placeholder derivado de classe/level (ver nota da
        /// classe): NÃO é garantido válido para o cliente — destinado ao golden test e ao caminho de captura.
        /// </summary>
        public static PlayerCharacter ForBot(string name, byte charClass, byte level, byte team)
        {
            byte[] guid = DeterministicGuid(name);
            byte[] appearance = PlaceholderAppearance(charClass, level);
            string strTeam = team <= 1 ? team.ToString() : "";
            return new PlayerCharacter(name, strTeam, guid, appearance);
        }

        /// <summary>
        /// Mesma identidade (nome/team/GUID) mas com o appearance REAL capturado de um humano (32B). É o caminho
        /// CORRETO p/ não crashar o cliente: o blob veio de uma sessão real, logo é uma forma que o cliente já viu.
        /// Use quando a captura existir (ver GAP no relatório).
        /// </summary>
        public static PlayerCharacter WithCapturedAppearance(string name, byte team, ReadOnlySpan<byte> appearance32)
        {
            byte[] guid = DeterministicGuid(name);
            string strTeam = team <= 1 ? team.ToString() : "";
            return new PlayerCharacter(name, strTeam, guid, appearance32);
        }

        /// <summary>GUID estável de 16B derivado do nome (MD5 truncado): mesmo bot → mesmo GUID entre rounds.</summary>
        private static byte[] DeterministicGuid(string name)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("openrakion-bot:" + (name ?? "")));
            var guid = new byte[PlayerCharacter.GuidSize];
            Array.Copy(hash, guid, PlayerCharacter.GuidSize);   // MD5 = 16B = GuidSize
            return guid;
        }

        /// <summary>
        /// Appearance placeholder de 32B (NÃO-zerado): byte 0 = classe, byte 1 = level, resto 0. HIPÓTESE de
        /// layout — a codificação real do Rakion é desconhecida (GAP). Serve p/ ter um blob não-nulo no golden
        /// test; NÃO é o blob que o cliente espera renderizar.
        /// </summary>
        private static byte[] PlaceholderAppearance(byte charClass, byte level)
        {
            var a = new byte[PlayerCharacter.AppearanceSize];
            a[0] = charClass;
            a[1] = level;
            return a;
        }
    }
}
