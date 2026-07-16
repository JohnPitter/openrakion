# Rakion v258 — inventário, equipamento e storage (RE e auditoria)

## Escopo e conclusão

Este documento cobre os três modelos de item visíveis no cliente:

- **box/storage da conta**: grade de 120 células;
- **equipamento/inventário do personagem**: aparência e slots associados ao personagem;
- **quickslot de poções**: células usadas durante lobby/stage.

Também cobre abertura/saída da UI, compra, venda, movimento, stack, bags, character/potion
slots, sets, serial, duração e repaint.

Conclusão: abertura/fechamento, compra/venda, células físicas, quickslot, stack, repaint,
storage ↔ equipamento e as expansões (`0x32/0x35/0x6F/0x70/0x71`) estão implementados. Os
status e snapshots de compra/venda foram fechados nos dois binários e corrigidos no backend. Todas as
mutações persistentes confirmam sucesso somente após commit. A identidade e o serial local foram
unificados em `useriteminfo`; expiração no login e durante a sessão está implementada e validada
no protocolo headless. A confirmação gráfica da remoção ainda está pendente.

## Modelo original e modelo atual

### Schema original

`useriteminfo` representa itens associados ao personagem:

```text
id, userid, characterid, itemid, item_sn, sn_type,
level, limittime, slot, exp
```

O dump também contém o storage legado `itembox`:

```text
id, userid, itemid, limittime
```

Versões anteriores do `.NET` adicionavam a `itembox`:

```text
qslot TINYINT NOT NULL DEFAULT 0
level TINYINT NOT NULL DEFAULT 0
boxslot SMALLINT NULL
```

Essas colunas são mantidas apenas para compatibilidade de schema e rollback. No boot, uma migração
transacional move as linhas legadas para `useriteminfo` e remove a origem. O modelo canônico atual é:

```text
storage da conta: characterid=0, slot=0..119
equipamento/quickslot: characterid=<char ativo>, slot=0..18
```

### Identidade e ownership

- `useriteminfo.userid` referencia `usergameinfo.id`;
- `useriteminfo.characterid` referencia `characterinfo.id`;
- cash é indexado pelo nome da conta, não pelo id numérico;
- a linha do item (`id`) deve ser a identidade para gear/refino;
- poções iguais podem ser tratadas como fungíveis somente quando a regra de stack permitir.

## Contratos C→S

| Op | Método do cliente | Payload confirmado | Handler/rota atual |
|---:|---|---|---|
| `2C` | `SendInventoryEnter` | handles/contexto da UI | interceptado; ack capturado |
| `2D` | `SendInventoryLeave` | sem campos de negócio | handler canônico; estados `0/1/2` e persistência original fechados |
| `2E` | `SendInventoryBuy` | `[u16 item][u8 currency][u8 useCoupon][u16 couponSlot se 1]` | handler canônico; compra/cupom transacionais |
| `2F` | `SendInventorySell` | `[u8 boxSlot]` | handler canônico; venda transacional |
| `31` | `SendInventoryMove` | `[u8 srcType][u8 src][u8 dstType][u8 dst]` | handler canônico; swap storage/equip/poções transacional |
| `32` | `SendInventoryBuyBag` | `[u8 mode][u16 couponSlot se mode!=0]` | handler canônico; transação implementada |
| `33` | `SendInventoryAllocationPoint` | `[u8 stat]` | implementado, domínio personagem |
| `34` | `SendInventoryBuyPowerUser` | `[u8 mode][u8 couponFlag][u16 couponSlot se flag!=0]` | handler canônico transacional |
| `35` | `SendInventoryBuyCharacterSlot` | `[u8 mode][u16 couponSlot se mode!=0]` | handler canônico; transação implementada |
| `6F` | `SendInventoryBuyPotionSlot` | vazio | handler canônico transacional |
| `70` | `SendInventoryBuyStageRankClear` | vazio | handler canônico transacional |
| `71` | `SendInventoryBuyStageLevelFree` | vazio | handler canônico transacional |
| `73` | `SendInventoryStackPotion` | `[u8 source][u8 destination]` | handler canônico; validação e callbacks originais reconstruídos |
| `74` | EnchantReinforce (alias) | target/catalisador/count/materiais; preview, depois commit `0x28` | duas fases implementadas; sem export homônimo no engine |

`0x33`, `0x34`, `0x35`, `0x6F`, `0x70` e `0x71` serão detalhados também na documentação
de progressão/Power User. Eles aparecem aqui porque nascem na UI de inventário.

## Abertura e saída da UI

Contratos do World original:

| Request | Resposta lógica | Regra comprovada |
|---:|---|---|
| `2C` | `[2c 00][status:u8][sessionRef:u32]` | `0=aberto`; `1/2` preservam a fase e usam referência zero |
| `2D` | `[2d 00][status:u8]` | `0=fechado`, `1=já fechado`, `2=mutação em andamento` |

`user+0x144C` é a máquina de estado: `0=fechado`, `1=aberto`, `2=operação DB pendente`.
`FUN_0040B000` faz `0→1` no `0x2C`. No `0x2D`, `FUN_0040C960` devolve `1` se já fechado e `2`
se ocupado. No estado aberto, coleta diferenças de equipamento/box e atributos, volta a zero e:

- sem diferenças, responde `0x2D status=0` imediatamente;
- com diferenças, enfileira o comando DB interno `0x13`; `FUN_00419730` persiste itens/atributos e
  `FUN_0041CCA0` responde `0x2D status=0` após o worker.

O `0x13` dessa cadeia é comando da fila DB, não resposta WorldNet nem lista para o cliente. As
capturas descriptografadas têm 12 bytes por causa do bloco AES. No `0x2C`, somente
`status + sessionRef:u32` são consumidos; no `0x2D`, a cauda `2C 00 00 + handle` veio da
stack/padding e não integra o contrato. `engine.dll:0x36193680` lê apenas o primeiro byte após
`0x2D`. O `.NET` persiste cada mutação transacionalmente antes do retorno e, ao fechar, produz o
mesmo resultado observável pela única `InventoryUiState`.

`ClientSession.Inventory.cs` sintetiza a referência de abertura usando o session handle do `0x0C`.
Handles capturados de outra sessão não são reutilizáveis.

## Box e repaint

### Estado em memória

O servidor mantém arrays paralelos para até 120 células:

```text
BoxItems, BoxCount, BoxLevel, BoxRowId
```

`BoxRowId` vincula a célula visual à linha exata de `useriteminfo`, necessário para enchant e
remoção precisa. Célula vazia é `itemId=0`; o grid é esparso e uma venda não desloca as demais.

### Forma segura do `0x31`

No cliente atual, o grid é pintado pelo handler `FUN_0047d1d0` do `0x31`. O repaint seguro
observado segue estas regras:

1. origem do frame sintético sempre `type=0` (box);
2. usar uma célula vazia como origem no-op ao pintar quickslot;
3. não pintar item zero como item real;
4. respeitar limite de 120 células do box;
5. pintar quickslot uma única vez no login e usar a primeira abertura como fallback;
6. não repetir a lista inteira durante polling de saída.

O caminho de origem `type=1` escreve em array de widgets sem bounds-check; células 13–15 já
causaram corrupção e crash no draw. Essa é uma restrição concreta do cliente, não uma regra de
negócio do backend.

## Quickslot e stack de poções

O original mantém arrays equivalentes a:

```text
user+0x1e2c = box
user+0x1da4 = quickslot
```

O modelo atual persiste posição em `useriteminfo`:

- `characterid=0`, `slot=0..119`: permanece no storage;
- `characterid=<char>`, `slot=13..18`: quickslot do personagem;
- múltiplas linhas do mesmo `itemId` formam a quantidade do stack.

`LoadQuickslotAsync` agrupa por `itemId`. `SaveInventoryLayoutAsync` bloqueia as linhas da conta e
reatribui `characterid/slot` na mesma transação. Falha restaura o snapshot em memória e não envia
confirmação. Itens não renderizáveis continuam ocupando célula real dentro de `bag*30`, evitando
colisão entre o estado visível e o banco.

O World original classifica poções explicitamente pela faixa `12000..12999` em `FUN_0040CD70`;
portanto essa é a regra compatível da build, não uma heurística local. O catálogo foi auditado como
controle cruzado: os 84 itens `type=12` ficam em `12000..12292`, e todo ID existente dentro de
`12000..12999` tem `type=12`. A faixa permanece centralizada em `StorageEconomyRules.IsPotion`.

## Stack de poções `0x73`

O builder `engine.dll:0x36191B40` envia exatamente `[source:u8][destination:u8]`. O handler
`World:0x00421A50` exige conta, personagem e fase `Status=2`; slots `>=120` causam disconnect
`E0` (origem) ou `E1` (destino). `FUN_0040C140` produz os estados:

| Estado | Significado comprovado |
|---:|---|
| `0` | validação aceita |
| `1` | inventário fechado |
| `2` | outra mutação de inventário em andamento |
| `3` | categorias locais dos dois itens não coincidem |
| `4` | um dos IDs não está na faixa de poções `12000..12999` |

Falhas retornam o frame lógico `[0x73:u16][status:u8]`. O sucesso usa o subtype `0x27`, não um
ack `0x73`: carrega identidades, slots, valores agregados e listas de alterações do inventário.
O callback `rakion.bin:0x004756E0` de `0x27` apenas encerra o estado ocupado; o callback de erro
`0x004782E0` trata os estados e, no caminho zero, aplica a mutação já preparada pelo cliente.
O helper original valida e publica diferenças contra um shadow, mas não altera os arrays do World.

A implementação .NET preserva esse efeito observável: não duplica a mutação no servidor, envia
erros no `0x73` e confirma por `SendMessage(0x27, ...)`. Como o modelo atual não mantém o shadow
original por sessão, a lista de box da confirmação é um snapshot compatível dos slots ocupados,
em vez da otimização por deltas de `FUN_0040BCB0`. O callback desta build ignora essa cauda; ainda
assim, equivalência visual em cliente aberto permanece pendente.

## Compra `0x2E`

Payload interpretado por `FUN_00421210` e pelo handler atual:

```text
[u16 itemId][u8 currency][u8 useCoupon][u16 couponSlot quando useCoupon==1]
currency = 0 cash; qualquer outro valor = gold
```

Regras atuais:

- item precisa existir no cache de `iteminfo`;
- `iteminfo.shop` precisa corresponder à moeda e o preço deve ser positivo;
- `useCoupon=1` resolve a célula do box, valida `11000..11999` e `couponinfo.for_cash`;
- saldo é conferido no backend;
- `ShopBuyInProgress` tenta impedir duplo clique;
- set `type=10` é expandido nas peças;
- item comprado é inserido em `useriteminfo` com `characterid=0`, célula e serial únicos;
- response `0x14` confirma e envia delta;
- response `0x2E` atualiza gold/cash ao vivo;
- frames `0x31` repintam o box.

### Implementação transacional ativa

`WorldDatabase.Storage.cs` executa a compra em uma única transação:

```text
lock cupom/wallet -> validar quote/capacidade -> inserir grants -> ledger -> debitar -> commit
-> atualizar memória -> enviar resposta
```

O serviço serializa mutações pelo `GameInfoId`, bloqueia cupom/carteira com `FOR UPDATE`, valida as
células no banco e insere todas as peças do set, ledger e consumo de cupom no mesmo commit do
débito. O handler antigo que usava `Task.Run` e o interceptor foram removidos; `0x2E` chega pela
entrada canônica `Op_InventoryBuy` e delega a transação a `ClientSession.Shop.cs`.

`FUN_0040CB10` fecha os erros: `1` UI de compra inativa, `2` operação em andamento, `3` saldo/falha
de criação, `4` sem espaço e `0x14/0x15/0x16` para cupom inválido, moeda incompatível e definição
de cupom ausente. O callback Rakion `FUN_00478A70`, resolvido pelo slot `+0x1D8` da vtable, trata
visualmente `1..4`; os três códigos de cupom retornam sem popup nessa build.

## Venda `0x2F`

O request contém o slot do box. O preço reconstruído:

- loja gold: `round(gold * 0.4)`;
- loja cash: `round(cash * 1.5)`, creditado em gold;
- poções e itens fora da loja: zero.

A implementação bloqueia a carteira, remove gear pelo `useriteminfo.id` exato (ou toda a pilha fungível
da poção naquela célula), credita gold e confirma no mesmo commit. Memória, repaint e saldo são
alterados apenas depois do sucesso.

`FUN_0040CD70` retorna `1` quando a UI está inativa, `2` quando já está processando e `3` para
célula vazia; `FUN_004215A0` devolve esses erros como `[u16 0x2F][u8 status]`. O backend antes
respondia incorretamente com opcode `0x2E` em falha e fabricava sucesso para célula vazia; ambos os
caminhos foram corrigidos.

O gate original exige conta, personagem e `Status=2`, com `DISC 39/3A`; não exige os flags locais
`InField/FieldSecondary`. O interceptor que acrescentava essa condição foi removido e `0x2F` agora
chega somente por `Op_InventorySell`.

O sucesso usa o snapshot mínimo `0x15`, com 30 bytes quando não há outros deltas:

```text
[u16 0x15][u16 seq]
[u32 gameInfoId][u32 characterId]
[u16 soldItemId][u32 creditedGold][u8 soldSlot]
[u32 itemRowHandle][u8 level][u32 exp]
[u8 activeDeltaCount=0][u8 boxDeltaCount=0]
```

O row handle vem de `useriteminfo.id`, carregado pelo original no array `user+0x1BC4`; não é
`item_sn`. A resposta local agora preserva handle, nível e EXP obtidos sob lock na mesma transação.
Os dois campos de identidade ecoam `GameInfoId/ActiveCharId`, equivalentes a
`user+0x1460/+0x14A4`, e não booleanos ou IDs de sala sintetizados.
O callback de erro de venda `FUN_00477AC0` foi identificado no slot `+0x1DC` da mesma vtable.

## Equipamento do personagem

`useriteminfo` é carregado em `ClientSession.Items`, e o fluxo storage ↔ equipamento usa a mesma
identidade de linha. O validador original foi recuperado em `FUN_0040a7b0`: exige nível, máscara
`iteminfo.class & (1 << characterClass)` e a faixa de slot definida pelo tipo.

| Tipo de item | Slots ativos permitidos |
|---:|---:|
| `0..6` | slot igual ao tipo |
| `7` | `7..9` |
| `8..10`, `13+` | `10..12` |
| `11` | nenhum; cupom é rejeitado |
| `12` | `13..18` |

Portanto, `type=1` no `0x31` é a zona ativa inteira de 19 slots, não apenas o quickslot de poção.
`EquipmentRules` é agora a fonte única dessa validação. O `0x0C` voltou a projetar os sete slots de
gear e seus níveis, mas somente para itens compatíveis; isso elimina o crash anterior causado por
arma de outra classe. Um probe controlado projetou `item 3001` no slot 0 como `B9 0B` e a fixture
incompatível permaneceu zerada.

No wire, o helper original testa `type==0` para box e trata qualquer valor não zero como zona
ativa; o builder normal envia `1`. Os limites são `slot<120` para box e `slot<19` para ativo.
`FUN_0040CF10` executa somente swap: origem vazia retorna status `3`, item incompatível com o slot
ativo retorna `4`, inventário fechado retorna `1` e mutação concorrente retorna `2`. Poções iguais
não são fundidas por `0x31`; o merge pertence exclusivamente ao `0x73 StackPotion`.

A resposta lógica possui 21 bytes:

```text
[0x31:u16][status:u8]
[srcType:u8][srcSlot:u8][newSrcItem:u16][srcMeta:u8][srcValue:u32]
[dstType:u8][dstSlot:u8][newDstItem:u16][dstMeta:u8][dstValue:u32]
```

O handler .NET agora está conectado diretamente à tabela canônica. O interceptor e o corpo falso
`RoomSetMode` foram removidos. A persistência transacional continua sendo uma garantia adicional:
em falha, o snapshot é restaurado antes de repintar o estado anterior no cliente.

O RE do DB também fechou a persistência original: equipar atualiza `slot/characterid` na linha
existente; desequipar preserva `id`, `item_sn`, refino e duração ao retornar para `characterid=0`.
O `0x31` valida tipo, classe, nível e slot antes do commit. Itens incompatíveis encontrados no
login são movidos de modo transacional para uma célula livre do storage.

Aplicação/remoção de atributos é client-side nesta build. Depois de escrever os IDs ativos, o
callback `FUN_0047D1D0` copia os dez atributos base com `FUN_00477E70`, recalcula derivados por
classe em `FUN_0047AB30`, atualiza a projeção e chama o modelo 3D. Somar os bônus novamente em
`characterinfo` no backend duplicaria os valores a cada equip/relog. O servidor deve persistir e
validar o loadout; a autoridade de dano continua P2P/client-side, conforme a documentação de combate.

O preview de cada personagem também filtra os sete slots com a classe e o nível daquele próprio
`CharacterInfo`, não com o personagem ativo global. A validação estática fica fechada; ainda falta
executar o teste visual 3D nas cinco classes.

A projeção foi isolada em `CharacterPreviewProjection` e validada para as cinco classes aceitas pelo
create desta build (`0..4`). O catálogo real fornece conjuntos de level 2 nos slots `0..5` com IDs
`1001..1501`, `2001..2501`, `3001..3501`, `4001..4501` e `5001..5501`; o slot 6 compartilhado usa
`6022`, máscara `31`. A matriz comprova filtro por máscara, level, tipo e ausência de contaminação
entre personagens antes de serializar o `0x0C`. Classes `5+` agora são rejeitadas também no load de
fixtures legadas, coerente com o gate `class<5` de `FUN_0041FCD0`.

## Bags e character slots (`0x32/0x35`)

O schema contém:

- `usergameinfo.bag`;
- `usergameinfo.slot`;
- `characterinfo.potionslot`;
- `usergameinfo.stagelevelfree`.

O RE estático e os probes no original fecharam:

- bag: preço base `8000` cash, produto de ledger `10006`, limite `3`;
- character slot: preço base `12000` cash, produto `10007`, limite `6`;
- pagamento `mode=0` usa cash direto; `mode=1` carrega a célula visual do cupom;
- status `0/1/2/3/4` = sucesso/falha/em andamento/limite/saldo insuficiente;
- falhas de cupom preservam `0x14/0x15/0x16`.

`WorldDatabase.Entitlements.cs` bloqueia conta, wallet e cupom, incrementa o entitlement, debita
cash, grava `logbuycashitem`, consome/grava `logcoupon` e concede eventual random present no mesmo
commit. Só depois o serviço atualiza sessão, `0x0C` e callback. O callback confirmado contém
`gold`, `cash`, novo valor, flag/id do cupom e até quatro presentes.

O login agora carrega `usergameinfo.bag/slot`, e `CharList.SlotCount` usa o valor real. A conexão
define `TreatTinyAsBoolean=false`: sem isso o schema legado `TINYINT(1)` convertia `slot=4` em `1`.

### Potion slot (`0x6F`)

O produto é escolhido pelo valor atual: `3→10008` (8.000 cash), `4→10009` (100.000 gold) e
`5→10010` (31.000 cash). A transação bloqueia personagem e wallets, incrementa
`characterinfo.potionslot`, debita a moeda indicada por `iteminfo.shop`, grava
`logbuycashitem`/`loguseritem` e só então publica o novo limite.

As células de poção são `13..18`. Movimento, load, repaint e projeção no `0x0C` aceitam apenas
`13 .. 13+potionslot-1`; os slots `0..12` do mesmo array continuam reservados ao equipamento.

### Stage rank clear (`0x70`)

Por nível, escolhe `10011` (`10..20`, 2.900 cash), `10012` (`21..40`, 6.400) ou `10013`
(`>40`, 9.900). A mesma transação apaga todos os `userstageinfo` do personagem, debita cash, grava
ledger/presente e publica a projeção de ranks zerada. Status `2` indica saldo insuficiente e `3`
personagem abaixo do level 10.

### Stage level free (`0x71`)

O produto `10014` custa 16.500 cash. O marcador persistido usa exatamente a unidade do original:
`(TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW())`. Uma nova compra só é permitida quando
`agora > stagelevelfree + 1440`; dentro das 24 horas retorna status `3`, e saldo insuficiente
retorna `2`. Marcador, débito, ledger e eventual presente compartilham a mesma transação.

O callback de sucesso é `[0x71][status][gold][cash][minuteMarker][presentCount][presentIds...]`.
No login, `stagelevelfree` é carregado como inteiro de 64 bits no banco e projetado como `uint` na
sessão. O schema legado `TINYINT(1)` foi migrado porque truncava o timestamp real.

## Sets, enchant, duração e serial

### Sets

O servidor interpreta item `type=10` como bundle e usa campos do catálogo para expandir membros.
`UnpackSetInStorageAsync` remove o set e insere todas as peças em `useriteminfo` dentro de uma
transação idempotente. Todas as peças reservam células dentro da capacidade atual da bolsa; falta
de espaço reverte a operação inteira.

### Enchant

`0x74` possui implementação server-authoritative e configuração própria. O aprofundamento fica
em [`../economy/enchant-reinforce.md`](../economy/enchant-reinforce.md). Para este domínio, a
exigência é que consumo de materiais e update
do alvo compartilhem a mesma transação e usem ids exatos de linha.

### Duração

O original usa minutos absolutos e remove no login com a fronteira estrita
`limittime>0 AND limittime<agora`; `limittime==agora` ainda é válido. O `.NET` replica esse delete
antes de carregar qualquer personagem/storage, e os loaders mantêm o mesmo filtro como fallback
se a limpeza falhar. Durante a sessão, o World verifica contas conectadas a cada 15 segundos,
serializa a conta com as demais mutações de economia, remove linhas vencidas e recarrega storage,
equipamento e quickslot. Deltas do box usam `0x31`; slots ativos usam os callbacks de add/move já
consumidos pelo cliente. A prova headless confirmou o `0x31` zerando a célula expirada. Essa
varredura online é uma extensão operacional da reconstrução; no original, a evidência fechada é a
limpeza no login.

O clear online de slot ativo foi extraído para `InventoryExpirationFrames`. Gear (`0..6`) e
quickslot (`13..18`) usam descritores `type=1` dentro do range de 19 células. Havendo célula vazia,
o destino é o storage `type=0`; com box cheio, o frame faz clear in-place no mesmo slot ativo, sem
índice sintético fora do array. Goldens cobrem gear, quickslot e o fallback de box cheio.

### Serial

O fluxo original `FUN_00419A40` usa o `mysql_insert_id` do ledger como `item_sn` e grava
`sn_type=1` para Gold ou `2` para Cash. A reconstrução usa os mesmos namespaces. Itens de fontes
sem ledger de compra usam o namespace local `3`, com `8000000 + useriteminfo.id`; o índice único é
composto por `(sn_type,item_sn)`. Não existe emissor externo necessário para a compra `0x2E`.

## Auditoria de arquitetura e qualidade

| Área | Estado |
|---|---|
| contratos de abertura/saída | comprovados por captura |
| repaint de box/quickslot | funcional com restrições conhecidas |
| compra/venda | transacional; sucesso/erro validados via protocolo e banco |
| quickslot | layout transacional e persistência após relog validados |
| sets | unpack transacional |
| equipamento | regra, projeção e mutação persistente validadas com equip/relog/desequip |
| bag/character slot | transacionais, com cash/cupom, limites, ledger e callbacks validados |
| potion slot | transacional; moedas/produtos, limite e células `13..18` validados |
| stage rank clear | transacional; faixas, preços, delete, ledger e callback validados |
| stage level free | transacional; preço, cooldown, ledger e callback validados |
| duração/serial | namespaces `1=Gold`, `2=Cash`, `3=local` e expiração login/online fechados; visual pendente |
| ownership por row id | fechado para storage, equipamento, venda, cupom, presente e enchant |

Compra/venda foram extraídas para `ClientSession.Shop.cs`, `WorldServer.Storage.cs` e
`WorldDatabase.Storage.cs`. `ClientSession.Inventory.cs` ainda excede o alvo de 400 linhas e
mistura repaint, Power User, stack e level-up; essa divisão continua necessária.

## Arquitetura de implementação

Slices recomendados:

```text
Inventory/OpenClose
Inventory/Move
Inventory/StackPotion
Storage/List
Storage/Buy
Storage/Sell
Equipment/Equip
Equipment/Unequip
Entitlement/BuyBag
Entitlement/BuyCharacterSlot
Entitlement/BuyPotionSlot
```

Componentes compartilhados:

- `InventoryCatalog`: tipo, classe, preço, slots e regras de stack;
- `InventoryTransactionService`: saldo + itens + logs em um commit;
- `InventoryState`: snapshot de box, equipment e quickslot;
- codecs de request/response sem regra de negócio;
- `InventoryProjection`: frames/deltas e repaint após commit.

Não manter `BoxItems`, `BoxCount`, `BoxLevel` e `BoxRowId` como listas independentes. Um DTO por
célula reduz inconsistências e permite validar row id, item, quantidade, nível e duração juntos.

## Ativação e rollback

Compra, venda, movimento, equipamento e entitlements transacionais estão ativos diretamente nas
rotas canônicas. `useriteminfo` é a fonte única; `itembox` fica vazio após a migração de boot. Não
há feature flag que mantenha o caminho legado de persistência acessível.

Ordem:

1. preservar a abertura/saída, compra/venda e layout já validados;
2. preservar `0x32/0x35/0x6F/0x70/0x71`;
3. preservar as regras de equipamento por classe;
4. habilitar preview ampliado somente após teste visual de todas as classes.

Rollback de codec/handler pode usar flag. Operações já commitadas não devem ser revertidas por
troca de flag; compensação de economia precisa de log e ferramenta administrativa explícita.

## Testes obrigatórios

- golden `0x2C`, estados `0/1/2` do `0x2D`, `0x2E`, `0x2F`, `0x31` e `0x73`;
- compra gold/cash, saldo insuficiente, set e duplo clique concorrente;
- falha injetada em cada statement da compra/venda;
- duas sessões na mesma conta;
- cópias iguais com níveis/durações diferentes;
- move e stack com slots inválidos/ocupados;
- relog após quickslot, compra, venda, equip e enchant;
- expiração durante login e durante sessão;
- todas as classes com equipamento compatível/incompatível;
- teste visual de box, shop, quickslot, Previous e preview 3D.

### Evidência executada em 2026-07-14

- 239/239 testes .NET aprovados; build sem warnings;
- migração de boot moveu 17 linhas de `itembox` para `useriteminfo`; origem terminou vazia;
- 23 linhas canônicas terminaram com 23 seriais distintos e índice único ativo;
- pilha `12000` moveu storage→quickslot, persistiu após relog e retornou ao storage em grupo;
- compra gold de `1001`: débito `2700` e insert na célula 1 no mesmo resultado; fixture restaurada;
- compra cash de `1009`: débito `4800` e insert na célula 1 no mesmo resultado; fixture restaurada;
- saldo zero: status `3`, sem débito e sem insert;
- venda de `1001`: remoção da linha exata e crédito `1080` no mesmo resultado; fixture restaurada.
- bag cash: `bag 1→2`, cash `39350→31350`, produto `10006` e callback exato;
- character slot cash: `slot 4→5`, cash `31350→19350`, produto `10007` e callback exato;
- limite `slot=6`: status `3`, sem débito/log; cash `7999`: status `4`, sem mutação;
- bag com cupom 50%: custo `4000`, consumo da célula, `logcoupon(item_id=10006,
  discount_amount=4000)`, vínculo no ledger e callback com cupom `11000`; fixture restaurada.
- potion slots `3→4→5→6`: débitos `8000 cash`, `100000 gold`, `31000 cash`, dois ledgers,
  callbacks e limite `status=3` confirmados; saldo insuficiente retornou `status=4`;
- célula 16 rejeitada com `potionslot=3`, aceita e persistida com `potionslot=4`, depois restaurada.
- rank-clear level 40: cinco ranks apagados, cash `39350→32950`, produto `10012` e callback exato;
  saldo insuficiente preservou rank com status `2`, level 9 preservou com status `3`; fixture restaurada.
- stage-level-free: cash `39350→22850`, produto `10014`, marcador em minutos e callback exato;
  repetição retornou `3`, saldo `16499` retornou `2`, ambos sem mutação; fixture restaurada.
- equipamento: 16 casos de tipo/slot/classe/nível aprovados; projeção runtime do item compatível
  `3001` no slot 0 confirmou `B9 0B` no `0x0C`;
- `item 3401`, refino 5, equipou no slot 4, reapareceu no `0x0C` após relog e desequipou preservando
  a mesma linha/serial; item de classe incompatível retornou status 4 sem mutação;
- presente `1001` foi aceito na célula 17: FIFO removido, `accept_time` gravado e linha canônica
  criada com serial único no mesmo commit; fixture restaurada.
- dois itens expirados, um no storage e outro equipado, foram removidos no login antes do `0x0C`;
  a conta retornou a 23 linhas/23 seriais e a fixture não precisou de limpeza compensatória.
- com a sessão já selecionada, um item carregado no box foi marcado como vencido; a varredura
  removeu o row, publicou `0x31` com item/count zerados para a célula 0 e manteve a conexão ativa.

### Evidência adicionada em 2026-07-15

- catálogo MariaDB: 84 itens `type=12`, todos em `12000..12292`; a faixa original
  `12000..12999` não contém item de outro tipo;
- `FUN_0040A810` usa os doubles `0,4` e `1,5` (`3FD999999999999A` e
  `3FF8000000000000`) e retorna zero para `shop` diferente de `1/2`;
- handlers `FUN_00421210`/`FUN_004215A0`, helpers `FUN_0040CB10`/`FUN_0040CD70` e callbacks
  `FUN_00478A70`/`FUN_00477AC0` fecharam os status de compra/venda;
- erro de venda passou a usar `0x2F`, célula vazia passou a status `3` e sucesso passou ao snapshot
  mínimo `0x15`; golden de 30 bytes aprovado;
- callback `0x31` e recalculadores client-side fecharam a aplicação/remoção dos atributos sem
  mutação cumulativa no backend;
- 388/388 testes .NET aprovados, build sem warnings.

### Evidência adicionada em 2026-07-16

- catálogo MariaDB cruzado para as cinco classes: seis peças específicas por classe nos slots
  `0..5` e item compartilhado `6022` no slot 6;
- matriz de projeção cobre compatível, classe incompatível, level insuficiente, não-gear e classe
  fora de `0..4`; cinco frames `0x0C`, um por classe, preservam equipamento/refino do record;
- goldens do clear `0x31` cobrem gear, quickslot e box cheio com descritores válidos;
- smoke MySQL remove, numa única operação por conta, item vencido do storage, gear e quickslot,
  preservando item permanente, futuro e item de outra conta;
- 468/468 testes do World aprovados e build Release sem warnings;
- o limite restante é somente observação gráfica; servidor, persistência, projeção e wire possuem
  evidência automatizada separada.

## Pontos não resolvidos

- confirmação visual da expiração de box/equipamento/quickslot no cliente gráfico;
- confirmação visual de compra, venda, set, não-gear e preview 3D nas cinco classes.

## Fontes locais

- [`../../archive/protocol-world-legacy.md`](../../archive/protocol-world-legacy.md);
- [`../../protocol/world.md`](../../protocol/world.md);
- `server/RakionServer/src/RakionServer.World/Network/ClientSession.Inventory.cs`;
- `server/RakionServer/src/RakionServer.World/Network/InventoryExpirationFrames.cs`;
- `server/RakionServer/src/RakionServer.World/CharSelect/CharacterPreviewProjection.cs`;
- `server/RakionServer/src/RakionServer.World/Network/WorldHandlers.Generated.Shop.cs`;
- `server/RakionServer/src/RakionServer.World/Database/WorldDatabase.cs`;
- `server/RakionServer/src/RakionServer.World/Database/WorldDatabase.Storage.cs`;
- `server/RakionServer/src/RakionServer.World/WorldServer.Storage.cs`;
- `server/RakionServer/src/RakionServer.World/Network/ClientSession.Shop.cs`;
- `server/RakionServer/tests/RakionServer.World.Tests/CharacterPreviewProjectionTests.cs`;
- `server/RakionServer/tests/RakionServer.World.Tests/InventoryExpirationFrameTests.cs`;
- `server/RakionServer/tests/RakionServer.World.Tests/InventoryExpirationDatabaseSmokeTests.cs`;
- `C:\temp\mitm_inv_previous.log`, `C:\temp\mitm_poweruser_potion.log`,
  `C:\temp\shop_capture.log`, `C:\temp\world_shop_request.txt`,
  `C:\temp\world_shop_pricing.txt`, `C:\temp\client_shop_responses.txt`,
  `C:\temp\rakion_inventory_callback_vtable.txt`, `C:\temp\client_equipment_effects.txt` e
  `C:\temp\client_inv*.txt`;
- `/server/DB/rakion_all.sql` da imagem `openrakion-server:latest`.
