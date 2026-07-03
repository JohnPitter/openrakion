using System;
using System.Collections.Generic;

namespace RakionServer.World.CharSelect
{
    /// <summary>
    /// Resumo de um personagem para a lista do char-select (frame 0x0C). Contrato de borda —
    /// montado do estado de domínio, nunca a entidade crua. Stats na ordem do wire:
    /// hit1,hit2,hit3,hit4,chit,hp,ap,attackspeed,speed,maxcp.
    /// </summary>
    public sealed record CharSummary
    {
        public string Name { get; init; } = "";
        public byte Slot { get; init; }
        public byte Class { get; init; }            // TODO: offset no record ainda não mapeado (capturas tinham Class=0)
        public byte Level { get; init; } = 1;
        public uint Exp { get; init; }
        public byte LevelPoint { get; init; }
        public uint Win { get; init; }
        public uint Lose { get; init; }
        public uint Draw { get; init; }
        public ushort[] Stats { get; init; } = new ushort[10];
        public ushort[] Equip { get; init; } = new ushort[7];
        public byte[] Enhance { get; init; } = new byte[7];
        public ushort[] Quickslot { get; init; } = new ushort[6];
        public byte[] StageRanks { get; init; } = Array.Empty<byte>();   // 1 byte/stage, indexado por stage (arr[N]=stage N; índice 0 não usado)
    }

    /// <summary>
    /// Lista de chars + dados de conta para o frame 0x0C do login. <see cref="SessionHandle"/> é o handle
    /// gerado pelo servidor (vai no header @13 e nos acks 0x2c/0x2d).
    /// </summary>
    public sealed record CharList
    {
        public string AccountName { get; init; } = "";
        public uint UserId { get; init; }              // @7-8 do header (u16)
        public byte SlotCount { get; init; } = 4;      // @64 = nº de slots da conta (usergameinfo.slot)
        public uint Gold { get; init; }
        public uint Cash { get; init; }                // @60 do 0x0C (cash / EX points)
        public ushort PowerLevelPoint { get; init; }
        public byte[] SessionHandle { get; init; } = new byte[4];
        // @9-10 do header: handle de sessão do PEER — o cliente o ecoa no connect P2P (0x0201) e na abertura do
        // canal reliable (role=0xff). Precisa ser ÚNICO por sessão: dois clientes com o mesmo valor colidem e o
        // canal reliable não abre (2º humano vira observador). 0x042E = default legado (1 cliente); ver SpawnStageForHumans.
        public ushort SessionPeerHandle { get; init; } = 0x042E;
        public IReadOnlyList<CharSummary> Chars { get; init; } = Array.Empty<CharSummary>();
    }
}
