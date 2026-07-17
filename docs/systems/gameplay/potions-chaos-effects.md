# Engenharia reversa de poções, Chaos e efeitos — Rakion v258

## Estado atual

O fluxo de inventário e a autorização de uso estão implementados. O World valida a célula, o item,
a quantidade e a regra por modo, reduz exatamente uma linha de `useriteminfo` e não fabrica um
pacote de efeito. Isso reproduz a divisão do original: o World autoriza/contabiliza; o cliente aplica
o efeito e o distribui pelo evento P2P `EUsePotion`.

A validação headless está concluída. As fórmulas executadas vêm dos Lua originais em `Scripts.xfs`,
enquanto `items.dat` liga cada item à família de script. O dispatcher nativo, os oito efeitos, o
multiplicador de Steam e os resets de Steam/Scouter também foram fechados estaticamente. O relay
possui parser tipado para `EUsePotion` e rejeita tamanho ou kind incompatível com o dispatcher.
Ainda falta captura visual com dois clientes. O passe cruzado dos quatro módulos de gameplay fechou
as durações comerciais como não implementadas nesta build: os timestamps são gravados, mas nunca
lidos para expiração.

## Evidência binária

| Artefato | Rotina | Resultado |
|---|---:|---|
| `worldserv.exe` | `FUN_00428C90` | handler World do opcode `0x6E` |
| `worldserv.exe` | `FUN_0040E5F0` | valida slot/item/contagem, marca uso e decrementa |
| `entitiesmp.dll` | `CPlayer::Use*Potion` | cria e envia `EUsePotion` |
| `entitiesmp.dll` | `CPlayer::Main @ 0x35163420` | handler ativo que despacha `EUsePotion` |
| `entitiesmp.dll` | ramo `0x35164791` | switch nativo dos oito `potionKind` |
| `entitiesmp.dll` | `CPlayer::ReceiveDamage @ 0x35152DA0` | aplica o fator `1,3` do Steam ao atacante |
| `entitiesmp.dll` | `CPlayer::StartRound @ 0x3514A8B0` | limpa Steam e Scouter |
| `entitiesmp.dll` | `CPlayer::Death @ 0x3515E830` | limpa Scouter |
| `entitiesmp.dll` | `CPlayer::IncreaseChaosPoint(short/int)` | carga de Chaos, clamp e sinalização de pronto |
| `entitiesmp.dll` | `CPlayer::IsChargeChaosPoint` | consulta se Chaos ainda pode ser carregado |
| `entitiesmp.dll` | `CPlayer::ChaosProc` | máquina de entrada/saída do modo Chaos |
| `Scripts.xfs` | `Scripts\item\12000.lua` ... `12070.lua` | guards, fórmulas locais e sender P2P |
| `DataSetup.xfs` | `datasetup\items.dat` | item, família, script e descrição comercial |

Scripts reproduzíveis:

- `tools/ghidra/DecompileWorldPotionFlow.py` gera `C:\temp\world_potion_flow.txt`;
- `tools/ghidra/DecompileClientPotionEffects.py` gera `C:\temp\client_potion_effects.txt`.
- `tools/extract_potion_catalog.py` gera o catálogo JSON de itens, aliases e fórmulas;
- `tools/ghidra/FindPotionStateConsumers.py` audita os subobjetos/propriedades de Steam e Scouter
  entre `CPlayer+0x2C40..+0x2C68` e gera `C:\temp\potion_state_consumers.txt`.
- `tools/ghidra/AuditPotionDurationConsumers.py` varre offsets e escalares de duração em
  `entitiesmp`, `engine`, `gamemp` e `rakion_orig`, gerando
  `C:\temp\potion_duration_consumers_<programa>.txt`.
- `tools/ghidra/DecompileClientChaosState.py` extrai a máquina `ChaosProc`, carga, modificadores,
  morte, animação e armas para `C:\temp\client_chaos_state.txt`.
- `tools/ghidra/DumpBasicEffectTypes.py` extrai a enum canônica para
  `C:\temp\basic_effect_types.txt`.

## Contrato World `0x6E`

```text
C -> World: [u16 opcode=0x006E][u8 cell][s16 itemId]
World -> C: nenhum pacote
World -> peers: nenhum pacote
```

`cell` indexa diretamente o array de 19 células do usuário. Equipamento usa `0..12`; poções usam
`13..18`, limitadas por `characterinfo.potionslot` (`3..6`). O `itemId` precisa ser igual ao item
equipado na célula.

O handler original aplica, nesta ordem:

1. exige `InField` e `FieldSecondary`, ou desconecta com `0xD0`;
2. exige status `3`, ou desconecta com `0xD1`;
3. lê `[u8 cell][s16 itemId]`;
4. exige item idêntico ao slot, contagem diferente de zero e uso permitido;
5. decrementa a contagem, marca a célula como usada e atualiza o mapa de contadores;
6. em falha, desconecta com `0xD2`; em sucesso, retorna sem resposta.

Quando `field.Mode == 0`, a marca de uso não bloqueia chamadas seguintes. Quando o modo é diferente
de zero, uma célula já marcada não pode ser usada novamente durante a partida. A marca é reiniciada
na entrada do match.

O broadcast `S -> peers 0x6E` que existia na reconstrução era incorreto e foi removido.

## Evento client/P2P

`EUsePotion` possui event id `0x01910025`, tamanho `0x10` e payload próprio de oito bytes:

```text
[i32 potionKind][i32 argument]
```

Ele é enviado por `CEntity::SendEvent(..., route=1)` e segue dentro do transporte P2P `0x830C` já
documentado em [combat-actions-status.md](combat-actions-status.md). O mapeamento comprovado dos
senders é:

| `potionKind` | Sender exportado |
|---:|---|
| `0` | `UseHPPotion` |
| `1` | `UseSteamPotion` |
| `2` | `UseHorroPotion1` |
| `3` | `UseHorroPotion2` |
| `4` | `UseAPPotion` |
| `5` | `UseScouterPotion` |
| `6` | `UseCPPotion` |
| `7` | `UseChaosPotion(int)` |

Os senders não aplicam a fórmula: eles publicam o efeito depois que o Lua do item altera o jogador
local. Por isso procurar porcentagens dentro de `Use*Potion` produz um falso negativo.

No World reconstruído, `GameplayPeerDatagramCodec.TryParseUsePotion` valida exatamente os oito
bytes, decodifica os dois inteiros e aceita somente kinds `0..7`. Eventos de entidade desconhecidos
continuam compatíveis; somente um `EUsePotion` conhecido e malformado é recusado pelo relay.

## Regras executadas pelos scripts originais

| Script | Guard | Alteração local | Evento |
|---:|---|---|---|
| `12000.lua` | HP não cheio | adiciona `20% de MaxHP` e envia `Send_HP_AP` | HP, kind `0` |
| `12001.lua` | HP não cheio | adiciona `40% de MaxHP` e envia `Send_HP_AP` | HP, kind `0` |
| `12010.lua` | AP não cheio | adiciona `20% de MaxAP` e envia `Send_HP_AP` | AP, kind `4` |
| `12011.lua` | AP não cheio | adiciona `40% de MaxAP` e envia `Send_HP_AP` | AP, kind `4` |
| `12020.lua` | HP acima de `30% de MaxHP` | remove `30% de MaxHP` | Steam, kind `1` |
| `12030.lua` | `IsHoldAttack() == 0` | sem mutação direta no Lua | Horror 1, kind `2` |
| `12040.lua` | `IsHoldAttack() == 0` | sem mutação direta no Lua | Horror 2, kind `3` |
| `12050.lua` | sempre | sem mutação direta no Lua | Scouter, kind `5` |
| `12060.lua` | `IsChargeChaosPoint()` | `UseChaosPotion(2)` | Chaos, kind `7`, argumento `2` |
| `12070.lua` | CP não cheio | adiciona `30% de MaxCP` | CP, kind `6` |

O comentário original de Chaos define argumento `1` como meia célula e `2` como uma célula. Logo,
o item desta build carrega exatamente uma célula. As descrições em `items.dat` declaram ainda:

- Steam: sacrifica 30% de HP, aumenta o ataque em 30% por 30 segundos;
- Horror: derruba inimigos ao redor;
- Scouter: revela a energia do inimigo por um minuto.

As porcentagens de HP/AP/CP, o custo de HP e o argumento de Chaos são regras executáveis e estão
confirmados. O bônus de Steam também é executável; os `30 s/60 s` de Steam/Scouter são apenas
descrição comercial e não devem virar regra autoritativa compatível com a v258.

### Dispatcher e efeitos nativos

`CPlayer::HandleEvent @ 0x35130D30` é um thunk para `CRationalEntity::HandleEvent @
engine.dll:0x36124AE0`, que resolve estado/evento por `CEntity::HandlerForStateAndEvent @
engine.dll:0x3611FFC0`. A tabela de `CPlayer_DLLClass @ 0x3538DA98` leva o estado inicial `1` ao
estado ativo `0x0191002C`; seu handler é `CPlayer::Main @ 0x35163420`. Nele, o índice
`0x01910025 - 0x01910015 = 0x10` da jump table aponta para `0x35164791`, o switch de `EUsePotion`.

O switch comprovado é:

| kind | ramo | efeito nativo | detalhes |
|---:|---:|---:|---|
| `0` | `0x35164885` | `0x53` — `HP Charge Effect` | efeito de HP |
| `1` | `0x351648CA` | `0x55/0x56` — `Steam/Steam2 Charge Effect` | braços esquerdo/direito; ativa Steam e grava timestamp |
| `2` | `0x35164A1F` | `0x54` — `Fear Charge Effect` | `Spine_Middle`, raio `90`, variante `1` |
| `3` | `0x35164A75` | `0x54` — `Fear Charge Effect` | `Spine_Middle`, raio `90`, variante `2` |
| `4` | `0x35164ACB` | `0x57` — `AP Charge Effect` | efeito de AP |
| `5` | `0x35164B10` | `0x58` — `Scouter Effect` | `Spine_Middle`, raio `90` |
| `6` | `0x35164B5B` | `0x59` — `CP Charge Effect` | efeito de CP |
| `7` | `0x35164B9D` | `0x5A` — `Chaos Charge Effect` | raio `90` e `IncreaseChaosPoint(argument)` |

Os nomes não são inferidos: `BasicEffectType_values @ 0x3537FC20` liga literalmente os códigos
`0x53..0x5A` às strings em `0x352B6F98..0x352B7024`. Horror é instantâneo neste fluxo: cria o
`Fear Charge Effect` com raio e variante, sem flag ou timer persistente no `CPlayer`. A descrição
“derruba inimigos ao redor” continua sendo contrato do catálogo; a animação/queda observável exige
o teste gráfico P2P.

### Estado temporário de Steam e Scouter

Os campos são subobjetos de propriedade, não inteiros crus isolados:

| efeito | ativo | timestamp | gravação | consumidores diretos |
|---|---|---|---|---|
| Steam | subobjeto `+0x2C40`, propriedade `+0x2C44` | subobjeto `+0x2C4C`, propriedade `+0x2C50` | handler kind `1` grava `1` e o relógio atual | `ReceiveDamage` e `StartRound` |
| Scouter | subobjeto `+0x2C58`, propriedade `+0x2C5C` | subobjeto `+0x2C64`, propriedade `+0x2C68` | `UseScouterPotion` grava `1` e o relógio atual | `Death` e `StartRound` |

Em `CPlayer::ReceiveDamage`, o alvo do teste de tipo é o atacante. Se a propriedade Steam dele é
`1`, o fator usado nas duas componentes de dano muda de `1.0f` para `1.3f`. Isso confirma que o
bônus é ataque `+30%`, e não vulnerabilidade do usuário da poção.

`CPlayer::StartRound` grava `0` nas propriedades ativas de Steam e Scouter. `CPlayer::Death` grava
`0` somente em Scouter. Nenhuma instrução de `entitiesmp.dll` lê diretamente `+0x2C4C/+0x2C50` ou
`+0x2C64/+0x2C68` depois da gravação. A auditoria adicional cobriu os quatro módulos que compõem o
gameplay carregado:

| módulo | offsets de timestamp | `30000` | `60000` | classificação |
|---|---|---:|---:|---|
| `entitiesmp.dll` runtime | duas gravações, zero leituras | 0 | 0 | estado Steam/Scouter |
| `gamemp.dll` | 0 | 0 | 0 | wrapper sem regra temporal |
| `engine.dll` | hits homônimos em `AccountInfo`/clã | 0 | 1 | conversão hora/minuto, não `CPlayer` |
| `rakion_orig.exe` | hits homônimos em UI/conta | 3 | 2 | keepalive, voto e IPC, não poção |

Logo, o comportamento executável da v258 não possui expiração por relógio: Steam fica ativo até
`StartRound`; Scouter fica ativo até `Death` ou `StartRound`. Os timestamps armazenados são estado
morto nesta build. Implementar 30/60 segundos seria uma correção/feature nova baseada no catálogo,
não reprodução fiel do binário.

A varredura anterior que incluía `CPlayer+0x277C/+0x277E/+0x277F/+0x2958` foi invalidada: esses
offsets pertencem a contadores/timestamp de combate e não participam deste sistema.

## Chaos comprovado

O cliente mantém três propriedades relacionadas a Chaos nos offsets `CPlayer+0x2AD8`, `+0x2AF0`
e `+0x2960`. Os tipos exatos dessas propriedades ainda não foram nomeados, mas as relações são
claras:

- `+0x2AD8 == 0` é pré-condição para carregar Chaos;
- `+0x2AF0` é o valor atual;
- o byte de `+0x2960` funciona como limite/meta;
- `IsChargeChaosPoint` retorna verdadeiro somente enquanto atual é menor que a meta;
- `IncreaseChaosPoint(int)` ignora zero, limita a entrada a `[-12, 12]`, atualiza o valor com clamp
  e emite o evento/som `SoundsSV_UI_Message_chaos_ready` ao alcançar a meta;
- `IncreaseChaosPoint(short)` usa a mesma meta, mas deriva o incremento por outra rotina do engine;
- `ChaosProc` controla a transição do modo e eventos visuais correspondentes.

Isso comprova recurso, carga, clamp, sinal de pronto e state machine no cliente. Não comprova ainda
duração comercial independente nem argumento de itens ausentes; o item `Chaos+1` desta build passa
`2`, isto é, uma célula completa.

### Modificadores do modo

O passe integral encontrou 52 acessos a `CPlayer+0x2AD8` em 16 funções já delimitadas e em métodos
exportados adicionais. Quando o valor decodificado é `1`:

| consumidor | alteração executável |
|---|---|
| `CPlayer::GetHitPower @ 0x351413C0` | ignora o canal pedido e retorna a soma de `+0x2B38/+0x2B44/+0x2B50` |
| `CPlayer::GetMoveSpeed @ 0x35147D30` | multiplica a velocidade-base por `1,1` |
| `CPlayer::ReceiveDamage @ 0x35152DA0` | usa fator de dano recebido `0,5` em vez de `1,0` |
| `SetupDownState/GetDamageAnimName` | seleciona estado e animação próprios de Chaos |
| `CPlayerAnimator::AnimateAttack_Chaos` | executa a animação de ataque específica |
| `CPlayerWeapons::GetDamageMotionType/SetupDamageInfo/UpdateWeaponHit` | troca motion, dados e condições de hit pelo ramo Chaos |
| `SetModelsColor/SetArmor/SetModelsOriginalColor/GetModeName` | alterna apresentação, armadura e nome do modo |

Isso fecha os modificadores centrais sem transportar regra de combate para o World: o original os
aplica no `entitiesmp.dll` host-authoritative e os distribui pelos eventos P2P já tipados.

### Entrada, saída e morte

`CPlayer::ChaosProc(float) @ 0x3515A7F0` roda somente para a entidade local. Os campos comprovados
são `mode +0x2AD8`, pontos `+0x2AF0`, meta `+0x2960`, estado de transição `+0x2B14` e relógios
`+0x2AF8/+0x2B04/+0x2B28`. No modo normal, a máquina segue `0 → 1 → 2`: valida gauge/meta e o
gate local, publica `EChaosGuageFull`, promove o estado e finalmente publica `EChangeMode(1)`.
`CPlayer::ChangeMode @ 0x3515C480` grava o modo, zera pontos/estado auxiliar, registra o relógio de
entrada e recria modelo, armor, som e efeito visual.

No modo Chaos, `ChaosProc` mantém os relógios conforme round/property e, quando os dois predicados
internos de `+0x2B28/+0x2B04` são satisfeitos, publica `EChangeMode(0)`, zera o estado de transição
e limpa ambos os relógios. Os símbolos não preservam nomes para esses dois predicados, então a doc
mantém os offsets em vez de atribuir uma duração inventada. `CPlayer::Death @ 0x3515E830` é o outro
caminho fechado: se `mode == 1`, limpa transição/relógio, constrói `EChangeMode(0)` e chama
`ChangeMode` antes de continuar a morte. Portanto Chaos não sobrevive à morte.

## Catálogo, aliases e persistência

O catálogo possui 84 registros de potion: 42 em `120xx` e espelhos em `122xx`. Os dois grupos ligam
para os mesmos Lua, portanto o efeito local é igual; preço, origem e política comercial continuam
campos separados.

| Faixa | Conteúdo observado |
|---|---|
| `12000..12002`, `12011..12013` e espelhos `122xx` | HP 20%, stacks 10/30/100 |
| `12003..12005`, `12010`, `12014..12015` e espelhos | HP+1 40%, stacks 10/30/100 |
| `12006..12008`, `12016..12018` e espelhos | AP 20%, stacks 10/30/100 |
| `12020..12022`, `12220..12222` | SteamPack 1/10/100 |
| `12030..12032`, `12230..12232` | Fear/Horror 1, stacks 5/30/100 |
| `12041..12043`, `12241..12243` | Fear/Horror 2, stacks 5/30/100 |
| `12050..12052`, `12250..12252` | Scouter 10/30/100 |
| `12060..12062`, `12070..12072` e espelhos | AP+1 40%, stacks 10/30/100 |
| `12080..12082`, `12280..12282` | Chaos+1 10/30/100 |
| `12090..12092`, `12290..12292` | CP 10/30/100 |

`12288` não existe como item em `items.dat` desta build. A ocorrência anterior vinha de uma
constante não relacionada no Broker e foi removida; não há evidência para “All in One”.

Cada unidade é uma linha em `useriteminfo`. O login agrupa por `slot,itemid` e usa `COUNT(*)` como
stack. No uso, `ConsumeFieldPotionAsync` abre transação serializável, bloqueia uma linha compatível
com `FOR UPDATE`, deleta somente essa linha, recalcula o restante e confirma o commit. Item expirado
não pode ser consumido. Falha de persistência é logada e encerra a sessão com `0xD2`.

O espelho em memória mantém contagem disponível e reservas pendentes separadas. Cada autorização
reserva uma unidade antes do I/O; quando o banco confirma, o restante retornado é reconciliado
descontando reservas que ainda aguardam. Isso impede que o primeiro commit de duas utilizações
rápidas restaure temporariamente uma unidade e autorize um terceiro efeito. Em falha, a reserva
pendente é liberada e a sessão encerrada; como o efeito P2P já ocorreu, não há garantia de
exactly-once durante indisponibilidade do banco.

Não foi necessária migração de schema. A regra atual considera `12000..12999` como poção, igual aos
helpers já reconstruídos; refinar essa faixa depende de fechar o catálogo integral do cliente.

## Arquitetura implementada

- `FieldPotionRules`: regra pura e reconciliação de reservas, sem I/O;
- `ClientSession.AuthorizeFieldPotionUse`: espelho em memória do helper original;
- `WorldDatabase.ConsumeFieldPotionAsync`: consumo transacional de uma unidade;
- `Op_FieldUseItem`: tradução do wire e códigos de desconexão;
- `GameplayPeerDatagramCodec`: envelope e payload tipado de `EUsePotion`;
- cliente/P2P: efeito visual e de gameplay legado, sem duplicação no World.

Não existe flag de ativação: o handler é registrado na tabela canônica de opcodes e fica ativo ao
subir o World reconstruído.

## Como compilar e ativar

```powershell
$dotnet = 'C:\Users\joaop\.dotnet\dotnet.exe'
& $dotnet test .\server\RakionServer\tests\RakionServer.World.Tests\RakionServer.World.Tests.csproj
& $dotnet build .\server\RakionServer\src\RakionServer.World\RakionServer.World.csproj -c Release
```

Depois, publique/substitua o World pelo build atual conforme
[tutorial do servidor](../../../server/RakionServer/TUTORIAL.md) e inicie a stack normalmente.
O banco existente já é suficiente. Para validar em jogo:

1. equipe uma poção em uma célula liberada entre `13..18`;
2. entre em uma partida e use uma unidade;
3. confirme no log `0x6e autorizado` e `poção consumida`;
4. confira que uma única linha correspondente desapareceu de `useriteminfo`;
5. reconecte e confirme o contador restante;
6. repita com dois clientes e observe o efeito no sender e no peer.

## Testes cobertos e pendências

Os testes de domínio cobrem célula bloqueada, ID fora da faixa, mismatch do item, stack zero, a
diferença entre mode `0` e mode não zero e a reconciliação de duas reservas concorrentes, commit e
falha. O codec cobre kinds `0..7`, limites inválidos e tamanho exato. O extrator também possui testes
para alias de script, fórmula de restauração e argumento de Chaos. A contagem vigente da suíte deve
ser obtida executando `dotnet test`.

Ainda pendem:

- teste de integração MySQL concorrente e falha injetada durante o consumo;
- captura visual de cada `potionKind` em dois clientes;
- confirmar visualmente a queda das variantes Horror e a HUD revelada por Scouter;
- endurecimento autoritativo opcional, pois o protocolo legado deixa o efeito no cliente/P2P.

O sistema de transporte/autorização e a aplicação nativa dos oito kinds estão reconstruídos. Steam
tem multiplicador e resets comprovados; Horror tem efeito/raio/variantes; Scouter tem estado,
timestamp e resets. O RE temporal está fechado pela ausência de consumidores; o domínio visual só
poderá ser chamado de validado após observação em jogo, sem confundir build verde com resultado
visível.
