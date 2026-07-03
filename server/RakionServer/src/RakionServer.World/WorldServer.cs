using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;
using RakionServer.World.CharSelect;

namespace RakionServer.World
{
    /// <summary>
    /// Objeto central do world (equivalente ao CWorld do worldserv.exe): aceita
    /// conexoes TCP (porta do jogo), liga os sockets UDP de gameplay, mantem o
    /// canal IPC com o broker e o estado das sessoes/usuarios.
    /// </summary>
    public sealed partial class WorldServer
    {
        private readonly WorldConfig _cfg;
        private readonly WorldDatabase _db;
        private BrokerLink? _broker;

        private Socket? _listener;
        private Network.UdpGameplay? _udpGame;
        private CancellationTokenSource? _cts;

        private readonly ConcurrentDictionary<ushort, ClientSession> _sessions = new();
        private readonly ConcurrentDictionary<string, DateTime> _validated = new(StringComparer.Ordinal);
        private int _currentUsers;
        private int _nextSlotHint;

        /// <summary>Canais/IDCs do mundo (this+0x60/0x64/0x68). Padrao: 1 canal normal.</summary>
        public readonly List<Domain.Channel> Channels = new() { new Domain.Channel(1, "Channel 1") };

        /// <summary>Variaveis de servidor setaveis por GM (this+0x51c8, 0x200 entradas u32). Opcodes 0x08/0x0a.</summary>
        public readonly uint[] GmVars = new uint[0x200];

        /// <summary>Referencia de ping e contador de matches (anti-cheat de latencia, opcode 0x61).</summary>
        public int PingReference;
        public int PingMatchCount;

        /// <summary>Fields/partidas (this+0xe4) e rooms/chat (this+0xdc).</summary>
        public readonly List<Domain.Field> Fields = new();
        public readonly List<Domain.Room> Rooms = new() { new Domain.Room(0) };

        public Domain.Field? GetField(int id) => id < 0 ? null : Fields.Find(f => f.Id == id);
        public Domain.Room? GetRoom(int id) => id < 0 ? null : Rooms.Find(r => r.Id == id);

        private int _nextFieldId;

        /// <summary>
        /// Aloca um field/sala (espelha a varredura de this+0xe4 por slot livre no
        /// RoomCreate FUN_00423580). Cria a entrada no dominio e devolve o Field.
        /// </summary>
        public Domain.Field CreateField(string name, byte mapId, byte mode, ushort capacity, ClientSession master)
        {
            lock (Fields)
            {
                var f = new Domain.Field(_nextFieldId++)
                {
                    Name = name,
                    Mode = mode,
                    MapId = mapId,
                    // capacity e ushort (ate ~0x4ba=1210 nas salas ranqueadas); o cast cru truncava >255.
                    // Clamp: 0 -> default 8; acima de 255 satura em byte.MaxValue (sem wrap).
                    MaxPlayers = capacity == 0 ? (byte)8 : (byte)System.Math.Min((int)capacity, byte.MaxValue),
                    Master = master,
                    MasterSlot = master.Slot,
                    State = 1, // ocupado (field+8 != 0)
                };
                f.Add(master);
                Fields.Add(f);
                // FUN_0040b7b0: vincula o master ao field (estado "em field")
                master.FieldId = f.Id;
                master.InField = true;
                master.FieldSecondary = true;
                master.Status = Domain.UserStatus.InField;
                Log.Info("field", "[{0}] criou field {1} '{2}' (map={3} mode={4} cap={5})",
                    master.Slot, f.Id, name, mapId, mode, f.MaxPlayers);
                return f;
            }
        }

        /// <summary>
        /// Garante que a sessao tem um Field ativo com seat alocado (ponte da cadeia de entrada
        /// 0x3b/0x4b — proven-working — para o modelo de partida real). Solo: cria um field
        /// dedicado; multi: reusa o field ja associado (FieldId). Marca State=2 (em jogo) p/ o
        /// motor rodar e seta os campos de seat do user (FUN_0040b7b0).
        /// </summary>
        /// <summary>Se true, um cliente que entra no stage se JUNTA ao field de OUTRO humano já em jogo (em vez de
        /// criar um field SOLO). É o multiplayer "por atalho" — sem game list/salas: o 1º entra no stage, o 2º cai
        /// no MESMO field. Necessário p/ 2 clientes juntos (captura do blob da Cell + PvP real). Ambos devem estar
        /// no MESMO mapa (criar sala com o mesmo mapa) senão dessincroniza.</summary>
        public static bool AutoJoinSameField = false;   // desligado: o certo é game list + join (não atalho)

        /// <summary>Acha um field EM JOGO (State==2) com outro humano jogando p/ o <paramref name="s"/> se juntar.</summary>
        private Domain.Field? FindJoinableField(ClientSession s)
        {
            if (!AutoJoinSameField) return null;
            lock (Fields)
            {
                foreach (var f in Fields)
                {
                    if (f.Id == s.FieldId || f.State != 2) continue;
                    foreach (var r in f.Slots)
                        if (r.Session != null && r.Session != s && !r.IsBot && r.Playing)
                        {
                            Log.Ok("field", "[{0}] JOIN automático no field {1} (host seat {2}) — multiplayer sem salas",
                                s.Slot, f.Id, r.Slot);
                            return f;
                        }
                }
            }
            return null;
        }

        public Domain.Field EnsureFieldForSession(ClientSession s)
        {
            // JOIN tem PRIORIDADE sobre o field-da-sala próprio: o cliente já teve FieldId setado ao criar a sala
            // (GetOrCreateRoomField), então GetField pegaria o dele e nunca juntaria. FindJoinableField primeiro =
            // 2º cliente cai no field do 1º (multiplayer sem game list).
            Domain.Field f = FindJoinableField(s)
                ?? GetField(s.FieldId)
                ?? CreateField(s.CharName.Length > 0 ? s.CharName : $"field{s.Slot}", mapId: 0, mode: 0, capacity: 8, master: s);
            if (f.Id != s.FieldId) s.FieldId = f.Id;   // re-vincula ao field juntado
            f.State = 2; // field+8 = 2 (em jogo)
            int seat = f.AssignSeat(s);
            if (seat >= 0) { s.FieldSeat = (byte)seat; s.FieldObjectIndex = (ushort)seat; }
            if (f.MasterSlot < 0 || f.MasterSlot >= 0x14) f.MasterSlot = seat;
            return f;
        }

        /// <summary>
        /// Field da SALA pré-partida do host. O 0x3b só guarda os params (PendingRoom*); o Field nasce sob
        /// demanda — e o /addbot precisa dele AINDA na sala (Status=2), antes do stage. Reusa o Field já
        /// vinculado (FieldId) ou cria um dos params do host, mas SEM tocar no Status de lobby (a sala
        /// pré-partida é Status=2; o stage promove a 3 depois reusando este mesmo Field via
        /// <see cref="EnsureFieldForSession"/>). Devolve null se o host não criou sala (sem PendingRoom*),
        /// p/ não materializar field-fantasma no lobby.
        /// </summary>
        public Domain.Field? GetOrCreateRoomField(ClientSession host)
        {
            lock (Fields)
            {
                var existing = Fields.Find(f => f.Id == host.FieldId);
                if (existing != null) return existing;
                if (host.PendingRoomMap == 0 && string.IsNullOrEmpty(host.PendingRoomName)) return null;
                var f = new Domain.Field(_nextFieldId++)
                {
                    Name = host.PendingRoomName,
                    Mode = host.PendingRoomMode,
                    MapId = host.PendingRoomMap,
                    MaxPlayers = 16,            // 8v8; o bot só precisa de um assento livre no time oposto
                    Master = host,
                    State = 1,                  // ocupado (pré-partida) — não 2 (em jogo)
                    // CRÍTICO: o tick liquida `State==1 && !Settled` (= partida ACABADA -> DiscardBots). A sala
                    // pré-partida é State=1 igual a um match encerrado e seria liquidada na hora (bots somem em
                    // ~50ms). Marcando Settled=true o tick a ignora; ResetMatch (engage 0x43) zera p/ false ao
                    // começar a partida, restaurando a liquidação pós-match normal.
                    Settled = true,
                };
                Fields.Add(f);
                f.Add(host);                                  // lista Players (count/broadcast)
                int seat = f.AssignSeat(host);                // ASSENTO real (Slots): FindRec/Team/MasterSlot dependem disto
                if (seat >= 0) { host.FieldSeat = (byte)seat; host.FieldObjectIndex = (ushort)seat; f.MasterSlot = seat; }
                host.FieldId = f.Id;           // vincula; NÃO toca em Status/InField (sala pré-partida = Status=2)
                Log.Info("field", "[{0}] field da sala criado sob demanda (id={1} map={2} mode={3}) p/ roster/bot",
                    host.Slot, f.Id, host.PendingRoomMap, host.PendingRoomMode);
                return f;
            }
        }

        /// <summary>Remove o usuario do field; se ficar vazio, libera o field.</summary>
        public void LeaveField(ClientSession s)
        {
            var f = GetField(s.FieldId);
            if (f == null) return;
            f.Remove(s);
            s.FieldId = -1;
            // O último HUMANO saiu (Count = só humanos; bots não entram em Players): descarta os bots
            // e libera o field. Bots nunca mantêm uma sala viva — sem humano, a sala morre.
            if (f.Count == 0)
            {
                int bots = f.BotCount;
                DiscardBots(f);
                lock (Fields) Fields.Remove(f);
                Log.Info("field", "field {0} '{1}' liberado (sem humanos; {2} bot(s) descartado(s))",
                    f.Id, f.Name, bots);
            }
        }

        public WorldServer(WorldConfig cfg, WorldDatabase db)
        {
            _cfg = cfg;
            _db = db;
        }

        public bool Locked { get; private set; }                 // this+0x50 (servidor fechado p/ GM)
        public PuConfig PuConfig { get; private set; } = new();   // pu_config: preço/bônus/multiplicadores do PU (lida no boot)
        public EnchantConfig EnchantConfig { get; private set; } = new();   // enchant_*: coeficientes do refino (lida no boot)
        public int MaxUser => _cfg.MaxUser;                       // this+0x536c
        public int CurrentUsers => Volatile.Read(ref _currentUsers);

        /// <summary>Sessoes ativas (iteracao thread-safe sobre o dicionario).</summary>
        public IEnumerable<ClientSession> Sessions => _sessions.Values;

        /// <summary>Sessao por slot (indice do pacote UDP de gameplay).</summary>
        public ClientSession? GetSession(ushort slot) => _sessions.TryGetValue(slot, out var s) ? s : null;

        /// <summary>
        /// Sessao por IP de origem. O 0x0C replayado fixa slot 0, entao o cliente sempre
        /// manda slot 0 nos pacotes UDP; quando o slot nao casa com a sessao TCP real (que
        /// tem slot incremental), resolvemos pelo IP do remetente (fallback do handshake UDP).
        /// </summary>
        public ClientSession? GetSessionByIp(string ip)
        {
            foreach (var s in _sessions.Values)
                if (s.Connected && s.SlotActive && s.RemoteIp == ip)
                    return s;
            return null;
        }

        /// <summary>Envia um tick de gameplay (1583 + SEQ) ao endpoint UDP do jogador.</summary>
        public void SendGameplayTick(System.Net.IPEndPoint to, byte seq) => _udpGame?.SendTick(to, seq);

        /// <summary>Envia um datagrama de gameplay SINTETIZADO (move/ação 0x30a do bot) ao endpoint UDP de
        /// um peer humano. O bot não tem cliente p/ enviar — o servidor monta o pacote do estado do bot.</summary>
        internal void SendGameplayRaw(System.Net.IPEndPoint to, byte[] pkt) => _udpGame?.SendRaw(to, pkt);

        /// <summary>
        /// Motor da partida por-field (FUN_00409940 + FUN_004069a0): roda a maquina de estado
        /// de cada field ativo (Pre->Playing->RoundEnd->proximo-round/fim) e dispara os
        /// broadcasts (0x48/0x49/0x4a/0x44). Roda em loop unico (~200ms), NAO por-sessao.
        /// Tambem mantem o tick 1583 (UDP) idle a cada ~150ms p/ os players in-field.
        /// </summary>
        private async Task FieldEngineLoopAsync(CancellationToken ct)
        {
            Log.Ok("field", "motor da partida (FieldEngine) iniciado");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Domain.Field[] snapshot;
                    lock (Fields) snapshot = Fields.ToArray();
                    foreach (var f in snapshot)
                    {
                        if (f.State == 2) MatchTick(f);
                        else if (f.State == 1 && !f.Settled) SettleMatch(f);
                    }

                }
                catch (Exception ex) { Log.Debug("field", "engine tick: {0}", ex.Message); }
                await Task.Delay(100, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Relogio de gameplay 1583 (150ms) APENAS p/ salas BATTLE/PvP (Mode != 0): GameSeq
        /// INCREMENTA a cada tick — e' o frame/clock da partida; seq fixo congela o personagem e
        /// cadencia errada deixa o cliente congelado ate o seq alinhar (~2min observados a 200ms).
        /// Solo stage (Mode 0) e' client-side: sem tick (eco/timer no solo interrompia combos).
        /// Loop dedicado p/ manter os 150ms (o engine loop dorme 100ms -> tick efetivo de 200ms).
        /// </summary>
        private async Task GameClockLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Domain.Field[] snapshot;
                    lock (Fields) snapshot = Fields.ToArray();
                    foreach (var f in snapshot)
                    {
                        if (f.State != 2) continue;   // solo E PvP — sem o clock o cliente solo nao manda input (trava no briefing)
                        // 2+ HUMANOS = P2P: o CLIENTE (host) dirige o relógio 1583 e o manda direto ao outro
                        // (captura do original: o 1583 é P2P 2301↔2302, NÃO servidor->cliente). O tick do servidor
                        // conflitava com o do peer -> o 2º cliente ficava "fantasma". Só solo/bot usa o clock do server.
                        if (f.HumanCount >= 2) continue;
                        foreach (var r in f.Slots)
                        {
                            var s = r.Session;
                            if (s == null || !r.Occupied || s.UdpEndpoint == null) continue;
                            unchecked { s.GameSeq++; }
                            _udpGame?.SendTick(s.UdpEndpoint, s.GameSeq);
                        }
                    }
                }
                catch (Exception ex) { Log.Debug("field", "game clock: {0}", ex.Message); }
                await Task.Delay(150, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        // escrito pelo engine loop E pelos handlers de sessao (NotifyPlayerReady) -> concurrent
        private readonly ConcurrentDictionary<int, long> _fieldStatusBeat = new();

        /// <summary>
        /// Um tick do motor de um field (FUN_00409940). Avanca as fases pelo deadline (field+0x2b8),
        /// re-broadcasta 0x48 (FieldStatus) a cada ~1s, e dispara fim-de-round / fim-de-match.
        /// </summary>
        private void MatchTick(Domain.Field f)
        {
            long now = Environment.TickCount64;

            switch (f.Phase)
            {
                case Domain.MatchPhase.Playing:
                    // SOLO PvE (Mode 0, time-attack Stage Clear): combate + countdown + clear sao
                    // CLIENT-SIDE. NAO re-enviar 0x48 (re-envio glitchava o countdown 3->1 e
                    // interrompia combos) e NAO rodar logica de round/placar; o cliente conduz.
                    if (f.Mode == 0) break;

                    // PvP (GOLEM/DEATHMATCH/TEAMDEATH/BOSS): motor de round servidor-side.
                    // SEM re-broadcast periodico de 0x48: o re-envio interrompia combos no solo e a
                    // captura do room flow (mitm_full_113423) mostra UM 0x48 so na entrada. O cliente
                    // conta o tempo sozinho a partir dele; cadencia real em PvP = pendente de captura.
                    if (f.Warned30 == 0 && f.RemainingSec() <= 30)
                    {
                        f.Warned30 = 1; // field+0x2be (flag aviso de 30s)
                        Log.Info("field", "field {0} round {1}: 30s restantes", f.Id, f.Round);
                    }
                    // tempo esgotado -> fim de round por placar (FUN_00409940 deadline)
                    if (now >= f.DeadlineMs)
                    {
                        f.EndRound(f.DecideRoundWinnerByScore());
                        // FIELD 0x4a aos playing: body=[cause/2bd][2bf][2c0][2c1] (mesmo layout dos
                        // handlers 0x4a/0x4d de fim-de-round)
                        f.BroadcastFieldPlaying(0x4a,
                            new byte[] { f.LastRoundWinner, f.WinnerSide, f.Wins0, f.Wins1 });
                    }
                    else
                    {
                        // IA dos bots (spawn 0x45 + decisão/movimento/ataque sintetizados) durante o round.
                        BotTick(f);
                    }
                    break;

                case Domain.MatchPhase.RoundEnd:
                    if (now >= f.DeadlineMs)
                    {
                        f.Round++;
                        if (f.Round > f.MaxRounds)
                        {
                            f.EndMatch(2); // acabaram os rounds (empate em rounds)
                            f.BroadcastLobby(f.BuildMatchEnd(2));
                            _fieldStatusBeat.TryRemove(f.Id, out _);
                        }
                        else if (f.CountPlaying() == 0)
                        {
                            f.EndMatch(5); // sem jogadores
                            f.BroadcastLobby(f.BuildMatchEnd(5));
                            _fieldStatusBeat.TryRemove(f.Id, out _);
                        }
                        else
                        {
                            // PROXIMO ROUND: reinicia o relogio/golens e anuncia (0x49 NovoRound + 0x48).
                            f.StartRound();
                            f.BroadcastLobby(f.Build0x49());
                            f.BroadcastLobby(f.Build0x48());
                            Log.Ok("field", "field {0} -> round {1}/{2} (w0={3} w1={4})", f.Id, f.Round, f.MaxRounds, f.Wins0, f.Wins1);
                        }
                    }
                    break;

                case Domain.MatchPhase.Pre:
                default:
                    break;
            }
        }

        /// <summary>
        /// Liquida o resultado do MATCH no DB (roda 1x apos EndMatch, field+8==1): incrementa
        /// win/lose/draw do characterinfo de cada jogador conforme o time vencedor (Wins0 vs
        /// Wins1; empate = draw p/ todos) e atualiza o overlay em memoria (CharWin/Lose/Draw).
        /// Mode 0 (solo PvE) nao liquida — o resultado vem do cliente pelos 0x50/0x53.
        /// </summary>
        private void SettleMatch(Domain.Field f)
        {
            f.Settled = true;
            // Fim de match (rounds acabaram / sem players): descarta TODOS os bots — eles nunca
            // persistem no roster pós-partida; a volta à sala mostra só humanos (re-adicionar refaz).
            DiscardBots(f);
            if (f.Mode == 0) return;
            byte winner = f.Wins0 > f.Wins1 ? (byte)0 : f.Wins1 > f.Wins0 ? (byte)1 : (byte)2;
            foreach (var r in f.Slots)
            {
                var s = r.Session;
                if (s == null || !r.Occupied || s.ActiveCharId <= 0) continue;
                int win = 0, lose = 0, draw = 0;
                if (winner == 2) draw = 1;
                else if (r.Team == winner) win = 1;
                else lose = 1;
                s.CharWin += (uint)win; s.CharLose += (uint)lose; s.CharDraw += (uint)draw;
                _ = _db.AddCharacterResultAsync(s.ActiveCharId, win, lose, draw, exp: 0);
                Log.Ok("field", "field {0} settle: char {1} seat {2} -> {3} (score {4})",
                    f.Id, s.ActiveCharId, r.Slot, win != 0 ? "WIN" : lose != 0 ? "LOSE" : "DRAW", r.Score);
            }
        }

        /// <summary>
        /// Dispara o 0x48 FieldStatus de inicio (handler 0x48 / FUN_00408440): o player marcou ready.
        /// Se a partida (re)iniciou, broadcasta o 0x48 a todos. Usado pelos handlers de campo.
        /// </summary>
        public void NotifyPlayerReady(Domain.Field f, ClientSession s)
        {
            // Time-attack solo: cada entrada no stage RECOMECA o cronometro do 0:00. Reseta o DeadlineMs
            // (RemainingSec volta ao cheio = 603); senao um field reaproveitado (Phase ja Playing -> StartRound
            // nao roda) deixaria o DeadlineMs obsoleto e o HUD comecaria fora do zero.
            f.DeadlineMs = Environment.TickCount64 + (f.RoundDurationSec + 3) * 1000L;
            bool started = f.OnPlayerReady(s);
            if (started)
            {
                _fieldStatusBeat[f.Id] = Environment.TickCount64;
                f.BroadcastLobby(f.Build0x48());
                Log.Ok("field", "[{0}] partida iniciada no field {1} (0x48 a {2} player(s))", s.Slot, f.Id, f.CountPlaying());
            }
            else
            {
                // spawn tardio / aguardando os demais: 0x48 so a este player
                try { s.SendEncryptedFrame(f.Build0x48()); } catch { }
            }
        }

        /// <summary>Define o estado "servidor fechado" (GM open/close, opcode 0x03).</summary>
        public void SetLocked(bool locked) => Locked = locked;

        /// <summary>Desconecta todos os usuarios que nao sao GM (usado no GM close).</summary>
        public void DisconnectNonGm(byte reason)
        {
            foreach (var s in _sessions.Values)
                if (s.Status != 0 && s.Status != Domain.UserStatus.LobbyGm)
                    s.Disconnect(reason);
        }
        public WorldConfig Config => _cfg;

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();

            // TCP do jogo
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Bind(new IPEndPoint(IPAddress.Any, _cfg.Port));
            _listener.Listen(128);
            Log.Ok("world", "TCP do jogo ouvindo na porta {0}", _cfg.Port);

            // canal IPC com o broker — fica dono da porta UDP de IPC (= _cfg.Port,
            // a mesma usada pelo gameplay no original; aqui o BrokerLink a possui).
            _broker = new BrokerLink(_cfg, GetStats, ValidateLoginAsync);
            _broker.Start();

            // UDP de gameplay (recv + relay aos peers do field). Roda na porta de gameplay
            // que nao colide com o IPC do broker (Port1 == _cfg.Port e do broker; usa Port2).
            int gamePort = _cfg.UdpPort2 != _cfg.Port ? _cfg.UdpPort2 : _cfg.UdpPort1;
            if (gamePort != _cfg.Port)
            {
                _udpGame = new UdpGameplay(this, gamePort);
                _udpGame.Start();
            }

            // Log em arquivo para diagnóstico sem precisar copiar do console.
            Log.EnableFileLog(Path.Combine(AppContext.BaseDirectory, "worldserver.log"));

            // Trilha fina do mini-peer (categoria 'peer'): cada estado/frame do handshake de sessão dos bots.
            // Liga o sink do slice RakionServer.Peer (I/O isolado) ao Log do servidor p/ o teste in-game cravar
            // o ponto exato do stall. Sai como [DBG] [peer] (gateado por Log.DebugEnabled).
            RakionServer.Peer.PeerTrace.Sink = line => Log.Ok("peer", "{0}", line);

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _ = Task.Run(() => FieldEngineLoopAsync(_cts.Token)); // motor da partida por-field (FUN_00409940)
            _ = Task.Run(() => GameClockLoopAsync(_cts.Token));   // relogio 1583 (150ms) das salas Battle/PvP

            await _db.PingAsync();
            await _db.EnsureSchemaAsync();     // provisiona itembox.qslot + pu_config se faltarem
            PuConfig = await _db.LoadPuConfigAsync();
            Log.Ok("shop", "pu_config: preço={0} bônus={1} {2}d  xp×{3} gold×{4}{5}", PuConfig.Price,
                PuConfig.BonusPoints, PuConfig.DurationDays, PuConfig.ExpMult, PuConfig.GoldMult,
                PuConfig.PromoActive ? " (promo ON)" : "");
            EnchantConfig = await _db.LoadEnchantConfigAsync();
            Log.Ok("enchant", "config: {0} catalisador(es)  evento×{1} PU×{2}",
                EnchantConfig.CatalyzerCount, EnchantConfig.EventMult, EnchantConfig.PuMult);
            await LoadItemDefsCacheAsync();   // catalogo de itens (iteminfo) p/ a compra 0x2e
            _levelCurve = await _db.LoadLevelCurveAsync(); // curva de exp por classe (level-up 0x50)
            Log.Ok("level", "curva de level carregada: {0} entradas (classlevelinfo)", _levelCurve.Count);
            _ = Task.Run(() => ConfigReloadLoopAsync(_cts.Token));   // reload a quente de pu_config/enchant_* (admin sem restart)
            Log.Ok("world", "World Server pronto (ServerId={0})", _cfg.ServerId);
        }

        /// <summary>Reload a quente das configs editáveis pelo painel admin (pu_config + enchant_*): relê a cada 15s
        /// e troca a referência. Cada config é imutável após o load, então a troca é atômica e segura p/ os leitores
        /// (roll do refino, bônus de PU) sem lock. Deixa ligar/desligar evento SEM reiniciar o World; um load vazio
        /// (hiccup do DB) é ignorado p/ não derrubar um evento ativo.</summary>
        private async Task ConfigReloadLoopAsync(System.Threading.CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
                catch (OperationCanceledException) { break; }
                try
                {
                    var ench = await _db.LoadEnchantConfigAsync();
                    if (ench.CatalyzerCount == 0) continue;   // DB indisponível -> mantém a config atual
                    bool changed = ench.EventMult != EnchantConfig.EventMult || ench.PuMult != EnchantConfig.PuMult;
                    EnchantConfig = ench;
                    PuConfig = await _db.LoadPuConfigAsync();   // DB confirmado OK pelo load acima
                    if (changed) Log.Ok("enchant", "config recarregada: evento×{0} PU×{1}", ench.EventMult, ench.PuMult);
                }
                catch (Exception ex) { Log.Warn("db", "reload de config: {0}", ex.Message); }
            }
        }

        /// <summary>Acesso ao DB para os handlers (compra 0x2e).</summary>
        public WorldDatabase Db => _db;

        private System.Collections.Generic.Dictionary<(byte Cls, byte Level), int> _levelCurve = new();

        /// <summary>Exp TOTAL p/ avancar do nivel atual (classlevelinfo). 0 = sem proximo nivel.</summary>
        public int NextLevelExp(byte cls, byte level) => _levelCurve.TryGetValue((cls, level), out var e) ? e : 0;

        /// <summary>
        /// Credita exp ao char ativo e processa level-ups (FUN_0040d300): acumula CharExp,
        /// sobe CharLevel/CharLevelPoint e persiste exp + nivel no characterinfo. O threshold de cada
        /// nivel e' o MEIO do intervalo da curva classlevelinfo ((curva[L-1]+curva[L])/2) — house-rule
        /// p/ "barra cheia = upa" exato (o cliente desenha o cheio no meio do span; ver loop).
        /// Devolve quantos niveis subiu (0 = nenhum).
        /// </summary>
        public int GrantExp(ClientSession s, uint exp)
        {
            if (s.ActiveCharId <= 0 || exp == 0) return 0;
            s.CharExp += exp;
            _ = _db.AddCharacterResultAsync(s.ActiveCharId, 0, 0, 0, exp);
            return SettleLevels(s);
        }

        /// <summary>Aplica o RESULTADO de um stage solo (handler 0x53): bônus de PU sobre exp/gold, credita gold
        /// (saldo + DB), grava o MELHOR rank do stage (userstageinfo) e concede exp (curva classlevelinfo).
        /// Devolve o nº de level-ups. Regra de negócio do motor de partida/progressão — fora do handler de rede.</summary>
        public int ApplyStageResult(ClientSession s, byte stage, byte rank, uint exp, uint gold)
        {
            exp = s.BonusExp(exp); gold = s.BonusGold(gold);                       // bônus de PU (pu_config)
            s.Gold += gold;
            if (gold > 0 && s.GameInfoId > 0) _ = _db.AddGoldAsync(s.GameInfoId, (int)gold);
            if (rank > 0 && s.ActiveCharId > 0) _ = _db.SaveStageRankAsync(s.ActiveCharId, stage, rank); // melhor rank por stage
            return GrantExp(s, exp);
        }

        /// <summary>Domínio do messenger (add buddy, 0x19): resolve o nick -> conta dona e, se existe e não é o
        /// próprio jogador, persiste a amizade RECÍPROCA (buddylist nos 2 sentidos). Devolve (status, accountId do
        /// dono) p/ o handler serializar a resposta — status 0=ok, 2=char não existe (lang 598). O AddBuddy do
        /// cliente é MUDO (não emite SVC_ADD_BUDDY), então a amizade é persistida aqui, não no Buddy.</summary>
        public async Task<(byte Status, string Account)> ResolveAndAddBuddyAsync(ClientSession s, string targetNick)
        {
            string? targetAccount = await _db.GetCharOwnerByNickAsync(targetNick);
            if (targetAccount == null) return ((byte)2, "");                 // char não existe (lang 598)
            if (!string.Equals(targetAccount, s.UserId, StringComparison.OrdinalIgnoreCase))
            {
                await _db.AddBuddyReciprocalAsync(s.UserId, targetAccount);
                Log.Ok("buddy", "[{0}] amizade '{1}' <-> '{2}' (nick '{3}') persistida", s.Slot, s.UserId, targetAccount, targetNick);
            }
            return ((byte)0, targetAccount);
        }

        /// <summary>Aplica o UPGRADE do refino (handler 0x74 = clique de upgrade). SERVER-AUTHORITATIVE: este build do
        /// worldserv DESCARTAVA o roll de FUN_0040c310 (o cliente fica em "Upgrading Now" esperando o resultado), então
        /// reconstruímos o comportamento pretendido — valida (CUser::CheckEnchantReinforce), ROLA a probabilidade,
        /// aplica o delta de nível na arma (persiste itembox.level) e consome catalyzer + materiais. Regra do motor de
        /// refino — fora do handler de rede. Devolve o result code (0=+1, 1=nada, 2=-1; 6/7/8 = erro de validação).</summary>
        public byte ApplyEnchant(ClientSession s, byte weaponSlot, byte catalyzerSlot,
            System.Collections.Generic.IReadOnlyList<byte> materialSlots)
        {
            // VALIDAÇÃO (FUN_0040c310): arma presente e <8000, catalyzer 0x32c9..0x32cd, materiais 0x36b1..0x36b3,
            // caps de nível. Erro -> 6/7/8 (o cliente mostra "não dá"), sem mexer no estado.
            int weaponId = BoxItemAt(s, weaponSlot);
            int catId = BoxItemAt(s, catalyzerSlot);
            if (weaponId == 0 || weaponId >= 8000) return 6;
            if (!EnchantConfig.TryGetCatalyzer(catId, out var cat)) return 7;   // catalisador desconhecido
            int curLevel = weaponSlot < s.BoxLevel.Count ? s.BoxLevel[weaponSlot] : 0;
            if (curLevel >= 15) return 6;
            if (curLevel > cat.LevelCap) return 8;             // arma acima do teto do catalisador (Mithril +4, etc.)
            int j1 = 0, j2 = 0, j3 = 0;
            foreach (byte ms in materialSlots)
            {
                int mid = BoxItemAt(s, ms);
                if (mid == 0x36b1) j1++;
                else if (mid == 0x36b2) j2++;
                else if (mid == 0x36b3) j3++;
                else return 7;
            }

            // ROLL server-authoritative (a fórmula de probabilidade roda no servidor) -> delta de nível.
            // s.PuActive entra no roll: a EnchantConfig aplica o multiplicador de Power User (e o de evento).
            byte result = RollEnchant(catId, curLevel, j1, j2, j3, s.PuActive);
            int delta = EnchantDelta(result);
            int newLevel = System.Math.Clamp(curLevel + delta, 0, 15);
            if (weaponSlot < s.BoxLevel.Count) s.BoxLevel[weaponSlot] = newLevel;
            int wRow = weaponSlot < s.BoxRowId.Count ? s.BoxRowId[weaponSlot] : 0;
            if (wRow > 0) _ = _db.UpdateItemBoxLevelAsync(wRow, newLevel);          // persiste o +N

            // CONSOME catalyzer + materiais (some do box; remoção persistida pela linha EXATA do itembox)
            Consume(s, catalyzerSlot);
            foreach (byte ms in materialSlots) Consume(s, ms);

            Log.Ok("enchant", "[{0}] refino: arma slot {1} +{2}->+{3} (result={4}); catalyzer {5:x} + {6} joia(s) [j1={7} j2={8} j3={9}] consumidos",
                s.Slot, weaponSlot, curLevel, newLevel, result, catId, materialSlots.Count, j1, j2, j3);
            return result;
        }

        /// <summary>Lê o itemId de uma célula do box (0 = vazia/fora de faixa).</summary>
        private static int BoxItemAt(ClientSession s, byte slot) => slot < s.BoxItems.Count ? s.BoxItems[slot] : 0;

        /// <summary>Esvazia a célula e persiste a remoção da linha EXATA do itembox.</summary>
        private void Consume(ClientSession s, byte slot)
        {
            var (_, rowId) = s.ClearBoxCell(slot);
            if (rowId > 0) _ = _db.DeleteItemBoxByIdAsync(rowId);
        }

        /// <summary>Roleta do refino: a probabilidade de sucesso vem da <see cref="EnchantConfig"/> (banco —
        /// coeficientes por catalisador + multiplicadores de evento/PU; estrutura do polinômio fiel ao FUN_0040c310).
        /// Aqui só sorteamos sucesso vs falha e, na falha, se vira downgrade (-1) ou nada. Destroy (result 5) é
        /// neutralizado neste build. Devolve o result code (0=+1, 1=nada, 2=-1).</summary>
        private byte RollEnchant(int catalyzerId, int curLevel, int j1, int j2, int j3, bool puActive)
        {
            double p0 = EnchantConfig.SuccessChance(catalyzerId, curLevel, j1, j2, j3, puActive);
            if (_rng.NextDouble() < p0) return 0;   // SUCESSO +1
            double downgrade = (1.0 - p0) * EnchantConfig.DowngradeFactor(curLevel);
            return _rng.NextDouble() < downgrade ? (byte)2 : (byte)1;   // 2=-1 (downgrade) ou 1=nada
        }

        private static int EnchantDelta(byte resultCode) => resultCode switch
        {
            0 => +1, 2 => -1, 3 => -2, 4 => -3, _ => 0,
        };

        private readonly System.Random _rng = new();

        /// <summary>Sobe os niveis PENDENTES pela curva (sem creditar exp) e persiste. Chamado ao GANHAR exp
        /// (<see cref="GrantExp"/>) E no LOAD do char — um char carregado JÁ acima do limiar upa na hora, sem
        /// precisar ganhar exp de novo. Devolve quantos niveis subiu (0 = nenhum).</summary>
        public int SettleLevels(ClientSession s)
        {
            if (s.ActiveCharId <= 0) return 0;
            int ups = 0;
            while (s.CharLevel < 99)
            {
                int full = NextLevelExp(s.CharClass, s.CharLevel);              // curva ORIGINAL: exp do proximo nivel (curva[L])
                if (full <= 0) break;                                           // teto da curva (sem proximo nivel)
                int floor = NextLevelExp(s.CharClass, (byte)(s.CharLevel - 1)); // exp do nivel atual (curva[L-1])
                // HOUSE-RULE (desvio consciente; o DB fica intacto): o cliente desenha a barra "cheia" a ~2/5 do
                // span do nivel (display nv4 = 496 ~ 386 + (658-386)*2/5 = 494). O meio-a-meio antigo (522)
                // deixava a barra visualmente cheia SEM upar. Subimos nesse ponto p/ "barra cheia = upa".
                int next = floor + (full - floor) * 2 / 5;
                if (s.CharExp < next) break;
                s.CharLevel++;
                s.CharLevelPoint++;
                ups++;
            }
            if (ups > 0)
            {
                _ = _db.UpdateCharacterLevelAsync(s.ActiveCharId, s.CharLevel, (byte)Math.Min(s.CharLevelPoint, 255));
                Log.Ok("level", "[{0}] char {1} LEVEL UP -> {2} (+{3} nivel(is), exp total {4})",
                    s.Slot, s.ActiveCharId, s.CharLevel, ups, s.CharExp);
            }
            return ups;
        }

        private System.Collections.Generic.Dictionary<int, Database.ItemDef> _itemDefs = new();
        /// <summary>Catalogo de itens (iteminfo) carregado no boot. Preco Gold/Cash por itemId.</summary>
        public Database.ItemDef? FindItemDef(int itemId) => _itemDefs.TryGetValue(itemId, out var d) ? d : null;

        /// <summary>
        /// O item pode ser DESENHADO no grid do armazem (box)? Só "gear" = tipos 0-5 (slots de equipamento,
        /// Class bitmask 1-16, ids 1xxx-5xxx). Tipos 6-14 (transform=8, lotto=11, especial=13, etc., todos
        /// Class 31) NÃO têm ícone de box no cliente GG-removido -> renderizam invisíveis e crasham o painel
        /// ao reconstruir (botão "Previous"). Esses ainda são comprados/persistidos no itembox, só não pintam
        /// no grid. Catálogo do cliente: o box-visual (FUN_004774e0) só trata especial o tipo 0x0c, e o gear
        /// tipo 0 foi confirmado em jogo; tipos 8/13 foram confirmados invisíveis+crash.
        /// </summary>
        public bool IsBoxDisplayable(int itemId)
        {
            var d = FindItemDef(itemId);
            if (d == null) return false;
            // Gear (0-5) + materiais/consumiveis/cash (6,7,9-14, ex: Mithril 13001 type 13) têm ícone de
            // box e pintam normalmente. O crash do painel no Previous que motivava o filtro antigo (só
            // type<=5) JÁ foi resolvido (acks 0x2c/0x2d fiéis ao original).
            // EXPERIMENTO captura-de-Cell (2026-07-01): type 8 (cell/creature) LIBERADO no grid p/ o jogador
            // arrastar a cell ao deck de creature e invocar no stage (captura do 0x307 real → HIT×N nativo).
            // RISCO ACEITO no teste: o cliente GG-removido pode pintar a cell SEM ícone (invisível) e o botão
            // "Previous" pode crashar o painel com type-8 no box. Reverter p/ `return d.Type != 8;` se instabilizar.
            return true;
        }

        /// <summary>Item é um SET (type 10) — um BUNDLE de peças, não uma peça equipável direta.</summary>
        public bool IsSet(int itemId) => FindItemDef(itemId)?.Type == 10;

        /// <summary>Composição de um SET (type 10): as colunas hit1-4/chit/ap do iteminfo guardam os itemIds
        /// dos membros (1 por slot de gear, faixa 0-5) — confirmado: 9012 -> 1009/1109/1209/1309/1409/1509.
        /// Fonte ÚNICA da composição. Só retorna membros que são itens válidos do catálogo (um valor que não
        /// resolve em item é stat, não membro -> filtrado). Vazio se não for set ou sem membros válidos.</summary>
        public System.Collections.Generic.IReadOnlyList<int> ExpandSetMembers(int setItemId)
        {
            var d = FindItemDef(setItemId);
            if (d == null || d.Type != 10) return System.Array.Empty<int>();
            var members = new System.Collections.Generic.List<int>(6);
            foreach (var m in new[] { d.Hit1, d.Hit2, d.Hit3, d.Hit4, d.CHit, d.Ap })
                if (m > 0 && FindItemDef(m) != null) members.Add(m);
            return members;
        }

        /// <summary>Carrega o catalogo de itens uma vez (iteminfo).</summary>
        public async Task LoadItemDefsCacheAsync()
        {
            var list = await _db.LoadItemDefsAsync();
            var map = new System.Collections.Generic.Dictionary<int, Database.ItemDef>(list.Count);
            foreach (var d in list) map[d.Id] = d;
            _itemDefs = map;
            Log.Ok("shop", "catalogo de itens carregado: {0} definicoes", map.Count);
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _broker?.Stop();
            try { _listener?.Close(); } catch { }
            _udpGame?.Stop();
        }


        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                Socket sock;
                try { sock = await _listener.AcceptAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log.Error("world", "accept: {0}", ex.Message); continue; }

                if (!TryAllocateSlot(out ushort slot))
                {
                    Log.Warn("world", "sem slots livres (max {0}) — recusando {1}", MaxUser,
                        (sock.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? "?");
                    try { sock.Close(); } catch { }
                    continue;
                }

                var session = new ClientSession(sock, slot, this);
                _sessions[slot] = session;
                session.Start();
            }
        }

        private bool TryAllocateSlot(out ushort slot)
        {
            for (int i = 0; i < MaxUser; i++)
            {
                ushort cand = (ushort)((_nextSlotHint + i) % MaxUser);
                if (!_sessions.ContainsKey(cand))
                {
                    _nextSlotHint = (cand + 1) % MaxUser;
                    slot = cand;
                    return true;
                }
            }
            slot = 0;
            return false;
        }

        /// <summary>Resolve o nome esperado da sessao (validada pelo broker). Vazio = nao validada.</summary>
        public string ResolveSessionName(string userId)
            => _validated.ContainsKey(userId) ? userId : "";

        /// <summary>
        /// Sucesso do login (FUN_0041f6c0): promove a sessao e envia o LoginComplete
        /// imediatamente (o handler original responde ali mesmo). A carga do jogo e o
        /// log de conexao no DB rodam em background para nao atrasar a resposta.
        /// </summary>
        public async Task OnLoginSuccessAsync(ClientSession s, string userId, string field2, string field3, ushort tail)
        {
            s.SlotActive = true;
            s.Authenticated = true;
            // field2 = USER/conta (login: connType, userID='D' artefato, field2=user, field3=senha). userId
            // parseado ('D') NAO e' a conta -> usar field2 ('test') p/ achar usergameinfo/cash/char no DB.
            s.UserId = field2.Length > 0 ? field2 : userId;
            s.Status = Domain.UserStatus.LoggedIn;
            s.CharName = field2;
            s.GroupId = Channels.Count > 0 ? Channels[0].Id : 0;   // canal default (origem real: locale/IDC do client)
            Interlocked.Increment(ref _currentUsers);

            // Carrega gold/cash/level/itens do DB ANTES do 0x0C: a síntese do 0x0C serializa gold/cash do
            // estado vivo (o display reflete a compra). Sincrono p/ garantir s.Gold/s.Cash setados no 0x0C.
            await LoadAndLogAsync(s, s.UserId);
            s.SendLoginResponse();   // 0x0C sintetizado (lista de chars) + 0x0D — 0x10 vai apos o handshake UDP
        }

        private async Task LoadAndLogAsync(ClientSession s, string userId)
        {
            var gi = await _db.LoadGameInfoAsync(userId);
            if (gi == null)
            {
                Log.Warn("login", "[{0}] '{1}' logado mas sem usergameinfo (DB indisponivel?)", s.Slot, userId);
                return;
            }
            s.Game = new WorldDatabaseInfo { UserId = gi.Id, Name = gi.Name, CharName = gi.CharName, Gold = gi.Gold };
            s.GameInfoId = gi.Id;                                       // usergameinfo.id (debito gold + useriteminfo.userid)
            s.Gold = (uint)(gi.Gold < 0 ? 0 : gi.Gold);
            int cash = await _db.GetCashAsync(userId);                  // cash keyed por account-name
            s.Cash = (uint)(cash < 0 ? 0 : cash);
            s.PowerLevelPoint = (uint)(gi.PowerLevelPoint < 0 ? 0 : gi.PowerLevelPoint); // PU Bonus Points -> 0x0C @48
            s.PuActive = gi.PuActive;                                   // powertimedate > now -> bônus de XP/gold
            s.ExpBonusActive = gi.PuActive;                            // flag original do bônus de XP (user+0x236c)
            if (gi.PuActive) Log.Info("shop", "[{0}] PU ATIVO -> bônus xp×{1} gold×{2}", s.Slot,
                PuConfig.EffectiveExpMult(DateTime.Now), PuConfig.EffectiveGoldMult(DateTime.Now));
            var ch = await _db.LoadActiveCharacterAsync(gi.Id);
            if (ch != null)
            {
                s.ActiveCharId = ch.Id;                                 // useriteminfo.characterid
                s.CharClass = ch.Class;                                 // classe -> curva de level (0x50)
                s.CharExp = ch.Exp < 0 ? 0 : ch.Exp;                    // exp acumulado (level-up server-side)
                s.CharLevel = ch.Level == 0 ? (byte)1 : ch.Level;       // overlay 0x0C @96 (nivel na tela)
                s.CharWin = (uint)(ch.Win < 0 ? 0 : ch.Win);            // overlay 0x0C @73
                s.CharLose = (uint)(ch.Lose < 0 ? 0 : ch.Lose);         // overlay 0x0C @77
                s.CharDraw = (uint)(ch.Draw < 0 ? 0 : ch.Draw);         // overlay 0x0C @81
                s.CharLevelPoint = (uint)(ch.LevelPoint);               // pontos de level -> overlay 0x0C @101
                SettleLevels(s);                                        // upa niveis pendentes JÁ no load (barra cheia do relog)
                // stats alocados (hit1..maxcp) -> Stats[0..9], p/ a alocacao 0x33 partir do valor real salvo
                s.Stats[0] = ch.Hit1; s.Stats[1] = ch.Hit2; s.Stats[2] = ch.Hit3; s.Stats[3] = ch.Hit4;
                s.Stats[4] = ch.Chit; s.Stats[5] = ch.Hp; s.Stats[6] = ch.Ap; s.Stats[7] = ch.AttackSpeed;
                s.Stats[8] = ch.Speed; s.Stats[9] = ch.Maxcp;
                // nome do PERSONAGEM (characterinfo.name) é autoritativo: o CharName provisório do login era o
                // login da CONTA ("test") — sem sobrescrever aqui, o roster 0x38 e o nome no stage mostravam a conta.
                if (!string.IsNullOrEmpty(ch.Name)) s.CharName = ch.Name;
                s.Items = await _db.LoadItemsAsync(ch.Id);              // inventario do char p/ o Box (0x2f)
                // armazem (itembox) -> exibido no box + slot da compra. FILTRA p/ só gear (type<=5): itens
                // não-gear (transform/especial/lotto) ficam no DB mas NÃO carregam no box -> sem célula
                // invisível e sem crash do painel no "Previous" (ver IsBoxDisplayable).
                var loadedBox = await _db.LoadItemBoxAsync(gi.Id);
                // SETS (type 10) são BUNDLES de peças de gear (iteminfo hit1-4/chit/ap = itemIds dos membros).
                // Desempacota no armazem (troca o set pelas peças) — o cliente não tem ação de "usar set", então
                // sem isto o set fica inerte no box. Idempotente: após desempacotar não resta set p/ desempacotar.
                var setsInBox = loadedBox.FindAll(t => IsSet(t.ItemId));
                if (setsInBox.Count > 0)
                {
                    var doneSets = new HashSet<int>();
                    foreach (var t in setsInBox)
                        if (doneSets.Add(t.ItemId)) await _db.UnpackSetInBoxAsync(gi.Id, t.ItemId, ExpandSetMembers(t.ItemId));
                    loadedBox = await _db.LoadItemBoxAsync(gi.Id);          // recarrega já desempacotado
                    Log.Ok("login", "[{0}] {1} set(s) type-10 desempacotado(s) no armazem", s.Slot, doneSets.Count);
                }
                var boxGear = loadedBox.FindAll(t => IsBoxDisplayable(t.ItemId));   // só gear entra no grid
                s.SetBoxItems(boxGear);   // consolida poções por id (1 célula + contador); gear 1 por célula, com nível de refino
                s.LoadPotionSlot(await _db.LoadQuickslotAsync(gi.Id));     // quickslot de pocao persistido (itembox.qslot)
                s.StageRanks = await _db.LoadStageRanksAsync(ch.Id);       // ranks de stage -> overlay 0x0C@333 (RANK X CLEAR na seleção)
                int boxHidden = loadedBox.Count - boxGear.Count;
                Log.Ok("login", "[{0}] char ativo='{1}' id={2} class={3} lvl={4} itens={5} box={6}{7}", s.Slot, ch.Name, ch.Id, ch.Class, ch.Level, s.Items.Count, boxGear.Count, boxHidden > 0 ? $" (+{boxHidden} não-gear ocultos)" : "");
            }
            else { Log.Warn("login", "[{0}] '{1}' sem char ativo (characterinfo.used=1 ausente)", s.Slot, userId); }
            s.LoginCharList = await BuildLoginCharListAsync(s);   // lista de chars do char-select (0x0C), sintetizada do DB
            await _db.LogUserConnectAsync(gi.Id, userId, _cfg.ServerId, s.RemoteIp);
            // Messenger (F9): grava a identidade do buddy (account+nick+IP). O login do Buddy é cifrado e não
            // carrega o nick -> o Buddy resolve a conexão TCP por IP (messenger_session) p/ saber quem é, carregar
            // a lista de amigos e anunciar presença. O nick é o nome do char ativo (id de rede do messenger).
            await _db.UpsertMessengerSessionAsync(s.UserId, ch?.Name?.Length > 0 ? ch.Name : userId, s.RemoteIp);
            Log.Ok("login", "[{0}] '{1}' logado (char='{2}', gold={3}, cash={4}) — {5}/{6} online",
                s.Slot, userId, gi.CharName, s.Gold, s.Cash, CurrentUsers, MaxUser);
        }

        /// <summary>Monta a lista de chars do char-select (0x0C) a partir do DB — síntese de raiz, sem replay.</summary>
        private async Task<CharList> BuildLoginCharListAsync(ClientSession s)
        {
            var chars = await _db.LoadCharactersAsync(s.GameInfoId);
            var quickslot = await _db.LoadQuickslotAsync(s.GameInfoId);   // account-level (itembox.qslot)
            var summaries = new List<CharSummary>(chars.Count);
            foreach (var ch in chars)
            {
                var ranks = await _db.LoadStageRanksAsync(ch.Id);
                summaries.Add(BuildCharSummary(ch, ranks, ch.Id == s.ActiveCharId ? quickslot : null));
            }
            return new CharList
            {
                AccountName = chars.Count > 0 ? chars[0].Name : s.CharName,   // @41 (truncado a 2 chars no writer)
                UserId = (uint)s.GameInfoId,
                Gold = s.Gold,
                Cash = s.Cash,
                PowerLevelPoint = (ushort)Math.Min(s.PowerLevelPoint, (uint)ushort.MaxValue),
                Chars = summaries,
            };
        }

        private static CharSummary BuildCharSummary(CharacterInfo ch, byte[] ranks,
            List<(int Cell, int ItemId, int Count)>? quickslot)
        {
            // Equip NÃO entra no char-select: o preview 3D veste o gear no modelo da classe e crasha em classes
            // sem o bone da arma ('Weapon01_ON_R'). TODO: reabilitar (só armadura, ou tratar o bone por classe).
            var qs = new ushort[6];
            if (quickslot != null)
                foreach (var (cell, itemId, _) in quickslot)
                    if (cell is >= 13 and <= 18) qs[cell - 13] = (ushort)itemId;
            return new CharSummary
            {
                Name = ch.Name, Slot = ch.Slot, Class = ch.Class,
                Level = ch.Level == 0 ? (byte)1 : ch.Level, Exp = (uint)Math.Max(0, ch.Exp), LevelPoint = ch.LevelPoint,
                Win = (uint)Math.Max(0, ch.Win), Lose = (uint)Math.Max(0, ch.Lose), Draw = (uint)Math.Max(0, ch.Draw),
                Stats = new ushort[] { ch.Hit1, ch.Hit2, ch.Hit3, ch.Hit4, ch.Chit, ch.Hp, ch.Ap, ch.AttackSpeed, ch.Speed, ch.Maxcp },
                Quickslot = qs, StageRanks = ranks ?? System.Array.Empty<byte>(),
            };
        }

        public async Task RemoveSessionAsync(ClientSession s)
        {
            if (_sessions.TryRemove(s.Slot, out _))
            {
                LeaveField(s);
                if (s.Authenticated)
                    Interlocked.Decrement(ref _currentUsers);
                // Messenger (F9): a identidade do buddy é por IP -> ao sair, apaga a messenger_session p/ uma
                // conexão futura do mesmo IP não ser resolvida como esta conta (e o amigo cair offline na presença).
                if (s.UserId.Length > 0) await _db.DeleteMessengerSessionAsync(s.UserId);
                Log.Info("world", "[{0}] sessao encerrada ('{1}') — {2}/{3} online",
                    s.Slot, s.UserId, CurrentUsers, MaxUser);
            }
        }

        // ---- broker -----------------------------------------------------------

        private BrokerLink.Stats GetStats()
            => new BrokerLink.Stats(MaxUser, CurrentUsers, _cfg.MaxField, 0);

        /// <summary>Validador chamado pelo broker (RequestLogin) — autentica no DB.</summary>
        private async Task<BrokerLink.LoginResult> ValidateLoginAsync(string userId, string password, ushort ipcId)
        {
            var acc = await _db.AuthenticateAsync(userId, password);
            if (acc == null)
                return new BrokerLink.LoginResult(1, ipcId, ""); // result != 0 = falha

            _validated[userId] = DateTime.UtcNow; // sessao validada (o TCP login vai casar o nome)
            Log.Ok("broker", "login validado para '{0}' (ipcId={1})", userId, ipcId);
            return new BrokerLink.LoginResult(0, ipcId, "");
        }
    }
}
