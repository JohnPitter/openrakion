using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World.Domain
{
    public enum PlayerReadyTransition : byte
    {
        Ignored,
        Waiting,
        Started,
        JoinedPlaying,
        JoinedRoundEnd
    }

    /// <summary>
    /// Estado do player dentro de um Field (espelha o registro de 0x14 bytes em
    /// field+0x124 + i*0x14 do worldserv.exe). state: 0=vazio, 1=aguardando,
    /// 2=pronto, 3=spawning, 4=playing/alive, 5=slot fechado.
    /// </summary>
    public sealed class PlayerRec
    {
        public ClientSession? Session;   // +0 userSlot (resolvido p/ a sessao)
        public byte State;               // +2 (=field+slot*0x14+0x126)
        public byte WeaponState = 1;     // arma atual do jogador: 1=armaA, 2=armaB
        public bool Dead;                // +4 (0x128) flag morto
        public byte RoundScore;          // +8 (+0x12c): kills/pontos do round individual
        public byte CounterA;            // +9 (+0x12d): contador acumulado do lado A
        public byte CounterB;            // +10 (+0x12e): contador acumulado do lado B
        public uint ResultPoints;         // +12 (+0x130): pontos/EXP acumulados no resultado
        public byte VoteState;            // +16 (+0x134): 0=nao votou, 1=sim, 2=nao, 3=abstencao
        public byte Cause;               // ultima causa de morte
        public bool LobbyReady => State == 2;
        public bool UsesTunneling;         // user+0x1478: sem rota UDP direta confirmada
        public BotPlayer? Bot;             // peer sintético server-side (assento ocupado por bot)
        public BotVector Position;          // última posição observada (do 0x030A do humano / IA do bot) p/ mira do bot
        public float Heading;               // rumo observado do 0x030A humano, usado no cone de melee
        public long NextBotMeleeAttackMs;   // anti-repetição de hooks do mesmo golpe humano
        public byte[]? InitialMovement;     // primeiro 0x4B do match, usado para sincronizar peers que carregaram depois
        public byte Team => (byte)(Slot < 10 ? 0 : 1); // slots 0..9 = time0, 10..0x13 = time1
        public int Slot;                 // indice no array (0..0x13)

        public bool IsBot => Bot != null;
        public bool Occupied => State != 0 && State != 5;
        public bool Playing => State == 4;
        public bool Ready => State == 3;
    }

    /// <summary>Fase do match (field+0x2b4): 0=pre/countdown, 1=playing, 2=fim-round/intermissao.</summary>
    public enum MatchPhase : byte { Pre = 0, Playing = 1, RoundEnd = 2 }

    /// <summary>
    /// Modos de jogo do field (field+0x119). Valores deduzidos da validacao de FieldCreate
    /// (mode != 0 && mode &lt; 5; mode 2/3 com restricao de nivel) + a ordem do usablemode no
    /// stage-DB do cliente decifrado (GOLEM,DEATHMATCH,TEAMDEATH,BOSS). Mapas Battle 200-213.
    /// </summary>
    public enum GameMode : byte { Golem = 1, Deathmatch = 2, TeamDeath = 3, Boss = 4 }

    public readonly record struct LifetimeMatchRecord(uint Win, uint Lose, uint Draw);

    /// <summary>
    /// Field = partida/sala de jogo (array this+0xe4 do worldserv.exe, entradas de
    /// 0x3c0 bytes). Modela o field-record + a maquina de estado da partida
    /// (FUN_00409940 = motor; FUN_00408440 = ready/spawn; FUN_00407be0 = fim-match;
    /// FUN_00407e00 = morte/scoring). O motor roda por-field (WorldServer tick global),
    /// NAO por-sessao.
    /// </summary>
    public sealed partial class Field
    {
        public const byte NoSeat = 0x14;
        private const ushort DefaultRoundDurationSec = 432; // +3s de countdown => 0x01B3, captura original que destrava o stage

        public int Id;
        public string Name = "";
        public string CreatorCharacterName = "";
        public string Password = "";
        public string Description = "";
        public bool Searchable = true;
        public byte Mode;            // +0x119: 0=stage, 1=Golem, 2=Deathmatch, 3=Team Death, 4=Boss
        public byte MaxPlayers = 8;
        public ClientSession? Master;
        public int MasterSlot = -1;  // slot do dono (field+0x121 host/owner)
        public byte State;           // field+8: 0=livre, 1=fim-match, 2=em jogo
        public int SlotEnabled;      // mascara/contagem de slots habilitados
        public byte MapId;           // field+0x118
        public byte MinLevel;        // field+0x111
        public byte MaxLevel;        // field+0x112
        public byte LevelRangeCode;  // field+0x113: classificação da faixa de level da sala
        public byte LeaderSlotA = NoSeat; // field+0x122 (líder Boss do time A; 0x14 = nenhum)
        public byte LeaderSlotB = NoSeat; // field+0x123 (líder Boss do time B; 0x14 = nenhum)
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
        public byte RoundEndReason;     // +0x2bd: 1=normal/score, 2=objetivo, 2/3 no fluxo stage
        public byte LosingSideWire;     // +0x2bf: 0=time1 venceu, 1=time0 venceu, 2=empate
        public byte Wins0;              // +0x2c0 (rounds ganhos time0)
        public byte Wins1;              // +0x2c1 (rounds ganhos time1)
        public byte Score0;             // +0x11f (placar/kills time0 do round)
        public byte Score1;             // +0x120 (placar/kills time1 do round)
        public byte Mvp0 => LeaderSlotA;
        public byte Mvp1 => LeaderSlotB;

        // ---- estado client/P2P-authoritative reportado ao World ----
        public short ObjectivePairA;    // +0x2c4 (0x4D primeiro s16)
        public short ObjectivePairB;    // +0x2c6 (0x4D segundo s16)
        public ushort BossTargetA;      // +0x2c8 (0x60 arg0 < 10)
        public ushort BossTargetB;      // +0x2ca (0x60 arg0 >= 10)
        public bool ObjectiveDecided;
        public bool HasTunnelingClient; // +0x2cc: agregado publicado por 0x54/0x55

        public bool Settled;            // resultado do match ja liquidado no DB (WorldServer.SettleMatch)
        public Guid MatchId { get; private set; }
        public readonly object SyncRoot = new();

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

        /// <summary>Gate exato de FUN_00424B60 para o reporte competitivo de pontos.</summary>
        public bool CanAcceptGamePoint() =>
            Mode != 0 && State == 2 && Phase == MatchPhase.RoundEnd;

        /// <summary>Replica FUN_00405980: 1=win, 2=lose, 3=draw no snapshot do 0x50.</summary>
        public byte GamePointOutcome(byte seat, ushort resultMarker)
        {
            if (resultMarker == 0) return 3;
            if (Mode != 1 && Mode != 3 && Mode != 4) return 3;
            return LosingSideWire switch
            {
                0 => seat < 10 ? (byte)2 : (byte)1,
                1 => seat < 10 ? (byte)1 : (byte)2,
                _ => 3
            };
        }

        public LifetimeMatchRecord ProjectGamePointRecord(
            byte seat, ushort resultMarker, LifetimeMatchRecord current) =>
            GamePointOutcome(seat, resultMarker) switch
            {
                1 => current with { Win = current.Win + 1 },
                2 => current with { Lose = current.Lose + 1 },
                _ => current with { Draw = current.Draw + 1 }
            };

        public void Add(ClientSession s)
        {
            lock (Players) { if (!Players.Contains(s)) Players.Add(s); }
        }

        public void Remove(ClientSession s)
        {
            lock (Players) Players.Remove(s);
            var rec = FindRec(s);
            if (rec != null)
            {
                rec.Session = null;
                rec.State = 0;
                rec.WeaponState = 1;
                rec.Dead = false;
                rec.RoundScore = 0;
                rec.CounterA = 0;
                rec.CounterB = 0;
                rec.ResultPoints = 0;
                rec.VoteState = 0;
                rec.UsesTunneling = false;
                rec.Position = default;
                rec.Heading = 0;
                rec.NextBotMeleeAttackMs = 0;
                rec.InitialMovement = null;
            }
        }

        public TunnelingPresenceChange RegisterTunnelingPresence(byte seat, bool usesTunneling)
        {
            PlayerRec? record = RecAt(seat);
            if (record == null) return TunnelingPresenceChange.None;

            record.UsesTunneling = usesTunneling;
            if (!record.UsesTunneling) return TunnelingPresenceChange.None;
            if (HasTunnelingClient) return TunnelingPresenceChange.SyncEnabled;

            HasTunnelingClient = true;
            return TunnelingPresenceChange.Enabled;
        }

        public TunnelingPresenceChange UnregisterTunnelingPresence(byte seat)
        {
            PlayerRec? departing = RecAt(seat);
            if (departing == null || !departing.UsesTunneling)
                return TunnelingPresenceChange.None;

            departing.UsesTunneling = false;
            foreach (PlayerRec record in Slots)
                if (record.State is 3 or 4 && record.UsesTunneling)
                    return TunnelingPresenceChange.None;

            if (!HasTunnelingClient) return TunnelingPresenceChange.None;
            HasTunnelingClient = false;
            return TunnelingPresenceChange.Disabled;
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

        /// <summary>Resolve o relay direcionado de 0x62: alvo pelo seat pedido e origem pelo seat real.</summary>
        public bool TryResolveSlotUdpRelay(
            ClientSession sender, byte targetSeat, out ClientSession? target, out byte senderSeat)
        {
            target = null;
            senderSeat = NoSeat;
            PlayerRec? source = FindRec(sender);
            PlayerRec? destination = RecAt(targetSeat);
            if (source == null || destination?.Session == null || !destination.Occupied) return false;

            target = destination.Session;
            senderSeat = (byte)source.Slot;
            return true;
        }

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
                    Slots[i].State = 1; // humano entra no wait-room antes de Ready/Start
                    Slots[i].WeaponState = 1;
                    Slots[i].Dead = false;
                    Slots[i].RoundScore = 0;
                    Slots[i].CounterA = 0;
                    Slots[i].CounterB = 0;
                    Slots[i].ResultPoints = 0;
                    Slots[i].VoteState = 0;
                    Slots[i].Position = default;
                    Slots[i].Heading = 0;
                    Slots[i].NextBotMeleeAttackMs = 0;
                    return i;
                }
            }
            return -1;
        }

        public void InitializeLobbySlots()
        {
            if (Mode == 0)
            {
                CloseSeatRange(MaxPlayers, Slots.Length);
                return;
            }

            int teamASeats = (MaxPlayers + 1) / 2;
            int teamBSeats = MaxPlayers / 2;
            CloseSeatRange(teamASeats, 10);
            CloseSeatRange(10 + teamBSeats, Slots.Length);
        }

        private void CloseSeatRange(int start, int end)
        {
            for (int seat = Math.Clamp(start, 0, Slots.Length);
                 seat < Math.Clamp(end, 0, Slots.Length);
                 seat++)
            {
                Slots[seat].State = 5;
            }
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

        public bool HasHumanInGameplay()
        {
            foreach (PlayerRec record in Slots)
                if (record.Session?.Status == UserStatus.InField) return true;
            return false;
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
        /// 0x48 FieldStatus: 9 bytes lógicos conforme FUN_00408440/FUN_00409940:
        /// [48 00][round][u16 tempoSeg][win0][win1][mvp0][mvp1]. O bloco AES completa 12 bytes;
        /// a cauda 00 A0 0F de uma captura era conteúdo de stack fora do comprimento lógico.
        /// </summary>
        public byte[] Build0x48() => Build0x48(RemainingSec());

        public byte[] Build0x48(ushort remainingSec)
        {
            return new byte[]
            {
                0x48, 0x00,
                Round == 0 ? (byte)1 : Round,
                (byte)(remainingSec & 0xff), (byte)(remainingSec >> 8),
                Wins0, Wins1, Mvp0, Mvp1,
                0x00, 0x00, 0x00,
            };
        }

        /// <summary>0x49 NovoRound (5B): [49 00][round][mvp0][mvp1].</summary>
        public byte[] Build0x49() => new byte[] { 0x49, 0x00, Round, Mvp0, Mvp1 };

        /// <summary>0x4a FimRound — corpo (4B): [reason][losingSide][wins0][wins1].
        /// layout fiel ao caminho validado in-game (Golem War); modos 2-clientes precisam re-teste.</summary>
        public byte[] Build0x4a() => new byte[] { RoundEndReason, LosingSideWire, Wins0, Wins1 };

        /// <summary>
        /// 0x44 FimMatch PvP de FUN_00407BE0: [44 00][reason], com zero-pad do bloco AES.
        /// O formato longo com nome da sala pertence somente à ponte visual do stage solo.
        /// </summary>
        public byte[] BuildMatchEnd(byte reason)
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x44).WriteByte(reason).WriteBytes(new byte[9]);
            return writer.ToArray();
        }

        public ushort RemainingSec()
        {
            long now = Environment.TickCount64;
            long rem = (DeadlineMs - now) / 1000;
            if (rem < 0) rem = 0;
            if (rem > 0xffff) rem = 0xffff;
            return (ushort)rem;
        }

        /// <summary>
        /// FUN_00408440: processa 0x48 apenas para field ativo e registro state=3.
        /// </summary>
        public PlayerReadyTransition OnPlayerReady(ClientSession s) =>
            OnPlayerReady(s, Environment.TickCount64);

        public PlayerReadyTransition OnPlayerReady(ClientSession s, long now)
        {
            var rec = FindRec(s);
            if (State != 2 || rec?.State != 3) return PlayerReadyTransition.Ignored;

            rec.State = 4;
            rec.Dead = false;
            if (Phase == MatchPhase.Pre)
            {
                if (CountReady() != 0) return PlayerReadyTransition.Waiting;
                StartRound(now);
                return PlayerReadyTransition.Started;
            }

            if (Phase == MatchPhase.Playing) return PlayerReadyTransition.JoinedPlaying;
            if (Phase != MatchPhase.RoundEnd) return PlayerReadyTransition.Ignored;

            if (LosingSideWire is 0 or 1)
            {
                if (rec.Slot < 10) rec.CounterB = AddByte(rec.CounterB, 1);
                else rec.CounterA = AddByte(rec.CounterA, 1);
            }
            return PlayerReadyTransition.JoinedRoundEnd;
        }

        public void RelayPlayerMovement(PlayerRec source, byte[] movement)
        {
            source.InitialMovement ??= (byte[])movement.Clone();
            byte[] payload = BuildPlayerMovement(source, movement);
            BroadcastFieldPlaying(0x4b, payload, source.Session);
        }

        public int ReplayInitialMovementsTo(ClientSession target)
        {
            int replayed = 0;
            foreach (PlayerRec source in Slots)
            {
                if (!source.Playing || source.Session == target || source.InitialMovement == null) continue;
                target.SendMessage(0x4b, BuildPlayerMovement(source, source.InitialMovement));
                replayed++;
            }
            return replayed;
        }

        public int ReplayPlayerSpawnsTo(ClientSession target)
        {
            int replayed = 0;
            foreach (PlayerRec source in Slots)
            {
                if (!source.Playing || source.Session == target) continue;
                target.SendMessage(0x45, FieldLifecycleFrames.Spawn((byte)source.Slot));
                replayed++;
            }
            return replayed;
        }

        private static byte[] BuildPlayerMovement(PlayerRec source, byte[] movement)
        {
            using var writer = new PacketWriter();
            writer.WriteByte((byte)source.Slot).WriteWord((ushort)movement.Length).WriteBytes(movement);
            return writer.ToArray();
        }

        /// <summary>Inicia o round 1 / reinicia o relogio da partida (transicao Pre/RoundEnd -> Playing).</summary>
        public void StartRound() => StartRound(Environment.TickCount64);

        public void StartRound(long now)
        {
            Phase = MatchPhase.Playing;
            State = 2;
            if (Round == 0) Round = 1;
            DeadlineMs = now + (RoundDurationSec + 3) * 1000L;
            Warned30 = 0;
            Score0 = 0; Score1 = 0;
            // reset por ROUND (nao por match): cada round do Golem/Boss comeca com os Master Golens
            // cheios e o objetivo em aberto de novo.
            ObjectivePairA = 1; ObjectivePairB = 1; BossTargetA = 1; BossTargetB = 1;
            ObjectiveDecided = false;
            foreach (var r in Slots) if (r.Occupied) r.Dead = false;
            SelectBossLeaders();
            Log.Ok("field", "field {0} round {1} iniciado (dur={2}s mode={3})", Id, Round, RoundDurationSec, Mode);
        }

        /// <summary>
        /// Reset de inicio de MATCH (LAB_00407ab0 zera os contadores do match no engage 0x43):
        /// rounds, wins, placar, objetivo e a flag de liquidacao — necessario p/ rematch no
        /// mesmo field (senao o Round/Wins do match anterior encerram o novo imediatamente).
        /// </summary>
        public void ResetMatch()
        {
            MatchId = Guid.NewGuid();
            Round = 0; Wins0 = 0; Wins1 = 0; Score0 = 0; Score1 = 0;
            LosingSideWire = 0; RoundEndReason = 0; Warned30 = 0;
            ObjectivePairA = 0; ObjectivePairB = 0; BossTargetA = 0; BossTargetB = 0;
            ObjectiveDecided = false;
            Settled = false;
            foreach (var r in Slots)
            {
                if (!r.Occupied) continue;
                r.RoundScore = 0;
                r.CounterA = 0;
                r.CounterB = 0;
                r.ResultPoints = 0;
                r.InitialMovement = null;
            }
        }

        /// <summary>
        /// Vencedor do round quando o TEMPO esgota (deadline do motor, FUN_00409940):
        /// DEATHMATCH (FFA) = time do jogador com maior Score (empate no topo = 2/empate);
        /// demais modos = placar por time (Score0 vs Score1; igual = 2/empate).
        /// </summary>
        public byte DecideRoundWinnerByScore()
        {
            if (Mode == (byte)GameMode.Deathmatch)
            {
                uint best = 0; int atBest = 0; byte side = 2;
                foreach (var r in Slots)
                {
                    if (!r.Occupied) continue;
                    if (atBest == 0 || r.RoundScore > best) { best = r.RoundScore; side = r.Team; atBest = 1; }
                    else if (r.RoundScore == best) atBest++;
                }
                return atBest == 1 ? side : (byte)2;
            }
            return Score0 > Score1 ? (byte)0 : Score1 > Score0 ? (byte)1 : (byte)2;
        }

        /// <summary>FUN_00408440: no Boss, escolhe por time o jogador ativo de maior nível.</summary>
        public void SelectBossLeaders()
        {
            LeaderSlotA = NoSeat;
            LeaderSlotB = NoSeat;
            if (Mode != (byte)GameMode.Boss) return;

            byte bestLevelA = 0;
            byte bestLevelB = 0;
            foreach (PlayerRec record in Slots)
            {
                if (!record.Playing || record.Session == null) continue;
                byte level = record.Session.CharLevel;
                if (record.Team == 0 && level > bestLevelA)
                {
                    bestLevelA = level;
                    LeaderSlotA = (byte)record.Slot;
                }
                else if (record.Team == 1 && level > bestLevelB)
                {
                    bestLevelB = level;
                    LeaderSlotB = (byte)record.Slot;
                }
            }
        }

        /// <summary>
        /// FUN_00407e00: processa a saída do próprio jogador pelo handler 0x46.
        /// A morte reportada por 0x4F possui scoring próprio em ApplyReportedDeath.
        /// </summary>
        public bool OnPlayerExit(int victimSeat, byte cause)
        {
            var v = RecAt(victimSeat);
            if (v == null || !v.Occupied || v.Dead) return false;
            bool wasActive = v.State is 3 or 4;
            bool wasLeader = victimSeat == LeaderSlotA || victimSeat == LeaderSlotB;
            v.Dead = true;
            v.Cause = cause;
            v.State = 1; // eliminado/aguardando respawn

            return wasActive && EndRoundAfterDeparture((byte)victimSeat, wasLeader);
        }

        public void ArmMatch(long now)
        {
            ResetMatch();
            State = 2;
            Phase = MatchPhase.Pre;
            DeadlineMs = now + 40000;
            foreach (var record in Slots)
            {
                if (!record.Occupied) continue;
                record.State = 3;
                record.Dead = false;
            }
        }

        /// <summary>Encerra o round atual: contabiliza wins, vai p/ fase RoundEnd (intermissao 15s).</summary>
        public void EndRound(byte winningTeam)
        {
            if (Mode == (byte)GameMode.Deathmatch)
            {
                RoundEndReason = 1;
                LosingSideWire = winningTeam < 2 ? (byte)(1 - winningTeam) : (byte)2;
                BeginRoundEndTimer();
                return;
            }
            if (winningTeam < 2) CompleteTeamRound(winningTeam, 1);
            else
            {
                RoundEndReason = 1;
                LosingSideWire = 2;
                BeginRoundEndTimer();
            }
        }

        /// <summary>FUN_00407be0: fim de match (motivo). field+8=1, players -> state 1.</summary>
        public void EndMatch(byte reason)
        {
            State = 1;
            Phase = MatchPhase.Pre;
            foreach (PlayerRec record in Slots)
            {
                if (!record.Occupied) continue;
                record.State = record.Bot == null ? (byte)1 : (byte)2;
                record.Dead = false;
                record.Position = default;
                record.Heading = 0;
                record.NextBotMeleeAttackMs = 0;
                record.InitialMovement = null;
                record.Bot?.ResetForLobby();
            }
            Log.Ok("field", "field {0} MATCH OVER (motivo={1})", Id, reason);
        }

        public bool ApplyObjectivePair(short first, short second)
        {
            if (State != 2 || Phase != MatchPhase.Playing) return false;
            ObjectivePairA = first;
            ObjectivePairB = second;
            if (first == 0) CompleteTeamRound(1, 2);
            else if (second == 0) CompleteTeamRound(0, 2);
            else return false;
            return true;
        }

        public bool ApplyBossTarget(byte reporterSeat, byte targetGroup, ushort value)
        {
            if (State != 2 || Phase != MatchPhase.Playing || Mode != (byte)GameMode.Boss) return false;
            if (reporterSeat != LeaderSlotA && reporterSeat != LeaderSlotB) return false;
            if (targetGroup < 10) BossTargetA = value;
            else BossTargetB = value;
            return true;
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
