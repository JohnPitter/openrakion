using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public Domain.Field EnsureFieldForSession(ClientSession s)
        {
            Domain.Field f = GetField(s.FieldId)
                ?? CreateField(s.CharName.Length > 0 ? s.CharName : $"field{s.Slot}", mapId: 0, mode: 0, capacity: 8, master: s);
            f.State = 2; // field+8 = 2 (em jogo)
            int seat = f.AssignSeat(s);
            if (seat >= 0) { s.FieldSeat = (byte)seat; s.FieldObjectIndex = (ushort)seat; }
            if (f.MasterSlot < 0 || f.MasterSlot >= 0x14) f.MasterSlot = seat;
            return f;
        }

        /// <summary>Remove o usuario do field; se ficar vazio, libera o field.</summary>
        public void LeaveField(ClientSession s)
        {
            var f = GetField(s.FieldId);
            if (f == null) return;
            f.Remove(s);
            s.FieldId = -1;
            if (f.Count == 0)
            {
                lock (Fields) Fields.Remove(f);
                Log.Info("field", "field {0} '{1}' liberado (vazio)", f.Id, f.Name);
            }
        }

        public WorldServer(WorldConfig cfg, WorldDatabase db)
        {
            _cfg = cfg;
            _db = db;
            AntiCheat = new Security.AntiCheatService(cfg.AntiCheat, new Security.CompositeViolationSink(
                new Security.LogViolationSink(),
                new Security.DbViolationSink(cfg.Db.ConnectionString)));
        }

        /// <summary>Anti-cheat server-side (OpenGuard): integridade, anomalia de protocolo e flood.</summary>
        public Security.AntiCheatService AntiCheat { get; }

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

        /// <summary>Messenger "add buddy": persiste a amizade (buddylist) a partir do 0x19. O AddBuddy do cliente
        /// e' MUDO (mascara +0x140d4 bit12=0 -> o Buddy2.dll nao emite SVC_ADD_BUDDY), entao o WORLD — que conhece
        /// os dois lados no 0x19 (dono logado + alvo resolvido) — grava a amizade; o buddy server carrega a lista
        /// no login. Regra de dominio (valida nao-self) fora do handler de rede. Account-names (usergameinfo.name).</summary>
        public async Task<bool> AddBuddyAsync(ClientSession owner, string buddyAccount, string buddyNick)
        {
            if (string.IsNullOrEmpty(owner.UserId) || string.IsNullOrEmpty(buddyAccount)) return false;
            if (string.Equals(owner.UserId, buddyAccount, StringComparison.OrdinalIgnoreCase)) return false; // self-add degenerado
            bool added = await _db.AddBuddyAsync(owner.UserId, buddyAccount);
            // RECÍPROCO: o chat P2P/presença é bidirecional (cada lado casa o outro por nick). Sem fluxo de
            // aceitação de convite no servidor offline (uso pessoal de poucos chars), gravamos os dois sentidos.
            await _db.AddBuddyAsync(buddyAccount, owner.UserId);
            Log.Ok("buddy", "[{0}] '{1}' <-> '{2}' (conta {3}) -> {4}", owner.Slot, owner.UserId, buddyNick, buddyAccount,
                added ? "amizade gravada (recíproca)" : "ja existia");
            return added;
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
            _ = _db.UpsertMessengerSessionAsync(s.UserId, s.RemoteIp);   // identidade p/ o buddy (login cifrado -> resolve por IP)
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
                if (s.CharName.Length == 0) s.CharName = ch.Name;
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
                AntiCheat.ForgetSession(s.Slot);
                LeaveField(s);
                if (s.Authenticated)
                    Interlocked.Decrement(ref _currentUsers);
                _ = _db.RemoveMessengerSessionAsync(s.UserId);   // libera a identidade do buddy
                Log.Info("world", "[{0}] sessao encerrada ('{1}') — {2}/{3} online",
                    s.Slot, s.UserId, CurrentUsers, MaxUser);
            }
            await Task.CompletedTask;
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
