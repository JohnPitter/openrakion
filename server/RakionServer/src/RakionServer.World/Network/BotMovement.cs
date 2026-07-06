using System;
using System.Buffers.Binary;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Codec do MOVIMENTO/AÇÃO do bot. Converte o estado de IA do <see cref="BotPlayer"/> (posição,
    /// orientação, ação) na mensagem de gameplay que o cliente já renderiza. Isola o ÚNICO ponto gated
    /// do sistema de bots: o WRAPPER externo do datagrama UDP. O CORPO da mensagem está decodificado
    /// byte-a-byte (engine.dll, ver docs/bot-movement-capture.md); síntese da posição do bot, nunca relay.
    ///
    /// MOVE+AÇÃO = CNetMessage **0x30a** (CPlayerSource::SendAction_Relay @engine.dll 0x103cb0; recv
    /// CSessionState::GetActionFromMessage @0x10afe0). Corpo (19B), DECODE byte-a-byte da captura real
    /// (udp_gameplay_decode.out.txt §2):
    ///   [u16 dt][u16 actState][s16 x][s16 y][s16 z][s16 heading][u8 subFrame][s16 ax][s16 ay][s16 az]
    ///   x/y/z e ax/ay/az = PackFloatToSWord(coord) = (short)(coord/0.01) = coord*100. heading = graus
    ///   assinados [-180..180]. dt = ms do frame (~100; NÃO 0). subFrame = nonce que VARIA todo frame.
    /// CANAL = UDP UNRELIABLE (CNet::SendData_Unreliable @0x1000e0), por-peer a cada player state==3 ≠ si.
    /// A origem do relay é o SEAT do dono: vai no srcSlot=seat do HEADER E nos 5 bits baixos do actState do
    /// corpo (cravado na captura: srcSlot=0x00→actState=0x0020; srcSlot=0x0a→actState=0x002a) — os dois têm
    /// de bater no mesmo slot p/ o cliente rotear o move ao avatar. O 0x30a vem SEMPRE acompanhado do 0x030f
    /// (keystate, §3) e o golpe é o 0x0311 (§4).
    /// ⇒ o bot emite pelo socket UdpGameplay p/ o endpoint de cada humano, NÃO pelo relay TCP 0x4b.
    /// </summary>
    public static class BotMovement
    {
        /// <summary>Tipo CNetMessage de move+ação (CPlayerSource::SendAction → SendData_Unreliable(0x30a)).</summary>
        public const ushort MsgAction = 0x030a;

        /// <summary>
        /// Registro de endpoint UDP do peer (CNetMessage 0x319). É o handshake que ABRE o gate do movimento:
        /// o dispatcher do cliente (engine.dll @0x361005ad) grava `playerRec[slot].addr/port` (+0x1e8/+0x1ec)
        /// com o IP:porta de ORIGEM deste datagrama, INCONDICIONALMENTE (sem host-check/sequência). O check de
        /// movimento `IsValidUDP_ForPlayer @0x36109da0` exige que o 0x30a venha desse mesmo (IP,porta). O
        /// type-7 (AddRemotePlayer) preenche o slot mas NÃO o endpoint — só o 0x319 o faz.
        /// </summary>
        public const ushort MsgPeerRegister = 0x0319;

        /// <summary>Companheiro de keystate (animação) do 0x30a — mandar SEMPRE logo após o move (§3).</summary>
        public const ushort MsgKeystate = 0x030f;

        /// <summary>Evento de golpe; o tail u16 = actionId (arma/combo/skill) (§4).</summary>
        public const ushort MsgAttack = 0x0311;

        /// <summary>
        /// CreateNpc — cria uma ENTIDADE NPC remota de COLISÃO REAL (acertável), o mecanismo PRÓPRIO da
        /// engine p/ os monstros (Cell/golem), FORA do muro do type-7 do jogador. RE cravada (engine.dll,
        /// dispatcher <c>CSessionState::HandleMessage</c>@0x3610d7c0, handler @0x3610d80a →
        /// <c>AddRemoteGeneralNpc</c>@0x361097a0 → <c>CWorld::CreateNpc_t</c>@0x360c5c50): o blob da entidade
        /// é VAZIO (a <c>Read_t</c> em vtable+0x118 é stub <c>ret 0x4</c>, lê zero bytes) ⇒ mensagem é só o
        /// header de 28B. Mesmo dispatcher/canal do 0x30a → passa pela mesma via UDP já injetada.
        /// </summary>
        public const ushort MsgCreateNpc = 0x0307;

        /// <summary>
        /// Move/estado do NPC (handler @0x3610dd6c). Chaveado pela tabela NPC <c>this+0x1d70</c> via
        /// <c>owner*9+sub</c> com <see cref="NpcMoveType"/>=2; <c>vtable+0x194</c> aplica a posição. NÃO é o
        /// 0x30a (esse move a tabela de JOGADOR <c>+0x1d20</c>, slot 5-bit) — o NPC tem chave própria.
        /// </summary>
        public const ushort MsgNpcMove = 0x030b;

        /// <summary>Type-byte do corpo 0x30b que seleciona a tabela NPC <c>+0x1d70</c> (cravado @0x3610de69
        /// <c>cmp cl,2</c>); 3=map-npc (<c>+0x2048</c>), 4=<c>+0x2040</c>.</summary>
        public const byte NpcMoveType = 2;

        /// <summary>Type-id da classe no blob do 0x307 (campo <c>entity+0x3920</c>, indexa a tabela de
        /// propriedades). CRAVADO p/ CNpcCrossBow1 = 0x02 (class init @0x35202b89); CrossBow2/3/4 = 0x12/13/14.
        /// É por classe — este valor casa com a classe default <see cref="NpcClassCrossBow"/>.</summary>
        public const byte NpcCrossBowTypeId = 0x02;

        /// <summary>Type-id (campo <c>entity+0x3920</c>) por classe: o CrossBow seta 0x02 na própria
        /// SetDefaultProperties (@0x35202b89); o CGrunt e os monstros <c>CEnemyBase</c> NÃO setam +0x3920 →
        /// valor natural 0 (RE: re_grunt3.py). Mandar o type-id certo evita lixo no campo de tipo do NPC.</summary>
        private static byte TypeIdForClass(ushort classId) => classId == NpcClassCrossBow ? NpcCrossBowTypeId : (byte)0;

        /// <summary>
        /// Class id do <c>0x307</c> que carrega <c>Classes\Player.ecl</c> (engine.dll @0x360f42d7
        /// <c>mov [esi],0x191</c>). RENDERIZA modelo de jogador, MAS a classe Player é PASSIVA pela via NPC
        /// (espera o handshake de jogador → fantasma sem física/colisão). NÃO usar p/ o avatar funcional.
        /// </summary>
        public const ushort NpcClassPlayer = 0x0191;

        // Class ids dos NPCs HUMANOIDES funcionais do Rakion (CEnemyBase → física/colisão/andam sozinhos),
        // lidos do entitiesmp.dll DESEMPACOTADO (rakion-new), campo CDLLEntityClass+0x28 (nome em +0x20):
        /// <summary>CNpcCrossBow1 — arqueiro humanoide medieval (id 0x46c).</summary>
        public const ushort NpcClassCrossBow = 0x046c;
        /// <summary>CNpcBlazer1 — mago humanoide (id 0x461).</summary>
        public const ushort NpcClassBlazer = 0x0461;
        /// <summary>CNpcLongBow1 — arqueiro humanoide (id 0x46f).</summary>
        public const ushort NpcClassLongBow = 0x046f;
        /// <summary>CGrunt — soldado humanoide (id 0x157; classe SeriousSam, pode não estar registrada no Rakion).</summary>
        public const ushort NpcClassGrunt = 0x0157;
        /// <summary>CNpcGolem1 — golem (id 0x468); fallback funcional se o humanoide não resolver.</summary>
        public const ushort NpcClassGolem = 0x0468;
        // MONSTROS CNpc do Rakion (REGISTRADOS no runtime — o 0x307 instancia de verdade, ≠ CGrunt/SeriousSam que
        // não renderizou). Ids lidos do entitiesmp.dll (re_cells.py: 424 _DLLClass). Modo ESTUDO: sumonar um
        // "Cell" nativo e observar hit/movimento/IA reais p/ replicar no bot.
        public const ushort NpcClassDragon = 0x0463;       // CNpcDragon1
        public const ushort NpcClassPanzer = 0x0467;       // CNpcPanzer1
        public const ushort NpcClassTaurus = 0x046a;       // CNpcTaurus1
        public const ushort NpcClassAngelKnight = 0x0460;  // CNpcAngelKnight1
        public const ushort NpcClassSuccubus = 0x0455;     // CNpcSuccubus1
        public const ushort NpcClassNak = 0x0466;          // CNpcNak1

        /// <summary>Mapeia um token do <c>/addbot</c> p/ um class id de NPC (0 = usar o default <see cref="NpcClassBot"/>).
        /// Permite testar classes diferentes in-game sem recompilar: "/addbot arqueiro", "/addbot mago", "/addbot golem".</summary>
        public static ushort ParseNpcClass(string lower)
        {
            if (lower.Contains("crossbow") || lower.Contains("arqueiro") || lower.Contains("besta")) return NpcClassCrossBow;
            if (lower.Contains("longbow") || lower.Contains("arco")) return NpcClassLongBow;
            if (lower.Contains("blazer") || lower.Contains("mago")) return NpcClassBlazer;
            if (lower.Contains("grunt") || lower.Contains("soldado")) return NpcClassGrunt;
            if (lower.Contains("golem")) return NpcClassGolem;
            if (lower.Contains("dragon") || lower.Contains("drag")) return NpcClassDragon;
            if (lower.Contains("panzer")) return NpcClassPanzer;
            if (lower.Contains("taurus") || lower.Contains("touro")) return NpcClassTaurus;
            if (lower.Contains("angel") || lower.Contains("knight") || lower.Contains("anjo")) return NpcClassAngelKnight;
            if (lower.Contains("succu") || lower.Contains("succubus")) return NpcClassSuccubus;
            if (lower.Contains("nak")) return NpcClassNak;
            if (lower.Contains("player") || lower.Contains("jogador")) return NpcClassPlayer;
            return 0;   // sem token de classe -> default (NpcClassBot)
        }

        /// <summary>
        /// Classe ATIVA (default) do avatar: <see cref="NpcClassGolem"/> — um MONSTRO CNpc REGISTRADO do Rakion.
        /// O bot é um "Cell" NATIVO real (colisão/hit/movimento pela própria engine, ≠ fantasma type-7).
        /// Troque in-game sem recompilar: "/addbot golem|dragon|panzer|taurus|angel|succu|nak".
        /// </summary>
        public const ushort NpcClassBot = NpcClassGolem;

        /// <summary>
        /// AVATAR do bot: <c>false</c> = **type-7** (CPlayer fantasma via entity-message + 0x30a/0x319) que RENDERIZA
        /// + ANDA + reage a hit (recuo server-arbitrado) — validado in-game. <c>true</c> = 0x307 CreateNpc (entidade
        /// NPC real, HIT×N nativo).
        ///
        /// **0x307 DESLIGADO — hipótese unreliable TESTADA E REFUTADA in-game (2026-07-06):** o último cabo solto do
        /// caminho NPC era o create UNRELIABLE (0x0307). Racional (RE): o dispatch do cliente (<c>0x3610d350</c>) é
        /// por-opcode e mascara o bit 0x8000, e o move NPC 0x30b unreliable já é processado — logo o create 0x0307
        /// unreliable deveria cair no mesmo handler. TESTADO com o blob golden + mini-peer removido (estado limpo): o
        /// create foi ENTREGUE ao cliente do humano (log "RELAY NPC → 1 humano", 50B) mas **NENHUMA entidade
        /// apareceu**. As 6 capturas reais confirmam o porquê: <c>owner=0</c> = o create é do JOGADOR LOCAL que
        /// invoca a Cell no PRÓPRIO cliente; o 0x8307 no fio é o envio p/ relay, e um create relayado de fonte
        /// NÃO-PEER não instancia a entidade no cliente que não a invocou (nem reliable nem unreliable). ⇒ render
        /// nativo de entidade REMOTA exige o canal de replicação de sessão da SE1 = peer-host real (headless-H3,
        /// barrado pela gamemp packed; OU 2º cliente, vetado). O caminho FUNCIONAL é o type-7 (0x4b + 0x30a):
        /// renderiza + anda + dano server-arbitrado. Ver docs/cell-monster-re.md §2.4/§2.5.
        /// </summary>
        public static bool UseNpcAvatar => false;

        /// <summary>
        /// Modo PONTE move-o-bot-por-SetPlacement (robótico/sem colisão): DESLIGADO. Com o NPC real, o movimento
        /// é aplicado pela PRÓPRIA engine (0x30b → tick nativo de movimento/colisão), não por injeção externa.
        /// </summary>
        public static bool UseClientBridge => false;

        /// <summary>
        /// Datagrama 0x307 CreateNpc completo (50B): <c>[u16 0x0307][u32 seq][u8 srcSlot][corpo 43B]</c> — UNRELIABLE.
        /// O CORPO (43B) é a estrutura GOLDEN das 6 capturas reais de Cell; só o TRANSPORTE muda p/ unreliable: o
        /// dispatch do cliente é por-opcode (mascara 0x8000) e o create <c>0x0307</c> cai no mesmo handler
        /// (AddRemoteGeneralNpc → CreateNpc) que o move <c>0x30b</c> unreliable — sem depender do canal reliable que
        /// o <c>0x8307</c> exigia (e que o mini-peer não estabelecia). Emitido UMA vez por spawn (re-enviar re-cria a
        /// entidade → "unknown error" → destrói). <c>srcSlot</c> = seat do dono (transporte). Corpo =
        /// <see cref="EncodeCreateNpcBody"/>. Ver <see cref="UseNpcAvatar"/> p/ o racional completo.
        /// </summary>
        public static byte[] BuildCreateNpcDatagram(BotPlayer bot, ushort classId, uint seq, byte srcSlot)
        {
            using var w = new PacketWriter();
            w.WriteWord(MsgCreateNpc);                                     // +00 u16 0x0307 (UNRELIABLE — dispatch por-opcode)
            w.WriteUInt32(seq);                                            // +02 u32 seq
            w.WriteByte(srcSlot);                                          // +06 u8 srcSlot (seat do dono — transporte, como 0x30a)
            w.WriteBytes(EncodeCreateNpcBody(bot, bot.NpcOwner, bot.NpcSub, classId)); // +07 corpo 43B
            return w.ToArray();                                           // 50B
        }

        /// <summary>
        /// Datagrama 0x30b NPC move completo (19B): <c>[u16 0x030b][u32 seq][corpo 13B]</c>. Move a entidade
        /// NPC criada (mesma chave <c>owner*9+sub</c>). Corpo = <see cref="EncodeNpcMoveBody"/>.
        /// </summary>
        public static byte[] BuildNpcMoveDatagram(BotPlayer bot, uint seq)
        {
            using var w = new PacketWriter();
            w.WriteWord(MsgNpcMove);                                        // +00 u16 0x030b (unreliable)
            w.WriteUInt32(seq);                                            // +02 u32 seq
            w.WriteBytes(EncodeNpcMoveBody(bot, bot.NpcOwner, bot.NpcSub)); // +06 corpo 13B
            return w.ToArray();                                           // 19B
        }

        /// <summary>Quantização de posição: coord = short*SCALE; SCALE=_DAT_3621acac=0.01 ⇒ short=coord*100.</summary>
        private const float PosScale = 0.01f;

        /// <summary>
        /// Flag ativo/move do actState (bit 0x20) do corpo 0x30a. Os 5 bits BAIXOS do actState codificam o
        /// SLOT do dono (cravado na captura: srcSlot=0x00→actState=0x0020; srcSlot=0x0a→actState=0x002a). O
        /// header srcSlot e o actState têm de bater no MESMO slot, senão o cliente não roteia o move ao avatar.
        /// </summary>
        private const ushort ActStateMoveFlag = 0x0020;

        /// <summary>Máscara dos 5 bits baixos do actState que carregam o slot do dono.</summary>
        private const int SlotMask = 0x1f;

        /// <summary>Tail de keystate (0x030f) parado (captura).</summary>
        private const ushort KeyIdle = 0x0300;

        /// <summary>Tail de keystate (0x030f) andando (captura).</summary>
        private const ushort KeyMoving = 0x0100;

        /// <summary>Delta de frame em ms no corpo 0x30a (taxa real ~100ms da captura).</summary>
        private const ushort DtMs = 100;

        /// <summary>True: o layout do datagrama UDP está CRAVADO da captura (validado byte-a-byte, §2/§6).</summary>
        public static bool UdpFramingKnown => true;

        /// <summary>
        /// Habilita os frames TCP do bot ao cliente: spawn no stage (0x48 FieldGameStart + 0x4b AddPlayer) e
        /// morte (0x4f). O 0x4b é o DECODE byte-a-byte da captura MITM real do servidor original
        /// (rakion-work/ghidra-proj/stage_spawn_decode.out.txt): blob de 67B com posição+stats, no canal FIELD.
        /// O MOVIMENTO (0x30a UDP) segue gated em <see cref="UdpFramingKnown"/> (canal/origem distintos, ainda
        /// não validados): com isto o bot APARECE no stage mas fica parado até a validação UDP.
        /// </summary>
        // RELIGADO (2026-06-25): o spawn no stage saiu de PALPITE (que crashava) para o 0x4b CRAVADO da
        // captura — é a forma que o cliente já viu do servidor original, respeitando a lição-mestra. O 0x38 do
        // slot da SALA nunca dependeu disto.
        public static bool ClientFramesEnabled => true;

        /// <summary>
        /// Corpo CNetMessage 0x30a (19B) da posição/ação do bot — DECODE byte-a-byte da captura (§2). É o
        /// payload que o destino lê em GetActionFromMessage. O <paramref name="seat"/> do dono entra DUAS
        /// vezes: no srcSlot do header E nos 5 bits baixos do actState (= 0x0020|seat); ambos têm de bater no
        /// mesmo slot p/ o cliente rotear o move ao avatar do bot. <paramref name="subFrame"/> = nonce que
        /// DEVE variar a cada frame (o cliente não valida o valor, só exige que mude).
        /// </summary>
        public static byte[] EncodeActionBody(BotPlayer bot, int seat, byte subFrame)
        {
            using var w = new PacketWriter();
            w.WriteWord(DtMs);                                          // +00 u16 dt (ms; ~100)
            w.WriteWord((ushort)(ActStateMoveFlag | (seat & SlotMask))); // +02 u16 actState (flag move | slot)
            w.WriteInt16(Pack(bot.X));                   // +04 s16 x
            w.WriteInt16(Pack(bot.Y));                   // +06 s16 y
            w.WriteInt16(Pack(bot.Z));                   // +08 s16 z
            // +0a s16 heading. A convenção do wire é OPOSTA ao Yaw "aponta-pro-alvo" do domínio (in-game o avatar
            // ficava de COSTAS pro alvo) -> +180 p/ o avatar encarar o alvo/movimento.
            w.WriteInt16((short)NormalizeDeg(bot.Yaw + 180f));
            w.WriteByte(subFrame);                       // +0c u8  subFrame nonce (varia)
            w.WriteInt16(Pack(bot.AimX));                // +0d s16 aimX (0 parado; !=0 no golpe)
            w.WriteInt16(Pack(bot.AimY));                // +0f s16 aimY
            w.WriteInt16(Pack(bot.AimZ));                // +11 s16 aimZ
            return w.ToArray();                          // 19B
        }

        /// <summary>
        /// Datagrama 0x30a completo (26B): [u16 msgType][u32 seq][u8 srcSlot][corpo 19B]. msgType = 0x30a
        /// (unreliable; bit 0x8000 não setado). seq = contador por-sender (++ a cada pacote). srcSlot =
        /// <paramref name="seat"/> do bot (origem do relay; o mesmo slot ecoa no actState do corpo). O alvo
        /// NÃO entra no pacote (é o endereço do SendTo). Corpo cravado em <see cref="EncodeActionBody"/>.
        /// </summary>
        public static byte[] BuildActionDatagram(BotPlayer bot, int seat)
        {
            byte[] body = EncodeActionBody(bot, seat, bot.NextSubFrame());
            using var w = new PacketWriter();
            w.WriteWord(MsgAction);          // +00 u16 0x030a (unreliable)
            w.WriteUInt32(bot.UdpSeq++);     // +02 u32 seq (++ por pacote do sender)
            w.WriteByte((byte)seat);         // +06 u8  srcSlot = seat do bot
            w.WriteBytes(body);              // +07 corpo 19B
            return w.ToArray();              // 26B
        }

        /// <summary>
        /// Datagrama 0x319 (8B) que REGISTRA o endpoint UDP do bot no cliente do humano — o handshake que
        /// destrava o movimento. Enviado do socket do SERVIDOR (a mesma origem que relaya os 0x30a) ANTES dos
        /// 0x30a, faz o cliente gravar `playerRec[slot].addr/port` = socket do servidor; daí os 0x30a relayados
        /// passam em `IsValidUDP_ForPlayer`. Layout (cravado em engine.dll @0x361005ad): [u16 0x0319][u32 seq]
        /// [u8 slot@6][u8 slot@7]. O handler lê só o opcode (offset 0) e o slot (offset 7); o resto é ignorado.
        /// </summary>
        public static byte[] BuildPeerRegister(int seat, uint seq)
        {
            using var w = new PacketWriter();
            w.WriteWord(MsgPeerRegister);   // +00 u16 0x0319 (unreliable: bit 0x8000 limpo → caminho de dispatch)
            w.WriteUInt32(seq);             // +02 u32 seq (ignorado pelo handler do 0x319)
            w.WriteByte((byte)seat);        // +06 u8 slot (eco; não lido pelo 0x319)
            w.WriteByte((byte)seat);        // +07 u8 slot LIDO ([esp+0x3f]) → indexa playerRec[slot]
            return w.ToArray();             // 8B
        }

        /// <summary>
        /// Companheiro 0x030f (14B) de keystate/animação — mandar SEMPRE logo após o 0x30a (§3). O cliente
        /// lê o par (posição + keystate) p/ escolher a animação. Corpo: [u8 srcSlotEcho][u16 0x0008][u16
        /// 0x0001][u16 tail], tail = 0x0100 (andando) / 0x0300 (parado) — ambos vistos na captura. srcSlot e
        /// srcSlotEcho = <paramref name="seat"/> do bot (mesma origem do 0x30a).
        /// </summary>
        public static byte[] BuildKeystateDatagram(BotPlayer bot, int seat, bool moving)
        {
            using var w = new PacketWriter();
            w.WriteWord(MsgKeystate);                    // +00 0x030f
            w.WriteUInt32(bot.UdpSeq++);                 // +02 seq (mesmo contador, ++)
            w.WriteByte((byte)seat);                     // +06 srcSlot = seat do bot
            w.WriteByte((byte)seat);                     // +07 srcSlotEcho = seat do bot
            w.WriteWord(0x0008);                         // +08 const (tipo de bloco de input)
            w.WriteWord(0x0001);                         // +0a const
            w.WriteWord(moving ? KeyMoving : KeyIdle);   // +0c tail keystate
            return w.ToArray();                          // 14B
        }

        /// <summary>
        /// Golpe 0x0311 (10B) — emitir no INSTANTE do ataque (não todo frame, §4). [u16 0x0311][u32 seq]
        /// [u8 srcSlot][u8 sub][u16 actionId]. <paramref name="actionId"/> = id de arma/combo/skill (1 =
        /// básico; 4/8/9 = combo na captura). srcSlot e sub = <paramref name="seat"/> do bot (mesma origem do
        /// 0x30a). O impacto/direção viaja no aim-vec do 0x30a seguinte.
        /// </summary>
        public static byte[] BuildAttackDatagram(BotPlayer bot, int seat, ushort actionId)
        {
            using var w = new PacketWriter();
            w.WriteWord(MsgAttack);          // +00 0x0311
            w.WriteUInt32(bot.UdpSeq++);     // +02 seq (++)
            w.WriteByte((byte)seat);         // +06 srcSlot = seat do bot
            w.WriteByte((byte)seat);         // +07 sub = seat do bot
            w.WriteWord(actionId);           // +08 actionId
            return w.ToArray();              // 10B
        }

        // ---- SPAWN/MOVE como NPC (0x307/0x30b) — entidade de COLISÃO real, fura o type-7 ----

        /// <summary>
        /// Corpo do CNetMessage do 0x307 CreateNpc (após <c>[opcode][seq][srcSlot]</c> do datagrama): header 28B
        /// <c>[u8 owner][u8 sub][u16 classId][6×f32 placement]</c> + blob 15B <c>[f32 HPcur][f32 HPmax][01 00 01
        /// 00 00 00 00]</c>. Total 43B.
        ///
        /// GOLDEN (2026-07-01): estrutura CRAVADA de 6 invocações REAIS de Cell capturadas do cliente
        /// (log worldserver, opcode reliable <c>0x8307</c>): Golem 0x468/HP400, Dragon 0x463/HP260, Nak 0x466/HP50.
        /// O blob real é ENXUTO (só HP cur/max + tail constante) — o §2.3c (~50-90B, RE do rakion-new = binário
        /// ERRADO) era ruído e por isso nunca renderizou. <paramref name="owner"/> = seat do dono (chave
        /// <c>world+0x1d70</c> idx <c>owner*9+sub</c>; o time da creature é herdado do dono → facção inimiga do humano).
        /// </summary>
        public static byte[] EncodeCreateNpcBody(BotPlayer bot, byte owner, byte sub, ushort classId)
        {
            using var w = new PacketWriter();
            // --- header 28B (handler @0x3610d80a) ---
            w.WriteByte(owner);                          // +00 u8  owner (seat do dono → chave + time)
            w.WriteByte(sub);                            // +01 u8  sub
            w.WriteWord(classId);                        // +02 u16 classe (0x468 Golem / 0x463 Dragon / 0x466 Nak)
            w.WriteSingle(bot.X);                        // +04 f32 pos.x
            w.WriteSingle(bot.Y);                        // +08 f32 pos.y
            w.WriteSingle(bot.Z);                        // +0c f32 pos.z
            w.WriteSingle((float)NormalizeDeg(bot.Yaw)); // +10 f32 heading (GRAUS — captura real em [-180,180])
            w.WriteSingle(-0f);                          // +14 f32 pitch (-0.0 na captura: 0x80000000)
            w.WriteSingle(0f);                           // +18 f32 bank
            // --- blob 15B (GOLDEN da captura real): HP cur/max + tail constante ---
            float hp = bot.MaxHp;
            w.WriteSingle(hp);                           // +1c f32 HP cur
            w.WriteSingle(hp);                           // +20 f32 HP max
            w.WriteByte(1); w.WriteByte(0); w.WriteByte(1);                    // +24 tail 01 00 01
            w.WriteByte(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);    //     00 00 00 00
            return w.ToArray();                          // 43B (28 header + 15 blob)
        }

        /// <summary>
        /// Corpo do 0x30b NPC move (13B): <c>[s16 A][u8 type=2][u8 owner][u8 sub][s16 x][s16 y][s16 z]
        /// [s16 heading]</c>. x/y/z = <c>coord*100</c> (mesmo empacotamento do 0x30a, decoder <c>0x360f96d0
        /// = s16*0.01</c>); heading = graus assinados (o handler faz int→float cru, sem escala). <c>A</c>
        /// (→ <c>entity+0x354</c> escalado) = aim/view; 0 = neutro até a RE do campo. Chave <c>owner*9+sub</c>
        /// = a MESMA do <see cref="EncodeCreateNpcBody"/> (endereça o NPC criado).
        /// </summary>
        public static byte[] EncodeNpcMoveBody(BotPlayer bot, byte owner, byte sub)
        {
            using var w = new PacketWriter();
            w.WriteInt16(0);                                  // +00 s16 A (aim/view → entity+0x354; 0=neutro)
            w.WriteByte(NpcMoveType);                         // +02 u8  type=2 (tabela NPC +0x1d70)
            w.WriteByte(owner);                               // +03 u8  owner
            w.WriteByte(sub);                                 // +04 u8  sub
            w.WriteInt16(Pack(bot.X));                        // +05 s16 x (coord*100)
            w.WriteInt16(Pack(bot.Y));                        // +07 s16 y
            w.WriteInt16(Pack(bot.Z));                        // +09 s16 z
            w.WriteInt16((short)NormalizeDeg(bot.Yaw));       // +0b s16 heading (graus)
            return w.ToArray();                               // 13B
        }

        // ---- SPAWN no STAGE (frames TCP/FIELD; DECODE byte-a-byte da captura MITM real; gated em
        //      ClientFramesEnabled) — rakion-work/ghidra-proj/stage_spawn_decode.out.txt ----
        private const byte  StageSpawnLead   = 0x08;   // +0x00 marcador de estado inicial (const na captura)
        private const float StageSpawnX      = 97.0f;  // ponto de spawn do mapa (captura; por-mapa = futuro)
        private const float StageSpawnY      = 97.0f;
        private const float StageSpawnZ      = 0.0f;
        private const uint  StageSpawnAnimId = 31;     // +0x11 (0x1f) const na captura
        private const float StageSpawnFacing = 0.98f;  // +0x22 orientacao/escala

        /// <summary>
        /// Corpo do 0x4b AddPlayer (canal FIELD) que INSTANCIA o avatar 3D do bot no stage do host — DECODE
        /// byte-a-byte da captura. O frame completo é [u16 0x4b]+este corpo = [u8 seat][u16 67][blob 67B]; o
        /// blob leva 2 blocos de posição (atual+anchor) + ids + stats, SEM nome/tag (a identidade já veio no
        /// 0x38 do roster). Posição/stats = literal da captura (a forma que o cliente já viu — lição-mestra;
        /// parametrizar por classe/mapa do <paramref name="bot"/> é melhoria futura). Decode §1.1.
        /// </summary>
        public static byte[] BuildStageAddPlayer(BotPlayer bot, int seat) => BuildStageAddPlayer(seat);

        /// <summary>
        /// 0x4b AddPlayer de um HUMANO relayado a OUTRO humano — usa a POSIÇÃO e os STATS REAIS que o peer subiu no
        /// próprio 0x4b (<paramref name="upload"/> = os 68B do <c>data</c> do handler), montados na estrutura 67B
        /// known-good (a mesma do bot, que o cliente aceita). O upload é <c>[0x41][00][blob 66B]</c>: o blob (offset 2)
        /// tem o MESMO layout do frame de recebimento (só o par 0x41/00 e o pad de cauda diferem). Relay de estado VIVO
        /// do peer (papel do servidor), não replay de blob. Fallback ao spawn constante se o upload for curto/ausente.
        /// </summary>
        public static byte[] BuildStageAddPlayerFromUpload(int seat, byte[]? upload)
        {
            const int b = 2;                       // blob começa após [0x41][00]
            if (upload == null || upload.Length < b + 0x3a) return BuildStageAddPlayer(seat);
            float ReadF(int o) => BinaryPrimitives.ReadSingleLittleEndian(upload.AsSpan(b + o));
            uint ReadU(int o) => BinaryPrimitives.ReadUInt32LittleEndian(upload.AsSpan(b + o));

            using var w = new PacketWriter();
            w.WriteByte((byte)seat);
            w.WriteWord(67);
            w.WriteByte(StageSpawnLead);
            w.WriteSingle(ReadF(0x01)); w.WriteSingle(ReadF(0x05)); w.WriteSingle(ReadF(0x09)); // posX/Y/Z reais
            w.WriteSingle(ReadF(0x0d));
            w.WriteUInt32(ReadU(0x11));                  // animId
            w.WriteUInt32(ReadU(0x15));                  // flag ativo/visível
            w.WriteByte(0);
            w.WriteSingle(ReadF(0x1a)); w.WriteSingle(ReadF(0x1e));   // anchor X/Y reais
            w.WriteSingle(ReadF(0x22));                  // facing
            w.WriteUInt32(ReadU(0x26)); w.WriteUInt32(ReadU(0x2a)); w.WriteUInt32(ReadU(0x2e)); // stats reais
            w.WriteUInt32(ReadU(0x32)); w.WriteUInt32(ReadU(0x36));
            w.WriteBytes(new byte[9]);
            return w.ToArray();
        }

        /// <summary>0x4b AddPlayer por SEAT (sem bot) — instancia o avatar no spawn do field. Usado p/ o BOT (que não
        /// tem cliente p/ subir a própria posição). Posição/stats literais da captura (o cliente move o avatar depois
        /// via 0x30a relayado). P/ 2 humanos ver <see cref="BuildStageAddPlayerFromUpload"/> (posição real do peer).</summary>
        public static byte[] BuildStageAddPlayer(int seat)
        {
            using var w = new PacketWriter();
            w.WriteByte((byte)seat);          // seat/senderSlot (distingue qual avatar)
            w.WriteWord(67);                  // blobLen = 0x0043
            w.WriteByte(StageSpawnLead);      // +00 lead
            w.WriteSingle(StageSpawnX);       // +01 posX (spawn do field)
            w.WriteSingle(StageSpawnY);       // +05 posY
            w.WriteSingle(StageSpawnZ);       // +09 posZ
            w.WriteSingle(0f);                // +0d 4o float do 1o bloco
            w.WriteUInt32(StageSpawnAnimId);  // +11 id/anim
            w.WriteUInt32(1);                 // +15 flag (ativo/visivel)
            w.WriteByte(0);                   // +19 pad de alinhamento
            w.WriteSingle(StageSpawnX);       // +1a posX2 (anchor, = atual p/ spawn parado)
            w.WriteSingle(StageSpawnY);       // +1e posY2
            w.WriteSingle(StageSpawnFacing);  // +22 facing/escala
            w.WriteUInt32(90);                // +26 statA (captura)
            w.WriteUInt32(100);               // +2a statB
            w.WriteUInt32(110);               // +2e statC
            w.WriteUInt32(100);               // +32 statD
            w.WriteUInt32(15);                // +36 statE
            w.WriteBytes(new byte[9]);        // +3a pad final (zeros) -> fecha 67
            return w.ToArray();
        }

        /// <summary>Corpo do 0x48 FieldGameStart (round-load) ao host: [u8 0x01][u32 roundId][u32 0x1414][u8 0].
        /// O servidor original o emite na entrada do stage; roundId é handle de sessão que o cliente só ecoa.</summary>
        public static byte[] BuildFieldGameStart(uint roundId = 0x0000012f)
        {
            using var w = new PacketWriter();
            w.WriteByte(0x01);                // subtype = game start/load
            w.WriteUInt32(roundId);           // u32 roundId (captura: 0x12f)
            w.WriteUInt32(0x00001414);        // u32 param2 (copiado da captura)
            w.WriteByte(0x00);                // pad
            return w.ToArray();
        }

        /// <summary>
        /// Atualização de posição do bot via TCP (opcode 0x4C). O cliente já processa 0x4C como
        /// field update — o log mostra que o servidor original envia 0x4C logo após o 0x4B. O blob
        /// é o mesmo formato do 0x4B AddPlayer (67B) mas com posição atualizada. Enviado via
        /// SendMessage (TCP/canal FIELD), o mesmo canal que o host já aceita.
        /// </summary>
        public static byte[] BuildStagePositionUpdate(BotPlayer bot, int seat)
        {
            using var w = new PacketWriter();
            w.WriteByte((byte)seat);
            w.WriteWord(67);
            w.WriteByte(StageSpawnLead);
            w.WriteSingle(bot.X);             // +01 posX ATUALIZADO
            w.WriteSingle(bot.Y);             // +05 posY
            w.WriteSingle(bot.Z);             // +09 posZ ATUALIZADO
            w.WriteSingle(0f);                // +0d
            w.WriteUInt32(StageSpawnAnimId);  // +11
            w.WriteUInt32(1);                 // +15
            w.WriteByte(0);                   // +19
            w.WriteSingle(bot.X);             // +1a posX2 (anchor)
            w.WriteSingle(bot.Y);             // +1e posY2
            w.WriteSingle(bot.Yaw == 0 ? StageSpawnFacing : bot.Yaw / 180f); // +22 facing
            w.WriteUInt32(90);                // +26
            w.WriteUInt32(100);               // +2a
            w.WriteUInt32(110);               // +2e
            w.WriteUInt32(100);               // +32
            w.WriteUInt32(15);                // +36
            w.WriteBytes(new byte[9]);        // +3a pad
            return w.ToArray();
        }

        private static short Pack(float coord) =>
            (short)System.Math.Clamp(coord / PosScale, short.MinValue, short.MaxValue);

        /// <summary>Yaw em graus normalizado p/ [-180..180] (heading do corpo 0x30a).</summary>
        private static int NormalizeDeg(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return (int)deg;
        }
    }
}
