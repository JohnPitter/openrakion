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

        public WorldServer(WorldConfig cfg, WorldDatabase db)
        {
            _cfg = cfg;
            _db = db;
            Bots = new BotManager(() => _udpGame?.Port ?? 40708);
            Items = new ItemCatalog(db);
            Progression = new ProgressionService(db);
            Enchant = new EnchantService(db, () => EnchantConfig);   // config recarregável — lê a vigente
            Buddy = new BuddyService(db);
        }

        /// <summary>Catálogo de itens (iteminfo) — serviço extraído; carregado no boot, consultado por shop/login.</summary>
        public ItemCatalog Items { get; }

        /// <summary>Progressão (exp/level/stage-result) — serviço extraído; opera sobre a sessão + DB.</summary>
        public ProgressionService Progression { get; }

        /// <summary>Refino/enchant (0x74) — serviço extraído; opera sobre o box da sessão + DB + config recarregável.</summary>
        public EnchantService Enchant { get; }

        /// <summary>Buddy/messenger (add buddy 0x19) — serviço extraído; persistência de amizade recíproca.</summary>
        public BuddyService Buddy { get; }

        /// <summary>Subsistema de BOTS (IA, roster, peer, ciclo de vida) — extraído do WorldServer p/ isolar o
        /// acoplamento; depende só da porta do gameplay UDP. Entradas: BotTick/SpawnFieldBotsInStage/DiscardBots
        /// (motor de partida) e AddBotToField/RemoveBotsFromField (chat /addbot).</summary>
        public BotManager Bots { get; }

        public bool Locked { get; private set; }                 // this+0x50 (servidor fechado p/ GM)
        public PuConfig PuConfig { get; private set; } = new();   // pu_config: preço/bônus/multiplicadores do PU (lida no boot)
        public EnchantConfig EnchantConfig { get; private set; } = new();   // enchant_*: coeficientes do refino (lida no boot)
        public int MaxUser => _cfg.MaxUser;                       // this+0x536c
        public int CurrentUsers => Volatile.Read(ref _currentUsers);

        /// <summary>Sessoes ativas (iteracao thread-safe sobre o dicionario).</summary>
        public IEnumerable<ClientSession> Sessions => _sessions.Values;

        /// <summary>Entrada da user list de uma sessão (DTO de borda). ChanSlot = low-byte do Slot (estável;
        /// é a chave do remove 0x20).</summary>
        private static Network.LobbyFrames.UserListEntry UserEntryOf(ClientSession s) =>
            new((byte)(s.Slot & 0xff), (ushort)(s.GameInfoId > 0 ? s.GameInfoId : s.Slot), s.CharName, s.CharClass);

        /// <summary>Snapshot dos USUÁRIOS ONLINE (conectados com char carregado) p/ a user list do canal (0x1e).
        /// DEDUP por nome de char (fica a sessão mais recente = maior Slot): uma reconexão deixa a sessão velha
        /// ainda "Connected" até o TCP notar, e o char aparecia DUPLICADO na lista.</summary>
        public IReadOnlyList<Network.LobbyFrames.UserListEntry> SnapshotChannelUsers()
        {
            var byName = new Dictionary<string, (ushort Slot, Network.LobbyFrames.UserListEntry Entry)>();
            foreach (var s in _sessions.Values)
            {
                if (!s.Connected || string.IsNullOrEmpty(s.CharName)) continue;
                if (!byName.TryGetValue(s.CharName, out var cur) || s.Slot > cur.Slot)
                    byName[s.CharName] = (s.Slot, UserEntryOf(s));
            }
            var list = new List<Network.LobbyFrames.UserListEntry>(byName.Count);
            foreach (var v in byName.Values) list.Add(v.Entry);
            return list;
        }

        /// <summary>User list INCREMENTAL (o widget do cliente ACUMULA 0x1e): avisa os DEMAIS no channel lobby
        /// que <paramref name="joiner"/> entrou — um 0x1e só com o novato. A lista cheia vai só a quem entra
        /// (senão cada rebroadcast duplicava todo mundo na tela de quem já estava).</summary>
        public void AnnounceChannelUserJoined(ClientSession joiner)
        {
            byte[] frame = Network.LobbyFrames.ChannelList(new[] { UserEntryOf(joiner) });
            foreach (var s in _sessions.Values)
                if (s != joiner && s.Connected && s.Status == Domain.UserStatus.FieldLobby && !string.IsNullOrEmpty(s.CharName))
                    try { s.SendEncryptedFrame(frame); } catch { }
        }

        /// <summary>Broadcast do chat do canal/game-list (0x22): reecoa o texto a TODOS no channel lobby (mesmos
        /// que a user list 0x1e mostra), INCLUINDO o remetente — o cliente não desenha o próprio 0x22 local, só o
        /// eco do servidor (por isso "não aparecia nada"). O nome do remetente já vem embutido no texto pelo
        /// cliente; o chanSlot = low-byte do Slot (índice do remetente no canal, como o 0x1e/0x20).</summary>
        public void BroadcastChannelChat(ClientSession sender, string text)
        {
            byte[] frame = Network.LobbyFrames.ChannelChat((byte)(sender.Slot & 0xff), text);
            foreach (var s in _sessions.Values)
                if (s.Connected && s.Status == Domain.UserStatus.FieldLobby && !string.IsNullOrEmpty(s.CharName))
                    try { s.SendEncryptedFrame(frame); } catch { }
        }

        /// <summary>Par do append: 0x20 [slotIdx] remove o membro da user list dos que ficam (deslogou).</summary>
        public void AnnounceChannelUserLeft(ClientSession leaver)
        {
            byte[] frame = Network.LobbyFrames.ChannelUserRemove((byte)(leaver.Slot & 0xff));
            foreach (var s in _sessions.Values)
                if (s != leaver && s.Connected && s.Status == Domain.UserStatus.FieldLobby && !string.IsNullOrEmpty(s.CharName))
                    try { s.SendEncryptedFrame(frame); } catch { }
        }

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

        /// <summary>Sessao pelo UserId publicado na user list do canal (0x1e): GameInfoId se &gt;0, senao Slot
        /// (mesma regra do <see cref="UserEntryOf"/>). O invite 0x72 endereca o alvo por esse id.</summary>
        public ClientSession? GetSessionByUserId(ushort userId)
        {
            foreach (var s in _sessions.Values)
            {
                if (!s.Connected || string.IsNullOrEmpty(s.CharName)) continue;
                ushort uid = (ushort)(s.GameInfoId > 0 ? s.GameInfoId : s.Slot);
                if (uid == userId) return s;
            }
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
                        f.EndRound(f.DecideRoundWinnerByScore(), cause: 2);   // 2 = placar/tempo (+0x2bd)
                        // FIELD 0x4a aos playing: body=[causa/2bd][winner-wire/2bf][2c0][2c1]
                        f.BroadcastFieldPlaying(0x4a, f.Build0x4a());
                    }
                    else
                    {
                        // IA dos bots (spawn 0x45 + decisão/movimento/ataque sintetizados) durante o round.
                        Bots.BotTick(f);
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
            Bots.DiscardBots(f);
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
            await Items.LoadAsync();   // catalogo de itens (iteminfo) p/ a compra 0x2e
            await Progression.LoadCurveAsync();            // curva de exp por classe (level-up 0x50)
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
