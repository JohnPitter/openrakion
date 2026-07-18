# Engenharia reversa de enchant/refino — Rakion v258

## Escopo e veredito

Cobre preview `0x74`, commit `0x28`, arma, catalisadores, joias, nível `+N`, chance,
Power User/evento, consumo, persistência e replay.

**Veredito:** o RE e a implementação headless estão completos. A máquina original de duas fases
está fechada e a reconstrução deixou de aplicar o
refino prematuramente no primeiro `0x74`. O servidor agora escolhe o resultado, fixa a versão da
configuração no preview e, no commit `0x28`, bloqueia as instâncias, atualiza o alvo, consome todos
os insumos e grava `logenchant` em uma transação. A fórmula, os seis buckets de falha e os cinco
coeficientes padrão também foram extraídos do binário. Constantes, arredondamento `float32` e grade
do sorteio foram alinhados ao disassembly x87. A prova MariaDB confirmou commit, replay e a chance
calculada. Resta validação gráfica da animação e do repaint.

## Máquina original comprovada

`DecompileWorldEnchant.py` fechou três funções do `worldserv.exe`:

- `FUN_00421E10`: recebe `0x74`, valida e envia o preview de subtipo `0x28`;
- `FUN_0040C310`: valida itens/caps e calcula um result code `0..6`;
- `FUN_0041DE40`: recebe o commit `0x28`, aplica o result code e responde `0x74`.

Wire:

```text
C -> S 0x74: [u8 target][u8 catalyst][u8 count<4][count*u8 materialSlot]

S -> C preview 0x28:
[u16 seq][u16 0x28][u32 fieldHandle]
[u8 target][u32 targetSerial]
[u8 catalyst][u32 catalystSerial]
[u8 count][3 * (u8 materialSlot + u32 materialSerial)]
[u8 snapshotFlag][u32 secondaryHandle][u8 snapshotCount][snapshot opcional]

C -> S commit 0x28, 8 bytes fixos:
[u8 status][u8 target][u8 catalyst][u8 count]
[u8 material0][u8 material1][u8 material2][u8 clientResult]

S -> C 0x74:
[u8 result][u8 target][u8 catalyst][u8 count][count*u8 materialSlot]
```

O campo `mode` antes documentado no request `0x74` não existe. O export table de `engine.dll` desta
fixture também não contém `SendInventoryEnchantReinforce`; esse nome era um alias semântico do
catálogo antigo. O sender de inventário exportado termina em `SendInventoryStackPotion`.

O original zera o result calculado em `FUN_00421E10` antes do preview e depois aceita
`clientResult` em `FUN_0041DE40`. A reconstrução preserva o wire e as duas fases, mas ignora esse
resultado não confiável: o roll fica no backend e o cliente apenas confirma slots/status.

## Itens, caps e resultados

- alvo: item presente no box, ID `<8000`, nível `0..14`;
- catalisadores observados: `0x32C9..0x32CD`;
- materiais: `0x36B1`, `0x36B2`, `0x36B3`, no máximo três;
- `0x32C9` aceita alvo até `+4`; `0x32CC`, até `+9`; os demais chegam ao limite geral;
- status de validação confirmado: `6` alvo/limite, `7` catalisador/material, `8` cap;
- result original: `0=+1`, `1=inalterado`, `2=-1`, `3=-2`, `4=-3`, `5=destruir`, `6=fallback`.

Nesta build, o pós-processamento sempre neutraliza destroy: em nível `<4`, `result=5` vira `4`
(`-3`); em nível `>=4`, vira `1` (inalterado). A reconstrução aplica a mesma conversão e sorteia
todos os buckets efetivos `0/1/2/3/4/6`; `5` não chega ao wire.

## Fórmula e coeficientes extraídos

`FindEnchantCoefficientWriters.py` localizou o inicializador `FUN_0041E470` e o calculador
`FUN_0040C310`. Para os catalisadores `13001..13005`, os pares `(base, decay)` são:

```text
13001 (0.70, 0.06)   13002 (0.85, 0.06)   13003 (0.90, 0.05)
13004 (0.75, 0.04)   13005 (0.95, 0.03)
```

Com `j1/j2/j3` iguais às quantidades de `0x36B1/0x36B2/0x36B3`, respectivamente:

```text
floor = j3 * 0.05
p0 = (1-floor) * base + floor
para level=1..nivelAtual: p0 = (1-level*decay) * (1-floor) * p0 + floor
failure = 1-p0
p1 = failure * (4 + 0.2*j1 + 1.2*j2) / 15
p2 = failure * (3 + 0.6*j2) / 15
p3 = failure * (2 + 0.4*j2) / 15
p4 = failure * (1 + 0.1*j2) / 15
p5 = failure * (1 - 0.2*j1) / 15
p6 = restante
```

`j1` e `j2` redistribuem as falhas; não aumentam `p0`. `j3` introduz o piso de sucesso. O modo
original é o padrão (`original_outcomes=1`, `config_version=2`); os multiplicadores de evento e PU
continuam aplicados pelo backend. O modo customizado anterior permanece selecionável pelo banco.

`DumpWorldEnchantPrecision.py` confirmou os bits IEEE-754 usados por `FUN_0040C310`, incluindo
`0.05f`, `0.01f`, `0.2f`, `0.6f`, `0.4f`, `0.1f`, `1/15` em float e, especificamente no primeiro
bucket de falha, `1.2` e `1/15` em double. Os seis buckets são armazenados em `float32` antes da
seleção. A reconstrução usa as mesmas precisões e ordem algébrica; os testes fixam os valores
float exatos, não aproximações decimais arredondadas.

O seed original é `rand() * 0x1.0002p-15`, isto é, `sample/32767` para `sample=0..32767`. Portanto
`1.0` é um valor possível e cai no fallback `6`. O backend mantém CSPRNG, mas agora projeta o sample
na mesma grade inclusiva de 32.768 valores; antes usava uma grade de 24 bits em `[0,1)`.

Slots repetidos são rejeitados antes do preview. A identidade usada no banco é o row ID de
`useriteminfo`; o preview publica `item_sn` real, não item ID sintético.

## Transação e auditoria

No preview, `PendingEnchant` fixa:

```text
Selection(row IDs, slots, item IDs, expected level)
ServerResult, NewLevel, Chance, ConfigVersion, Serials, OperationId
```

No commit:

1. a conta é serializada pelo mesmo lock das demais mutações econômicas;
2. alvo, catalisador e materiais são relidos com `FOR UPDATE`;
3. ownership, localização, item, slot, nível esperado e expiração são revalidados;
4. alvo é atualizado e todos os insumos são removidos;
5. `logenchant` registra operação, níveis, rows consumidos, resultado, chance e versão;
6. somente após commit a projeção da sessão é recarregada e o `0x74` é enviado.

`operation_id` é único no banco. Repetição idêntica na mesma sessão por 30 segundos devolve o
mesmo resultado sem executar nova transação. Um duplicate do journal também reconcilia o resultado
já comprometido. O wire não traz id de operação porque o preview pendente pertence à conexão; após
reinício do processo a conexão também deixa de existir e não há commit antigo válido a correlacionar.
Persistir preview entre processos seria uma extensão, não comportamento da build v258.

O roll usa `RandomNumberGenerator`, não `System.Random`. O reload de config cria outro objeto;
operações pendentes mantêm a instância e `config_version` que existiam no preview.

## Evidência executada em 2026-07-15

- Ghidra: 21 funções de preview/commit/regra, inicialização e dependências decompiladas;
- `engine.dll`: nenhum export com `Enchant`; request `0x74` confirmado sem `mode` pelo World;
- golden do preview protege 40 bytes com seriais reais e três descritores materiais;
- 299/299 testes .NET aprovados e build sem warnings;
- probe entrou em sala, enviou `0x74`, recebeu preview `0x28` e enviou commit `0x28` com
  `clientResult=5` falso;
- servidor escolheu `result=3`, persistiu alvo `+4→+2`, removeu exatamente catalisador e joia e
  gravou uma linha `logenchant` com chance `0.3608577`/config `2`;
- replay retornou o mesmo frame `74 00 03 00 01 01 02`; continuou existindo um único ledger;
- a migração registrou `original_outcomes=1` e os cinco pares exatos de base/decay;
- itens e ledger temporários foram removidos após a prova.

## Evidência automatizada em 2026-07-18

`EnchantPersistenceE2ETests` transformou o probe manual em gate obrigatório reproduzível. Contra o
World vivo e MariaDB real, o cliente headless:

1. carregou alvo `1001 +4`, catalisador `13001` e material `14001` por row/célula;
2. criou uma sala, enviou `0x74` e validou os três descritores com `item_sn` no preview `0x28`;
3. confirmou pelo `0x28` com `clientResult=5` deliberadamente falso;
4. recebeu apenas o result autoritativo do backend, confirmou nível/consumo e `logenchant`;
5. repetiu o commit byte a byte e recebeu o mesmo frame sem segundo ledger;
6. reconectou e reencontrou o alvo no novo nível, sem catalisador/material;
7. removeu rows e ledger temporários ao final.

A matriz obrigatória atual ficou em 26/26 E2E no fio e a suíte World em 819/819 testes Release, sem
skip. O padding AES de 12 bytes é aceito pelo harness, mas os 40 bytes úteis do preview e os sete
bytes úteis do resultado continuam protegidos pelos asserts.

## Validações adicionais

- falha injetada entre update, cada delete e journal para provar rollback;
- duas sessões tentando as mesmas instâncias;
- distribuição estatística longa da grade original;
- todos os result codes, inclusive política de destroy;
- animação “Upgrading Now”, repaint e relog no cliente gráfico.

## Critério de conclusão

O núcleo transacional, o wire headless, a fórmula, a precisão dos buckets e a grade do sorteio estão
fechados. O domínio está completo no headless. A confirmação final visível é executar o ciclo
preview/animação/resultado/repaint e relog no cliente real; fault injection e concorrência são gates
adicionais de robustez, não lacunas do contrato reconstruído.
