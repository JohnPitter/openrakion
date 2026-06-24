using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Bot = participante de partida SINTETIZADO pelo servidor, sem <see cref="ClientSession"/>.
    /// Ocupa um <see cref="PlayerRec"/> como um jogador de verdade (entra na contagem de time, no
    /// motor de round, no placar), mas todo o comportamento — entrada no campo, movimento, ataque,
    /// morte — é GERADO do domínio (estado de IA -> DTO -> bytes), nunca por relay/replay de pacote
    /// capturado. É a regra-mestra do projeto: implementação de raiz.
    ///
    /// Identidade é EFÊMERA: o bot só existe enquanto a sala/partida vive. Ao fim do match, ou quando
    /// o último humano sai da sala, todos os bots são descartados — não voltam ao roster nem persistem
    /// no DB. Por isso não há characterid/usergameinfo: o bot nunca toca persistência.
    /// </summary>
    public sealed class BotPlayer
    {
        public BotPlayer(int id, string name, byte level, byte charClass, byte team)
        {
            Id = id;
            Name = name;
            Level = level;
            CharClass = charClass;
            Team = team;
        }

        /// <summary>Id efêmero por field (apenas p/ logs e correlação; não é id de DB).</summary>
        public int Id;

        /// <summary>Nome exibido no slot/roster do cliente do host.</summary>
        public string Name;

        /// <summary>Nível exibido no card do slot.</summary>
        public byte Level;

        /// <summary>Classe do char (modelo do avatar que o cliente renderiza no spawn 0x45).</summary>
        public byte CharClass;

        /// <summary>Time alvo do bot (0/1). O seat real é derivado deste time ao alocar o slot.</summary>
        public byte Team;

        // ---- estado de combate/IA (server-side; dirigido pelo motor de IA, task #5) ----

        /// <summary>Energia máxima do bot (placeholder de balanceamento até a RE de combate).</summary>
        public ushort MaxHp = 100;

        /// <summary>Energia atual; ao zerar, o servidor SINTETIZA a morte do bot (0x4f victim=bot).</summary>
        public ushort Hp = 100;

        /// <summary>Marcado morto no round atual (aguardando respawn/fim de round).</summary>
        public bool Dead;

        /// <summary>O spawn (0x45) já foi anunciado aos clientes neste round? (evita re-anúncio por tick).</summary>
        public bool SpawnedThisRound;

        /// <summary>Seat do alvo atual (humano) escolhido pela IA; -1 = sem alvo.</summary>
        public int TargetSeat = -1;

        /// <summary>Próximo tick de decisão da IA (Environment.TickCount64).</summary>
        public long NextDecisionMs;

        /// <summary>Cooldown do próximo ataque (Environment.TickCount64).</summary>
        public long NextAttackMs;

        /// <summary>Próximo envio de movimento sintetizado (Environment.TickCount64).</summary>
        public long NextMoveMs;

        // ---- posição/orientação server-side (dirigida pelo motor de IA; serializada pelo BotMovement) ----
        public float X, Y, Z;
        public float Yaw;

        /// <summary>Vetor de ação/mira (direção do golpe/movimento) — campo action-vec do CNetMessage 0x30a.</summary>
        public float AimX, AimY, AimZ;

        /// <summary>Contador de sequência do datagrama UDP de gameplay (= *(CNet+4)++ do cliente original).</summary>
        public uint UdpSeq;

        /// <summary>Reset por ROUND: energia cheia, vivo, sem alvo (espelha o reset do round do field).</summary>
        public void ResetForRound()
        {
            Hp = MaxHp;
            Dead = false;
            TargetSeat = -1;
            NextAttackMs = 0;
            SpawnedThisRound = false;
        }
    }
}
