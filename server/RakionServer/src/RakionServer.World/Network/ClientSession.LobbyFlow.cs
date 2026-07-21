using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.CharSelect;
using RakionServer.World.Database;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        // --- ENTRADA NO LOBBY/CAMPO (síntese do protocolo de canal/sala/stage) ------------------
        // Após o char-select, o cliente faz o handshake UDP (echo 0x0201) e manda esta cadeia TCP; o world
        // responde levando-o à LISTA DE CANAIS (0x1e, owner 100 + "channel01") e à sala/stage. Cada frame é MONTADO do
        // estado por <see cref="LobbyFrames"/> (síntese pura, sem blob de captura): o userid/nome vêm do
        // domínio e casam com o 0x0C (senão o cliente trava re-mandando 0x14), o nome da sala vem do 0x3b,
        // o tempo do stage da duração da sala. Os tokens/handles autorais do servidor são constantes
        // nomeadas em LobbyFrames. A captura original vive só no golden test (LobbyFrameGoldenTests).
        internal bool TryHandleLobbyEntry(ushort opcode, byte[] data)
        {
            if (IsLateBattlePacket(opcode))
            {
                Log.Debug("combat", "[{0}] pacote tardio 0x{1:X2} consumido após sair da partida",
                    Slot, opcode);
                return true;
            }
            RequestGateResult gate = WorldRequestGatePolicy.Evaluate(
                opcode, GameInfoId, ActiveCharId, Status);
            if (!gate.Allowed)
            {
                Log.Warn("protocol", "[{0}] gate 0x{1:X2} rejeitado: ação={2} código={3} status={4}",
                    Slot, opcode, gate.Action, gate.Code, Status);
                if (gate.Action == RequestGateAction.ReplyStatus)
                    SendEncryptedFrame(WorldHandlers.Prefix(opcode, new[] { checked((byte)gate.Code) }));
                else
                    Disconnect(gate.Code);
                return true;
            }

            switch (opcode)
            {
                case 0x28 when Status == UserStatus.FieldLobby:
                    _ = HandleEnchantCommitAsync(data);
                    return true;
                case 0x3c when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomChangeMaster();
                    return true;
                case 0x3d when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomReady(data);
                    return true;
                case 0x3e when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomChangeTeam();
                    return true;
                case 0x3f when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomClose();
                    return true;
                case 0x40 when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomKick(data);
                    return true;
                case 0x41 when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomRule(data);
                    return true;
                case 0x42 when Status == Domain.UserStatus.FieldLobby:
                    HandleRoomSlotStatus(data);
                    return true;
                case 0x43 when Status == Domain.UserStatus.FieldLobby:
                    HandleMatchStart();
                    return true;
                case 0x4A: // 0x4A com data[0]=0x02 = StageClear. Combate usa outras 0x4A -> nao intercepta.
                    if (data.Length >= 1 && data[0] == 0x02)
                    {
                        var stageField = _server.GetField(FieldId);
                        if (stageField == null || stageField.Mode != 0) return false;
                        IReadOnlyList<ClientSession>? participants = _server.MarkStageClear(this);
                        if (participants == null)
                        {
                            Log.Warn("stage", "[{0}] clear rejeitado: execução de stage ausente ou divergente", Slot);
                            return true;
                        }
                        // Manda SO a tela de Rank agora; o 0x44 (match-end) + refresh do lobby vem com um
                        // pequeno DELAY p/ o overlay de Rank ter tempo de aparecer. (Mandar tudo junto, ou
                        // disparar no 0x53, fazia o Rank sumir.) O 0x53 GameResultReport que o cliente manda
                        // logo apos e' CONSUMIDO (case 0x53) p/ nao cair no handler que desconecta em solo.
                        foreach (ClientSession participant in participants)
                        {
                            participant.SendEncryptedFrame(LobbyFrames.StageEndResult(2));
                            _ = participant.ScheduleLobbyReturnAfterRankAsync();
                        }
                        Log.Ok("lobby", "[{0}] 0x4A StageClear -> Rank para {1} player(s)",
                            Slot, participants.Count);
                        return true;
                    }
                    return false;
                case 0x4b when System.Threading.Volatile.Read(ref _gameClockStarted) == 0:
                    // Primeiro 0x4B da entrada no stage (72B). Os 0x4B seguintes são relay de
                    // gameplay e precisam alcançar Op_0x4B_Recon.
                    // GameSeq e manda o tick 1583 (o cliente ecoa o seq; seq avancando = timer corre).
                    InField = true;
                    Status = UserStatus.InField; // reafirma a promoção feita no 0x43 para fluxos legados sem room-start
                    ResetFieldPotionUsage();
                    StartGameClock();
                    Log.Ok("lobby", "[{0}] 0x4b (spawn) -> STAGE; relógio iniciado e payload segue para o relay canônico (udp={1})", Slot, UdpEndpoint?.ToString() ?? "-");
                    return false;
                // (0x4b acima inicia o relogio)
                case 0x46: // o cliente manda 0x46 ao SAIR/dar giveup no stage. Sem o eco FIELD 0x46 [seat]
                    // (+ 0x44 fim) ele trava "waiting for world" — MESMO motivo documentado no Op_0x46_Recon
                    // (PvP). Em SOLO o opcode caía no catch-all e era ENGOLIDO -> o "sair do stage" não voltava.
                    {
                        var fld = _server.GetField(FieldId);
                        if (fld == null || fld.Mode != 0) return false;   // PvP/sem field -> dispatch real (Op_0x46_Recon)
                        lock (fld.SyncRoot)
                        {
                            fld.BroadcastField(0x46, new[] { FieldSeat });
                            FinishStageRun();
                            fld.OnPlayerExit(FieldSeat, cause: 0);
                            if (fld.CountAlive(0) + fld.CountAlive(1) == 0)
                            {
                                fld.EndMatch(0);
                                fld.BroadcastLobby(LobbyFrames.MatchEnd(
                                    2, fld.Name.Length > 0 ? fld.Name : "asdd"));
                            }
                        }
                        Status = UserStatus.FieldLobby;
                        Log.Ok("lobby", "[{0}] 0x46 saída do stage -> game room (status=2)", Slot);
                        return true;
                    }
                case 0x4F: // FUN_004087D0 marca a vítima e publica 0x4F. FUN_004063A0 encerra
                    // com 0x4A tipo 1 somente quando todos os slots 0..9 morreram.
                    {
                        var fld = _server.GetField(FieldId);
                        if (fld == null || fld.Mode != 0) return false;   // PvP/sem field -> dispatch real (Op_0x4F_Recon)
                        byte cause = data.Length > 0 ? data[0] : (byte)0;
                        byte killer = data.Length > 1 ? data[1] : (byte)0;
                        IReadOnlyList<ClientSession>? endedParticipants = null;
                        lock (fld.SyncRoot)
                        {
                            DeathReportResult death = fld.ApplyReportedDeath(FieldSeat, killer, cause);
                            if (!death.Processed) return true;
                            fld.BroadcastFieldPlaying(0x4f,
                                new byte[] { FieldSeat, cause, killer, death.ScoreA, death.ScoreB });
                            if (fld.Phase == MatchPhase.RoundEnd)
                            {
                                var participants = new List<ClientSession>();
                                foreach (PlayerRec record in fld.Slots)
                                    if (record.Playing && record.Session != null)
                                        participants.Add(record.Session);
                                endedParticipants = participants;
                            }
                        }
                        if (endedParticipants != null)
                        {
                            foreach (ClientSession participant in endedParticipants)
                            {
                                participant.FinishStageRun();
                                participant.SendEncryptedFrame(LobbyFrames.StageEndResult(1));
                                _ = participant.ScheduleLobbyReturnAfterRankAsync();
                            }
                        }
                        Log.Ok("lobby", "[{0}] 0x4f morte no stage; party encerrada={1}",
                            Slot, endedParticipants != null);
                        return true;
                    }
                default:
                    // Rotas não interceptadas abaixo seguem para os handlers reconstruídos. O 0x39
                    // (FieldListReq = Quick Start / lista de salas, FUN_00423300) e 0x74 (RoomMoveAction =
                    // UPGRADE do refino, FUN_00421e10 -> reply SendMessage 0x28): sem o reply o cliente trava
                    // ("esperando a resposta do world" / "Upgrading Now").
                    if (opcode == 0x2d || opcode == 0x33 || opcode == 0x39 ||
                        opcode == 0x64 || opcode == 0x65 || opcode == 0x66 ||
                        opcode == 0x69 || opcode == 0x74)
                        return false;
                    if (opcode is 0x1e or 0x20 or 0x22)
                        return false;
                    // Durante a partida, estes opcodes pertencem aos handlers reconstruídos do motor
                    // de campo. O lobby só os intercepta enquanto a sala ainda está em FieldLobby.
                    if (Status == Domain.UserStatus.InField &&
                        opcode is 0x29 or 0x2a or 0x3e or 0x3f or 0x40 or 0x41 or 0x42 or 0x43 or 0x45 or 0x4b or 0x4c
                              or 0x47 or 0x56 or 0x57 or 0x59 or 0x5a or 0x5b or 0x5d or 0x5e
                              or 0x60 or 0x72)
                        return false;
                    // Salas BATTLE/PvP (Mode != 0): os opcodes de COMBATE vao aos handlers reais do
                    // motor de partida — 0x4d (par golem/facing: y==0 = golem inimigo destruido ->
                    // fim de round), 0x3d (troca de arma), 0x50 (reporte de exp/gold do fim de partida ->
                    // level-up). No SOLO (Mode 0) continuam engolidos: o combate e' client-side e o 0x4d
                    // (y==0) encerraria o stage. (0x46 saída e 0x4f morte têm case próprio acima — solo tb.)
                    if (opcode == 0x4d || opcode == 0x3d || opcode == 0x50)
                    {
                        var bf = _server.GetField(FieldId);
                        if (bf != null && bf.Mode != 0) return false; // dispatch -> Op_0xNN_Recon
                    }
                    // Todos os cases restantes seguem para a jump table canônica. O antigo catch-all
                    // em InField também engolia handlers já reconstruídos (0x16..0x18, 0x61/0x62,
                    // 0x75/0x76 e 0x77..0x79), tornando-os inalcançáveis após o character-select.
                    return false;
            }
        }

        internal void BeginSuccessUdp(byte[] data)
        {
            if (!SuccessUdpRequest.TryParse(data, out SuccessUdpRequest request))
            {
                Log.Warn("udp", "[{0}] 0x0E payload inválido: {1} bytes", Slot, data.Length);
                Disconnect(0x16);
                return;
            }

            var observed = UdpObservedEndpoint ?? NetworkEndpointCodec.Unspecified;
            var advertised = UdpAdvertisedEndpoint ?? observed;
            SendEncryptedFrame(LobbyFrames.Endpoints(observed, advertised));
            SendGameGuardChallengeOnce();
            Log.Ok("udp", "[{0}] SuccessUDP resultado={1}; endpoints {2} / {3}",
                Slot, request.Result, observed, advertised);
        }

        internal void BeginCharacterSelect(byte[] data) => _ = HandleCharacterSelectAsync(data);

        private async Task HandleCharacterSelectAsync(byte[] data)
        {
            if (!CharacterSelectRequest.TryParse(data, out CharacterSelectRequest request))
            {
                Log.Warn("character", "[{0}] 0x14 tamanho inválido: {1}", Slot, data.Length);
                Disconnect(0x1e);
                return;
            }

            if (request.CharacterId == 0)
            {
                Disconnect(0x1e);
                return;
            }

            Database.CharacterSelectStatus status = await _server.SelectCharacterAsync(
                this, request.CharacterId);
            if (status != Database.CharacterSelectStatus.Success)
            {
                SendEncryptedFrame(LobbyFrames.CharacterSelectAck(status));
                Log.Warn("character", "[{0}] 0x14 char id={1} falhou: {2}",
                    Slot, request.CharacterId, status);
                return;
            }

            SendEncryptedFrame(LobbyFrames.CharacterSelectAck());
            InField = true; FieldSecondary = true; SecondActive = true; Status = Domain.UserStatus.FieldLobby;
            if (!_server.EnterChannelLobby(this))
            {
                Disconnect(7);
                return;
            }
            if (!_potionLoginPainted) { _potionLoginPainted = true; PaintQuickslot(); }
            Log.Ok("lobby", "[{0}] 0x14 char={1} + 0x1f + 0x1e; channel-lobby ativo",
                Slot, request.CharacterId);
        }

        internal void BeginCharacterCreate(byte[] data) => _ = HandleCharacterCreateAsync(data);

        private async Task HandleCharacterCreateAsync(byte[] data)
        {
            if (!CharacterCreateRequest.TryParse(data, out var request))
            {
                Disconnect(0x1a);
                return;
            }

            if (request.Class >= 5) { Disconnect(0x1b); return; }
            if (request.Slot >= 6) { Disconnect(0xea); return; }

            var result = await _server.CreateCharacterAsync(
                this, request.Name, request.Class, request.Slot);
            SendEncryptedFrame(LobbyFrames.CharacterCreateAck(
                result.Status, result.Character?.Id ?? 0));
            if (result.Status != Database.CharacterCreateStatus.Success)
                Log.Warn("character", "[{0}] create nome='{1}' class={2} slot={3} rejeitado: {4}",
                    Slot, request.Name, request.Class, request.Slot, result.Status);
        }

        internal void BeginCharacterStateClear(byte[] data) =>
            _ = HandleCharacterStateClearAsync(data);

        private async Task HandleCharacterStateClearAsync(byte[] data)
        {
            if (!CharacterStateClearRequest.TryParse(data, out var request))
            {
                Disconnect(0x2b);
                return;
            }

            var result = await _server.ClearCharacterStateAsync(
                this, request.PaymentType, request.PaymentValue);
            SendEncryptedFrame(LobbyFrames.CharacterStateClearAck(result));
            if (result.Status != Database.CharacterStateClearStatus.Success)
                Log.Warn("character", "[{0}] reset char={1} rejeitado: {2}", Slot, ActiveCharId, result.Status);
        }

        internal void BeginCharacterRename(byte[] data) => _ = HandleCharacterRenameAsync(data);

        private async Task HandleCharacterRenameAsync(byte[] data)
        {
            if (!CharacterRenameRequest.TryParse(data, out var request))
            {
                Disconnect(0x2c);
                return;
            }

            var result = await _server.RenameCharacterAsync(
                this, request.Name, request.PaymentType, request.PaymentValue);
            SendEncryptedFrame(LobbyFrames.CharacterRenameAck(result));
            if (result.Status != Database.CharacterRenameStatus.Success)
                Log.Warn("character", "[{0}] rename char={1} para '{2}' rejeitado: {3}",
                    Slot, ActiveCharId, request.Name, result.Status);
        }

        internal void BeginCharacterDelete(byte[] data) => _ = HandleCharacterDeleteAsync(data);

        private async Task HandleCharacterDeleteAsync(byte[] data)
        {
            if (!CharacterDeleteRequest.TryParse(data, out var request))
            {
                Disconnect(0x1c);
                return;
            }

            var result = await _server.DeleteCharacterAsync(
                this, request.CharacterId, request.DeleteKey);
            SendEncryptedFrame(LobbyFrames.CharacterDeleteAck(
                (byte)result, LoginCharList?.Clan));
            if (result != Database.CharacterDeleteResult.Success)
                Log.Warn("character", "[{0}] delete char={1} rejeitado: {2}",
                    Slot, request.CharacterId, result);
        }

        internal void BeginBuddyNameChange(byte[] data) => _ = HandleBuddyNameChangeAsync(data);

        private async Task HandleBuddyNameChangeAsync(byte[] data)
        {
            if (!BuddyNameChangeRequest.TryParse(data, out var request) ||
                !Domain.LegacyIdentity.IsValidBuddyName(request.Name))
            {
                Disconnect(0x20);
                return;
            }

            var result = await _server.ChangeBuddyNameAsync(this, request.Name);
            byte status = result == Database.BuddyNameChangeResult.Success ? (byte)0 : (byte)1;
            SendEncryptedFrame(LobbyFrames.BuddyNameAck(status, request.Name));
            if (status != 0)
                Log.Warn("character", "[{0}] buddyname '{1}' rejeitado: {2}",
                    Slot, request.Name, result);
        }

        internal void BeginCharacterTutorialClear(byte[] data)
        {
            if (!CharacterTutorialClearRequest.TryParse(data))
            {
                Disconnect(0x2a);
                return;
            }
            _ = _server.MarkTutorialClearAsync(this);
        }

        private bool IsLateBattlePacket(ushort opcode)
        {
            return Status == UserStatus.FieldLobby && PendingRoomMode != 0 &&
                opcode is 0x46 or 0x4b or 0x4f or 0x50;
        }

        internal void BeginCharacterIdentityLookup(byte[] data) =>
            _ = HandleCharacterIdentityLookupAsync(data);

        private async Task HandleCharacterIdentityLookupAsync(byte[] data)
        {
            if (!CharacterGetUserNameRequest.TryParse(data, out var request) ||
                !request.IsWithinOriginalLimit)
            {
                Disconnect(0x29);
                return;
            }

            Database.CharacterIdentityLookupResult result =
                await _server.FindCharacterIdentityAsync(request.Value);
            SendEncryptedFrame(LobbyFrames.CharacterIdentityLookup(
                result.Status, result.Identity?.AccountId ?? "", result.Identity?.BuddyName ?? ""));
            Log.Info("character", "[{0}] identidade de '{1}': status={2} account='{3}'",
                Slot, request.Value, result.Status, result.Identity?.AccountId ?? "");
        }

        /// <summary>LoginComplete (msgType 2) — sucesso de FUN_0041f6c0.</summary>
        public void SendLoginComplete(string field2, string field3, ushort tail)
        {
            using var p = new PacketWriter();
            p.WriteCString(field2);
            p.WriteCString(field3);
            p.WriteWord(tail);
            p.WriteInt32(0);
            p.WriteByte(1);
            SendMessage(Protocol.SubType.LoginComplete, p.ToArray());
            Log.Ok("login", "[{0}] LoginComplete enviado (field2='{1}')", Slot, field2);
        }

        // ---- Resposta de login: 0x0C (lista de chars do char-select) + 0x0D (tabela zerada) ----
        /// <summary>
        /// Envia a resposta de login SINTETIZADA de raiz: 0x0C (char-select, serializado de
        /// <see cref="LoginCharList"/> pelo <see cref="LoginCharListWriter"/>) + 0x0D (tabela zerada, 1332B).
        /// Sem replay de captura. O 0x10 (GameGuard) sai após estes frames e não depende do handshake UDP.
        /// </summary>
        public void SendLoginResponse()
        {
            if (UdpKey == 0)
                UdpKey = (uint)RandomNumberGenerator.GetInt32(1, 0x8000);
            var list = (LoginCharList ?? new CharList()) with
            {
                ServerTimeMarker = ServerTimeMarker,
                UdpSessionKey = UdpKey,
            };
            byte[] f0c = LoginCharListWriter.Build(list);
            SendEncryptedFrame(f0c);
            byte[] f0d = new byte[1332]; f0d[0] = 0x0d; f0d[3] = 0x01;   // 0x0D = tabela zerada (sintetizada)
            SendEncryptedFrame(f0d);
            SendGameGuardChallengeOnce();
            Log.Ok("login", "[{0}] login sintetizado: 0x0C {1}B ({2} chars) + 0x0D {3}B + 0x10",
                Slot, f0c.Length, list.Chars.Count, f0d.Length);
        }

        private void SendGameGuardChallengeOnce()
        {
            if (System.Threading.Interlocked.Exchange(ref _gameGuardChallengeSent, 1) != 0) return;
            SendEncryptedFrame(LobbyFrames.GameGuard());
        }

        /// <summary>
        /// Spawn no stage (0x4b da cadeia de entrada): garante que a sessao tem um Field real com
        /// seat alocado, marca o player como ready e delega ao MOTOR DA PARTIDA por-field
        /// (WorldServer.FieldEngine / FUN_00409940). O 1583 idle e o broadcast 0x48 sao do motor
        /// global, NAO mais por-sessao (substitui o StartGameClock parcial).
        /// </summary>
        private void StartGameClock()
        {
            if (System.Threading.Interlocked.Exchange(ref _gameClockStarted, 1) != 0) return;
            SnapshotPowerUserBenefits();
            GameSeq = 5;
            var f = _server.EnsureFieldForSession(this);
            lock (f.SyncRoot)
            {
                if (f.Settled) f.ResetMatch();
                if (PendingRoomMode != 0) { f.Mode = PendingRoomMode; f.MapId = PendingRoomMap; }
                if (PendingRoomDurationSec != 0) f.RoundDurationSec = PendingRoomDurationSec;
                if (PendingRoomRounds != 0) f.MaxRounds = PendingRoomRounds;
                if (f.Mode == 0 && !_server.BeginStageRun(this, f)) return;
                if (PlayerSpawnMatchId != f.MatchId &&
                    WorldHandlers.Combat_0x45_SpawnInto(
                        f, FieldSeat, _server.Config.ForceTunneling))
                {
                    PlayerSpawnMatchId = f.MatchId;
                    int peerSpawns = f.ReplayPlayerSpawnsTo(this);
                    _server.Bots.SendMatchSpawnsTo(this, f);
                    Log.Info("field", "[{0}] entrada no stage sincronizou {1} spawn(s) de peer",
                        Slot, peerSpawns);
                }
            }
            Log.Ok("field", "[{0}] sala aplicada ao field {1}: mode={2} map={3} dur={4}s rounds={5}",
                Slot, f.Id, f.Mode, f.MapId, f.RoundDurationSec, f.MaxRounds);
            // Sala Battle/PvP (mode != 0) = fluxo NETWORKED: o SERVER inicia o loop UDP com o 1o
            // tick 1583 (mitm_full_113423: o original manda 1583 ANTES do 1o 0040 do cliente; sem
            // ele o input fica congelado). Stage solo (mode 0) fica client-side: sem tick.
            if (f.Mode != 0 && UdpEndpoint != null) _server.SendGameplayTick(UdpEndpoint, GameSeq);
            Log.Ok("field", "[{0}] spawn -> motor da partida (field {1}, seat {2}, mode {3})", Slot, f.Id, FieldSeat, f.Mode);
        }

        /// <summary>
        /// Pos-StageClear: depois de mostrar a tela de Rank (0x4A), espera um pouco p/ o overlay aparecer
        /// e entao manda 0x44 (match-end) + refresh do lobby POS-CLEAR (0x1f/0x1e/0x36) -> volta a selecao.
        /// O delay e' o que faz o Rank ser VISIVEL (mandar tudo junto fazia o cliente pular direto pro lobby).
        /// </summary>
        private async Task ScheduleLobbyReturnAfterRankAsync()
        {
            try { await Task.Delay(12000, _cts.Token); }
            catch (OperationCanceledException) { return; }
            catch { }
            if (!Connected) return;
            // SO o 0x44 (match-end): devolve o cliente ao GAME ROOM onde ele estava (p/ rejogar).
            // NAO mandar 0x1f/0x1e/0x36 aqui: o 0x1e e' a LISTA DE CANAIS (owner 100 + "channel01") e levava o
            // cliente pra LISTA DE GAMES em vez do room (o original volta pro room apos o stage).
            // Volta ao room lobby (Status=2, InField/FSec) -> shop continua disponivel no room pos-stage.
            InField = true; FieldSecondary = true; SecondActive = true; Status = 2;
            SendEncryptedFrame(LobbyFrames.MatchEnd(2, PendingRoomName.Length > 0 ? PendingRoomName : "asdd"));  // reason=2; nome da sala do domínio
            Log.Ok("lobby", "[{0}] pos-Rank (delay) -> 0x44 match-end (volta ao game room, sala='{1}')", Slot,
                PendingRoomName.Length > 0 ? PendingRoomName : "asdd");
        }

        /// <summary>Handler do 0x53 GameResult (resultado do stage solo, FUN_00425010): traduz bytes -> chamada de
        /// domínio (<see cref="WorldServer.ApplyStageResultAsync"/>, que credita exp/gold/rank) e serializa as respostas
        /// — level-up (0x51) + ack (0x53). A regra de negócio (progressão) mora no WorldServer; aqui só parse +
        /// validação de limites + serialização. Layout: [stage][rank][count][count×u16 mapSlots]
        /// [exp:u32][gold:u32][cellExpSlot1:u32][cellExpSlot2:u32][cellExpSlot3:u32].</summary>
        private async Task OnStageResultAsync(byte[] data)
        {
            if (!StageResultProtocol.TryParse(
                    data, out StageResultReport? report, out StageResultParseError parseError))
            {
                Log.Warn("stage", "[{0}] 0x53 parse rejeitado: {1} ({2} bytes)",
                    Slot, parseError, data.Length);
                return;
            }

            StageResultReport parsed = report!;
            StageSettlementResult result = await _server.ApplyStageResultAsync(this, parsed);
            if (result.Status == StageSettlementStatus.Failed)
            {
                Log.Warn("stage", "[{0}] 0x53 rejeitado: stage={1} rank={2} exp={3} gold={4}",
                    Slot, parsed.StageId, parsed.Rank, parsed.ReportedExp, parsed.ReportedGold);
                return;
            }
            if (result.Status == StageSettlementStatus.Applied && result.LevelUps > 0)
                SendLevelUp();
            SendEncryptedFrame(LobbyFrames.StageResultAck(
                parsed.StageId, parsed.Rank, parsed.MapSlots));
            Log.Ok("stage", "[{0}] 0x53 {1}: stage={2} rank={3} exp={4} gold={5} cellExp={6}/{7}/{8}",
                Slot, result.Status, parsed.StageId, parsed.Rank,
                parsed.ReportedExp, parsed.ReportedGold,
                parsed.CellExpSlot1, parsed.CellExpSlot2, parsed.CellExpSlot3);
        }

        internal void BeginFieldGameStagePoint(byte[] data) => _ = OnStageResultAsync(data);

        // ---- handshake UDP -------------------------------------------------------------
        // Fluxo REAL (capturado): o cliente faz ping UDP -> world ecoa 0x0201 (UdpGameplay/BrokerLink)
        // -> cliente manda TCP 0x0e -> TryHandleLobbyEntry responde 0x0e(endpoints)+0x10+... Aqui so
        // registramos o endpoint UDP do jogador (o 0x10 NAO sai aqui — vai apos o 0x0e do cliente).
        public void NotifyUdpReady(IPEndPoint observedEndpoint, IPEndPoint advertisedEndpoint, byte endpointIndex)
        {
            if (endpointIndex == 0) UdpEndpoint1 = observedEndpoint;
            else UdpEndpoint2 = observedEndpoint;
            UdpAdvertisedEndpoint = advertisedEndpoint;
        }

        /// <summary>Erro de login (FUN_004038e0, cat 3, {0x0C, sub}).</summary>
        public void SendLoginError(byte sub)
        {
            using var p = new PacketWriter();
            p.WriteByte(Protocol.LoginError.Main); // 0x0C
            p.WriteByte(sub);
            SendMessage(Protocol.LoginError.Category, p.ToArray());
        }
    }
}
