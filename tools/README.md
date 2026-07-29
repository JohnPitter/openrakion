# Ferramentas

## Integridade das fontes de RE

`verify_re_inputs.ps1` valida os hashes do cliente, World original, binário importado no Ghidra e
World usado nas capturas ao vivo. Uma divergência encerra com código `1` e deve ser resolvida antes
de promover offsets ou payloads a contrato canônico.

`world_character_probe.py [porta] <ação> [argumentos]` envia login e uma operação de personagem
usando o framing AES real. Ações disponíveis:

```text
create [nome] [classe] [slot]
delete
buddy [nome]
resolve
tutorial
reset [tipoPagamento] [valor]
rename [nome] [tipoPagamento] [valor]
present-peek
present-accept [pendingId] [slot]
present-dispose [pendingId]
storage-buy [itemId] [currency] [couponSlot opcional]
storage-sell [slot]
storage-move [srcType] [srcSlot] [dstType] [dstSlot]
buy-bag [paymentType] [couponSlot]
buy-char-slot [paymentType] [couponSlot]
buy-potion-slot
buy-stage-rank-clear
buy-stage-level-free
hold [segundos]
```

Execute mutações somente contra um banco descartável ou com fixture restaurável. `delete` remove o
personagem e suas dependências; `reset` altera atributos e saldo; `rename` altera nome, saldo e logs.

`world_enchant_probe.py [porta]` valida o ciclo `0x74 preview → 0x28 commit → 0x74 result` e replay.
Requer, na conta `test`, arma/catalisador/material nas células `0/1/2`, com seriais
`9912001/9912002/9912003` e `characterid=0` (storage); use somente fixture descartável.

`world_power_user_probe.py [porta] [hold_seconds]` valida o callback exato da compra `0x34`
(`gold/cash/powertime/points/presents`) e a alocação `0x33`; `hold_seconds` mantém a conexão aberta
para testar expiração online. O probe é mutável e requer fixture financeira/stat restaurável da
conta `test`.

`ghidra/FindWorldPowerUserCallbacks.py` extrai o worker DB interno `0x17`;
`ghidra/FindWorldPowerUserClientCallback.py` localiza a adaptação externa `FUN_004281B0`;
`ghidra/DecompileRakionPowerUser.py` e `ghidra/DecompileClientPowerUser.py` fecham o consumidor e
os exports do cliente. As saídas ficam em `C:\temp\world_power_user_callbacks.txt`,
`C:\temp\world_power_user_client_callback.txt`, `C:\temp\rakion_power_user.txt` e
`C:\temp\client_power_user.txt`.

`ghidra/FindCharacterDbCommands.py` localiza e decompila os comandos de banco e callbacks de
create, reset e rename no World original. Exemplo de execução headless:

```powershell
analyzeHeadless.bat <workspace-ghidra> wsSell -process worldserv.exe -noanalysis `
  -scriptPath .\tools\ghidra -postScript FindCharacterDbCommands.py
```

O relatório é gravado em `C:\temp\character_db_commands.txt`.

`ghidra/DecompileWorldGmQueryEntry.py` extrai o handler `0x09`, o serializer de nome da sala e
personagem criador e o inicializador que prova os offsets. A saída fica em
`C:\temp\world_gm_query_entry.txt`.

`ghidra/DecompileWorldRoomInfo.py` extrai o parser textual e o serializer de 26 linhas do comando
`/roominfo`. A saída fica em `C:\temp\world_room_info.txt`.

`ghidra/DecompileWorldFirstFieldMove.py` extrai o handler `0x4B` e o relay para provar que o
primeiro payload também segue pelo caminho canônico, sem repaint de inventário. A saída fica em
`C:\temp\world_first_field_move.txt`.

`ghidra/DecompileCharacterCoreLifecycle.py` extrai `0x12/0x13/0x15/0x1A` nas três pontas:
builders/parsers do `engine.dll`, handlers/fila/workers/callbacks do World e consumidores da UI em
`rakion.bin`. As saídas são `C:\temp\engine_character_core_lifecycle.txt`,
`C:\temp\world_character_core_lifecycle.txt` e `C:\temp\rakion_character_core_lifecycle.txt`.

`ghidra/DecompileSuccessUdp.py` fecha o handshake TCP `0x0E`: builder e parser em `engine.dll`,
handler e projeção dos endpoints no World e callback disponível em `rakion.bin`. Execute nos três
projetos correspondentes; as saídas são `C:\temp\engine_success_udp.txt`,
`C:\temp\world_success_udp.txt` e `C:\temp\rakion_success_udp.txt`.

`ghidra/DecompileKeepAlive.py` fecha o request vazio `0x0F`, seu gate de conta e a medição do
intervalo por sessão. Execute contra `engine.dll` e `worldserv.exe`; as saídas são
`C:\temp\engine_keep_alive.txt` e `C:\temp\world_keep_alive.txt`.

`ghidra/DecompileCharacterGetUserName.py` fecha o `0x19`: builder/parser no `engine.dll`, eco no
World e ponte/callback no `rakion.bin`. As saídas são `C:\temp\engine_character_get_user_name.txt`,
`C:\temp\world_character_get_user_name.txt` e `C:\temp\rakion_character_get_user_name.txt`.

`ghidra/DecompileInventoryStackPotion.py` extrai a cadeia completa de `0x73`: builder e parsers
do `engine.dll`, handler/validações/deltas do World e callbacks de UI do `rakion.bin`. As saídas são
`C:\temp\world_inventory_stack_potion.txt`, `C:\temp\engine_inventory_stack_potion.txt` e
`C:\temp\rakion_inventory_stack_potion.txt`.

`ghidra/DecompileInventoryMove.py` fecha o swap `0x31`, incluindo `FUN_0040CF10/0040BC10`, o
parser de 21 bytes e o callback que atualiza inventário/equipamento. As saídas são
`C:\temp\world_inventory_move.txt`, `C:\temp\engine_inventory_move.txt` e
`C:\temp\rakion_inventory_move.txt`.

`ghidra/DecompileCharacterResetRename.py` fecha o caminho completo de `0x1B/0x1C`: builders e
parsers em `engine.dll`, handlers, validação de cupom, fila/worker DB e callbacks no World, além dos
consumidores de UI em `rakion.bin`. Execute o script nos três projetos correspondentes; os relatórios
reproduzíveis são `C:\temp\engine_character_reset_rename.txt`,
`C:\temp\world_character_reset_rename.txt` e `C:\temp\rakion_character_reset_rename.txt`.

`ghidra/FindCouponPresentFlow.py` fecha a validação de cupom, os callbacks de reset/rename e o
sorteio de random presents. `ghidra/FindPresentConfigWrites.py` localiza as escritas do catálogo e
dos thresholds usados pelo sorteio. Eles podem ser executados no mesmo projeto headless:

```powershell
analyzeHeadless.bat <workspace-ghidra> wsSell -process worldserv.exe -noanalysis `
  -scriptPath .\tools\ghidra -postScript FindCouponPresentFlow.py

analyzeHeadless.bat <workspace-ghidra> wsSell -process worldserv.exe -noanalysis `
  -scriptPath .\tools\ghidra -postScript FindPresentConfigWrites.py
```

Os relatórios são gravados em `C:\temp\coupon_present_flow.txt` e
`C:\temp\present_config_writes.txt`.

`ghidra/FindPresentInboxFlow.py` decompila requests e SQL de Peek/Accept/Dispose;
`ghidra/FindPresentCallbacks.py` localiza os serializers `0x6A`–`0x6D` a partir dos callers do envio
ao cliente. As saídas são `C:\temp\present_inbox_flow.txt` e `C:\temp\present_callbacks.txt`.

Os scripts de entitlement fecham exports do cliente, handlers, callbacks e comandos DB:

- `ghidra/FindClientInventoryEntitlements.py` — requests `0x32/0x35/0x6F/0x70/0x71` no `engine.dll`;
- `ghidra/FindStageEntitlementDbCallers.py` — callers e SQL dos comandos DB de `0x70/0x71` no `worldserv.exe`;
- `ghidra/FindInventoryEntitlementFlows.py` — handlers e helpers de quote no World;
- `ghidra/FindInventoryEntitlementCallbacks.py` — callbacks de bag/character/potion slot;
- `ghidra/FindInventoryEntitlementDbCommands.py` — SQL e produtos das compras;
- `ghidra/FindStageEntitlementCallbacks.py` — callbacks de rank-clear/stage-level-free.

As saídas ficam em `C:\temp\client_inventory_entitlements.txt`,
`inventory_entitlement_flows.txt`, `inventory_entitlement_callbacks.txt`,
`inventory_entitlement_db_commands.txt` e `stage_entitlement_callbacks.txt`.

`ghidra/FindWorldShopEconomy.py` localiza as rotinas SQL de compra/venda, os kinds do ledger e
qualquer referência real a `buyinfo` no `worldserv.exe`. O relatório é gravado em
`C:\temp\world_shop_economy.txt`.

`ghidra/DecompileClientShopPurchase.py` fecha os exports `SendInventoryBuy/Sell` no `engine.dll`;
`ghidra/DecompileWorldShopRequest.py` decompila `FUN_00421210` e o helper de quote/cupom
`FUN_0040CB10`, além do handler/helper de venda `FUN_004215A0`/`FUN_0040CD70`.
`ghidra/DumpWorldShopPricing.py` preserva a regra x87 e as constantes de revenda. As saídas são
`C:\temp\client_shop_purchase.txt`, `C:\temp\world_shop_pricing.txt` e
`C:\temp\world_shop_request.txt`.
`ghidra/DecompileClientShopResponses.py` fecha os consumidores S->C `0x2E/0x2F`; a saída é
`C:\temp\client_shop_responses.txt`.
`ghidra/DecompileRakionShopCallbacks.py` extrai os callbacks de UI que recebem esses resultados;
a saída é `C:\temp\rakion_shop_callbacks.txt`.
`ghidra/TraceRakionInventoryCallbackVtable.py` resolve os slots vizinhos do callback `0x31` para
identificar compra/venda sem inferência por proximidade; a saída é
`C:\temp\rakion_inventory_callback_vtable.txt`.

`ghidra/TraceClientEquipmentEffects.py` parte do callback de move `0x31` e decompila os
recalculadores/projetores chamados após equipar ou desequipar. A saída é
`C:\temp\client_equipment_effects.txt`.
`ghidra/FindClientEquipmentStatConsumers.py` cruza chamadas a `GetItemInfo` com os offsets do
equipamento e dos atributos; a saída é `C:\temp\client_equipment_stat_consumers.txt`.

`ghidra/DecompileClientEnchant.py` audita a presença do alias de refino nos símbolos do engine;
`ghidra/DecompileWorldEnchant.py` extrai preview `0x74`, commit `0x28`, regra e dependências do
World original; `ghidra/FindEnchantCoefficientWriters.py` encontra os writers dos coeficientes e o
inicializador das tabelas; `ghidra/DumpWorldEnchantPrecision.py` grava os bits IEEE-754 das
constantes e o disassembly x87 da regra. As saídas são `C:\temp\client_enchant.txt`,
`C:\temp\world_enchant.txt`, `C:\temp\world_enchant_coefficients.txt` e
`C:\temp\world_enchant_precision.txt`.

`ghidra/FindWorldPowerUserPurchase.py` localiza o comando DB de compra do Power User e fecha seu
uso de cupom, validade, pontos e ledger. A saída é `C:\temp\world_power_user_purchase.txt`.

`ghidra/DecompileClientPcBangResult.py` decompila o renderer de resultado que usa
`Img_PCBang`; `ghidra/TraceClientPcBangFlag.py` rastreia os offsets `AccountInfo_s+0x2D80/+0x2D84`
em entities, cliente e engine. Juntos, eles comprovam que o badge representa Power User e que a
regra é EXP `×1,5`, com Gold bruto. As saídas são `C:\temp\client_pcbang_result.txt` e
`C:\temp\client_pcbang_flag_{entities,rakion,engine}.txt`.

`ghidra/FindWorldRankingFlows.py` inventaria referências a `totalrank/classrank` e às projeções
`*rankp` no World original. A saída é `C:\temp\world_ranking_flows.txt`.

`ghidra/DecompileRankUpdate.py` extrai do `RankUpdate.exe` dedicado a ordenação geral/por classe,
grades, ranking de membros/clãs e rotação atômica dos snapshots. A saída é
`C:\temp\rank_update.txt`.

`ghidra/FindWorldLotteryFlows.py` localiza SQL, handlers, callbacks e consultas de resultado da
loteria no World original. A saída é `C:\temp\world_lottery_flows.txt`.

`ghidra/AuditClientWorldLotterySupport.py` audita o dispatcher S→C do `engine.dll` e falha se
`0x75/0x76` aparecerem. Na build v258 analisada, ambos estão ausentes e o maior case é `0x74`; a
saída é `C:\temp\client_world_lottery_support.txt`.

`ghidra/FindWorldCashAccounting.py` localiza os consumidores de `cash`, `localsales` e tabelas de
auditoria financeira no World original. A saída é `C:\temp\world_cash_accounting.txt`.

`ghidra/FindBuddySmsFlows.py` localiza a montagem `SVC_SMS_SEND 0x2030` e o retorno `0x2031` no
`Buddy2.dll`. A saída é `C:\temp\buddy_sms_flows.txt`.

Os scripts de demo distinguem infraestrutura genérica da Serious Engine de feature Rakion ativa:

- `ghidra/TraceEngineDemoFlows.py` — exports, shell, chunks, gravação e playback da `engine.dll`;
- `ghidra/TraceRakionDemoUi.py` — scanners e alcançabilidade dos helpers no `rakion.bin`;
- `ghidra/TraceRakionDemoState.py` — acessos aos campos da lista `EFNMDemo`.

As saídas são `C:\temp\client_demo_flows_{engine,rakion_orig}.txt`,
`C:\temp\rakion_demo_ui.txt` e `C:\temp\rakion_demo_state.txt`.

Os scripts de call site WorldNet distinguem um offset virtual coincidente de uma chamada real da
UI Rakion:

- `ghidra/TraceRakionWorldNetAccessor.py` — decompila todos os callers do accessor
  `FUN_00471B70`;
- `ghidra/DecompileDormantWorldCallsites.py` — revisa os offsets virtuais dos exports que o World
  v258 rejeita.

As saídas são `C:\temp\rakion_worldnet_accessor.txt` e
`C:\temp\client_dormant_world_callsites.txt`.

`ghidra/TraceCharacterLifecycle.py` decompila `0x13/0x14`, o worker DB de exclusão e os consumidores
de erro do cliente. As saídas são `C:\temp\character_lifecycle_worldserv_exe.txt` e
`C:\temp\character_lifecycle_rakion_orig_exe.txt`.

`ghidra/TraceWorldChannelLobby.py` extrai o agregado de canal, entrada, snapshot, saída,
transferência de owner `0x28`, chat e os pares de ping `0x29/0x2A` e `0x59/0x5A`.
`ghidra/FindWorldCharacterHandle.py` rastreia o campo
`user+0x14D0` serializado na presença e confirma sua origem no bloco de clã. As saídas são
`C:\temp\world_channel_lobby.txt` e `C:\temp\world_character_handle.txt`.

`ghidra/DecompileClientChannelLobby.py` extrai o dispatcher e os consumidores S→C `0x1D..0x2A`
da `engine.dll`. Ele confirma o owner slot no `0x1E` e a troca de owner `0x28`. A saída é
`C:\temp\client_channel_lobby.txt`.

`ghidra/DecompileClientChannelListRequest.py` fecha o request C→S `0x1D`, o parser da resposta de
mesmo opcode e a origem dos dois argumentos no evento UI `0x174`. Execute em `engine.dll` e
`rakion_orig.exe`; as saídas são `C:\temp\client_channel_list_request_engine_dll.txt` e
`C:\temp\client_channel_list_request_rakion_orig_exe.txt`.

`ghidra/TraceClientClanPresence.py` fecha o caminho do `clanId` da presença `0x1E/0x1F` até a
linha visual do canal e compara sua vida útil com a troca para a tela de sala. Execute uma vez em
`engine.dll` e outra em `rakion.bin`; as saídas são
`C:\temp\client_clan_presence_engine.txt` e `C:\temp\client_clan_presence_rakion.txt`.

`ghidra/DecompileWorldWhisperLocation.py` extrai os handlers World `0x16/0x17/0x18`, os helpers de
identidade/localização, o carregamento de `ServerId` e os parsers/callbacks correspondentes de
`engine.dll` e `rakion.bin`.

`ghidra/DecompileInventoryLeave.py` fecha o `0x2C/0x2D`, `user+0x144C`, coleta de deltas, worker e
callback do comando DB interno `0x13`, além do builder/parser/callback do cliente.

`ghidra/DecompileCharacterSelect.py` extrai o builder e parser `0x14`, o handler do World, os
helpers que ativam o personagem e a presença de canal, e o callback final da UI em `rakion.bin`.

## `xfs_repack.py`

Leitor e **repacker** do formato XFS2 (Xenesis2) da SoftNyx, em Python puro. Permite editar arquivos dentro de um `.xfs` (ex.: `DataSetup.xfs`) e reempacotar de forma confiável — sem o `iXFS` (que tem bugs no reempacote). Round-trip validado: reconstrói o arquivo com todos os blocos idênticos.

Detalhes do formato em [../docs/guides/config-xfs.md](../docs/guides/config-xfs.md).

### Uso

```bash
# Teste de round-trip (reconstrucao identica) - ajuste o caminho do .xfs no script
python xfs_repack.py roundtrip

# Renomear o servidor (1o WorldServerName no locale.ini)
python xfs_repack.py rename "Meu Servidor" 0 saida.xfs
#   "Meu Servidor" = novo nome | 0 = checksum (o jogo nao valida) | saida.xfs = output
```

> Edite o caminho do `DataSetup.xfs` de origem dentro do script (`src=...`). Faça backup antes de substituir o arquivo do cliente.

## `xfs_read.py`

Lista os arquivos contidos em um `.xfs` (nome, offset, tamanhos).

```bash
python xfs_read.py DataSetup.xfs            # lista
python xfs_read.py DataSetup.xfs locale.ini # tenta extrair (single-block)
```

## Sondas de protocolo (servidor)

Clients headless que falam o protocolo do World direto (conectam, cifram/decifram o AES, validam pacotes) — úteis para testar o servidor sem abrir o jogo.

- **`worldprobe.py`** — login + lobby + inventário + loja: valida que o World responde corretamente (conta carrega do banco, box e ouro batem).
- **`listprobe.py`** — sonda a lista de canais/salas.
- **`difftest.py`** — **teste diferencial**: dirige um World (nativo OU .NET) pela mesma sequência e compara as respostas. `python difftest.py <porta>` (ex.: `40708`).
- **`world_character_probe.py`** — lifecycle, Gift Box, storage e entitlements. Além das ações de
  personagem, aceita compra/venda/move e `buy-bag`, `buy-char-slot`, `buy-potion-slot`,
  `buy-stage-rank-clear` e `buy-stage-level-free` após seleção/entrada automática no inventário.
  `hold` mantém a sessão selecionada aberta para validar timers e invalidações online.
- **`world_channel_probe.py`** — nove sessões autenticadas: entrada incremental `0x1F`, snapshots
  completos de 1 a 9, refresh `0x1E` limitado a 8, owner sentinel `100`, nome `channel01`, chat
  `0x22` e saída `0x20` com broadcast ao membro restante, sem transferência `0x28` indevida no
  canal padrão ownerless.
- **`world_room_probe.py`** — duas sessões simultâneas: create competitivo com senha, lista real com
  cursor/direção/filtro e validação de todos os campos da entrada `0x36` (incluindo `1/12` players),
  rejeição de senha com status `3` e join sem o ack `0x26` exclusivo de `mode=0`,
  `0x38` incremental,
  roster completo `0x37`, bloqueio de start por não-host e por falta de ready, broadcast de
  ready/start, entrada dos dois membros no stage por `0x4B`, primeiro payload entregue ao relay,
  início do round via `0x48` e
  relays geral/direcionado `0x4B/0x4C` sem eco ao remetente, além da transferência de host após
  disconnect.
  Requer as contas locais `test` e `test2` (a segunda pode ser uma fixture descartável).
- **`world_room_admin_probe.py`** — extensão administrativa em duas sessões: troca de time,
  quick join por `0x39→0x38/0x37`, lock de slot, regra, transferência explícita de host,
  autorização de kick, retorno da vítima
  à lista e fechamento da sala. Requer a mesma fixture descartável `test2`.
- **`ghidra/TraceWorldRoomEntry.py`** — extrai `0x36/0x38/0x39/0x3B` e seus callees diretos,
  fechando paginação, filtros, entrada direta/rápida e criação de sala.
- **`ghidra/DecompileClientRoomManagement.py`** — extrai o dispatcher S→C e os consumidores
  `0x36..0x43` do `engine.dll`, incluindo a ordem exata da entrada de lista `0x36`.
- **`ghidra/FindWorldFieldMessageConstants.py`** — localiza no código do World os builders
  `0x45/0x46/0x48/0x49/0x4A/0x4F` e as funções que os emitem. `world_combat_probe.py`
  também fecha a saída dos dois jogadores e exige o `0x44` PvP curto `[44 00][reason]`.
- **`ghidra/DecompileWorldMatchLifecycle.py`** — extrai `FUN_004065E0`, engage, ready,
  fim de match e o motor `FUN_00409940` para `C:\temp\world_match_lifecycle.txt`; é a fonte
  reproduzível dos prazos, comparações por modo e motivos entre rounds.
- **`world_combat_probe.py`** — cria uma sala Team Death com duas sessões, valida chat `0x47`,
  convite `0x72` com o blob completo da sala e dois broadcasts `0x4F`: kill especial `+2` e kill
  normal `+1`; o give-up do último membro do time 0 precisa publicar `0x46` e
  `0x4A [1,0,0,1]`, e a saída final exige o `0x44` PvP curto nos dois clientes. O field e o slot
  global alvo são derivados dos acks, sem IDs ou nomes de personagem fixos. Requer a mesma fixture
  descartável `test2`.
- **`world_vote_probe.py`** — cria uma sala Team Death com três sessões, abre `0x5D`, comprova que
  opener/alvo não recebem a abertura, valida status `5` para o alvo e o resultado final `0x5F`
  para os três players. Usa `test`, `test2` e `test3`.
- **`world_field_kick_probe.py`** — em três sessões, abre uma votação e remove o alvo por `0x40`;
  valida cancelamento `0x5F result=1`, saída `0x3A`, eventual `0x4A`, retorno da vítima por
  `0x1F/0x1E/0x36` e uma nova requisição na mesma conexão. Usa `test`, `test2` e `test3`.
- **`ghidra/TraceClientReliableCallers.py` / `TraceRakionReliableImports.py`** — seguem as APIs
  reliable no `engine.dll` e seus imports no `rakion.bin`; identificam tipos cujo bit `0x8000` é
  aplicado em runtime, incluindo `0x8313` como bad ping `[seat][flag]`.
- **`world_objective_probe.py`** — cria partidas Golem e Boss com duas sessões, extrai o `fieldId`
  real da resposta `0x3B`, valida o `0x4A [2,0,0,1]` do `0x4D`, comprova que `0x60` não é
  transmitido e testa um relay posterior. Também abre uma partida Golem separada para validar
  eliminação por `0x4F/0x4A` e mata o líder Boss para exigir a vitória do time oposto. Requer a
  mesma fixture descartável `test2`.
- **`world_gm_operation_probe.py`** — valida o contrato sem payload do `0x64`: substatus diferente
  de `0x34` desconecta com `B9`; membro de sala com IP fora de `[GM] AllowedIPs` recebe `BA`.
  Com `allowed` como segundo argumento, comprova o ramo aceito sem resposta nem desconexão.
- **`world_ch_code_probe.py`** — com `[Client] EnforceMD5=1` e os hashes de fixture do script,
  valida `0x65` fora do field (`BB`), hash correto sem resposta e divergência (`BC`).
- **`world_udp_probe.py`** — valida o handshake original nas duas portas com três sessões no mesmo
  IP, slots/chaves distintos, a ordem real `login→UDP→0x0E→0x14`, `0x0E/0x38` com endpoint observado + anunciado em network byte order,
  rejeição de chave cruzada, migração autenticada com invalidação da rota antiga, relay compatível,
  `0x030A/0x030F/0x0311`,
  reliable `0x0304/0x0305`, address `0x0319` e sync `0x4000/0x830C/0x8313/0x8315` somente ao
  peer do mesmo field. Também cobre os envelopes NPC `0x8307/08/09/0B/10/12`, rejeita snapshot
  truncado e source seat forjado, prova que pares diretos não recebem cópia TCP, e cobre ping e o bootstrap dirigido
  `0x62 targetSeat→senderSeat`. O ID da
  sala vem do ack `0x3B`, sem valor fixo. Com `--expect-no-relay`, prova o modo fiel configurado
  por `RelayCompatibilityEnabled=0`: handshake e roster continuam ativos, mas Port2 não retransmite
  gameplay. Usa `test`, `test2` e `test3`.
- **`world_tunneling_probe.py`** — abre duas sessões, registra UDP somente no host e confirma que
  o roster `0x38` marca o peer sem rota com flag `1`. Na sequência `0x43→0x45→0x4B`, valida
  `0x54`, TunnelAll nos
  sentidos direto→tunnel e tunnel→direto, TunnelOne dirigido e ausência de eco ao sender. Usa
  `test` e `test2`.
- **`world_tail_dispatch_probe.py`** — prova no processo Release que os cases liberados pelo fim do
  catch-all alcançam a tabela final: `0x61/0x77` são silenciosos sem derrubar a sessão, `0x76/0x78`
  respondem, `0x75` rejeita payment type inválido com `E7` sem mutação e `0x79` encerra com razão
  `1`. Usa `test`, `test2` e `test3`.
- **`ghidra/DecompileWorldGameplayTransport.py`** — extrai o dispatcher UDP original e prova que
  o World reconhece somente `0x0201/0202/0401/0402`; os `0x03xx/0x83xx` são do P2P direto e o
  relay correspondente no .NET é uma extensão de compatibilidade configurável.
- **`world_deathmatch_probe.py`** — cria uma sala modo `2` com frag limit válido `13`, inicia duas
  sessões, reporta kills especiais e valida `0x4F` com score individual `0/14` seguido de `0x4A`
  sem incrementar `Wins0/Wins1`. Usa `test` e a fixture descartável `test2`/personagem `9001`.
- **`world_stage_probe.py`** — cria Stage 3 solo com `test`/personagem `1`, inicia e faz clear;
  rejeita `0x53` antes do clear e com stage divergente, confirma o ACK pós-commit, aceita replay
  idêntico sem novo crédito e rejeita replay divergente. Concede `40 EXP`, `83 gold` e rank A;
  use snapshot/restauração ou uma fixture descartável.
- **`extract_stage_catalog.py <LevelData|DataSetup.xfs> [--summary] [--max-stage 48]`** — lê os
  `stage_*.txt`, remove comentários, calcula SHA-256 e extrai mapa, tempo, goal, limites,
  rank/reward, blocos `NpcSpawn` e o grafo de execução. Aceita diretamente a golden source XFS,
  sem exigir uma cópia previamente extraída. Sem `--summary`, produz JSON detalhado;
  `--runtime-output` e `--flow-output` regeneram os catálogos versionados.
- **`extract_cell_catalog.py --data-setup-xfs <DataSetup.xfs>`** — lê a golden source diretamente
  do XFS, cruza `creaturelist`, `items.dat`, `itemId=8000+index` e aliases de `LevelData`, e valida
  o layout `N × 8.118 + N × 4 × 33` de `creatures.dat`. Também aceita os arquivos já extraídos
  como `<creaturelist.txt> <items.dat> [--creatures-data creatures.dat]`. `--summary`,
  `--stage-directory` e `--active-item-ids` auditam o subconjunto SQL. A série `uint32 +0x18C`
  é EXP cumulativa da cell, `uint16 +0x166E` é o ganho de CP por morte de NPC e `uint16 +0x1734`
  é o custo CP de summon por nível. O JSON inclui as 24 séries de 99 valores, offsets
  serializados/runtime, tipos escalares e os dez labels validados por `language.txt`; campos sem
  consumidor comprovado permanecem `unknown_runtime_*`.

`ghidra/DumpCellCpEvidence.py` decompila os exports de CP e o loader de `creatures.dat` no dump
runtime de `entitiesmp.dll`; aceita filtros de nome como argumento. `DumpCpFieldReferences.py`
audita todos os acessos aos campos atual/máximo e a constante de perda na morte.
`extract_script_api_usage.py <Scripts.xfs> [API ...]` varre todas as entradas do arquivo e gera
JSON com arquivo, linha e API usada; sem lista explícita, audita as cinco APIs de CP.
`ghidra/DecompileClientCellRuntime.py` escolhe os alvos pelo programa aberto e extrai bindings Lua,
construtor/serializers de CP ou a sequência de late join. Pode receber o caminho de saída como
primeiro argumento; sem ele, grava `C:\temp\client_cell_runtime_<programa>.txt`.
`ghidra/DecompileClientNpcTargeting.py` extrai imports, strings, team helpers, master/owner,
validadores de inimigo/dano e a máquina de seleção do `CNpcWatcher`; grava por padrão
`C:\temp\client_npc_targeting.txt` ou aceita outro caminho como primeiro argumento.
`ghidra/DecompileClientNpcStats.py` extrai o painel de Cell, IDs de idioma, formatos, loader das
24 séries, accessor de status, dano-base e recompensa CP. Ele seleciona automaticamente os alvos
de `rakion_orig.exe`, `entitiesmp_dump.bin` ou `entitiesmp.dll` e aceita o output como argumento.
`DumpFunctionsByAddress.py`, `DumpCallersByAddress.py` e `DumpNpcSetupReferences.py` são auxiliares
reproduzíveis para seguir consumidores sem depender da interface gráfica do Ghidra. O último
fecha a composição dos três slots e do custo efetivo em `FUN_351DBFF0`.
`TraceRawPointers.py` recupera referências em tabelas de classe ainda não tipadas pelo Ghidra.
`DumpMemoryAround.py` inspeciona bytes e dwords dessas tabelas antes de criar tipos no projeto.
`DumpPointerStringPairs.py` decodifica tabelas repetidas `[nome, id]`, como os event types NPC.
`FindScalarConsumers.py` localiza os handlers que usam IDs numéricos específicos dessas tabelas.
`DumpInstructionsAround.py` preserva a evidência assembly quando o decompilador perde registradores.
`FindInstructionText.py` cobre deslocamentos x86 que não aparecem como operandos escalares no API.

`ghidra/DecompileBuddyServiceContracts.py` extrai os builders e consumers dos contratos Buddy de
amigos, grupos, perfil, presença e tunnel. Ele fecha os layouts de `0x2020/0x2021`,
`0x3000..0x3007`, `0x3100/0x3102/0x3104/0x3110`, `0x3150..0x3157` e `0x3FFF` sem depender da UI
do Ghidra. A saída padrão recomendada é `C:\temp\buddy_service_contracts.txt`:

```powershell
& '<ghidra>\support\analyzeHeadless.bat' '<buddy-project-dir>' buddy `
  -process Buddy2.dll -noanalysis -scriptPath tools\ghidra `
  -postScript DecompileBuddyServiceContracts.py C:\temp\buddy_service_contracts.txt
```

`ghidra/DecompileWorldRoomSynchronization.py` extrai do `worldserv.exe` os builders, mutadores e
handlers `0x3D..0x43` originais de saída, time, lock de slot, kick, regra, fechamento e start. A saída é gravada em
`C:\temp\world_room_synchronization.txt`.

`ghidra/DecompileWorldCombatAuthority.py` extrai a cadeia original de saída, morte, scoring,
anti-cheat e resultado. A saída é gravada em `C:\temp\world_combat_authority.txt`.

`ghidra/FindWorldGamePointConfig.py` localiza e decompila os leitores/escritores dos dez limites
de EXP/gold usados pelo anti-cheat. A saída é gravada em `C:\temp\world_game_point_config.txt`.

`ghidra/DecompileClientGamePoint.py` fecha os builders `SendFieldGamePoint`,
`SendFieldGameStagePoint` e `SendFieldSlotUDP` do `engine.dll`. A saída é
`C:\temp\client_game_point_engine_dll.txt`; para a build atual, `0x50` mede 25 bytes incluindo o
opcode e carrega 23 bytes de payload.

`ghidra/DecompileClientStageRewardProducer.py` extrai de `entitiesmp.dll:0x3515C760` o cálculo
literal de EXP/Gold por rank, o delta contra o melhor rank anterior, `EXP/3` para os três slots de
Cell e o acréscimo de 50% do Power User. A saída é
`C:\temp\client_stage_reward_producer.txt`.

`ghidra/DecompileClientRoomCreate.py` extrai de `rakion.bin` o produtor UI do request `0x3B`,
incluindo os oito pushes de opções, a enumeração de modos e os sete presets de faixa de level. A
saída é `C:\temp\client_room_create.txt`.

`ghidra/TraceWorldUdpHandshakeCallers.py` fixa o envelope removido pelo dispatcher UDP;
`ghidra/DisassembleWorldLoginResponse.py` fixa os offsets de slot/chave no `0x0C`; e
`ghidra/FindWorldUdpSessionKey.py` rastreia `user+0x1464/+0x1468`.

`ghidra/DecompileWorldTcpGameplayFallback.py` extrai os handlers/helpers de tunneling
`0x56/0x57` e ping `0x59/0x5A`, incluindo destino e transformação C→S/S→C.

`ghidra/DecompileWorldGolemBoss.py` extrai `0x4D/0x60`, os offsets de objetivo/target, o byte de
lado perdedor e as transições de round. A saída é `C:\temp\world_golem_boss.txt`.

`ghidra/DecompileClientFieldVote.py`, `DecompileWorldFieldVote.py` e
`DisassembleWorldFieldVote.py` fecham builders, handlers, agregado, retornos em `AL`, apuração e
serializer `0x5F`; `FindWorldFieldVotePenalty.py` prova os dez slots e o bloqueio de join de 30
minutos. Os scripts `FindClientFieldVoteConsumers.py`,
`DecompileClientFieldVoteConsumersNearby.py` e `FindClientFieldVoteConsumerTable.py` apoiam o
rastreamento dos callbacks S→C.

`ghidra/DecompileWorldGmOperation.py` extrai o handler `0x64`, os motivos `B9/BA`, o helper de
endpoint e a tabela inicializada por `inet_addr`; `ghidra/DecompileClientGmOperation.py` extrai o
export sem payload do `engine.dll`. As saídas são `C:\temp\world_gm_operation.txt` e
`C:\temp\client_gm_operation.txt`.

`ghidra/DecompileWorldChCode.py` extrai `FUN_00428430`, os writers do modo no login e os hashes
globais; `ghidra/DecompileClientChCode.py` extrai o builder `0x65`; e
`ghidra/TraceClientChCode.py` localiza o slot `0x150`, IAT e thunks; e
`ghidra/TraceClientWorldNetIntegrity.py` audita consumidores reais de `_pRakionWorldNet` nos slots
`0x144/0x150`. As saídas são `C:\temp\world_ch_code.txt`, `C:\temp\client_ch_code.txt`,
`C:\temp\client_ch_code_refs.txt` e `C:\temp\client_worldnet_integrity_<modulo>.txt`.

`ghidra/DecompileClientWorldEvents.py` extrai `SendEvent1/SendEvent4` do `engine.dll` e procura seus
consumidores em `rakion.bin`. Ele confirma builders vazios `0x66/0x69` e apenas referências de
ABI (vtable, IAT e thunks), sem call site ativo. `world_dormant_event_probe.py` confirma que o
dispatcher World original/reconstruído rejeita ambos com `DISC C9`.

`ghidra/TraceClientEventAssets.py` rastreia strings e até duas camadas de consumidores dos assets
genéricos de evento em cada módulo. A saída é `C:\temp\client_event_assets_<modulo>.txt`.

`ghidra/DecompileWorldAdminBanNotice.py` extrai `FUN_0041F1A0/0041F290`, incluindo os filtros e
respostas de `0x04/0x05`; `ghidra/DecompileClientAdminBanNotice.py` extrai os dois builders do
`engine.dll`. As saídas são `C:\temp\world_admin_ban_notice.txt` e
`C:\temp\client_admin_ban_notice.txt`.

`ghidra/DecompileNyxAutoFetch.py` parte das strings `DoFetch`, `CAutoFetch`, `COMMAND`,
`Replacer.exe` e mensagens de download para extrair o consumidor do manifesto no
`NyxLauncher.exe`. A saída é `C:\temp\nyx_autofetch.txt`.

`extract_nyx_config.py <NyxLauncherEnc.xfs>` localiza e descompacta todos os streams zlib do
container sem modificar o original. Ele expõe tanto `__NyxLauncher.INI` quanto os metadados XFS2.

`new_update_key.ps1` gera o par ECDSA P-256 do atualizador moderno. A chave privada fica somente no
LauncherWeb; a pública acompanha o launcher. `publish_update.ps1` copia um diretório de release para
`ContentRoot/AppId/Version`, recusa reparse points/caminhos reservados e cria `_ready` por último.

`ghidra/DecompileClientFieldExitChat.py` extrai builders/referências de `0x46/0x47` no
`engine.dll` e `rakion.bin`; `ghidra/DecompileWorldFieldExitChat.py` extrai handlers e helpers do
World. `world_combat_probe.py` valida o chat FIELD com sender seat `10` nas duas sessões, além do
scoring já coberto.

`ghidra/DecompileClientFieldInvitation.py` extrai builder e consumidor de `0x72` no `engine.dll`;
`ghidra/DecompileWorldFieldInvitation.py` extrai handler, resolução de field e serializer original.
O mesmo `world_combat_probe.py` valida a entrega direcionada e o blob completo da sala.

`ghidra/DecompileClientForceChangeTeam.py` extrai o builder `0x5B`; o par
`ghidra/DecompileWorldForceChangeTeam.py` fecha handler e helper de mudança de time, incluindo a
saída real `0x3E` e as condições de falha.

`ghidra/DecompileClientActionStreams.py` extrai do `engine.dll` os codecs de ação e o transporte
`0x0304/0x0305/0x0319/0x4000`. A saída é `C:\temp\client_action_streams.txt`.
`ghidra/DecompileClientActionProducer.py` cruza os exports de `entitiesmp.dll`, o dump runtime e
o wrapper de `gamemp.dll` para extrair `ctl_ComposeActionPacket`, `CPlayer::ApplyAction`,
`ActiveActions`, `AliveActions`, `UpdatePlacement` e os acessos aos acumuladores
`+0xAB0/+0xAB4/+0xAB8`. As saídas usam o nome do programa em
`C:\temp\*_action_producer.txt`; o passe fecha `CPlayerAction+0x38/+0x3C/+0x40` como
`pa_aViewRotation`.
`ghidra/DecompileClientCompanionActionStreams.py` cruza `engine.dll`, `rakion_orig.exe`, o dump
runtime e os exports de `entitiesmp.dll` para reproduzir `GetSyncData/ApplySyncData` do `0x030F`
e a união `DoAnimPacket` do `0x0311`, preservando decompile e disassembly nas saídas
`C:\temp\client_companion_action_streams_*.txt`.
`ghidra/DumpEnumValues.py` extrai tabelas `[u32 value, char* name]` por símbolo ou endereço
runtime; ele fecha `PlayerActionState` e `ePlayerAction` no dump de `entitiesmp.dll`.

`ghidra/DecompileClientEntitySync.py` extrai do `engine.dll` criação/estado de Master Golem, NPC e
map items, os envelopes `0x0307/08/09/0B/0C/10/12` e os call refs reliable. A saída é
`C:\temp\client_entity_sync.txt`.

`decode_entity_init_blob.py` decodifica com consumo integral as famílias `base`, `gold_golem` e
`chocolate_cake` do init blob de `0x0307/08/09`. O comando recebe família, `entityType` e bytes
hexadecimais; rejeita truncamento e sobra em vez de aceitar uma família incorreta silenciosamente.

`ghidra/DecompileEngineGolemObjective.py` fecha ownership local/remoto, Gold Golem e late join no
`engine.dll`. `ghidra/DecompileClientGolemObjective.py` e `FindClientGolemEventConsumers.py`
extraem Golden Sword e eventos Gold/Master Golem no dump runtime de `entitiesmp.dll`. As saídas são
`C:\temp\engine_golem_objective.txt`, `client_golem_objective.txt` e
`client_golem_event_consumers.txt`.

`ghidra/DecompileClientReliableTransport.py` extrai bind `2300..2399`, builders direto/relay,
bit reliable `0x8000`, ACK `0x4000`, limite de payload, fila de retransmissão, wrappers
TunnelAll/One e a decisão `IsTunneling_Client`. A saída é
`C:\temp\client_reliable_transport.txt`.

`ghidra/DecompileWorldSlotUdpRelay.py` extrai o handler World `0x62` e os helpers que convertem
`targetSeat` em slot global, enviam `S→C 0x62 [senderSeat]` somente ao alvo e disparam o bootstrap
UDP do cliente. A saída é `C:\temp\world_slot_udp_relay.txt`.

`ghidra/DecompileWorldDbQueues.py` fecha as duas filas do worker DB: `FUN_0041B940` enfileira
requests, `FUN_0041B3F0/FUN_0041AE50` despacham comandos e `FUN_0042BD70/FUN_004295C0` consomem
respostas. A saída `C:\temp\world_db_queues.txt` prova que o comando `0x0C` persiste
`CharacterInfo.exp`, seu ACK interno não tem consumer e o WorldNet `0x58` fica visível ao cliente.
O mesmo script extrai a criação de `LogUserConnect`, o fechamento com motivo e a atualização de
`RealIP` disparada exclusivamente pelo UDP Port1.

`ghidra/DecompileWorldRequestStateGates.py` decompila em lote os handlers C→S que o servidor
`.NET` intercepta antes da tabela principal. A saída `C:\temp\world_request_state_gates.txt`
permite comparar os gates `usergameinfo.id`, `characterinfo.id` e fase `2/3` sem depender de
decompilações antigas ou nomes aproximados.

`ghidra/DecompileEngineBrokerProtocol.py` extrai a superfície completa do Broker no `engine.dll`:
connect, threads TCP, `SendWorldList` (`0x0101`) e `SendDisconnect` (`0x0102`), incluindo callers.
A saída é `C:\temp\engine_broker_protocol.txt`; ela prova que esta build não envia credenciais ao
Broker e que `RequestLogin` do BrokenServer está dormente.

`ghidra/TraceClientGameplayMessageTypes.py` procura referências literais a
`0x030D/0313/0315/0319/830C/8313/8315`. A ausência de referências diretas confirma que esses
tipos são montados dinamicamente; a saída é `C:\temp\client_gameplay_message_types.txt`.

`decode_gameplay_p2p.py` decodifica hex capturado do canal direto/relay, incluindo tipo logical,
bit reliable, sequência, source, ACK `0x4000`, corpos `0x0307..0x0312` e eventos conhecidos de
arma, disparo, shuriken, hold, dano, HP/AP, poção, Golden Sword e Master Golem. Exemplo:

```powershell
python tools\decode_gameplay_p2p.py 0883010000000000010100000000000000000000000000000000000000000000000000
```

`dump_client_module.py` captura a imagem virtual de um módulo já carregado. Use Python 3.12, que
possui Frida nesta máquina. Por padrão a saída preserva o layout de memória, adequado ao
`BinaryLoader` do Ghidra; `--file-layout` só é aceito quando as seções não estão compactadas.
`--imports-output` também grava os imports que o Frida conseguir enumerar.

O executável original pede elevação. Para análise local sem alterar o cliente, copie-o para uma
pasta temporária e injete `native/as_invoker.manifest` nessa **cópia** com `mt.exe`; então informe a
cópia em `--client` e o `Bin` original em `--working-directory`. `native/module_loader.cpp` é apenas
um diagnóstico de `LoadLibrary`: módulos Rakion cujo `DllMain` depende do processo completo podem
falhar com erro `1114`, caso em que a captura deve partir do cliente.

`ghidra/DecompileClientPlayerCombat.py` trabalha sobre o dump runtime de `entitiesmp.dll` e extrai
dano, HP/AP, morte e respawn. `DecompileEngineEntityEvents.py` fecha o serializer genérico
`CEntity::SendEvent`; `DumpEngineCombatTargets.py` resolve no `engine.dll` os imports usados pelo
dump. `DecompileGameRespawnSettings.py` extrai registro e cópia dos defaults de respawn de
`gamemp.dll`; `DecompileSessionPropertyConsumers.py` audita os consumidores correspondentes em
`entitiesmp.dll`.

`ghidra/DecompileClientDeathReport.py` isola em `CPlayer::Death` a conversão dos três campos de
`EPlayerDeath` para `[cause][killerSeat]`, a chamada virtual `SendFieldGameDiePlayer+0x128`, o caso
explícito `NpcGoldGolem` e o `RET 0x14` do evento passado por valor. A saída fica em
`C:\temp\client_death_report.txt`.

`ghidra/DecompileClientWeaponEvents.py` extrai os cinco eventos reliable de arma/hold, seus
construtores, cópias, produtores, dispatcher e consumidores. `ghidra/DumpInstructionRanges.py`
aceita intervalos `inicio:fim` e imprime o assembly usado para confirmar offsets quando um objeto
de evento é passado por valor e o decompiler perde parte dos parâmetros.

`ghidra/DecompileWorldPotionFlow.py` extrai o handler `0x6E` e o helper de validação/consumo do
`worldserv.exe`. `ghidra/DecompileClientPotionEffects.py` extrai `EUsePotion`, os oito senders de
poção e as rotinas de carga/state machine de Chaos em `entitiesmp.dll`. As saídas são
`C:\temp\world_potion_flow.txt` e `C:\temp\client_potion_effects.txt`.

`ghidra/DecompileClientChristmasEvents.py` extrai os dez eventos de Christmas Box/EventItem,
copy constructors, consumidor ativo do player, renderização de mensagem/tempo e defaults de Santa.
A saída `C:\temp\client_christmas_events.txt` fecha tamanhos e offsets sem inferir a configuração
ausente dos stages.

`ghidra/FindBuddySmsFlows.py` extrai montagem, resposta e recebimento P2P do SMS do `Buddy2.dll`.
`ghidra/FindBuddySmsKey.py` localiza todas as instruções que acessam o contexto de cifra de SMS
(`CBuddy2+0x13B18`) e decompila as funções proprietárias. As saídas são
`C:\temp\buddy_sms_flows.txt` e `C:\temp\buddy_sms_key.txt`.

`ghidra/DecompileBuddyClientLifecycle.py` fecha a integração do `rakion.exe` com `Buddy2.dll`:
criação e destruição do host, entrada e saída do World, login, seleção, nickname, rebuild do F9,
callers e vtable de callbacks. Ele demonstra que cada reentrada cria uma nova instância e que
`host+0x24` é o discriminador nativo entre o primeiro `RET_LOGIN` e os refreshes seguintes.

`ghidra/DumpFunctionInstructions.py` exibe o assembly completo das funções que contêm os
endereços informados. Ele complementa `DumpInstructionsAround.py` quando o decompiler perde
parâmetros ou tipos de uma função inteira.
`ghidra/FindScalarConsumers.py` varre operandos escalares por uma constante (por padrão, o event id
`0x01910025` de `EUsePotion`) e decompila as funções proprietárias em
`C:\temp\scalar_consumers.txt`; aceite o valor e o caminho de saída como argumentos opcionais.
Para auditar todos os acessos diretos ao flag original de tunneling:

```powershell
analyzeHeadless.bat <workspace-ghidra> wsSell -process worldserv.exe -noanalysis `
  -scriptPath .\tools\ghidra -postScript FindScalarConsumers.py `
  0x1478 C:\temp\world_tunneling_flag_refs.txt
```
`ghidra/FindPotionStateConsumers.py` varre os subobjetos e propriedades internas de Steam/Scouter
em `CPlayer+0x2C40..+0x2C68`, incluindo aplicação, multiplicador e resets, e grava as funções
proprietárias em `C:\temp\potion_state_consumers.txt`.
`ghidra/AuditPotionDurationConsumers.py` cruza esses offsets com os escalares `30000/60000` em
`entitiesmp`, `engine`, `gamemp` e `rakion_orig` para separar gravações de timestamp dos hits
homônimos de UI, conta, keepalive e conversão de tempo.
`ghidra/DecompileClientChaosState.py` reproduz `IncreaseChaosPoint`, `ChaosProc`, `ChangeMode`,
morte e todos os consumidores principais de `CPlayer+0x2AD8`; também resolve os imports do dump e
preserva disassembly para validar multiplicadores e transições.
`ghidra/DumpBasicEffectTypes.py` extrai os 76 pares valor/nome da golden table
`BasicEffectType_values @ 0x3537FC20` para `C:\temp\basic_effect_types.txt`; ela é a fonte canônica
dos códigos compartilhados por poções, Chaos e efeitos de summon/desaparecimento de NPC.
Os antigos offsets `+0x277C/+0x277E/+0x277F/+0x2958` foram removidos porque pertencem a
contadores/timestamp de combate, não a Steam/Horror/Scouter.
`ghidra/FindSymbolCallers.py` aceita um ou mais trechos de nome, lista símbolos correspondentes e
decompila os chamadores diretos; é útil para seguir exports importados da UI até seus builders.
`ghidra/FindAsciiText.py` localiza textos ASCII no módulo e lista as referências reconhecidas pelo
projeto, inclusive quando a string ainda não recebeu um símbolo útil.
`ghidra/FindNpcSetupFieldConsumers.py` decompila somente os chamadores dos accessors de
`NpcSetup` que consomem os offsets informados, evitando falsos positivos de stack; o padrão é
`0x8c`.
`ghidra/DecompileClientEntityInitSerializers.py` decompila os três pares writer/reader de init de
NPC e valida os ponteiros runtime contra os exports tipados de `engine.dll`. A saída em
`C:\temp\client_entity_init_serializers.txt` permite distinguir os operadores de `float32`, `u8`
e `u32` usados na cauda polimórfica.
`ghidra/DumpClientWorldResponseCatalog.py` percorre o switch em
`engine.dll:0x36197320`, resolve os handlers dos 88 cases da fila `IScavengerWorldNet` S→C e grava
`C:\temp\client_world_response_catalog.tsv`. `ghidra/TraceClientWorldResponseDispatcher.py`
prova o único caller `ProcessWorldRecvBuffer @ 0x36197A40` e grava a cadeia em
`C:\temp\client_world_response_dispatcher_refs.txt`.
`extract_world_response_catalog.py` valida a cobertura e o SHA-256 da build antes de gerar
`docs/protocol/world-response-dispatch.md`. Cada row inclui o handler e o slot de callback final;
`0x61` é validado como a ação interna que ecoa o valor ao World.
`ghidra/DecompileClientWorldResponseHandlers.py` preserva os 87 consumidores concretos completos
em `C:\temp\client_world_response_handlers.txt`.
`ghidra/FindWorldSimpleResponseProducers.py` procura candidatos a produtor dos opcodes S→C
`0x5C/0x63/0x67..0x6A`, seguindo até quatro chamadas até os senders World e classificando cada
escalar como literal ou endereçamento. Como literais também podem ser razões de disconnect, a
saída continua sendo inventário de candidatos para revisão, não prova automática de causalidade.
No `0x5C`, o passe fecha a ausência de qualquer literal que alcance um sender. O relatório fica em
`C:\temp\world_simple_response_producers.txt`.
`ghidra/DecompileRakionWorldCallbackTable.py` cruza esses slots com a vtable
`rakion.bin:0x004DDC08` e gera `C:\temp\rakion_world_response_callbacks.tsv/.txt`. O catálogo é
reproduzido em três passes:

```powershell
& '<ghidra>\support\analyzeHeadless.bat' '<engine-project-dir>' engdll `
  -process engine.dll -noanalysis `
  -scriptPath tools\ghidra -postScript DumpClientWorldResponseCatalog.py
& '<ghidra>\support\analyzeHeadless.bat' '<rakion-project-dir>' rbin `
  -process rakion.bin -noanalysis `
  -scriptPath tools\ghidra -postScript DecompileRakionWorldCallbackTable.py
python tools\extract_world_response_catalog.py `
  --dispatch-tsv C:\temp\client_world_response_catalog.tsv `
  --callbacks-tsv C:\temp\rakion_world_response_callbacks.tsv `
  --engine '<cliente>\Bin\engine.dll' `
  --rakion '<cliente>\Bin\rakion.bin' `
  --output docs\protocol\world-response-dispatch.md
```

`ghidra/DecompileClientFieldMessagePump.py` extrai o pump real
`rakion.bin:0x004124A0` e o dispatcher externo `0x00411760` para
`C:\temp\client_field_message_pump.txt`. `ghidra/DumpClientFieldMessageCatalog.py` valida os nove
cases delegados a `CSessionState::HandleMessage @ engine.dll:0x3610D7C0`; o segundo passe
`extract_field_message_catalog.py` gera `docs/protocol/field-message-dispatch.md`. Esse catálogo é
do CNet/P2P e o mantém separado da fila interna `FUN_0041B940`, que não produz datagramas:

```powershell
& '<ghidra>\support\analyzeHeadless.bat' '<engine-project-dir>' engdll `
  -process engine.dll -noanalysis `
  -scriptPath tools\ghidra -postScript DumpClientFieldMessageCatalog.py
python tools\extract_field_message_catalog.py `
  --catalog-tsv C:\temp\client_field_message_catalog.tsv `
  --engine '<cliente>\Bin\engine.dll' `
  --rakion '<cliente>\Bin\rakion.bin' `
  --output docs\protocol\field-message-dispatch.md
```

`extract_entity_event_catalog.py` inventaria todos os exports `E*::GetSizeOf` e seus construtores.
`ghidra/DumpClientEntityEventCatalog.py` cruza esse inventário com o dump runtime para recuperar
o event ID passado ao construtor-base e o tamanho total; o segundo passe do extrator gera
`docs/protocol/entity-event-catalog.md`. `ghidra/DecompileClientNpcEvents.py` detalha os
construtores da família `0x044D0000..0018` em `C:\temp\client_npc_events.txt`.

`extract_entity_init_serializers.py` cruza os 47 tipos configurados com `Classes.xfs` e os exports
do `entitiesmp.dll` para resolver vtable e o par virtual `GetInitData/ApplyInitData`. Por padrão,
ele mapeia localmente as seções do PE32 na imagem virtual declarada pelo próprio módulo; não exige
cliente em execução nem dump de memória. `--memory-dump` permanece disponível para comparar uma
imagem capturada. A saída TSV agrupa as classes que compartilham o mesmo init blob e marca
separadamente manifest/export ausente; requer `objdump` no PATH.

```powershell
python tools/extract_entity_init_serializers.py `
  --module <cliente>\Bin\entitiesmp.dll `
  --data-setup-xfs <cliente>\DataSetup.xfs `
  --classes-xfs <cliente>\Classes.xfs
```

`extract_potion_catalog.py` cruza `DataSetup.xfs/items.dat` com os Lua de `Scripts.xfs`, lista cada
item/família e extrai as fórmulas executadas de HP/AP/CP, Steam e Chaos. Exemplo:

```powershell
python tools/extract_potion_catalog.py --data-setup <cliente>\DataSetup.xfs `
  --scripts <cliente>\Scripts.xfs --output C:\temp\potion_catalog.json
```

`capture_gameplay_p2p.ps1` usa o PktMon nativo do Windows para capturar as portas anunciadas dos
clientes sem alterar o executável. Deve ser executado em PowerShell administrativo:

```powershell
tools\capture_gameplay_p2p.ps1 Start -Ports 2301,2302
# entrar no stage com dois clientes
tools\capture_gameplay_p2p.ps1 Stop
```

Os artefatos ficam em `C:\temp\rakion-p2p\gameplay-p2p.pcapng` e `.txt`.

`capture_human_match.ps1` correlaciona uma partida completa entre dois clientes gráficos sem
capturar login ou senha. O marcador ativa os hooks já carregados pelo `RakionClientPatch`; o World
registra os frames TCP decifrados somente depois de o personagem entrar em uma sala. O resultado
contém ações locais, ações remotas, datagramas P2P/relay, posições, heading, controles, animações,
eventos de arma, HP/AP, dano, morte e respawn:

```powershell
tools\capture_human_match.ps1 Start -ClientRoots '<cliente-a>','<cliente-b>'
# jogar uma partida Battle completa
tools\capture_human_match.ps1 Stop
```

Cada sessão fica em `C:\temp\openrakion-human-match\<data-hora>`, com `timeline.csv`,
`timeline.jsonl`, `summary.md`, arquivos brutos e `manifest.json` com SHA-256. O `Start` exige que
os processos `rakion.exe` estejam fechados para que os hooks de ação sejam instalados desde o
bootstrap.

## Captura do servidor ORIGINAL (debug de compatibilidade)

Quando o cliente offline diverge do nosso .NET, o jeito mais rápido de descobrir o comportamento correto é **rodar o servidor original** (binários do autor) com captura total e observar o que ele faz. Três scripts PowerShell automatizam o ciclo:

- **`orig_capture.ps1`** — para o stack .NET, sobe o original (`rakion-cap`, imagem `openrakion-server:latest`) e liga **toda a captura**: `general_log` do MariaDB (cada query SQL — de qual tabela/userid/condição o server lê), MITM `41708→40708` (frames W↔C **decifrados** em `C:\temp\mitm.log`) e `docker logs`. `-NoMitm` pula o proxy.
- **`orig_diag.ps1`** — lê o estado do DB do original (char/itens/gold/ranks, com as semânticas de slot anotadas), as **queries capturadas** (general_log) e o stdout do server.
- **`orig_restore.ps1`** — tira o original + MITM e religa o stack .NET.

Apoio: `mitm_cap.py` (proxy AES-128-ECB) e `GameServers_cap.ini` (config do original apontando o World pro MITM).

```powershell
.\orig_capture.ps1     # sobe o original com captura; depois logue o cliente (test/test)
.\orig_diag.ps1        # estado do DB + queries que o server rodou
.\orig_restore.ps1     # volta pro stack .NET
```

> **Lição de ouro:** o `general_log` (`SET GLOBAL general_log=1`) destravou os bugs de inventário/quickslot — mostra de qual tabela e com qual filtro o server lê cada coisa (ex.: o quickslot vem de `useriteminfo` slots 13/14/15 no login → `0x0c@149`). Histórico na memória `cliente-crash-inventario-e-gameguard`.

## Outras (de terceiros, não inclusas)

- **GConfig** (gerador de `config.xfs`) e **Md5Check** — do [RakionLauncher do CarlosX](https://github.com/CarlosX/RakionLauncher) (`compiled/`).
- **iXFS** (editor GUI de XFS) — por *jdastridge*. Útil para inspeção.
