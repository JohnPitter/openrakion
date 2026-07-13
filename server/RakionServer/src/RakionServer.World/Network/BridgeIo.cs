using System.IO;
using System.Text;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Contrato de arquivos com a ponte de injeção no cliente (<c>bot_render.dll</c>). A ponte roda DENTRO do
    /// <c>rakion.exe</c> e fura o muro do peer por dentro: lê <c>bot_pos.txt</c> (posições dos bots →
    /// <c>SetPlacement</c> nativo = o bot ANDA na tela). Infra pura de I/O; o domínio entrega os números.
    /// Mecanismo PROVADO in-game (ver memória bot-movimento-injecao-cliente-real): loopback de arquivo a ~20Hz.
    /// </summary>
    internal static class BridgeIo
    {
        private const string Dir = @"C:\temp";
        private static readonly string BotPosPath = Path.Combine(Dir, "bot_pos.txt");
        static BridgeIo()
        {
            try { Directory.CreateDirectory(Dir); } catch { /* ponte é best-effort; servidor segue sem ela */ }
        }

        /// <summary>Escreve as posições dos bots vivos (uma linha <c>"slot x z yaw"</c> por bot). A ponte mapeia
        /// o slot ao <c>CPlayer</c> pelo heap e reposiciona. Coords de mundo da engine — o MESMO espaço do 0x30a
        /// do humano que a IA persegue, então a saída da IA (<c>bot.X/Z</c>) vai direto.</summary>
        internal static void WriteBotPositions(StringBuilder lines)
        {
            try { File.WriteAllText(BotPosPath, lines.ToString()); } catch { }
        }

        private static readonly string BotNpcPath = Path.Combine(Dir, "bot_npc.txt");

        /// <summary>FASE 1b/2 (colisão real via DLL): publica o(s) bot(s) vivo(s) como linha
        /// <c>"owner sub classId x y z heading"</c> p/ a <c>bot_reliable.dll</c> criar/mover um NPC de COLISÃO real
        /// no cliente (AddRemoteGeneralNpc @engine.dll 0x361097a0). Resolve parede+hit de raiz (o fake type-7 só
        /// aproxima). Coords de mundo (mesmo espaço do 0x30a). Ver createnpc-via-dll-rakion-final.</summary>
        internal static void WriteBotNpc(StringBuilder lines)
        {
            try { File.WriteAllText(BotNpcPath, lines.ToString()); } catch { }
        }

        private static readonly string BotSlotsPath = Path.Combine(Dir, "bot_slots.txt");

        /// <summary>Publica os SLOTS dos bots vivos de um stage p/ o abridor do canal reliable
        /// (<c>bot_reliable.dll</c>): a DLL seta <c>player[slot]+0x1d8=1</c> na memória do cliente do humano →
        /// o create-com-colisão que o servidor emite passa pelo dispatcher reliable (que dropa slots com
        /// +0x1d8==0) → o bot vira <c>CEntity</c> real acertável → o contador HIT×N dispara pelo código nativo.
        /// NÃO dirige posição (sem clipping). Só engine.dll (offsets estáveis). Ver docs/pvp-stage-re.md §12.
        /// <paramref name="slots"/> = slots separados por espaço ("" quando não há bot). CAVEAT: arquivo global —
        /// multi-stage simultâneo com slots colidentes é fora de escopo do teste (1 humano/1 field).</summary>
        internal static void WriteBotSlots(string slots)
        {
            try { File.WriteAllText(BotSlotsPath, slots); } catch { }
        }

    }
}
