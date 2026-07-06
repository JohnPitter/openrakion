using System;
using System.Collections.Generic;

namespace RakionServer.Peer
{
    /// <summary>
    /// A camada de BLOCO do game-stream da SE1 (CNetworkStreamBlock dentro de MSG_GAMESTREAMBLOCKS=26, §3.1 do
    /// blueprint). Um MSG_GAMESTREAMBLOCKS carrega N blocos; cada bloco é [INDEX seq][SLONG subSize][subBytes],
    /// onde subBytes começa pelo byte de tipo do sub-bloco (SEQ_ADDPLAYER 22, SEQ_ALLACTIONS 21, ...). O
    /// conjunto trafega como PAYLOAD do <see cref="ReliableStream"/>.
    ///
    /// O bot é o lado CLIENTE: PRECISA só EMITIR seu próprio game-stream (SEQ_ADDPLAYER no load + ações depois)
    /// e CONSUMIR o do host (descarta o conteúdo — não simula mundo, §5.6). Cada bloco que o bot emite recebe um
    /// nº de sequência crescente (ses-like). O parser de entrada é tolerante (frame curto/forjado → para, nunca
    /// estoura — segurança por construção).
    /// </summary>
    public sealed class GameStream
    {
        private int _outSequence;            // nº de sequência do próximo bloco que o bot emite (L2)
        private int _inLastProcessed = -1;   // ses_iLastProcessedSequence: nada processado ainda (começa -1)

        /// <summary>ses_iLastProcessedSequence — vai no REP_CRCCHECK (§5.5). Começa em -1.</summary>
        public int LastProcessedSequence => _inLastProcessed;

        /// <summary>
        /// Empacota UM sub-bloco (corpo já com o byte de tipo, ex.: SEQ_ADDPLAYER) num MSG_GAMESTREAMBLOCKS de
        /// um único bloco: [u8 26][INDEX seq][SLONG subSize][subBytes]. seq cresce por bloco emitido. Devolve o
        /// frame completo (pronto p/ virar payload de um SendMessage do ReliableStream).
        /// </summary>
        public byte[] WrapBlock(ReadOnlySpan<byte> subBlock)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.GameStreamBlocks);
            w.WriteIndex(_outSequence++);   // INDEX nsb_iSequenceNumber
            w.InsertSubMessage(subBlock);   // [SLONG size][bytes] (InsertSubMessage da fonte)
            return w.ToArray();
        }

        /// <summary>
        /// Lê os blocos de um MSG_GAMESTREAMBLOCKS recebido do host, em ordem. Cada item = (seq, subBloco crua);
        /// o 1º byte da subBloco é o tipo (SEQ_ALLACTIONS 21, SEQ_ADDPLAYER 22, ...). Avança
        /// <see cref="LastProcessedSequence"/> até o último seq lido (em loopback a ordem é contígua). O peer
        /// DESCARTA o conteúdo (§5.6) — só precisa do seq p/ o CRC e do cursor avançar. Frame curto → para.
        /// </summary>
        public IReadOnlyList<Block> ReadBlocks(NetMessage message)
        {
            var blocks = new List<Block>();
            if (message.Type != NetworkMessageType.GameStreamBlocks) return blocks;

            var r = message.BeginRead();   // cursor após o byte de tipo 26
            while (r.Remaining > 0)
            {
                int seq = r.ReadIndex();
                if (r.Overflowed) break;
                ReadOnlyMemory<byte> sub = r.ReadSubMessage();
                if (r.Overflowed) break;
                var type = sub.Length > 0 ? (NetworkMessageType)sub.Span[0] : (NetworkMessageType)0;
                blocks.Add(new Block(seq, type, sub));
                if (seq > _inLastProcessed) _inLastProcessed = seq;
            }
            return blocks;
        }

        /// <summary>Um bloco do game-stream: nº de sequência + tipo do sub-bloco + corpo cru (1º byte = tipo).</summary>
        public readonly record struct Block(int Sequence, NetworkMessageType SubType, ReadOnlyMemory<byte> Body);
    }
}
