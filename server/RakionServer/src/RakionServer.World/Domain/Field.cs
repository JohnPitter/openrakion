using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Estado do player dentro de um Field (espelha o registro de 0x14 bytes em
    /// field+0x124 + i*0x14 do worldserv.exe). state: 0=vazio, 1=alive/armaA,
    /// 2=alive/armaB, 3=ready/spawning, 4=playing/alive, 5=spectator/locked.
    /// </summary>
    public sealed class PlayerRec
    {
        public ClientSession? Session;   // +0 userSlot (resolvido p/ a sessao)
        public byte State;               // +2 (=field+slot*0x14+0x126)
        public byte WeaponState = 1;     // arma atual do jogador: 1=armaA, 2=armaB
        public bool Dead;                // +4 (0x128) flag morto
        public uint Score;               // pontuacao/kills do player no field-record
        public byte Cause;               // ultima causa de morte
        public byte Team => (byte)(Slot < 10 ? 0 : 1); // slots 0..9 = time0, 10..0x13 = time1
        public int Slot;                 // indice no array (0..0x13)

        public bool Occupied => State != 0 && State != 5;
        public bool Playing => State == 4;
        public bool Ready => State == 3;
    }

    /// <summary>Fase do match (field+0x2b4): 0=pre/countdown, 1=playing, 2=fim-round/intermissao.</summary>
    public enum MatchPhase : byte { Pre = 0, Playing = 1, RoundEnd = 2 }

    /// <summary>
    /// Modos de jogo do field (field+0x119). Valores deduzidos da validacao de Op_RoomCreate
    /// (mode != 0 && mode &lt; 5; mode 2/3 com restricao de nivel) + a ordem do usablemode no
    /// stage-DB do cliente decifrado (GOLEM,DEATHMATCH,TEAMDEATH,BOSS). Mapas Battle 200-213.
    /// </summary>
    public enum GameMode : byte { Golem = 1, Deathmatch = 2, TeamDeath = 3, Boss = 4 }

    /// <summary>
    /// Field = partida/sala de jogo (array this+0xe4 do worldserv.exe, entradas de
    /// 0x3c0 bytes). Modela o field-record + a maquina de estado da partida
    /// (FUN_00409940 = motor; FUN_00408440 = ready/spawn; FUN_00407be0 = fim-match;
    /// FUN_00407e00 = morte/scoring). O motor roda por-field (WorldServer tick global),
    /// NAO por-sessao.
    /// </summary>
    public sealed class Field
    {
        private const ushort DefaultRoundDurationSec = 432; // +3s de countdown => 0x01B3, captura original que destrava o stage

        public int Id;
        public string Name = "";
        public byte Mode;            // +0x119 modo de jogo (0/1=deathmatch, 2=survival, 3=score-cap, 4=MVP)
        public byte MaxPlayers = 8;
        public bool InGame;          // partida em andamento (legado)
        public ClientSession? Master;
        public int MasterSlot = -1;  // slot do dono (field+0x121 host/owner)
        public byte State;           // field+8: 0=livre, 1=fim-match, 2=em jogo
        public int SlotEnabled;      // mascara/contagem de slots habilitados
        public byte MapId;           // field+0x118
        public byte MinLevel;        // field+0x114
        public byte MaxLevel;        // field+0x115
        public byte LeaderSlotA = 0xff; // field+0x122 (MVP/lider time A; 0xff = nenhum)
        public byte LeaderSlotB = 0xff; // field+0x123 (MVP/lider time B; 0xff = nenhum)
        public byte StateB;             // field-object +8  (estado: deve ser 2 para liquidar pontos/resultado)
        public byte StateMode;          // field-object +0x2b4 (modo: deve ser 2 para liquidar pontos/resultado)
        public byte FieldFlag119;       // field-object +0x119 (flag de estado; deve ser 0 para aceitar settle/result)

        // ---- maquina de estado da partida (FUN_00409940) ----
        public MatchPhase Phase;        // +0x2b4
        public long DeadlineMs;         // +0x2b8 (Environment.TickCount64 alvo; RemainingSec = (Deadline-now)/1000)
        public byte Round;              // +0x2bc (1-based, round atual)
        public byte MaxRounds = 1;      // +0x11a
        public ushort RoundDurationSec = DefaultRoundDurationSec; // +0x11c (432 -> RemainingSec ~435, ground truth do original)
        public byte FragLimit;          // +0x11e (kills/score p/ vencer o round; 0 = sem limite)
        public byte Warned30;           // +0x2be (flag aviso de 30s)
        public byte LastRoundWinner;    // +0x2bd
        public byte WinnerSide;         // +0x2bf (0/1/2=empate)
        public byte Wins0;              // +0x2c0 (rounds ganhos time0)
        public byte Wins1;              // +0x2c1 (rounds ganhos time1)
        public byte Score0;             // +0x11f (placar/kills time0 do round)
        public byte Score1;             // +0x120 (placar/kills time1 do round)
        public byte Mvp0 => LeaderSlotA == 0xff ? (byte)0 : LeaderSlotA;
        public byte Mvp1 => LeaderSlotB == 0xff ? (byte)0 : LeaderSlotB;

        // ---- modo GOLEM/BOSS (objetivo): cada time tem um Master Golem com energia ----
        public ushort Golem0Hp = 100;   // energia do Master Golem do time 0 (stage-DB: "Master Golem has <%d%%> energy")
        public ushort Golem1Hp = 100;   // energia do Master Golem do time 1
        public bool ObjectiveDecided;   // ja houve vencedor por objetivo (Golem destruido)

        /// <summary>Array de 0x14 player-records (field+0x124, stride 0x14).</summary>
        public readonly PlayerRec[] Slots = NewSlots();

        public readonly List<ClientSession> Players = new();

        public Field(int id) => Id = id;

        private static PlayerRec[] NewSlots()
        {
            var a = new PlayerRec[0x14];
            for (int i = 0; i < a.Length; i++) a[i] = new PlayerRec { Slot = i };
            return a;
        }

        public int Count { get { lock (Players) return Players.Count; } }

        public void Add(ClientSession s)
        {
            lock (Players) { if (!Players.Contains(s)) Players.Add(s); }
        }

        public void Remove(ClientSession s)
        {
            lock (Players) Players.Remove(s);
            var rec = FindRec(s);
            if (rec != null) { rec.Session = null; rec.State = 0; rec.WeaponState = 1; rec.Dead = false; rec.Score = 0; }
        }

        // ---- helpers de player-record (resolvem field+0x124 + slot*0x14) ----

        /// <summary>Acha o player-record da sessao (espelha FUN_0040b7d0 -> seat).</summary>
        public PlayerRec? FindRec(ClientSession s)
        {
            foreach (var r in Slots) if (r.Session == s) return r;
            return null;
        }

        /// <summary>Player-record por slot/seat (0..0x13).</summary>
        public PlayerRec? RecAt(int seat) => (seat >= 0 && seat < Slots.Length) ? Slots[seat] : null;

        /// <summary>Aloca um seat livre para a sessao (FUN_0040b7b0). Devolve o seat ou -1.</summary>
        public int AssignSeat(ClientSession s)
        {
            var existing = FindRec(s);
            if (existing != null) return existing.Slot;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].State == 0 && Slots[i].Session == null)
                {
                    Slots[i].Session = s;
                    Slots[i].State = 3; // ready
                    Slots[i].WeaponState = 1;
                    Slots[i].Dead = false;
                    Slots[i].Score = 0;
                    return i;
                }
            }
            return -1;
        }

        public int CountPlaying()
        {
            int n = 0;
            foreach (var r in Slots) if (r.Playing) n++;
            return n;
        }

        public int CountReady()
        {
            int n = 0;
            foreach (var r in Slots) if (r.Ready) n++;
            return n;
        }

        public int CountAlive(int team)
        {
            int n = 0;
            foreach (var r in Slots) if (r.Playing && !r.Dead && r.Team == team) n++;
            return n;
        }

        /// <summary>
        /// Serializa a entrada deste field para a lista de salas (FUN_00405790): registro
        /// de tamanho variavel — campos fixos + nome (nul-term) + u16.
        /// </summary>
        public byte[] SerializeListEntry()
        {
            using var w = new PacketWriter();
            w.WriteByte(0);                          // +0 field+0x3f (flag, ex. senha)
            w.WriteByte(State == 2 ? 1 : 0);         // +1 field+8 == 2 (em jogo)
            w.WriteByte(MapId);                      // +2 field+0x118
            w.WriteByte(Mode);                       // +3 field+0x119
            w.WriteByte(0).WriteByte(0).WriteByte(0);// +4..6 field+0x111/0x112/0x113
            w.WriteByte((byte)Count);                // +7 field+0x2bc (jogadores atuais)
            w.WriteByte(MaxPlayers);                 // +8 field+0x11a (capacidade)
            w.WriteByte(MinLevel);                   // +9 field+0x117+0x116
            w.WriteByte(MaxLevel);                   // +10 field+0x115+0x114
            w.WriteInt32(Master?.Game?.UserId ?? 0); // +0xb master id (FUN_0040abe0 local_14)
            w.WriteWord(MasterSlot < 0 ? 0 : MasterSlot); // +0xf local_c
            w.WriteInt32(0);                         // +0x11 local_10
            w.WriteWord(0);                          // +0x15 local_a
            w.WriteCString(Name);                    // +0x17 field+0x16 (nome)
            w.WriteWord(0);                          // field+0x3a8
            return w.ToArray();
        }

        // ===================== BROADCAST =====================

        /// <summary>
        /// Broadcast canal LOBBY (FUN_004061f0 -> FUN_004038e0): payload = [u16 msgType][data].
        /// Itera os 0x14 player-records ocupados (state != 0 e != 5). E o canal do match-engine
        /// (0x48/0x49/0x4a/0x44) — confirmado pelo Build0x48 que ja funciona via SendEncryptedFrame.
        /// </summary>
        public void BroadcastLobby(byte[] payload, ClientSession? except = null)
        {
            foreach (var r in Slots)
            {
                var s = r.Session;
                if (s == null || !r.Occupied || s == except) continue;
                try { s.SendEncryptedFrame(payload); } catch { }
            }
        }

        /// <summary>
        /// Broadcast canal FIELD (FUN_0041b8a0 = ClientSession.SendMessage): cada destino recebe
        /// [u16 serverSeq][u16 msgType][data] com SEU PROPRIO serverSeq. Itera os player-records
        /// ocupados. Usado p/ acoes in-field (0x3d/0x42/0x43/0x45/0x46/0x47/0x4b/0x4f...).
        /// Quando 'except' != null, EXCLUI o sender (relay de acao, FUN_00405c00).
        /// </summary>
        public void BroadcastField(ushort msgType, byte[] data, ClientSession? except = null)
        {
            foreach (var r in Slots)
            {
                var s = r.Session;
                if (s == null || !r.Occupied || s == except) continue;
                try { s.SendMessage(msgType, data); } catch { }
            }
        }

        /// <summary>Broadcast FIELD apenas aos players em estado 'playing' (state==4).</summary>
        public void BroadcastFieldPlaying(ushort msgType, byte[] data, ClientSession? except = null)
        {
            foreach (var r in Slots)
            {
                var s = r.Session;
                if (s == null || !r.Playing || s == except) continue;
                try { s.SendMessage(msgType, data); } catch { }
            }
        }

        /// <summary>Compat: broadcast de payload [u16 subtype][...] no canal lobby (legado dos handlers).</summary>
        public void Broadcast(byte[] payload, ClientSession? except = null) => BroadcastLobby(payload, except);

        // ===================== MAQUINA DE ESTADO =====================

        /// <summary>
        /// 0x48 FieldStatus — GROUND TRUTH (mitm_move, servidor ORIGINAL que DESTRAVOU o personagem):
        /// 12 BYTES = [48 00][round][u16 tempoSeg][win0][win1][14 14][00 a0 0f].
        /// Ex. capturado: 480001 b301 0000 1414 00a00f (round=1, secs=0x01b3, trailer 00 a0 0f).
        /// O build anterior mandava SO 9 bytes (truncado em 00 a0 0f) -> cliente parseava errado e NAO
        /// liberava o controle (input congelado em val=05). Bytes 7-8 = 0x14,0x14 (confirmado no capture;
        /// NAO usar Mvp/LeaderSlot=0xFF). Trailer 00 a0 0f = constante do modo (RE pendente de 0x122/0x123).
        /// </summary>
        public byte[] Build0x48()
        {
            // secs = tempo RESTANTE. O primeiro 0x48 do original funcional veio com 0x01B3; 0x025B
            // zerava o HUD em alguns testes, mas deixava o cliente em estado de pre-game/travado.
            ushort secs = RemainingSec();
            return new byte[]
            {
                0x48, 0x00,
                Round == 0 ? (byte)1 : Round,
                (byte)(secs & 0xff), (byte)(secs >> 8),
                Wins0, Wins1, 0x14, 0x14,
                0x00, 0xa0, 0x0f,
            };
        }

        /// <summary>0x49 NovoRound (5B): [49 00][round][mvp0][mvp1].</summary>
        public byte[] Build0x49() => new byte[] { 0x49, 0x00, Round, Mvp0, Mvp1 };

        /// <summary>0x4a FimRound (6B): [4a 00][winnerSide][wins0][wins1][lastWinner].</summary>
        public byte[] Build0x4a() => new byte[] { 0x4a, 0x00, WinnerSide, Wins0, Wins1, LastRoundWinner };

        /// <summary>0x44 FimMatch (2B): [44 00][motivo].</summary>
        public static byte[] Build0x44(byte reason) => new byte[] { 0x44, 0x00, reason };

        public ushort RemainingSec()
        {
            long now = Environment.TickCount64;
            long rem = (DeadlineMs - now) / 1000;
            if (rem < 0) rem = 0;
            if (rem > 0xffff) rem = 0xffff;
            return (ushort)rem;
        }

        /// <summary>
        /// FUN_00408440: o player marcou ready/spawnou (handler 0x48). Marca state=4; em fase Pre,
        /// inicia o round quando nao restam mais 'ready' (todos spawnaram), ou imediatamente se solo.
        /// Devolve true se a partida (re)iniciou nesta chamada.
        /// </summary>
        public bool OnPlayerReady(ClientSession s)
        {
            var rec = FindRec(s);
            if (rec == null) return false;
            if (State != 2) State = 2; // garante field ativo
            if (Phase == MatchPhase.Pre)
            {
                rec.State = 4;
                rec.Dead = false;
                if (CountReady() == 0) { StartRound(); return true; }
                return false;
            }
            // ja em jogo: spawn tardio entra direto como playing
            rec.State = 4;
            rec.Dead = false;
            return false;
        }

        /// <summary>Inicia o round 1 / reinicia o relogio da partida (transicao Pre/RoundEnd -> Playing).</summary>
        public void StartRound()
        {
            Phase = MatchPhase.Playing;
            State = 2;
            if (Round == 0) Round = 1;
            DeadlineMs = Environment.TickCount64 + (RoundDurationSec + 3) * 1000L;
            Warned30 = 0;
            Score0 = 0; Score1 = 0;
            foreach (var r in Slots) if (r.Occupied) { r.Dead = false; if (r.State == 3) r.State = 4; }
            RecomputeMvp();
            Log.Ok("field", "field {0} round {1} iniciado (dur={2}s mode={3})", Id, Round, RoundDurationSec, Mode);
        }

        /// <summary>Recalcula MVP por time (maior Score) — modo 4 / placar.</summary>
        public void RecomputeMvp()
        {
            uint best0 = 0, best1 = 0; byte m0 = 0xff, m1 = 0xff;
            foreach (var r in Slots)
            {
                if (!r.Occupied) continue;
                if (r.Team == 0) { if (r.Score >= best0) { best0 = r.Score; m0 = (byte)r.Slot; } }
                else { if (r.Score >= best1) { best1 = r.Score; m1 = (byte)r.Slot; } }
            }
            LeaderSlotA = m0; LeaderSlotB = m1;
        }

        /// <summary>
        /// FUN_00407e00: processa a MORTE de um player (vitima), pelo handler 0x46/0x4f.
        /// Marca dead, credita o killer, reescolhe host se preciso, e pode encerrar o round.
        /// Devolve a lista de eventos a serem broadcastados (montados pelo chamador).
        /// </summary>
        public void OnPlayerDeath(int victimSeat, int killerSeat, byte cause)
        {
            var v = RecAt(victimSeat);
            if (v == null || !v.Occupied || v.Dead) return;
            v.Dead = true;
            v.Cause = cause;
            v.State = 1; // eliminado/aguardando respawn

            var k = RecAt(killerSeat);
            if (k != null && k.Occupied && k != v)
            {
                uint delta = cause == 8 ? 2u : (cause == 1 ? 0u : 1u);
                k.Score += delta;
                if (k.Team == 0) Score0 = (byte)Math.Min(255, Score0 + delta);
                else Score1 = (byte)Math.Min(255, Score1 + delta);
            }
            RecomputeMvp();

            // host saiu? reatribui ao 1o ocupado
            if (victimSeat == MasterSlot)
            {
                foreach (var r in Slots) if (r.Occupied) { MasterSlot = r.Slot; break; }
            }

            // frag-limit atingido -> fim de round (GATED POR MODO)
            if (FragLimit > 0)
            {
                if (Mode == (byte)GameMode.Deathmatch)
                {
                    // DEATHMATCH (FFA): o primeiro JOGADOR a atingir o frag-limit vence o round.
                    PlayerRec? top = null;
                    foreach (var r in Slots) if (r.Occupied && (top == null || r.Score > top.Score)) top = r;
                    if (top != null && top.Score >= FragLimit) EndRound(top.Team);
                }
                else if (Score0 >= FragLimit || Score1 >= FragLimit)
                {
                    // TEAMDEATH/GOLEM/BOSS: placar por TIME atinge o frag-limit.
                    EndRound(Score0 > Score1 ? (byte)0 : Score1 > Score0 ? (byte)1 : (byte)2);
                }
            }
        }

        /// <summary>Encerra o round atual: contabiliza wins, vai p/ fase RoundEnd (intermissao 15s).</summary>
        public void EndRound(byte winnerSide)
        {
            WinnerSide = winnerSide;
            LastRoundWinner = winnerSide;
            if (winnerSide == 0) Wins0++;
            else if (winnerSide == 1) Wins1++;
            Phase = MatchPhase.RoundEnd;
            DeadlineMs = Environment.TickCount64 + 15000;
            Log.Ok("field", "field {0} round {1} encerrado (winner={2} w0={3} w1={4})", Id, Round, winnerSide, Wins0, Wins1);
        }

        /// <summary>FUN_00407be0: fim de match (motivo). field+8=1, players -> state 1.</summary>
        public void EndMatch(byte reason)
        {
            State = 1;
            Phase = MatchPhase.Pre;
            foreach (var r in Slots) if (r.State == 3 || r.State == 4) r.State = 1;
            Log.Ok("field", "field {0} MATCH OVER (motivo={1})", Id, reason);
        }

        /// <summary>
        /// Modo GOLEM/BOSS: aplica dano ao Master Golem do time alvo (0/1). Quando a energia zera, o time
        /// ADVERSARIO vence (objetivo). Dano placeholder (formula/energia exata = RE/balanceamento; broadcast
        /// de "Master Golem has X%% energy" via opcode proprio = pendente de RE + teste 2-clientes).
        /// </summary>
        public void DamageGolem(int golemTeam, ushort dmg)
        {
            if (ObjectiveDecided) return;
            if (golemTeam == 0) Golem0Hp = (ushort)Math.Max(0, Golem0Hp - dmg);
            else Golem1Hp = (ushort)Math.Max(0, Golem1Hp - dmg);
            Log.Ok("field", "field {0} Master Golem time{1} energia={2}%", Id, golemTeam, golemTeam == 0 ? Golem0Hp : Golem1Hp);
            if (Golem0Hp == 0) EndMatchObjective(1);       // golem do time0 destruido -> time1 vence
            else if (Golem1Hp == 0) EndMatchObjective(0);  // golem do time1 destruido -> time0 vence
        }

        /// <summary>Encerra o match por OBJETIVO (Golem/Boss): contabiliza o time vencedor + EndMatch.</summary>
        public void EndMatchObjective(byte winnerTeam)
        {
            if (ObjectiveDecided) return;
            ObjectiveDecided = true;
            WinnerSide = winnerTeam;
            LastRoundWinner = winnerTeam;
            if (winnerTeam == 0) Wins0++; else Wins1++;
            Log.Ok("field", "field {0} OBJETIVO: time{1} venceu (Golem inimigo destruido)", Id, winnerTeam);
            EndMatch(0);
        }
    }

    /// <summary>
    /// Room = sala de chat/lobby (array this+0xdc, entradas de 0x358 bytes).
    /// </summary>
    public sealed class Room
    {
        public int Id;
        public readonly List<ClientSession> Members = new();

        public Room(int id) => Id = id;

        public void Add(ClientSession s) { lock (Members) { if (!Members.Contains(s)) Members.Add(s); } }
        public void Remove(ClientSession s) { lock (Members) Members.Remove(s); }

        public void Broadcast(byte[] payload, ClientSession? except = null)
        {
            ClientSession[] snapshot;
            lock (Members) snapshot = Members.ToArray();
            foreach (var m in snapshot)
                if (m != except) m.SendLobby(payload);
        }
    }
}
