using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;
using RakionServer.World.CharSelect;

namespace RakionServer.World
{
    /// <summary>
    /// Ciclo de vida da SESSÃO (partial de <see cref="WorldServer"/>): login bem-sucedido, hidratação do
    /// estado do char/itens/box a partir do DB, montagem da char-list do 0x0C e remoção da sessão. Concern
    /// separado do bootstrap/rede e do registro de fields.
    /// </summary>
    public sealed partial class WorldServer
    {
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
                Progression.SettleLevels(s);                                        // upa niveis pendentes JÁ no load (barra cheia do relog)
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
                var setsInBox = loadedBox.FindAll(t => Items.IsSet(t.ItemId));
                if (setsInBox.Count > 0)
                {
                    var doneSets = new HashSet<int>();
                    foreach (var t in setsInBox)
                        if (doneSets.Add(t.ItemId)) await _db.UnpackSetInBoxAsync(gi.Id, t.ItemId, Items.ExpandSetMembers(t.ItemId));
                    loadedBox = await _db.LoadItemBoxAsync(gi.Id);          // recarrega já desempacotado
                    Log.Ok("login", "[{0}] {1} set(s) type-10 desempacotado(s) no armazem", s.Slot, doneSets.Count);
                }
                var boxGear = loadedBox.FindAll(t => Items.IsBoxDisplayable(t.ItemId));   // só gear entra no grid
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
                AnnounceChannelUserLeft(s);   // 0x20 [slotIdx]: os que ficam removem este da user list
                Log.Info("world", "[{0}] sessao encerrada ('{1}') — {2}/{3} online",
                    s.Slot, s.UserId, CurrentUsers, MaxUser);
            }
        }
    }
}
