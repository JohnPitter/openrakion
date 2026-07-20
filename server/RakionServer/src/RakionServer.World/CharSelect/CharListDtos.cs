using System;
using System.Collections.Generic;
using RakionServer.World.Domain;

namespace RakionServer.World.CharSelect
{
    /// <summary>
    /// Resumo de um personagem para a lista do char-select (frame 0x0C). Contrato de borda —
    /// montado do estado de domínio, nunca a entidade crua. Stats na ordem do wire:
    /// hit1,hit2,hit3,hit4,chit,hp,ap,attackspeed,speed,maxcp.
    /// </summary>
    public sealed record CharSummary
    {
        public int CharacterId { get; init; }
        public string Name { get; init; } = "";
        public byte Slot { get; init; }
        public byte Auth { get; init; }
        public byte RankGrade { get; init; }
        public int TotalRank { get; init; }
        public int ClassRank { get; init; }
        public byte Class { get; init; }            // fieldsStart+22, confirmado por captura-diff
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
    /// Lista de chars + dados de conta para o frame 0x0C do login.
    /// </summary>
    public sealed record CharList
    {
        public string DisplayName { get; init; } = "";
        public ClanLoginSnapshot Clan { get; init; } = ClanLoginSnapshot.Empty;
        public uint ServerTimeMarker { get; init; }
        public uint PowerTimeMarker { get; init; }
        public ushort Country { get; init; }
        public ushort NetworkSlot { get; init; }       // @7 do header; slot da sessão TCP
        public byte SlotCount { get; init; } = 4;
        public uint Gold { get; init; }
        public uint Cash { get; init; }                // tail+16 do 0x0C (cash / EX points)
        public ushort PowerLevelPoint { get; init; }
        public uint UdpSessionKey { get; init; }          // @9 do header; user+0x1464 no world original
        public IReadOnlyList<CharSummary> Chars { get; init; } = Array.Empty<CharSummary>();
    }
}
