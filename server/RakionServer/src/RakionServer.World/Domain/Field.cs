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
        public BotPlayer? Bot;           // ocupante SINTETICO (sem socket); mutuamente exclusivo com Session
        public byte State;               // +2 (=field+slot*0x14+0x126)
        public byte WeaponState = 1;     // arma atual do jogador: 1=armaA, 2=armaB
        public bool Dead;                // +4 (0x128) flag morto
        public uint Score;               // pontuacao/kills do player no field-record
        public byte Cause;               // ultima causa de morte
        public byte Team => (byte)(Slot < 10 ? 0 : 1); // slots 0..9 = time0, 10..0x13 = time1
        public int Slot;                 // indice no array (0..0x13)

        // ---- posição server-side (atualizada pelo relay do 0x30a do humano; lida pela IA do bot) ----
        public float LastX, LastY, LastZ;
        public float LastHeading;        // graus (do 0x30a) — direção que o humano OLHA; p/ o cone de acerto
        public long LastPositionMs;      // timestamp da última atualização de posição

        /// <summary>Velocidade estimada do alvo no plano XZ (coord/ms, suavizada por EMA) — derivada do delta
        /// entre 0x30a consecutivos. A IA do bot usa p/ ANTECIPAR (liderar a mira/perseguição); zero = parado.</summary>
        public float VelX, VelZ;

        /// <summary>Seat ocupado por um bot sintetico (nunca recebe frames; o servidor gera o comportamento).</summary>
        public bool IsBot => Bot != null;

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
        public ushort MapSlot;       // slot de nível (0x122..0x4ba) do 0x3b -> trailing u16 do entry 0x36 (this+0x3a8)
        public string Password = ""; // senha da sala (0x3b, <9) — validada no join 0x38
        public bool IsRoom;          // sala criada por host (0x3b) e publicável na game list; false p/ field de stage-solo

        // ---- ENVOLTÓRIA DOS HUMANOS (chão VÁLIDO empírico): onde qualquer humano já pisou É chão. O clamp do bot
        //   usa isto em vez de geometria chutada (que prendia o bot numa "parede invisível"). Expande no 0x30a.
        private float _hullMinX = float.MaxValue, _hullMaxX = float.MinValue;
        private float _hullMinZ = float.MaxValue, _hullMaxZ = float.MinValue;
        public bool HasHumanHull => _hullMaxX >= _hullMinX;
        public void ExpandHumanHull(float x, float z)
        {
            if (x < _hullMinX) _hullMinX = x; if (x > _hullMaxX) _hullMaxX = x;
            if (z < _hullMinZ) _hullMinZ = z; if (z > _hullMaxZ) _hullMaxZ = z;
        }
        /// <summary>Caixa caminhável do bot = envoltória dos humanos + <paramref name="pad"/> de folga. Null até um
        /// humano ter enviado posição (aí o BotTick cai no clamp fixo do mapa como fallback).</summary>
        public Domain.WalkBounds? HumanHull(float pad) => HasHumanHull
            ? new Domain.WalkBounds(_hullMinX - pad, _hullMaxX + pad, _hullMinZ - pad, _hullMaxZ + pad)
            : (Domain.WalkBounds?)null;
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
        public ushort GoldGolemHp = 100; // energia do golem DOURADO central (neutro) — gate da rota de objetivo do bot
        public bool ObjectiveDecided;   // ja houve vencedor por objetivo (Golem destruido)
        // GOLD SWORD (regra oficial): quem dá o ÚLTIMO golpe no golem dourado pega a espada por 1min30; SÓ ela dana o
        // Master Golem inimigo; se o portador morre, o golem dourado revive. Reset por round.
        public int GoldSwordHolder = -1;         // seat portador; -1 = ninguém
        public long GoldSwordExpiryMs;           // expira em now + GoldSwordDurationMs
        public const long GoldSwordDurationMs = 90_000;  // 1min30

        public bool Settled;            // resultado do match ja liquidado no DB (WorldServer.SettleMatch)

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

        /// <summary>Troca o ocupante do <paramref name="seat"/> para o OUTRO bloco de time (0..9 = time0/vermelho,
        /// 10..0x13 = time1/azul) no primeiro slot livre. Devolve o novo seat, ou -1 se o outro bloco está cheio.
        /// Espelha FUN_004075a0 (0x3e re-spawn/troca de assento) — usado na tela da sala.</summary>
        public int SwitchTeamBlock(int seat)
        {
            var src = RecAt(seat);
            if (src == null || src.State == 0) return -1;
            int lo = seat < 10 ? 10 : 0;
            int hi = seat < 10 ? Slots.Length : 10;
            for (int i = lo; i < hi; i++)
            {
                var dst = Slots[i];
                if (dst.State != 0 || dst.Session != null || dst.Bot != null) continue;
                dst.Session = src.Session; dst.Bot = src.Bot; dst.State = src.State;
                dst.WeaponState = src.WeaponState; dst.Dead = src.Dead; dst.Score = src.Score;
                src.Session = null; src.Bot = null; src.State = 0; src.WeaponState = 1; src.Dead = false; src.Score = 0;
                if (dst.Session != null) { dst.Session.FieldSeat = (byte)i; dst.Session.FieldObjectIndex = (ushort)i; }
                if (MasterSlot == seat) MasterSlot = i;
                return i;
            }
            return -1;
        }

        /// <summary>Aloca um seat balanceado: prefere o bloco de time com MENOS ocupantes (empate -> vermelho 0..9),
        /// caindo p/ <see cref="AssignSeat"/> se o bloco preferido estiver cheio. Usado no JOIN de sala p/ o 2º
        /// jogador cair no time vazio (1-1) — destrava o Ready sem depender do botão de troca.</summary>
        public int AssignSeatBalanced(ClientSession s)
        {
            var existing = FindRec(s);
            if (existing != null) return existing.Slot;
            int red = 0, blue = 0;
            foreach (var r in Slots) if (r.State != 0) { if (r.Slot < 10) red++; else blue++; }
            int lo = blue < red ? 10 : 0, hi = blue < red ? Slots.Length : 10;
            for (int i = lo; i < hi; i++)
            {
                if (Slots[i].State != 0 || Slots[i].Session != null || Slots[i].Bot != null) continue;
                Slots[i].Session = s; Slots[i].State = 3; Slots[i].WeaponState = 1; Slots[i].Dead = false; Slots[i].Score = 0;
                return i;
            }
            return AssignSeat(s); // bloco preferido cheio -> qualquer livre
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
                    Slots[i].State = 3; // ready
                    Slots[i].WeaponState = 1;
                    Slots[i].Dead = false;
                    Slots[i].Score = 0;
                    return i;
                }
            }
            return -1;
        }

        // ---- bots (ocupantes sinteticos, sem socket) -----------------------------------------

        private int _nextBotId = 1;

        /// <summary>Jogadores HUMANOS no field (= <see cref="Players"/>; bots NAO entram aqui).</summary>
        public int HumanCount => Count;

        /// <summary>Quantos seats sao ocupados por bots.</summary>
        public int BotCount
        {
            get { int n = 0; foreach (var r in Slots) if (r.IsBot) n++; return n; }
        }

        /// <summary>Ha pelo menos um humano em algum seat ocupado? (cleanup: field so vive por humano).</summary>
        public bool HasHuman
        {
            get { foreach (var r in Slots) if (r.Occupied && r.Session != null) return true; return false; }
        }

        /// <summary>
        /// Aloca um seat para um BOT no bloco do seu time (0..9 = time0, 10..0x13 = time1), preferindo
        /// o time pedido e caindo p/ o outro bloco se cheio. Espelha AssignSeat mas marca o rec como
        /// ocupante sintetico (Session=null, Bot=b). Devolve o seat ou -1 (sala cheia).
        /// </summary>
        public int AssignBotSeat(BotPlayer bot)
        {
            int seat = FreeSeatInTeam(bot.Team);
            if (seat < 0) seat = FreeSeatInTeam((byte)(bot.Team ^ 1)); // time pedido cheio -> outro bloco
            if (seat < 0) return -1;
            var r = Slots[seat];
            r.Session = null;
            r.Bot = bot;
            r.State = 3; // ready (mesmo estado de um humano recem-alocado)
            r.WeaponState = 1;
            r.Dead = false;
            r.Score = 0;
            bot.Team = r.Team; // normaliza o time ao bloco real do seat
            return seat;
        }

        /// <summary>1o seat livre (State==0, sem ocupante) no bloco de um time; -1 se cheio.</summary>
        private int FreeSeatInTeam(byte team)
        {
            int lo = team == 0 ? 0 : 10;
            int hi = team == 0 ? 10 : 0x14;
            for (int i = lo; i < hi; i++)
                if (Slots[i].State == 0 && Slots[i].Session == null && Slots[i].Bot == null) return i;
            return -1;
        }

        /// <summary>Aloca um BotPlayer novo (id efemero por field) e o coloca num seat. Devolve (bot,seat) ou null.</summary>
        public (BotPlayer Bot, int Seat)? AddBot(string name, byte level, byte charClass, byte team,
                                                 BotDifficulty difficulty = BotDifficulty.Normal)
        {
            var bot = new BotPlayer(_nextBotId, name, level, charClass, team, difficulty);
            int seat = AssignBotSeat(bot);
            if (seat < 0) return null;
            _nextBotId++;
            return (bot, seat);
        }

        /// <summary>Esvazia o seat de um bot (cleanup de fim de match / saida do humano).</summary>
        public void ClearBotSeat(int seat)
        {
            var r = RecAt(seat);
            if (r == null || !r.IsBot) return;
            r.Bot = null; r.State = 0; r.WeaponState = 1; r.Dead = false; r.Score = 0; r.Cause = 0;
        }

        /// <summary>Remove TODOS os bots do field (descarta as identidades efemeras). Idempotente.</summary>
        public int RemoveAllBots()
        {
            int n = 0;
            foreach (var r in Slots)
                if (r.IsBot) { r.Bot = null; r.State = 0; r.WeaponState = 1; r.Dead = false; r.Score = 0; r.Cause = 0; n++; }
            if (n > 0) Log.Info("bot", "field {0}: {1} bot(s) removido(s)", Id, n);
            return n;
        }

        /// <summary>Enumera os seats ocupados por bot (rec + seat), p/ o motor de IA.</summary>
        public System.Collections.Generic.IEnumerable<PlayerRec> BotRecs()
        {
            foreach (var r in Slots) if (r.IsBot) yield return r;
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

        /// <summary>Ocupantes REAIS de um time (jogador/bot no slot, vivo ou morto) — usado p/ detectar abandono
        /// (time esvaziou) e decidir vitória por W.O.</summary>
        public int CountOccupiedTeam(int team)
        {
            int n = 0;
            foreach (var r in Slots) if (r.Occupied && (r.Session != null || r.Bot != null) && r.Team == team) n++;
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

        /// <summary>0x4a FimRound — corpo (4B) p/ BroadcastFieldPlaying(0x4a, ...): [lastWinner][winnerSide][wins0][wins1].
        /// layout fiel ao caminho validado in-game (Golem War); modos 2-clientes precisam re-teste.</summary>
        public byte[] Build0x4a() => new byte[] { LastRoundWinner, WinnerSide, Wins0, Wins1 };

        /// <summary>
        /// 0x44 FimMatch (formato CAPTURADO do original, _r44 do fluxo solo que FUNCIONA):
        /// [44 00][reason][00][u32 1][roomName] — a versao curta de 3B era IGNORADA pelo
        /// cliente (ele nao voltava a sala no fim/saida do match).
        /// </summary>
        public byte[] BuildMatchEnd(byte reason)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x44).WriteByte(reason).WriteByte(0).WriteInt32(1);
            w.WriteBytes(System.Text.Encoding.ASCII.GetBytes(Name));
            return w.ToArray();
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
            // reset por ROUND (nao por match): cada round do Golem/Boss comeca com os Master Golens
            // cheios e o objetivo em aberto de novo.
            Golem0Hp = 100; Golem1Hp = 100; GoldGolemHp = 100; ObjectiveDecided = false;
            GoldSwordHolder = -1; GoldSwordExpiryMs = 0;
            // REVIVE todos os ocupantes do match no início do round (cada round é um começo fresco — os mortos do
            // round anterior renascem). Antes só revivia State==3, deixando os mortos (State==1) fora do round seguinte
            // -> sem "evento de morte" o fim de round não disparava (round intermitente não encerrando).
            foreach (var r in Slots) if (r.Occupied) { r.Dead = false; if (r.State == 3 || r.State == 1) r.State = 4; r.Bot?.ResetForRound(); }
            RecomputeMvp();
            Log.Ok("field", "field {0} round {1} iniciado (dur={2}s mode={3})", Id, Round, RoundDurationSec, Mode);
        }

        /// <summary>
        /// Reset de inicio de MATCH (LAB_00407ab0 zera os contadores do match no engage 0x43):
        /// rounds, wins, placar, objetivo e a flag de liquidacao — necessario p/ rematch no
        /// mesmo field (senao o Round/Wins do match anterior encerram o novo imediatamente).
        /// </summary>
        public void ResetMatch()
        {
            Round = 0; Wins0 = 0; Wins1 = 0; Score0 = 0; Score1 = 0;
            WinnerSide = 0; LastRoundWinner = 0; Warned30 = 0;
            Golem0Hp = 100; Golem1Hp = 100; GoldGolemHp = 100; ObjectiveDecided = false;
            GoldSwordHolder = -1; GoldSwordExpiryMs = 0;
            Settled = false;
            foreach (var r in Slots) if (r.Occupied) r.Score = 0;
        }

        /// <summary>
        /// Vencedor do round quando o TEMPO esgota (deadline do motor, FUN_00409940):
        /// DEATHMATCH (FFA) = time do jogador com maior Score (empate no topo = 2/empate);
        /// demais modos = placar por time (Score0 vs Score1; igual = 2/empate).
        /// </summary>
        public byte DecideRoundWinnerByScore()
        {
            // GOLEM WAR: se o tempo acaba com os 2 Master Golens vivos, vence o time cujo golem tem MAIS energia
            // (regra oficial softnyx) — NÃO por placar de kills.
            if (Mode == (byte)GameMode.Golem)
                return Golem0Hp > Golem1Hp ? (byte)0 : Golem1Hp > Golem0Hp ? (byte)1 : (byte)2;
            if (Mode == (byte)GameMode.Deathmatch)
            {
                uint best = 0; int atBest = 0; byte side = 2;
                foreach (var r in Slots)
                {
                    if (!r.Occupied) continue;
                    if (atBest == 0 || r.Score > best) { best = r.Score; side = r.Team; atBest = 1; }
                    else if (r.Score == best) atBest++;
                }
                return atBest == 1 ? side : (byte)2;
            }
            return Score0 > Score1 ? (byte)0 : Score1 > Score0 ? (byte)1 : (byte)2;
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
            Log.Info("field", "field {0} MORTE seat {1} (team {2}) killer {3} mode {4} round {5} vivos[0={6} 1={7}]",
                Id, victimSeat, v.Team, killerSeat, Mode, Round, CountAlive(0), CountAlive(1));

            // GOLD SWORD: se o PORTADOR morre, o golem dourado REVIVE (regra oficial) e a espada é perdida.
            if (victimSeat == GoldSwordHolder)
            {
                GoldSwordHolder = -1; GoldGolemHp = 100;
                Log.Ok("field", "field {0} portador da Gold Sword (seat {1}) morreu -> golem dourado revive", Id, victimSeat);
            }

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

            // Fim de round por ELIMINAÇÃO — só nos modos SEM respawn (regra oficial softnyx). Deathmatch/TeamDeath
            // têm respawn (5s), então NÃO encerram por morte; Golem/Boss = mortos não renascem.
            if (Phase == MatchPhase.Playing && !ObjectiveDecided)
            {
                if (Mode == (byte)GameMode.Boss)
                {
                    // BOSS WAR: cada time tem um "boss"; se o boss morre, o time PERDE o round na hora (mesmo com aliados vivos).
                    if (victimSeat == BossSeat(v.Team)) EndRoundObjective((byte)(v.Team ^ 1));
                }
                else if (Mode == (byte)GameMode.Golem)
                {
                    // GOLEM WAR: um time perde quando TODOS os seus jogadores morrem. Conta ocupantes p/ não disparar
                    // com bloco vazio; empate (os 2 zeram no mesmo golpe) = winner 2.
                    int occ0 = 0, occ1 = 0;
                    foreach (var r in Slots)
                        if (r.Occupied && (r.Session != null || r.Bot != null)) { if (r.Team == 0) occ0++; else occ1++; }
                    bool t0dead = occ0 > 0 && CountAlive(0) == 0;
                    bool t1dead = occ1 > 0 && CountAlive(1) == 0;
                    if (t0dead || t1dead)
                        EndRoundObjective(t0dead && t1dead ? (byte)2 : t0dead ? (byte)1 : (byte)0);
                }
            }
        }

        /// <summary>Boss War: o "boss" do time = o 1º slot ocupado do bloco do time (0-9 = time0, 10-19 = time1).
        /// Protegê-lo é o objetivo; sua morte encerra o round. Designação exata (líder/sorteio) = RE/captura pendente.</summary>
        private int BossSeat(int team)
        {
            int lo = team == 0 ? 0 : 10, hi = team == 0 ? 10 : 20;
            for (int i = lo; i < hi && i < Slots.Length; i++)
                if (Slots[i].Occupied && (Slots[i].Session != null || Slots[i].Bot != null)) return i;
            return -1;
        }

        /// <summary>Alcance do golpe do humano no plano XZ (coord) p/ a arbitragem humano→bot. Aproximação do
        /// alcance de melee (aim-vec do 0x30a ≈ 6.0); cone/raycast exato por arma = RE pendente (§11). 5.0: o bot
        /// orbita a ~2.6 no melee, então o acerto conecta COLADO; 9 fazia o número aparecer "antes de hitar" (longe).</summary>
        public const float HumanMeleeRange = 3.0f;

        /// <summary>Throttle entre golpes do humano no MESMO bot (ms) — evita instakill por 0x311 repetidos.</summary>
        private const long BotHitCooldownMs = 250;

        /// <summary>Recuo (coord) do bot ao levar um golpe não-letal — reação VISÍVEL via 0x30a (sem o type-7).</summary>
        private const float HitKnockbackDist = 2.4f;

        /// <summary>Recuo (coord) maior no golpe LETAL — o bot "voa" para trás e congela (morte server-side).</summary>
        private const float DeathKnockbackDist = 3.0f;

        /// <summary>Duração do STAGGER (ms) ao apanhar — o bot trava o ataque/movimento e cambaleia. Maior que o
        /// throttle (250) p/ encadear durante o combo do humano (stun-lock realista; recupera ao parar de bater).</summary>
        private const long BotHitStaggerMs = 550;

        /// <summary>
        /// Arbitra um golpe do humano <paramref name="attackerSeat"/> (0x311) contra o BOT inimigo mais próximo
        /// dentro do alcance — o bot não tem cliente p/ reportar a própria morte (combate cliente-autoritativo),
        /// então o SERVIDOR detecta e aplica. Geometria = distância XZ ao alcance (cone/raycast exato = §11).
        /// Aplica o dano (placeholder por <paramref name="actionId"/> até a RE de iteminfo/combo, §11); se o bot
        /// zera HP, marca a morte (<see cref="OnPlayerDeath"/> credita o humano + placar/round) e devolve o seat
        /// do bot morto p/ o chamador broadcastar o 0x4f. Devolve null se não houve morte (errou/sobreviveu/throttle).
        /// </summary>
        public int? ResolveBotHitByHuman(int attackerSeat, ushort actionId, out int hitBotSlot, byte cause = 0)
        {
            hitBotSlot = -1;
            if (State != 2 || Phase == MatchPhase.Pre || Mode == 0)
            {
                Log.Ok("combat", "field {0}: golpe seat {1} IGNORADO (gate PvP: State={2} Phase={3} Mode={4})",
                    Id, attackerSeat, State, Phase, Mode);
                return null;   // só PvP em jogo
            }
            var a = RecAt(attackerSeat);
            if (a == null || !a.Playing || a.IsBot || a.LastPositionMs == 0)
            {
                Log.Ok("combat", "field {0}: golpe seat {1} IGNORADO (atacante inválido: null={2} playing={3} isBot={4} posMs={5})",
                    Id, attackerSeat, a == null, a?.Playing == true, a?.IsBot == true, a?.LastPositionMs ?? 0);
                return null;
            }

            // Direção que o humano OLHA (do heading do 0x30a). Mesma convenção do EncodeActionBody (Yaw+180):
            // o vetor "look" = ângulo (heading-180) → (lookX,lookZ) = (-sin, -cos). O acerto SÓ conta se o bot
            // estiver no CONE à frente (senão o golpe foi pro lado/trás e o número não deve aparecer).
            float hdg = a.LastHeading * (MathF.PI / 180f);
            float lookX = -MathF.Sin(hdg), lookZ = -MathF.Cos(hdg);
            const float ConeDotMin = 0.20f;   // ~cos(78°): golpe dentro de ±78° da mira

            PlayerRec? best = null;       // melhor alvo DENTRO do alcance E do cone
            PlayerRec? nearest = null;    // bot inimigo mais próximo (mesmo fora) — diagnóstico do "errou"
            float bestD2 = HumanMeleeRange * HumanMeleeRange;
            float nearestD2 = float.MaxValue;
            float bestDot = -2f;
            int enemyBots = 0;
            foreach (var r in Slots)
            {
                if (!r.IsBot || r.Bot == null || r.Dead || r.Team == a.Team || !r.Playing) continue;
                enemyBots++;
                float dx = r.Bot.X - a.LastX, dz = r.Bot.Z - a.LastZ;
                float d2 = dx * dx + dz * dz;
                if (d2 < nearestD2) { nearestD2 = d2; nearest = r; }
                float len = MathF.Sqrt(d2);
                float dot = len > 0.01f ? (dx * lookX + dz * lookZ) / len : 1f;   // >0 = bot à frente da mira
                if (d2 <= bestD2 && dot >= ConeDotMin) { bestD2 = d2; best = r; bestDot = dot; }
                if (best == null && r == nearest) bestDot = dot;   // diagnóstico: dot do +perto p/ calibrar a convenção
            }
            if (best?.Bot == null)
            {
                Log.Ok("combat", "field {0}: golpe seat {1} ERROU — bots={2}, +perto seat {3} a {4:F1} (alc {5}, hdg {10:F0}°, dot {11:F2}, cone≥{12}); humano=({6:F1},{7:F1}) bot=({8:F1},{9:F1})",
                    Id, attackerSeat, enemyBots, nearest?.Slot ?? -1, nearest != null ? MathF.Sqrt(nearestD2) : -1f,
                    HumanMeleeRange, a.LastX, a.LastZ, nearest?.Bot?.X ?? 0f, nearest?.Bot?.Z ?? 0f, a.LastHeading, bestDot, ConeDotMin);
                return null;
            }

            long now = Environment.TickCount64;
            if (now - best.Bot.LastHitMs < BotHitCooldownMs)
            {
                Log.Ok("combat", "field {0}: golpe seat {1} no bot seat {2} THROTTLE ({3}ms<{4}ms)",
                    Id, attackerSeat, best.Slot, now - best.Bot.LastHitMs, BotHitCooldownMs);
                return null;
            }
            best.Bot.LastHitMs = now;
            hitBotSlot = best.Slot;   // acerto confirmado (mesmo não-fatal) — a infra sinaliza o HIT×N à ponte
            ushort dmg = MeleeDamageFor(actionId);
            bool died = best.Bot.TakeDamage(dmg);
            best.Bot.ApplyKnockback(a.LastX, a.LastZ, died ? DeathKnockbackDist : HitKnockbackDist);   // recuo visível (0x30a)
            if (!died) best.Bot.Stagger(now, BotHitStaggerMs);   // cambaleia + interrompe o combo (reação de hit)
            Log.Ok("combat", "field {0}: humano seat {1} ACERTOU bot seat {2} -{3} (HP {4}/{5}) dist={6:F1} action=0x{7:X4}{8}",
                Id, attackerSeat, best.Slot, dmg, best.Bot.Hp, best.Bot.MaxHp, MathF.Sqrt(bestD2), actionId, died ? " -> MORREU" : "");
            if (!died) return null;       // sobreviveu

            OnPlayerDeath(best.Slot, attackerSeat, cause);
            return best.Slot;
        }

        /// <summary>Dano de melee por actionId do 0x311 — PLACEHOLDER até a RE de arma/combo→dano (iteminfo, §11).
        /// 40 ⇒ ~3 golpes p/ derrubar o bot de 100 de energia (balanceamento provisório).</summary>
        private static ushort MeleeDamageFor(ushort actionId) => 40;

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
        /// ADVERSARIO vence O ROUND (objetivo) — o match segue ate completar os rounds configurados na
        /// sala (MaxRounds, do 0x3b); quem fecha o match e' o motor (MatchTick). Dano placeholder
        /// (formula/energia exata = RE/balanceamento; broadcast de "Master Golem has X%% energy" pendente).
        /// </summary>
        /// <summary>True se o seat segura a Gold Sword VÁLIDA (não expirada). Regra oficial: SÓ ela dana o Master Golem.</summary>
        public bool HasGoldSword(int seat, long now) => seat >= 0 && seat == GoldSwordHolder && now < GoldSwordExpiryMs;

        /// <summary>Concede a Gold Sword ao seat (quem deu o ÚLTIMO golpe no golem dourado). Dura 1min30.</summary>
        public void GrantGoldSword(int seat, long now)
        {
            GoldSwordHolder = seat;
            GoldSwordExpiryMs = now + GoldSwordDurationMs;
            Log.Ok("field", "field {0} GOLD SWORD -> seat {1} ({2}s; só ela dana o Master Golem)", Id, seat, GoldSwordDurationMs / 1000);
        }

        public void DamageGolem(int golemTeam, ushort dmg, int attackerSeat = -1)
        {
            if (ObjectiveDecided) return;
            // GATE Gold Sword (regra oficial): só quem segura a espada dana o Master Golem inimigo. attackerSeat<0 =
            // via legado/interno sem gate. O atacante não pode danar o próprio golem (só o inimigo).
            if (attackerSeat >= 0)
            {
                if (!HasGoldSword(attackerSeat, Environment.TickCount64)) return;
                if (SeatTeam(attackerSeat) == golemTeam) return;   // não bate no golem do próprio time
            }
            if (golemTeam == 0) Golem0Hp = (ushort)Math.Max(0, Golem0Hp - dmg);
            else Golem1Hp = (ushort)Math.Max(0, Golem1Hp - dmg);
            Log.Ok("field", "field {0} Master Golem time{1} energia={2}%", Id, golemTeam, golemTeam == 0 ? Golem0Hp : Golem1Hp);
            if (Golem0Hp == 0) EndRoundObjective(1);       // golem do time0 destruido -> time1 vence o round
            else if (Golem1Hp == 0) EndRoundObjective(0);  // golem do time1 destruido -> time0 vence o round
        }

        /// <summary>Time (0/1) de um seat pelo bloco (0-9=0, 10-19=1).</summary>
        private static int SeatTeam(int seat) => seat < 10 ? 0 : 1;

        /// <summary>Aplica dano ao golem DOURADO central (neutro). Ao ZERAR, o <paramref name="attackerSeat"/> (quem
        /// deu o último golpe) GANHA a Gold Sword (regra oficial). Devolve true ao zerar. NÃO encerra o round.</summary>
        public bool DamageGoldGolem(ushort dmg, int attackerSeat = -1)
        {
            if (GoldGolemHp == 0) return false;
            GoldGolemHp = (ushort)Math.Max(0, GoldGolemHp - dmg);
            if (GoldGolemHp != 0) return false;
            Log.Ok("field", "field {0} golem DOURADO derrotado", Id);
            if (attackerSeat >= 0) GrantGoldSword(attackerSeat, Environment.TickCount64);
            return true;
        }

        /// <summary>Despacha o dano ao golem-alvo: <paramref name="target"/> 0/1 = Master Golem do time (gated pela Gold
        /// Sword; ao zerar dispara <see cref="EndRoundObjective"/>), 2 = golem dourado (ao zerar concede a espada ao
        /// atacante). <paramref name="attackerSeat"/> = quem golpeia (-1 = legado sem gate). Devolve true ao derrotar o alvo.</summary>
        public bool DamageGolemTarget(int target, ushort dmg, int attackerSeat = -1)
        {
            if (target == 2) return DamageGoldGolem(dmg, attackerSeat);
            DamageGolem(target, dmg, attackerSeat);
            return (target == 0 ? Golem0Hp : Golem1Hp) == 0;
        }

        /// <summary>
        /// Encerra o ROUND por OBJETIVO (Golem/Boss destruido): credita o round ao time vencedor e
        /// broadcasta o 0x4a de fim-de-round (mesmo layout dos handlers 0x4a/0x4d). ObjectiveDecided
        /// trava o re-disparo dentro do round; StartRound o re-arma p/ o proximo.
        /// </summary>
        public void EndRoundObjective(byte winnerTeam)
        {
            if (ObjectiveDecided) return;
            ObjectiveDecided = true;
            Log.Ok("field", "field {0} OBJETIVO: time{1} venceu o ROUND {2} (Golem inimigo destruido)", Id, winnerTeam, Round);
            EndRound(winnerTeam);
            BroadcastFieldPlaying(0x4a, Build0x4a());
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
