# Catálogo de evidências do protocolo World v258

## Finalidade

Este documento registra **qual artefato prova cada contrato**. Ele não substitui
[`world.md`](world.md); impede que uma hipótese de decompilação seja promovida a protocolo sem
captura e permite reproduzir a auditoria mesmo quando os logs ficam fora do repositório.

Os logs contêm dados de sessão e não são copiados integralmente para o Git. Os hashes abaixo
identificam exatamente os arquivos usados. Hexadecimais publicados aqui foram limitados aos frames
necessários e não contêm credenciais além da conta local de teste já presente no ambiente legado.

## Artefatos e integridade

Base local: `C:\Users\joaop\Desenvolvimento\Rakion\rakion-work`.

| Artefato | SHA-256 | Prova principal |
|---|---|---|
| `capture_field_entry/mitm_full_113423.log` | `16A3321139846E000FBF8BC90C110BA7C69DFC9B2ADB5CB18D8DA1A2E8ED377E` | login → UDP → lobby → sala → field |
| `capture_field_entry/mitm_move_133859.log` | `33069A4853F691DC603018D81F2C1556BACD999CAA5A5DC72A1F638E4B54E65E` | segunda personagem, clear, resultado e retorno |
| `capture_field_entry/mitm_inv_previous_104157.log` | `BCDDB99EDC3231F9D50D75D0ECB4FE52E3A7A7335D140BCE19ADDFE91D44C561` | inventário, Previous e duas sessões |
| `capture_field_entry/PROTOCOL_field_entry.md` | `44604E622AAB69409AE076B6724904353AA4A7471FD0E5051958881F4A86DAF7` | relatório contemporâneo da captura |
| `ghidra-proj/wsproto.out.txt` | `9523CF5B5C882B0BAA721E61EB4D1DF83BD99882B2C36D6221EED62EBFB3BB0D` | transporte e handlers World |
| `ghidra-proj/wsproto2.out.txt` | `43B1B67A46D30A0B607E2FE7167F0EF7BCE82318B73D92BB05227FFB9BBA42D1` | segundo passe de protocolo |
| `ghidra-proj/handlers.out.txt` | `3784D04C290EA8982F7E879B2D7A05A59F63C96731DF8B4AC15054403AB7EB8A` | jump table e corpos dos handlers |
| `ghidra-proj/cli_world_rx.clean.txt` | `46F1F22563E818D05CFB959C12B79C2F1828D54075C3878F2D976B4DDB2FDC1A` | recepção S→C no cliente |

## Fingerprints dos binários

O protocolo de envio do cliente está em `engine.dll`. A mesma DLL aparece no cliente final,
BinDev2, Bin258 e cliente de tutorial:

| Binário | SHA-256 | Observação |
|---|---|---|
| `rakion-final/Bin/engine.dll` | `83B20D6C32CD66B95C8F8E41AD6DE13A58E8F5F948CD21CBD118D42EF8CF88F2` | fonte dos exports `IScavengerWorldNet::Send*` |
| `rakion-final/Bin/rakion.bin` | `435F50E3FF9F3F140D4C335336B4BA4A758DF823C146210CC8DA90460960FFFF` | executável final com patches locais |
| `RakionWorldServ.ORIG.exe` | `BBB50355A4B0BA366FD3B2A5E85C21F846C0350456DBD3EA2AFE1C6703D770A2` | referência não modificada |
| `ghidra-proj/worldserv.exe` | `A661955168C481D5CF48BA39569180D4C0DE4AEC9EFE7C0B705FE1258E49DE6B` | uma alteração: `0x42D282`, `JNE` → `JMP` |
| World usado no probe/Docker | `1B8B5EB1AF36F414D7B2C4D58196E63C7D6918C403741A5DBA40D5EB9C8EE0E5` | seis bytes alterados em quatro regiões |

O World executado ao vivo difere do original em `0x41F800..0x41F805`, `0x41F8C9` e `0x42D282`.
As três primeiras alterações pulam verificações de hash no login; a última força um ramo de
configuração. Portanto, capturas desse executável comprovam framing e fluxo funcional, mas não
comprovam o comportamento original de rejeição por MD5. Os corpos dos handlers fora dessas regiões
continuam byte a byte iguais ao original.

Execute `tools/verify_re_inputs.ps1` antes de uma nova rodada para impedir análise silenciosa de
outro build.

## Regra de leitura dos logs

As linhas `data=` são o plaintext lógico depois da descriptografia. Para C→W:

```text
[opcode:u16][clientSeq:u16][body]
```

Para os frames lobby W→C o primeiro `u16` é o subtype. O logger exibe o bloco plaintext completo,
incluindo bytes até o múltiplo de 12 exigido pela cifra. A decompilação fornece o comprimento lógico
real. Comparações entre sessões demonstraram que a cauda além desse comprimento varia como lixo de
stack; ela **não** é token e deve ser zero no encoder determinístico.

## Cadeia dourada confirmada

```text
0C login
  -> 0C char/account data
  -> 0D data complementar
  -> 10 challenge GameGuard (assíncrono; não depende do UDP)
UDP 0102/0202
  -> UDP 0201 por porta
0E UDP-success
  -> 0E endpoints
14 selecionar personagem
  -> 14 ack
  -> 1F sessão/personagem
  -> 1E canais/personagens
36 listar/armar game list
  -> 36 lista
3B criar sala
  -> 3B ack
43 iniciar partida
  -> 43 ack
48 iniciar round + 4B adicionar player
  -> 48 tempo restante
4A clear
  -> 4A resultado preliminar
53 resultado do stage
  -> 53 ack
  -> 44 fim/retorno
1E/3A/36
  -> 1F + 1E + 36 lista restaurada
```

O `0x10` foi observado antes do `0x0E` em uma captura e depois dele em outra, devido à corrida entre
o bootstrap TCP e o handshake UDP. Um probe TCP sem tráfego UDP confirmou que ele é emitido após
`0x0C/0x0D` e não pode depender de `0x0E`. Já a ordem `0x1F/0x1E` e a segunda armação de `0x36`
altera a máquina de telas do cliente.

## Validação ao vivo do servidor original — 2026-07-14

Referência executada localmente: imagem Docker
`sha256:99d3424a5ed18cff51a6fec9264b356bf48fb648e249fa59c382dfbf0eedb488`.
A validação foi feita sem MITM, com `tools/listprobe.py` e `tools/worldprobe.py`.

O broker em `40706` respondeu exatamente:

```text
13 00 01 01 01 7f 00 00 01 a2 ec 00 00 d0 07 00 00 f4 01
```

Isso fecha `[size=19][opcode=0101][count=1][127.0.0.1][port=41708 BE]`
`[usedRooms=0][maxRooms=2000][usedUsers=0][maxUsers=500]` para a configuração testada.

O probe de login no World `40708`, sem enviar qualquer datagrama UDP, recebeu 2.422 bytes em três
frames cifrados consecutivos:

| Frame | Tamanho no fio | Plaintext descriptografado/padded | Conclusão |
|---:|---:|---:|---|
| `0C` | 610 | 456 | dados de conta/personagem |
| `0D` | 1.778 | 1.332 | tabela complementar |
| `10` | 34 | 24 | challenge enviado sem `0x0E` |

O general log do MariaDB confirmou que o bootstrap carrega, antes ou durante o login, as tabelas
`AdminInfo`, `StageInfo`, `ClassInfo`, `ClassLevelInfo`, `NPCInfo`, `ItemInfo` e `CouponInfo`. No login,
o original consulta `user`, `UserGameInfo`, `CharacterInfo`, `UserItemInfo`, `UserStageInfo`, cash e
presentes pendentes, além de registrar conexão e saldo. Essa sequência serve como checklist de estado;
não autoriza copiar SQL ou acoplamento de persistência do legado.

### Probes de mutação de personagem

`SendCharacterSelect @ engine.dll:0x36190E20` chama o finalizador com comprimento lógico `6`:
`[u16 0x14][u32 characterId]`. `FUN_0041FEF0` lê somente esse `u32`. Os quatro bytes posteriores
vistos no plaintext padded das capturas não são token e não pertencem ao DTO.

Para delete, `FUN_0047C7A0` no cliente associa `2/3/4/5/6/7/8/9` aos textos de personagem
inexistente, sete dias, autoridade de clã, chave incorreta, e-mail inválido, chave enviada, mestre de
clã e personagem principal. Já o worker `FUN_00412530` só emite `0/1/2/3/5/6/7/9` e um `0x34` de
ownership divergente; `clangrade` é selecionado, mas não lido, e não há emissão de `4/8` nessa
build. O assembly confirma hard-delete abaixo do nível 15, gate de sete dias apenas quando
`used != 0`, validade de chave por uma hora e soft-delete por `auth=10`. O backend implementa esses
branches, normaliza ownership externo para `2` e entrega a chave por pickup `.eml` configurável.

`tools/world_character_probe.py` usa o mesmo AES/framing do cliente. O primeiro `0x12 create`
solicitava slot `0`, já ocupado, e fechou `status=2`. Com slot `1`, o original consultou nome,
executou `INSERT INTO CharacterInfo(name,userid,class,slot,createtime,changetime)` e respondeu
`12 00 00 02 00 00 00 01 01`; nome repetido em slot livre fechou `status=4`. Isso prova que o
terceiro argumento é slot, não variant. Em `0x13 delete`, validou a linha com `deletekey` e executou
três deletes físicos: `CharacterInfo`, `UserItemInfo` e `UserStageInfo`. O container de captura é
descartável e foi recriado após os testes destrutivos.

Em `0x15 buddy`, o request `ProbeBuddy\0` gerou
`UPDATE UserGameInfo SET buddyname='ProbeBuddy' WHERE id=1` e o plaintext de resposta começou por
`15 00 00 50 72 6F 62 65 42 75 64 64 79 00`. Isso fecha opcode, status, string e persistência;
a cauda além do NUL voltou a conter lixo de stack e foi normalizada para zero no builder.
O mesmo probe contra a implementação .NET persistiu `ProbeBuddy`, devolveu
`15 00 00 50 72 6F 62 65 42 75 64 64 79 00` com zeros na cauda e restaurou o valor anterior.

Em `0x1A tutorial`, após preparar uma conta descartável com `tutorial=0`, o original executou
exatamente `UPDATE UserGameInfo SET tutorial=1 WHERE id=1`. O opcode não possui resposta própria:
o único frame observado depois do request foi o `0x10` assíncrono já previsto no bootstrap. Isso
fecha o body vazio, a persistência e a ausência de ack para essa build.
Contra a implementação .NET, o mesmo probe alterou a fixture local de `tutorial=0` para `1`, sem
resposta própria, e o valor inicial foi restaurado após a validação.

O passe reproduzível `tools/ghidra/DecompileCharacterCoreLifecycle.py` consolidou os quatro fluxos.
Os builders `engine.dll:0x36190D20/0x36190DB0/0x36190E70/0x36191090` fecham create, delete,
buddy name e tutorial; os handlers World `FUN_0041FCD0/0041FE10/00420120/00420840` confirmam
gates e comandos DB `0x07/0x08/0x0B/0x0E`. `FUN_0041C3D0` produz create em 3/7 bytes,
`FUN_00427570` produz delete curto no erro e snapshot de clã variável no sucesso, e `FUN_0041CB60`
produz buddy name variável. Os parsers `engine.dll:0x36192E30/0x36192E70/0x36192FB0` e callbacks
`rakion.bin:0x0047C4D0/0x0047C7A0/0x004785B0` fecham o consumo. O comando tutorial não possui
callback no dispatcher DB, coerente com a ausência de ack observada.

Em `0x1B state clear`, uma fixture level 40 com `levelpoint=100` e 55 pontos em stats executou:

```text
characterinfo: levelpoint=117 e dez stats=0
usergameinfo: powerlevelpoint=38
cash: 50000 -> 38000
logcharstateclear: totallevelpoint=155, usedpowerlevelpoint=38, cost=12000
```

Em `0x1C rename`, `JP -> ProbeRename` executou update do personagem, update condicional do char ativo,
cash `38000 -> 35000` e `logchangecharname` com custo 3000. `FUN_00427760` e `FUN_004278d0`
comprovam os callbacks finais. O binário vivo do container realizou o SQL, mas devolveu frames curtos
contendo o command id/buffer interno (`1B 00 11...` e `1C 00 11...`), em conflito com esses callbacks.
A implementação .NET usa os layouts finais decompilados e sua integração confirmou saldo, stats,
power points, nome, char ativo e as duas linhas de log; toda a fixture foi restaurada.

O passe reproduzível `tools/ghidra/DecompileCharacterResetRename.py` fechou também as duas pontas
do wire. Em `engine.dll`, `SendCharacterStateClear@0x361910D0` monta 3/5 bytes totais e
`SendCharacterChangeCharName@0x36191140` monta `strlen(nome)+4/+6`; removido o opcode, os corpos são
`type` com `u16` condicional e `nome\0 + type` com `u16` condicional. Os parsers S→C
`0x36195400/0x36195500` consomem os callbacks variáveis, sem exigir preenchimento lógico. No World,
`FUN_004208E0/FUN_00420A40` exigem somente identidade de conta, usam `FUN_0040BD80` para o cupom e
enfileiram os comandos DB internos `0x10/0x11`; `FUN_00427760/FUN_004278D0` devolvem erro em 3 bytes
ou sucesso com cash, estado/nome e lista variável de presentes. Isso refuta definitivamente a leitura
antiga de `0x1B/0x1C` como joins de field.

O caminho coupon foi sondado com storage `useriteminfo.characterid=0`, slots `0..9` e definições
carregadas no boot. O request usa o slot visual `u16`. Foram observados `0x14` para item fora de
`11000..11999`, `0x15` para `for_cash=0` e `0x16` para definição ausente. Com cupom 50%, reset
level 1 cobrou 3.500 e rename cobrou 1.500, consumiram a linha e gravaram `logcoupon` com produtos
10002/10005. A implementação local repetiu o probe em level 40: 12.000→6.000, produto 10003,
`coupon_log_id` real e commit atômico. As fixtures dos dois bancos foram restauradas.

`FUN_0041d570` e o construtor `FUN_0041e470` fecharam o random present: grade por blocos de 5.000,
roll quadrático módulo 1.000.000, doze thresholds e catálogo de oito grupos de quatro variantes por
classe. O sorteio só roda sem coupon. `FUN_00427760`/`FUN_004278d0` anexam os ids como `u32`; a
publicação paralela usa opcode `0x6A`.

Os callbacks `FUN_0041D4F0`, `FUN_00428AD0` e `FUN_00428C00` fecharam a Gift Box. Probes no original
confirmaram Peek vazio/preenchido, Accept `3` para célula ocupada e Dispose `0`; o Accept `0` foi
validado no `.NET` com remoção FIFO, criação em `useriteminfo` (`characterid=0`, célula e serial
únicos) e `logpresent.accept_time` no mesmo commit.
O Dispose local também removeu a fila e gravou `dispose_time`. As fixtures foram removidas.

`engine.dll` confirmou `0x32/0x35` como `[mode:u8][couponSlot:u16 se mode!=0]`. No World original,
os helpers usam preços `8000/12000`, produtos `10006/10007` e limites `bag=3/slot=6`; os requests
headless chegaram apenas até a quote (`subtypes 0x16/0x18`), pois o cash-server original completa
uma segunda etapa. Os callbacks decompilados fecharam a resposta final: status, gold, cash, novo
valor, flag/id do cupom e presentes.

No `.NET`, probes completos confirmaram cash direto, limite e saldo insuficiente. O caminho de
cupom 50% em bag cobrou `4000`, consumiu a célula, gravou `logcoupon(item_id=10006,
discount_amount=4000)` e vinculou seu id ao `logbuycashitem`; toda a fixture foi restaurada.

Para `0x34`, `FUN_00422B10` confirmou o request `[mode][couponFlag][couponSlot?]` e
`FUN_0040B2C0` os preços `8000/6000` e status `2/3/0x14/0x15/0x16`. `FUN_00415CB0` fechou a
operação DB interna `0x17`: soma 5 pontos e mantém `powertime` como minutos desde `TO_DAYS(0)`.
`FUN_004281B0` adapta o retorno para o frame externo de 18 bytes
`[0x34][status=0][gold:u32][cash:u32][powertime:u32][points:u16][presentCount:u8]`; presentes
opcionais seguem como `u32`. `FUN_00474F50` confirmou que o cliente atualiza os quatro campos e
recalcula a UI de validade. A adaptação provisória por status `2` e push `0x33` foi removida.

Para `0x6F`, o World original escolhe produtos `10008/10009/10010` quando `potionslot=3/4/5` e o
callback `FUN_00428f00` serializa status, gold, cash, novo total e presentes. O catálogo local fecha
`8000 cash`, `100000 gold` e `31000 cash`. O `.NET` percorreu `3→4→5→6`, gravou os ledgers de cada
moeda, retornou `status=3` no limite e `status=4` sem saldo. A célula 16 só persistiu após o quarto
slot; fixture e layout foram restaurados.

Para `0x70`, o handler original escolhe `10011/10012/10013` nos níveis `10..20/21..40/>40`, e o
comando DB executa `DELETE FROM userstageinfo WHERE characterid=%u`. Os preços do catálogo são
`2900/6400/9900 cash`. No `.NET`, o personagem level 40 apagou cinco ranks e passou de
`39350→32950`; sem saldo retornou `2` sem apagar, e level 9 retornou `3`. Ranks, timestamps, saldo e
auto-increment foram restaurados.

Para `0x71`, o assembly do comando DB original fecha o produto `10014`, preço `16500 cash` e o
marcador `((TO_DAYS(NOW())*24+HOUR(NOW()))*60+MINUTE(NOW()))`. A recompra falha com status `3`
quando `agora <= stagelevelfree + 1440`; saldo insuficiente usa status `2`. O worker serializa
status, gold, cash, marcador e presentes. No `.NET`, o probe confirmou `39350→22850`, ledger e
marcador; a repetição e o saldo `16499` não mutaram o banco. A fixture foi restaurada.

## Requests C→W capturados

`len` abaixo é o plaintext mostrado após `data=`, incluindo opcode e sequência.

| Op | len | Body confirmado na captura | Estado |
|---:|---:|---|---|
| `0E` | 12 | `00 00 00 00 00 00 00 00` | fechado para essa build |
| `12` | variável | `[cstr name][class:u8][slot:u8]` | fechado; sucesso/slot ocupado/nome duplicado sondados |
| `14` | 12 padded | `[character:u32]`; quatro bytes posteriores são padding variável | fechado |
| `1B` | 12 padded | corpo lógico `[paymentType:u8][boxSlot:u16 se type!=0]`; 1/3 bytes | cash/coupon e parser fechados |
| `1C` | variável | `[newName\0][paymentType:u8][boxSlot:u16 se type!=0]` | cash/coupon e parser fechados |
| `20` | 12 | corpo lógico vazio; cauda do bloco não é regra | fechado |
| `2C` | 12 | `FF FF FF FF` + handle de sessão `u32` | forma fechada |
| `2D` | 12 padded | corpo lógico vazio; os oito bytes posteriores são padding | fechado pelo builder |
| `32` | 12 | `[mode:u8][couponSlot:u16 se mode=1]` | cash/cupom/limite fechados |
| `35` | 12 | `[mode:u8][couponSlot:u16 se mode=1]` | cash/cupom/limite fechados |
| `36` | 24 padded | dez bytes lógicos: `[max:u8][cursor:u16][direction:u8][5*includeMode:u8][bypassEligibility:u8]` | fechado pelo builder e call site UI |
| `3A` | 12 | corpo lógico vazio | fechado |
| `3B` | 24/36 | nome C + opções de sala; tamanho cresce com o nome | forma e domínios fechados pelo builder, UI e guards do World |
| `43` | 12 padded | corpo lógico vazio; os oito bytes posteriores são padding | fechado pelo builder |
| `48` | 12 padded | corpo lógico vazio; os oito bytes posteriores são padding | fechado pelo builder |
| `4A` | 12 | primeiro byte `2` no clear + estado transitório | forma fechada |
| `4B` | 72 | `[u16 blobLen=65][blob(65)][u8 FF]` após header | forma fechada |
| `53` | 36 | stage/rank/map slots + cinco acumuladores | forma fechada; acumuladores em análise |
| `6F` | 12 | corpo lógico vazio | compra/limite/saldo fechados |
| `70` | 12 | corpo lógico vazio | faixas/delete/saldo fechados |
| `71` | 12 | corpo lógico vazio | preço/cooldown/saldo fechados |
| `6B` | 12 | corpo lógico vazio | fechado |
| `6C` | 12 | `[pendingId:u32][boxSlot:u16]` | fechado |
| `6D` | 12 | `[pendingId:u32]` | fechado |

### Comprimento lógico versus bloco capturado

A captura mostra o plaintext já expandido para o bloco da cifra; portanto seu `len` não define o
fim da mensagem construída pelo cliente. A inspeção direta dos builders do `engine.dll` fecha essa
fronteira:

- `SendInventoryLeave @ 0x36191700`, `SendFieldGameStart @ 0x36192140` e
  `SendFieldGameRoundStart @ 0x361922C0` passam comprimento `2` a `FUN_361905E0`: somente o
  `u16` do opcode é lógico em `0x2D`, `0x43` e `0x48`;
- `SendFieldList @ 0x36191BA0` passa comprimento `0x0C`: opcode mais dez bytes de consulta;
- o call site `rakion.bin:0x00421620` empilha, em ordem, limite `7/10`, cursor, direção, cinco
  seletores de modo e o booleano `fieldFilter == 0`. Isso confirma os nomes usados por
  `RoomListQuery` e elimina a antiga hipótese de handles adicionais.

Os retornos W→C com os mesmos opcodes são contratos independentes. O retorno `0x2D` possui somente
`[opcode:u16][status:u8]`; a referência `0x2C` e o handle vistos depois dele pertencem à área não
lógica do bloco AES. O status `0x48` continua tendo nove bytes lógicos com round e tempo restante.

### Identidades e fase da sessão

Os três offsets antes descritos genericamente como handles de field têm produtores suficientes
para receber nomes finais:

- `DBCommandLogin1 @ FUN_00426B30` lê o primeiro campo de
  `SELECT id,... FROM UserGameInfo WHERE name=...`, rejeita outra sessão com o mesmo valor e o
  passa a `FUN_0040C8F0`; esse setter grava `user+0x1460`. Logo, o campo é `usergameinfo.id`;
- o handler `0x14` encontra o personagem pelo primeiro dword do registro carregado e o passa como
  primeiro argumento a `FUN_0040AC30`, que grava `user+0x14A4`. Logo, o campo é o
  `characterinfo.id` ativo;
- `FUN_0040AF60` grava canal/slot em `+0x148C/+0x148D` e fase `2`; `FUN_0040B7B0` grava
  `fieldId/seat` em `+0x14A0/+0x14A2` e fase `3`. O offset `+0x1440` é uma fase, não um simples
  booleano de conexão.

A implementação usa `GameInfoId`, `ActiveCharId`, `FieldId` e `FieldSeat` como equivalentes. O
personagem `used=1` carregado para montar o preview do login fica em `PreviewCharId`; ele não ocupa
`ActiveCharId` antes do request `0x14`. Os aliases antigos `FieldHandleRaw/FieldSecondaryRaw`, que
confundiam identidades com a sala, foram removidos.

### Builders `0x3B`, `0x41` e `0x53`

`SendFieldCreate @ engine.dll:0x36191D60` confirma três C-strings seguidas por nove bytes:
`map`, `mode`, `rounds`, `duration:u16`, `fragLimit`, `minLevel`, `maxLevel` e `levelRangeCode`.
`SendFieldChangeRule @ 0x36191FE0` usa as mesmas três strings, mas sua cauda é exatamente
`[map:u8][mode:u8][duration:u16][minLevel:u8][maxLevel:u8]`. O call site
`rakion.bin:0x00421F90` distingue criação (`vtable+0xDC`) de alteração (`vtable+0xF4`) e confirma
que ambos vêm do mesmo formulário de sala.

`DecompileClientRoomCreate.py` fecha o produtor em `rakion.bin:0x0044AC70`: o assembly em
`0x0044B868..0x0044B8B6` empilha todos os oito escalares antes da chamada virtual `+0xDC`.
Os modos são `0=Stage`, `1=Golem`, `2=Deathmatch`, `3=Team Death`, `4=Boss`. O último byte
classifica a faixa de level: `1` acompanha `1..99`, `2` acompanha `11..30`, `3` acompanha
`31..99`; `0` cobre `1..10`, `11..99` e as janelas dinâmicas de `+/-5` e `+/-10` níveis.
Os demais escalares não são enums ocultos: `map` é o ID do catálogo (Stage `0..99` ou mapa battle
selecionado), `rounds` é a quantidade `1..21`, `duration` é armazenada em segundos e aceita
`290..1210` no battle, e `fragLimit` é o valor numérico digitado (`13..30` em Deathmatch e
`20..50` em Team Death). `minLevel/maxLevel` são os limites literais exibidos. Assim, não resta
enumeração sem nome no request `0x3B`; os IDs individuais de mapa pertencem aos catálogos de
stage/battle, não ao protocolo.

`SendFieldGameStagePoint @ 0x36192660` fixa o opcode `0x53` e o layout
`[stage:u8][rank:u8][count:u8][count*mapSlot:u16][5*u32]`. O produtor
`entitiesmp.dll:0x3515C760` fecha os cinco escalares como EXP, Gold e EXP das Cells equipadas nos
slots `10`, `11` e `12`, nessa ordem. O World recalcula EXP/Gold pelo `rankvar` e valida cada Cell
como `EXP/3`, com `x+x/2` quando Power User está ativo, antes de persistir o resultado.

“Forma fechada” significa tamanho, ordem bruta e transição confirmados. Não significa que todos os
campos já possuem nome semântico.

### Ciclo de conexão e UDP Port1

`FUN_004107D0` insere
`LogUserConnect(userid,username,serverid,userip,country,connecttime)` e guarda o
`mysql_insert_id` no estado retornado ao World, depois fixado em `user+0x1468`.
`FUN_00425D80` enfileira o comando DB `5` somente após o handshake Port1 aceito, com
`[advertisedIPv4:u32][connectionLogId:u32]`; `FUN_004121E0` consome o pedido com
`UPDATE LogUserConnect SET RealIP=... WHERE id=...`, sem resposta ao cliente.

No encerramento, `FUN_0041EB20` combina o mesmo ID, o motivo e `user+0x1460`; o comando DB `4`
chega a `FUN_00412140`, que grava `disconnecttime=now(), note='<reason>'`. O subtype `4` W→C tem
corpo `[connectionLogId:u32][reason:u16][userGameInfoId:u32]`. O teste golden
`SessionControlFrameTests` cobre a ordem dos bytes, e `ConnectionLogDatabaseSmokeTests` cobre
insert, `RealIP`, país, motivo e horário de desconexão em MariaDB.

## Responses W→C normalizadas

| Frame | Comprimento lógico | Estrutura comprovada | Teste atual |
|---:|---:|---|---|
| `0E` | 15 | status + `ip1:port1` + `ip2:port2`; os nove zeros restantes da captura eram padding AES | `Endpoints_DistinctPorts_MatchOriginalCapture` |
| `10` | 18 | opcode + nonce constante de 16 bytes | `GameGuard_MatchesOriginal` |
| `12` | 3/7 | status; sucesso acrescenta somente `characterId:u32` | `CharacterCreateAck_*` |
| `13` | 3/variável | status; sucesso acrescenta snapshot completo de clã | `CharacterDeleteSuccessCarriesOriginalClanSnapshot` |
| `14` | 3 | opcode + status | `CharacterSelectAck_HasExactLogicalLength` |
| `15` | variável | status + `buddyName\0` | `BuddyNameAck_MatchesLiveProbeWithoutStackGarbage` |
| `1B` | 3/12+ | status; sucesso acrescenta cash, LP, power LP, count e ids `u32` | goldens de reset/coupon/present |
| `1C` | 3/variável | status; sucesso acrescenta cash, nome, count e ids `u32` | golden de rename + integração coupon |
| `1F` | 13+ | status, slot local/global e presença `[nome\0,class,substatus,clanId]` | goldens solo e presença completa |
| `1E` | variável | type/count, nome/senha e registros com slots local/global | goldens solo e dois membros distintos |
| `2C` | 7 | status + referência `u32`; restante do bloco é padding | `InventoryEnterAck_MatchesOriginalCapture` |
| `2D` | 3 | status `0/1/2`; restante do bloco cifrado é padding | `InventoryLeaveResult_UsesLogicalThreeByteFrame` |
| `27` | variável | confirmação de StackPotion: identidades, slots, valores e listas de alterações | `SuccessBody_MatchesReconstructedLayout` |
| `32` | 3/16+ | status; sucesso acrescenta gold, cash, bag, cupom e presentes | goldens + integração MySQL |
| `35` | 3/16+ | status; sucesso acrescenta gold, cash, slots, cupom e presentes | goldens + integração MySQL |
| `36` | 3+ | count + entradas; vazio tem comprimento 3 | `GameListEmpty_RealLen3_ZeroPad` |
| `3B` | 5 | status + seat `u16` | `RoomCreateAck_RealLen5_ZeroPad` |
| `43` | 3 | status | `MatchStartAck_RealLen3_ZeroPad` |
| `44` PvP | 3 | reason | `FieldLifecycleFrameGoldenTests`; `FUN_00407BE0` |
| `44` solo | variável | reason, flag e nome da sala | `MatchEnd_MatchesOriginal` |
| `48` | 9 | round, tempo restante, wins e MVPs | `RemainingTime_RealLen9_ZeroPad`; `FUN_00408440/00409940` |
| `4A` | 6 | tipo de resultado + estado do field | dois goldens clear/death |
| `53` | variável | status, stage, rank, count e map slots | `StageResultAck_UsesCapturedRealLength` |
| `6F` | 3/13+ | status; sucesso acrescenta gold, cash, potion slots e presentes | goldens + integração MySQL |
| `70` | 3/12+ | status; sucesso acrescenta gold, cash e presentes | goldens + integração MySQL |
| `71` | 3/16+ | status; sucesso acrescenta gold, cash, marcador em minutos e presentes | goldens + integração MySQL |
| `73` | 3 | erro de StackPotion `[opcode:u16][status:u8]`; sucesso usa `0x27` | `Error_MatchesOriginalLogicalFrame` |

### Prova local de sala com duas sessões — atualizada em 2026-07-15

`tools/world_room_probe.py` executou duas conexões World simultâneas contra a build Release do
.NET. A sequência corrigida foi: create competitivo `0x3B` com field ID `1`, lista paginada
`0x36 count=1` usando cursor/direção/filtro de modo, senha errada `0x38 status=2`, entrada
incremental `0x38` entregue aos dois membros e snapshot `0x37`
com os dois personagens e 20 slots entregue ao novo membro, start por não-host `0x43 status=1`,
start antes do ready `status=2`, ready `0x3D`, start entregue duas vezes com `status=0` e novo
start autorizado. Em seguida, o host enviou `0x4B` e recebeu `0x48` com round `1` e `435 s`;
quando o segundo membro enviou `0x4B`, ambos receberam o mesmo `0x48`, e o log confirmou dois
players e o round 1 iniciado. Já em partida, `0x4B [len=3][abc]` do seat 0 chegou somente ao
outro membro como `0x4B [sender=0][len=3][abc]`; `0x4C [target=0][len=3][xyz]` do seat 1 chegou
somente ao alvo como `0x4B [sender=1][len=3][xyz]`. Ao fechar a primeira conexão, o membro restante recebeu exatamente
`0x3A [seat=0]` e `0x3C [newMasterSeat=1]`. Nenhum `0x26` foi emitido na entrada competitiva,
em conformidade com `FUN_00423100`; esse ack pertence ao caminho especial `mode=0`.
Esse resultado prova o wire produzido e consumido pelo servidor reconstruído; os códigos de
erro e a interpretação visual dos broadcasts ainda precisam ser comparados com a build gráfica.
Na repetição de 2026-07-15, o MySQL local recusou a credencial configurada; portanto a sonda
comprovou transporte e estado efêmero, mas não repetiu a autenticação/persistência real.

`tools/world_room_admin_probe.py` completou quick join e o recorte administrativo: `0x39`
seguido de `0x38/0x37`, sem o `0x26` exclusivo de `mode=0`,
`0x3E [status=0][seat 1][seat 10]`, `0x42`
lock do seat 2, `0x41` regra entregue às duas sessões, `0x3C host 0→10`, kick não autorizado
sem efeito, `0x40` autorizado publicando `0x3A [seat=0]` e retornando a vítima por
`0x1F/0x1E/0x36`, e `0x3F` fechando a sala com `0x36 count=0`. O domínio principal permaneceu
com 23 itens/23 seriais após a fixture.

Uma extração headless adicional do `worldserv.exe` (`DecompileWorldRoomSynchronization.py`)
fechou os builders originais: `FUN_004091E0` envia `0x3A [seat]` e, se necessário,
`0x3C [newMasterSeat]`; `FUN_004075A0` envia `0x3E [status][oldSeat][newSeat]`;
`FUN_00407910` envia `0x42 [seat][status]`; `FUN_004097C0`, usado pelo kick `0x40`, delega à
mesma saída. `FUN_0041B8B0` localiza a sessão da vítima e `FUN_0040AF60` a devolve ao canal com
`Status=2`; não encerra sua conexão. Portanto `0x40 [seat]` é request C→S, não broadcast S→C.

`TraceWorldRoomEntry.py` extraiu em conjunto `FUN_00422C90`, `FUN_00423100`, `FUN_00423300`,
`FUN_00423580` e seus callees diretos. A extração fechou os dez bytes do request `0x36`, o uso
do zero como cursor/sentinela, os campos persistidos em `user+0x148E..0x1498`, os filtros por
modo, nível e lotação, e a diferença entre entrada direta `mode=0` e roster competitivo.

`tools/world_field_kick_probe.py` fechou também a jornada em partida com três sessões. Ao remover
o alvo de uma votação ativa, host e eleitor receberam, nessa ordem,
`5F 00 00 01 00 00 00 00 01`, `3A 00 01` e `4A 00 01 01 01 00`. A vítima recebeu
`0x1F/0x1E/0x36` e respondeu a um novo `0x36` na mesma conexão. Isso confirma cancelamento com
`result=1`, ausência de penalidade, reavaliação do round e retorno ao lobby.

### Prova local de scoring Team Death — 2026-07-14

`DecompileWorldCombatAuthority.py` fechou a cadeia `0x46/0x4F/0x50`. Em particular,
`FUN_004087D0` confirma que o modo `2` usa score individual, o modo `3` usa scores de time e o
modo `4` encerra quando morre um dos seats líderes. O player-record separa score `+0x12C`,
contadores `+0x12D/+0x12E` e pontos de resultado `+0x130`; o port anterior misturava os três.

`tools/world_combat_probe.py` executou uma sala modo `3` com seats `0` e `10`. O primeiro
`0x4F [cause=8][killer=10]` foi entregue aos dois clientes como
`4F 00 00 08 0A 00 02`; um segundo `0x4F [cause=0][killer=10]` foi aceito sem respawn explícito
e entregue como `4F 00 00 00 0A 00 03`. Isso comprova score `+2/+1`, broadcast idêntico e que a
vítima permanece playing em Team Death. A fixture foi removida e a conta principal permaneceu
com 23 itens e 23 seriais.

Na repetição de 2026-07-15, os dois jogadores enviaram `0x46 [flag=2]`. Ambos receberam primeiro
`46 00 00`, depois `46 00 0A` e exatamente um fim PvP
`44 00 00 00 00 00 00 00 00 00 00 00`. Isso confirma os três bytes lógicos de
`FUN_00407BE0` e o padding AES zerado, sem reutilizar o frame longo do stage solo. A mesma rodada
detectou e corrigiu uma liquidação prematura: `State=1` também representa sala ocupada, portanto
uma sala recém-criada agora nasce `Settled=true`; somente o start autorizado chama `ResetMatch`
e abre um resultado pendente. O log final contém settle apenas após os dois `0x46`, nunca no create.

`FindWorldGamePointConfig.py` localizou a inicialização consumida por `FUN_0041CF80`. Os pares
máximos EXP/gold são: stage `1500/500`, Deathmatch e Boss `115/160`, Team Death `90/70`, Golem
`100/100` durante o match e `80/200` na rodada final. `GamePointRules` substitui o antigo teto
genérico e os seis limites possuem testes de fronteira.

### Prova completa de GamePoint `0x50` — 2026-07-15

`DecompileClientGamePoint.py` resolveu diretamente
`IScavengerWorldNet::SendFieldGamePoint @ engine.dll:0x361925B0`. O builder chama o transporte
com comprimento `0x19`: são dois bytes de opcode `0x50` e 23 bytes de payload, na ordem
`u32 exp`, `u32 gold`, `u8 flag`, três `u32` auxiliares e `u16 resultMarker`. Isso elimina a
antiga colisão inferida pela ordem dos exports; `SendFieldSlotUDP` é `0x62`, não `0x50`.

`DecompileWorldSlotUdpRelay.py` fechou o ciclo de `0x62`. `FUN_0041C2B0` lê
`[targetSeat:u8]`, recupera `fieldId/seat` reais do remetente em `FUN_0040B7D0` e chama
`FUN_00406930`. Este resolve o slot global em `field+0x124+targetSeat*0x14`, envia somente ao alvo
`S→C 0x62 [senderSeat]` por `FUN_0041B8A0` e não faz broadcast. No cliente,
`rakion.bin:0x00473980` responde ao callback enviando um pacote unreliable com seu próprio seat.
O .NET foi corrigido para usar o mesmo alvo e corpo; a regressão de domínio cobre alvo válido e
seat vazio.

No World, `FUN_00424B60` confirma os gates `mode!=0`, `State=2`, `Phase=2`, aplica o bônus de
EXP antes do anti-cheat e só envia `0x51 [level][levelPoint:u16]` quando `FUN_0040D300` retorna
level-up. A mesma função mostra que cada level-up soma 3 level points. `FUN_00405980` fecha o
resultado `1=win`, `2=lose`, `3=draw`: Golem, Team Death e Boss usam seat `<10/>=10` e
`LosingSideWire`; Deathmatch cai em draw nessa rotina; `resultMarker=0` força draw.

O mesmo produtor monta `0x52` em cinco bytes quando o personagem ou uma cell sobe de nível:
`[seat][newLevel][cellLevel0][cellLevel1][cellLevel2]`. No assembly, os dois primeiros níveis de
cell ocupam um `u16` apenas por conveniência de cópia; `engine.dll:0x36194130` lê `param[0..4]`
separadamente e `rakion.bin:0x00478CC0` chama `CPlayer::LevelUp(seat,newLevel)` e atualiza os três
slots locais de cell. `0x52 [seat][score0][score1]` era uma transcrição truncada e foi removida.

O último envio de `FUN_00424B60` passa `0x40` como comprimento para a fila interna
`FUN_0041B940`. O buffer contém o callback type `0x0A`; retirados a sequência interna e o tipo,
restam 60 bytes de corpo:
handles de field, gold concedido, level/EXP, W/L/D, level points e três trios de
handle/level/EXP de cell, terminados por dois zeros. O antigo envio `.NET` como subtype `0x40`
estava incorreto e foi substituído por `GamePointResultFrames`, com golden de 60 bytes.

A persistência de EXP, nível, level point e gold agora usa uma transação InnoDB e o ledger
`game_point_settlement_ledger`, chaveado por `(match_id,round_no,character_id)`. Smoke test em
MySQL real confirmou primeiro commit, replay idêntico, duas chamadas concorrentes com um único
crédito, rejeição de replay divergente e rollback quando o estado em memória diverge do banco.
O ledger separado de W/L/D confirmou também replay e rollback multi-participante.
`FUN_0040B940` usa os três `u32` para dar até 100 pontos aos cell slots `10..12`
(`itemId 8000..8999`) e pode subir o nível da cell. O índice
original é `(itemId-8000)*200+level`; a inspeção do banco local fechou sua fonte como
`npcinfo(npc=itemId-8000,level).exp`, com NPCs `0..33`, níveis até `255` no dump e uso do teto
de nível 99 pelo handler. O World agora carrega essa curva, aplica os três slots em
`useriteminfo` e grava `game_point_cell_settlement_ledger` na mesma transação de EXP/gold.
O smoke MySQL repetiu commit, replay idêntico, replay divergente, concorrência e rollback com
personagem, carteira e cells no mesmo snapshot.

### Prova de FieldExit/FieldChat `0x46/0x47` — 2026-07-15

`DecompileClientFieldExitChat.py` confirmou
`SendFieldGameExit @ engine.dll:0x36192200` como `[u16 0x46][u8 flag]` e
`SendFieldChat @ engine.dll:0x36192250` como `[u16 0x47][cstr text]`. Em `rakion.bin`, o chat
passa por `CAbuseStr_Filter` e chama o export ativamente.

`DecompileWorldFieldExitChat.py` fechou `FUN_00424350/004244F0`. `0x46` resolve sempre o seat do
próprio sender e chama `FUN_00407E00`; não recebe alvo nem aplica dano arbitrário. `0x47` exige os
dois handles de field, `Status=3`, texto menor que 129 bytes e publica pelo canal FIELD
`[u16 0x47][senderSeat][cstr text]` a todos os records ocupados.

O catch-all do lobby estava engolindo `0x47`, e o handler .NET usava canal lobby com seat zero.
Após a correção, `world_combat_probe.py` enviou chat do jogador no seat `10`; as duas sessões
receberam `47 00 0A 68 65 6C 6C 6F 00`. O mesmo probe manteve verdes troca de time e scoring.

### Prova de FieldInvitation `0x72` — 2026-07-15

`DecompileClientFieldInvitation.py` confirmou o builder C→S como `[u16 0x72][u16 valor]` e o
consumidor S→C como convite contendo slot/nome do remetente, referência do field e dados da sala.
O handler `FUN_00428520` prova que o `u16` C→S é o **slot global da sessão alvo**, não o id da
sala. O remetente precisa dos dois handles de field e `Status=3`; o alvo precisa estar associado a
um field.

`FUN_00406A80` fixa o blob S→C:
`[map][mode][field+111][field+112][field+113][maxRounds][duration:u16][name:cstr][description:cstr]`.
O frame completo é
`[0x72:u16][inviterSlot:u16][inviterName:cstr][fieldRef:u16][blob]` e segue pelo canal lobby ao
alvo. A implementação anterior fixava `fieldRef=0`, omitia o blob e era engolida pelo catch-all em
`Status=3`.

Após a correção, `world_combat_probe.py` deriva o slot global alvo do `0x38` incremental e o
field real do ack `0x3B`; numa execução limpa, entregou à segunda sessão:
`72 00 00 00 4A 50 00 01 00 01 03 01 63 00 01 B0 01 52 45 43 6F 6D 62 61 74 00 62 61 74 74 6C 65 00`.
Isso corresponde a inviter slot `0`, nome `JP`, field `1`, mapa `1`, modo `3`, regras `1/99/0`,
um round, 432 segundos, sala `RECombat` e descrição `battle`. Chat e scoring permaneceram verdes.

### Prova de ForceChangeTeam `0x5B` — 2026-07-15

`DecompileClientForceChangeTeam.py` confirmou
`SendFieldForceChangeTeam @ engine.dll:0x36192970` como `[u16 0x5B][u8 targetSeat]`.
`DecompileWorldForceChangeTeam.py` fechou `FUN_00425990`, `FUN_00409080` e `FUN_004075A0`.
Somente uma sessão de categoria especial (`SubStatus=1`) que também seja master
(`seat == field+0x121`) pode agir, com `Status=3`, field fora do
estado `2` e target `< 18`.

O target precisa ter state `1/2`; o helper move seu record para o primeiro seat vazio do bloco
oposto e publica pelo canal FIELD `0x3E [0,oldSeat,newSeat]`. Ready/lock ou ausência de vaga envia
apenas ao target `0x3E [2]`. O handler .NET anterior tratava o target como action id, não movia o
record e broadcastava um `0x5B` sem correspondente no original.

`FieldForceChangeTeamHandlerTests` atravessa agora a entrada canônica `0x5B` na janela real entre
`0x43` e o primeiro `0x4B`: sessões já estão em `Status=3`, enquanto o field ainda está em state
`1`. O teste fixa os dois corpos de resposta, move o target `seat 1→10` e confirma a identidade da
sessão no novo record. Movimento, cópia de score e negação também permanecem cobertos no domínio;
resta somente a apresentação gráfica dessa operação rara.

### Prova local de identidade e relay UDP — 2026-07-14

`TraceWorldUdpHandshakeCallers.py` confirmou que `FUN_00429530` remove sete bytes antes de chamar
`FUN_00425D80/FUN_00425FA0`: no datagrama completo, slot=`@7`, chave=`@9`, porta anunciada=`@17`
e echoData=`@19`, com mínimo de 23 bytes. `DisassembleWorldLoginResponse.py` mostrou que
`FUN_00426B30` gera a chave com `_rand()` e escreve slot/chave no response `0x0C @7/@9`.

`GameplayUdpHandshakeTests` fixa os dois tipos (`0x0201/0x0202`) e o echo de 12 bytes.
`tools/world_udp_probe.py` abriu três sessões em `127.0.0.1`, obteve slots `0/1/2` e três chaves,
autenticou ambas as portas, comprovou que chave de outra sessão não recebe echo e enviou ações e
controle UDP: apenas o peer do mesmo field recebeu os bytes; sender e sessão de outro field não
receberam. A base temporária importada usada nesta rodada possui zero itens e zero seriais na conta
principal; `test2/test3` são fixtures descartáveis removidas após o probe.

`FUN_0040AB90/0040ABE0` fixa no registro original endereço observado em `+0x1450`, endereço
anunciado em `+0x1454`, porta observada em `+0x1458`, porta anunciada em `+0x145A` e uma terceira
porta em `+0x145C`. O `0x0E` publica `[result][observed IP:port][advertised IP:port]`; o roster
escreve `[observed port BE][observed IP][advertised port BE]`. O probe protege esses bytes e o
handshake real `010201000000000400144300007f00000108fdeb54ea1b` confirma IPv4/porta em network byte order.

O passe reproduzível `tools/ghidra/DecompileSuccessUdp.py` fechou também o handshake TCP. O builder
`engine.dll:0x36190C20` monta exatamente `[0x0E:u16][result:u8]`; os sete zeros adicionais vistos
depois do seq nas capturas pertencem ao bloco AES. `FUN_0041FA40` não lê esse byte: exige conta
autenticada sem personagem ativo, chama `FUN_0040ABE0`, zera `user+0x1478` e envia exatamente
`0x0F` bytes lógicos. O parser `engine.dll:0x36192DA0` consome status e dois pares IPv4/porta.
O callback `rakion.bin:0x00477200` persiste ambos os endpoints nos status `0/1/2`; status `3` abre
a mensagem localizada de falha e qualquer outro valor segue para o erro desconhecido.

O passe `tools/ghidra/DecompileInventoryStackPotion.py` fechou o `0x73` nas três pontas. O builder
`engine.dll:0x36191B40` produz `[0x73:u16][source:u8][destination:u8]`. O handler
`World:0x00421A50` aplica gates `DE/DF`, limites de slot `E0/E1` e delega a validação a
`FUN_0040C140`: inventário fechado `1`, mutação ocupada `2`, categoria divergente `3` e item fora
de `12000..12999` `4`. Erros saem como três bytes lógicos no próprio `0x73`.

No sucesso, `FUN_0040BCB0` compara arrays atuais com o shadow da sessão, serializa listas de
alterações e o World publica subtype `0x27` com GameInfoId, slots, valores agregados, ActiveCharId,
deltas e bloco opcional de personagem. `engine.dll:0x361935D0` despacha essa confirmação para
`rakion.bin:0x004756E0`, que apenas limpa o estado pendente. O callback de `0x73` em
`0x004782E0` trata erros e contém a aplicação cliente da operação. Isso também comprova que o
helper original não deve ser reinterpretado como mutação autoritativa dos arrays do servidor.

`DecompileWorldTcpGameplayFallback.py` confirmou que `0x56` (all) e `0x57` (one) produzem a
mesma saída `0x57 [u16 len][blob]`, enquanto `0x59/0x5A` separam slot global e seat local. O mesmo
probe validou all sem eco/cross-field, one somente no alvo, request de ping ao host e response ao
slot alvo. Ele também comprovou que o catch-all do lobby precisava liberar esses opcodes ao
dispatcher durante `Status=InField`.

Em 2026-07-15, o probe foi repetido contra o World Release após endurecer o relay. O `fieldId`
passou a ser lido do ack `0x3B`, eliminando a hipótese antiga de sala `0`; os samples com campo de
origem foram ajustados ao seat real do emissor. Handshake com chave cruzada e `0x8315` com source
seat forjado foram descartados antes do fan-out. O mesmo probe passou a cobrir os envelopes de
entidade `0x8307/08/09/0B/10/12`, totalizando 17 shapes de ação/controle/entidade; todos chegaram
somente ao peer do mesmo field. Um snapshot `0x8312` truncado foi descartado. `0x0E/0x38`,
tunneling e ping continuaram passando com três sessões e dois fields.

### Prova local de Golem/Boss — 2026-07-14

`DecompileWorldGolemBoss.py` fechou `FUN_00424980/00405D70` (`0x4D`) e
`FUN_00425CC0/00405EF0` (`0x60`). O par Golem pertence ao field em `+0x2C4/+0x2C6`; zero no
primeiro valor vence para o time 1, zero no segundo vence para o time 0. `+0x2BF` é o lado
perdedor no wire, e o fim publica `0x4A [2,losingSide,wins0,wins1]`.

No modo Boss, somente os líderes `+0x122/+0x123` podem enviar `0x60`; o helper guarda o valor em
`+0x2C8` ou `+0x2CA` e não envia pacote. Não há cálculo de HP ou decisão de round nesse helper.

`tools/world_objective_probe.py` iniciou duas partidas com seats `0/10`. Um `0x4D [0,75]`
enviado pelo membro não-host gerou `4A 00 02 00 00 01` para ambos. Em Boss, `0x60` não apareceu
em nenhuma sessão e um `0x4B` posterior foi entregue ao peer, provando que o reporte foi consumido
sem desconectar. A sonda passou repetidamente após passar a extrair o `fieldId` real do `0x3B`.

### Prova local dos streams de ação — 2026-07-14

`DecompileClientActionStreams.py` reproduz `CSessionState::GetActionFromMessage @ 0x3610AFE0` no
`engine.dll`. O corpo `0x030A` tem 19 bytes: `u16 deltaMilliseconds`, um byte que compacta seat e
`PlayerActionState`, `u8 ePlayerAction`, três posições `s16`, `s16 angleWord`, `u8 angleByte` e
três componentes `s16` de `pa_aViewRotation`. Com header
`[u16 type][u32 sequence][u8 source]`, o datagrama mede 26 bytes. Capturas pareadas fixam ainda
`0x030F` em 14 bytes e `0x0311` em 10/12 bytes. O passe complementar
`DecompileClientCompanionActionStreams.py` fecha `0x030F` como `sourceEcho` mais os seis bytes
simétricos de `CPlayer::GetSyncData/ApplySyncData`; fecha `0x0311` como `sourceEcho`, kind
`Normal/Attack/Damage` e um ou três argumentos consumidos por `CPlayer::DoAnimPacket`.

`GameplayActionDatagram` protege os shapes e o codec de movimento. `world_udp_probe.py` enviou
as três famílias por um endpoint autenticado: somente o peer do mesmo field recebeu bytes
idênticos; sender e terceiro cliente em outro field não receberam. O teste também preservou o
marcador legado `0x0401` e os contratos TCP de tunnel/ping.

`PacketBufferRecvUpdate @ 0x361001F0` separa ACK de transporte `0x4000`, o par de aplicação
`0x0304/0x0305` e address update `0x0319`. `CEntity::SendEvent @ 0x36128A90` fecha `0x830C` como
`19 + payloadLength`, com source reliable, sender, rota `1/2/3/4/6/7`, dois índices de entidade,
ID e tamanho interno. `GameplayPeerDatagram` rejeita rota ou comprimento divergente; o probe
confirmou relay byte a byte somente ao peer do field. `0x8313` mede 9 B, `0x8315` mede 8 B e o
keepalive `0x030D` continua consumido sem fan-out.

`TraceClientReliableCallers.py` provou que o bit alto é aplicado em runtime. No `rakion.bin`,
`FUN_0045C6F0` chama `SendToOtherClientReliable(0x0313)` com `[mySeat][flag]`; o receiver no
`case 0x0313` chama `CPlayer::SetBadPing`. `FUN_0045CE60` usa RTT `>199 ms`, maioria
`ceil((players-1)/2)`, dez amostras por peer e janelas de 10 s. Assim, a captura
`13 83 75 04 00 00 0A 0A 00` significa source/seat 10 removendo o indicador de bad ping.

`DecompileClientEntitySync.py` fechou `SendInfoCreateMasterGolemTo @ 0x3610B1E0` e
`HandleMessage @ 0x3610D7C0`. O corpo reliable `0x0308` começa por
`[u8 hostSlot][u8 team][u16 entityField][6 x 4-byte placement]` e segue com init serializado.
O `0x030B` usa `[u16 timing/state][u8 kind][u8 group][u8 index][s16 x,y,z,heading]`; kinds `2/3/4`
são general NPC, map NPC e Master Golem. Map NPC ausente gera `0x0310`, não `0x030B`. O corpo de
carga e seus ACKs seguem diretamente entre clientes nas portas `2300..2399`; o World publica os
endpoints e oferece fallback, mas não deve fabricar `0x0308`.

A mesma extração fixa as constantes `0x0307/0308/0309/0310/0312`. `0x0307` e `0x0309` têm
`[u8 owner/host][u8 npc/map index][u16 entityField][6 x 4-byte placement][init blob]`.
`0x030C` contém `[u8 sender][u8 entityClass][u8 indexA][u8 indexB][u32 eventType]
[u32 payloadLength][payload]`; o dispatcher resolve classes `1/2/3/4/6/7`. Antes dele, o envelope
reliable direto já carrega `[type|0x8000][sequence][transportSource]`. `0x0312` contém
`[u8 count]` e pares `[u8 mapItemIndex][u8 state]`. Os init blobs variam entre três famílias já
inventariadas. IDs/tamanhos de eventos estão catalogados, mas valores runtime e golden bytes
específicos das classes ainda variam; os envelopes não.

O case `0x0310` lê exatamente `[targetSeat][entityKind][mapIndex]`, exige `entityKind=3`, resolve o
Map NPC e responde reliable ao `targetSeat` com o create. O fallback tipado rejeita comprimento,
ordem ou kind divergente e exige que `targetSeat` repita o seat autenticado do solicitante. A
fixture antiga `[00 00 03]` era sintética e incompatível com o consumer; a sonda e os testes usam
agora `[targetSeat 03 mapIndex]`.

O catálogo reproduzível [`entity-event-catalog.md`](entity-event-catalog.md) fecha as 269 classes
`E*` exportadas: todas tiveram o `event id` do construtor e o `GetSizeOf` resolvidos no dump
runtime. IDs pequenos repetidos são válidos e ficam escopados pela classe da entidade. Para a
família `0x044D0000..0018`, os construtores ainda fecham payloads vazios ou combinações exatas de
`vec3f`, `CEntityPointer`, `CEntityID`, `u32`, `u8` e padding; campos sem consumidor semântico
continuam nomeados apenas pela largura.

`DecompileClientWeaponEvents.py` e a inspeção de assembly do dispatcher fecharam também cinco
eventos do player. `ESetWeapon 0x01910006` possui dois `i32`; o primeiro seleciona os dois caminhos
de arma. `EShootWeapon 0x01910007` e `EShootShuriken 0x01910008` possuem dois `vec3f` e quatro
bytes finais; no primeiro, o byte inicial usa `EShootWeaponType 0..2`; no segundo, ele é o contador
que limita o loop de projéteis e o produtor observado grava `9`. `ERequestHoldAttack 0x01910009`
e `EHoldAttack 0x0191000A` possuem 16 bytes cada; o primeiro contém o `f32 maximumDistance` usado
na checagem geométrica, e o segundo leva os índices usados por `ExecuteHoldAttack`. O codec .NET e
`decode_gameplay_p2p.py` preservam também os bytes reservados/padding.

`EPlayerDamage 0x0191000B` fecha o encadeamento seguinte com 40 bytes:
`u32 playerId`, `DamageType`, `DamageMotionType`, `u16 reserved`, dois `f32` calculados e dois
`vec3f`. Os tipos e vetores vêm de `DamageInfo+0x50/+0x54/+0x58/+0x64`; os escalares são montados
dos coeficientes `u16` em `DamageInfo+2/+4` e seguem para `ApplyReceiveDamage/WorkReduce_HP_AP`.
Quando o golpe mata, `EPlayerDeath 0x01910016` leva `vec3f deathVector`, copiado do primeiro vetor
em `DamageInfo+0x58`; `ERespawn 0x01910017` continua com payload vazio.

O init blob é uma `CNetMessage` aninhada e polimórfica. O sender a preenche pelo slot virtual
`entity.vtable+0x04`; o handler de `0x0307` lê os 28 bytes fixos, extrai a mensagem restante,
cria a classe remota por `entityField` em `AddRemoteGeneralNpc @ 0x361097A0` e entrega o blob ao
slot virtual `entity.vtable+0x118` antes de inicializar a entidade. Portanto seu layout deve ser
catalogado por família/classe de `Entities.dll`, não como uma única cauda comum.
O inventário reproduzível `tools/extract_entity_init_serializers.py` mapeia as seções do PE32
diretamente, sem depender de um processo ou dump de memória, e fecha as 43 classes presentes
em três famílias: 41 usam `GetInitData@CNpcBase @ 0x350E3FA0` /
`ApplyInitData@CNpcBase @ 0x350E96E0`; `CNpcGoldGolem` usa `0x35100DC0/0x35100EB0`; e
`CNpcChocolateCake` usa `0x350F4EE0/0x350F4FE0`. Os quatro caminhos `NpcBlackDragon*` elevam o
catálogo configurado a 47, mas não têm manifest nem export de classe nesta build. A família Base
serializa `f32 +0x26C`, `f32 +0x7B0`, `u8 +0x7C4`, texto com comprimento `u8`, cinco bytes de
owner quando `entityType == 3`, `u8` calculado de `+0x620` e `u32 +0x7D0`. Esse byte vale `0`
sem link, `1` quando o helper retorna valor não zero e `2` quando retorna zero; o reader o grava
em `+0x800`. Gold Golem usa `f32 +0x38EC`, `f32 +0x38E8`, `u8 isAlive` e o mesmo owner
condicional. Chocolate Cake usa `f32 +0x38E4`, `f32 +0x38E0`, `u8 isAlive`, owner condicional e
`u32 +0x7D0` dentro desse ramo. `isAlive` vem do export `CNpcBase::IsAlive @ 0x350DCDA0`; zero
aciona `ENpcBaseDeath @ 0x350DC650` no reader. Os pares writer/reader usam exports simétricos de
`CNetMessage`; valores concretos e nomes de domínio dos floats ainda exigem captura runtime, não
o formato escalar. `tools/decode_entity_init_blob.py` implementa as três gramáticas e rejeita
truncamento ou sobra. Os setters derivados
confirmam defaults parciais: Cake começa com os dois floats próprios em `1.0f`, Gold com os dois
floats próprios em `0.0f`, e ambos começam com `+0x7B0=0.0f`, `+0x7C4=1`, `+0x620=0` e
`+0x7D0=0`.

`DecompileClientReliableTransport.py` fecha `CNet::SendData`, os builders direto/relay,
`PacketBufferRecvUpdate` e a fila de retry. O wire direto é
`[u16 type|0x8000][u32 sequence][u8 source][payload<=1000]`; `0x4000` confirma a sequência e o
sender retransmite após 1000 ms enquanto o registro continuar na fila. O receiver retira
`0x8000` antes de entregar ao dispatcher, logo criação `0x0308` cruza a rede como `0x8308`.
No `0x030A`, `SendAction @ 0x36103940` compacta o seat nos cinco bits baixos e
`PlayerActionState` nos bits `5..6`; o byte seguinte é `ePlayerAction`. As tabelas runtime fecham
os quatro estados e 32 ações de movimento/guard/troca de arma. O produtor
`ctl_ComposeActionPacket @ entitiesmp.dll:0x35139310`, o armazenamento em
`CPlayerAction+0x38/+0x3C/+0x40`, o consumidor `CPlayer::ActiveActions @ 0x35151300` e o golden
source do engine fecham os três `s16` finais como `pa_aViewRotation`.
`SendToOtherClient @ 0x36100780` e `SendToOtherClientReliable @ 0x36100980` sempre oferecem a
mensagem a `0x56` quando o World está conectado; `IsTunneling_Client @ 0x36194DB0` decide se o
UDP direto deve ser suprimido. O flag vem de `user+0x1478`, serializado por `FUN_0040B7F0` após
os dois nomes do roster e armazenado pelo engine em `session+0x1D6+seat*0x378`.

Os helpers World `FUN_00405F30/004060A0` fecham a matriz TCP: sender em tunnel alcança todos os
outros players ativos; sender direto alcança somente targets em tunnel; TunnelOne envia apenas
quando uma das pontas está em tunnel. Field fora de `state=2`, agregado zerado e par direto/direto
não geram `0x57`. `TunnelingRelayPolicyTests`, o probe UDP e `world_tunneling_probe.py` cobrem a
matriz e o flag do roster no `.NET`.
`DecompileEngineGolemObjective.py` confirmou que `AddRemotePlayer @ 0x3610E2B0` faz o client master
enviar ao novo peer `0x0307`, `0x0309`, os dois `0x0308` e `0x0312`; `SetMasterClient` alterna
Master Golems e map NPCs entre local/remoto e reconstrói o Gold Golem quando necessário. A
auditoria de callers mostrou que esse é o único chamador dos quatro builders. Ele primeiro aplica
o init blob recebido ao player remoto, envia o `GetInitData` do player local e então envia NPCs;
map NPCs, Master Golems e map items seguem nessa ordem apenas se o player local é o boss.

No dump runtime de `entitiesmp.dll`, `DecompileClientGolemObjective.py` fechou `EGoldSword`
`0x044D000B` com payload próprio `[enabled][secondary][padding]`, a propriedade de porte
`CPlayer+0x2B98`, reset nos 20 players e restore no respawn. Também foram confirmados
`EGoldGolemRespawn=0x04690000`, `EGoldGolemRebirth=0x04690001`,
`EMasterGolemRespawn=0x04650000` e `EMasterGolemDamage=0x044D0015`.

`0x0304/0x0305` são um par adicional da aplicação, não o reliable genérico. O socket escolhe a
primeira porta livre a partir de `2300 + últimoOctetoIPv4 % 100`, limitado à faixa `2300..2399`.

### Primeiro movimento `0x4B` — 2026-07-16

`DecompileWorldFirstFieldMove.py` extraiu `FUN_004247B0` e `FUN_00405C00`. O handler sempre lê
`len:u16`, aceita `0..200` e chama o relay depois dos gates `DISC 86/87/88`; não há ramo especial
para o primeiro pacote nem chamada de inventário. O helper exige field `state=2` e sender
`state=4`, monta `[0x4B:u16][senderSeat:u8][len:u16][blob]` e envia a todos os outros records em
`state=4`. Portanto, inclusive `len=0` produz o frame lógico de cinco bytes.

O interceptor .NET preserva apenas a inicialização de `StageRun`, relógio e prontidão, depois
devolve o pacote ao dispatcher canônico. O repaint especulativo de quickslot no spawn foi removido;
o `0x31` continua restrito ao login, operações de inventário e fallback da primeira abertura.

### Prova local de Stage/PvE — 2026-07-15

`tools/world_stage_probe.py` atravessou `0x3B` Mode 0, `0x43`, primeiro `0x4B`, clear `0x4A`
e resultado `0x53` no Stage 3. O World carregou os 48 registros de `stageinfo` e vinculou a
sessão ao `Field.MatchId` e ao Stage ID antes de iniciar o round. Um `0x53` antes do clear e outro
com Stage ID 4 não receberam ACK nem alteraram o banco.

O resultado histórico Stage 3/rank A/50 EXP/25 gold recebeu `53 00 00 03 04 00` somente depois do
commit. O replay idêntico recebeu o mesmo ACK; um replay com gold divergente foi rejeitado. A
consulta do banco confirmou uma única linha no `stage_result_settlement_ledger`, exatamente
`applied_exp=50`, `applied_gold=25` e delta de wallet `25`. A fixture foi restaurada para
EXP `37`, nível `2`, level-point `1`, gold `10113` e nenhum ledger residual.

`StageSettlementDatabaseSmokeTests` complementa o wire: cria `userstageinfo` como MyISAM,
comprova a migração para InnoDB e unique `(characterid,stage)`, primeiro commit, replay,
concorrência com um único crédito, melhor rank e rollback quando a progressão está stale.

Uma inspeção posterior de `DataSetup/LevelData` fechou a fonte de conteúdo que não existe em
`stageinfo`. `tools/extract_stage_catalog.py` leu 55 `stage_*.txt`; IDs `1..48` coincidem com o
catálogo SQL ativo e somam 1.210 blocos `NpcSpawn`/3.407 nomes de instância. Cada arquivo também
contém `time_limit`, goal e cinco `rankvar` com threshold, EXP, gold e multiplicador. Stage 3
confirma 288 s e rewards S→D `64/132`, `40/83`, `32/66`, `24/50`, `16/33` (EXP/gold). IDs
`49..55` existem como assets, mas não no SQL e não foram promovidos a conteúdo habilitado.

Em 2026-07-16, o caller real foi fechado em `entitiesmp.dll:0x3515C760`. Ele lê gold em
`_s_stage+rank*0x28+0x4C`, EXP em `+0x50`, subtrai a linha do melhor rank anterior e zera ambos
quando o rank não melhora. Os três argumentos seguintes são os slots de Cell `10..12`: cada Cell
equipado recebe `EXP/3`; Power User aplica `x+x/2`. A chamada virtual `+0x130` chega a
`rakion.bin:0x00478BF0`, que aplica gold, EXP, atualiza os três slots e chama
`IScavengerWorldNet::SendFieldGameStagePoint`. O helper intermediário é apenas
`CNetMessage::GetData @ engine.dll:0x360049D0`, usado para obter o ponteiro dos map slots.

A cadeia do loader também elimina a hipótese de EXP recalculada pela curva de level.
`GetLevelInfo @ 0x3522B880` chama `CLevelScriptor::GetLevelInfo @ 0x3522C190`, que retorna
`objeto+0x12C`; assim, `+0x4C/+0x50` são exatamente `objeto+0x178/+0x17C`. Em
`CLevelScriptor::SetStageParameter @ 0x352313D0`, as linhas são percorridas nesses campos em
passos de `0x28`, sem escrita de normalização. A EXP autoritativa é a literal do `rankvar` no XFS;
os antigos valores `72/54/36/27/18` eram uma inferência incorreta e foram removidos.

Com essa prova, o catálogo v258 foi embutido no World, a duração Mode 0 tornou-se autoritativa e
`0x53` passou a exigir o delta exato de EXP/gold e a EXP exata dos Cells. O probe foi atualizado
para Stage 3/A `40 EXP/83 gold`.

Em 2026-07-16, o smoke atualizado foi repetido contra um processo Release isolado nas portas
`41708/41709`. Ele atravessou seleção `0x14`, create/start/spawn, rejeitou `0x53` antes do clear e
com Stage ID divergente, recebeu `53 00 00 03 04 00` no commit e no replay idêntico e rejeitou o
replay com gold divergente. O ledger gravou `reported=40/83` e `applied=60/83`: a EXP aplicada foi
`40 x 1,5` porque a fixture tinha Power User ativo. A progressão temporária foi de EXP `37` para
`97`, gold recebeu `+83` e rank virou `4`; snapshot de personagem, wallet e rank foi restaurado ao
final. O mesmo percurso revelou e fechou o default incorreto de `ActiveCharId=-1` e o padding wire
do `0x53` descrito abaixo.

O dispatch efetivo também foi fechado. `ClientSession.DispatchOpcode` consulta
`TryHandleLobbyEntry` antes da jump table; portanto o request `0x53` sempre segue
`OnStageResultAsync -> StageResultProtocol.TryParse -> ApplyStageResultAsync`. Os dois handlers
alternativos existentes na tabela eram inalcançáveis e divergiam da transação; foram removidos.
O parser exige `23 + count*2` bytes lógicos ou o único tamanho equivalente após o padding da cifra
em blocos de 12 bytes; comprimentos intermediários ou maiores continuam rejeitados. Também exige
`stage < 100`, `rank < 6`, `count < 5`, e o domínio rejeita cada `mapSlot >= MaxUser`. O ACK só é
enviado depois do settlement aplicado ou de um replay idêntico reconhecido.

O settlement foi estendido para gravar `useriteminfo.level/exp` e
`stage_result_cell_settlement_ledger` no mesmo commit de personagem, wallet e rank. O snapshot
dos três Cells permanece ligado à run para que replay idêntico não reaplique EXP. O smoke MariaDB
verifica uma Cell equipada, três linhas de ledger por resultado, replay, divergência e segunda run.
Ele foi repetido nesta rodada com `RAKION_MYSQL_SMOKE_CONNECTION` contra schema temporário e passou
sem resíduos no schema `rakion`.

`FUN_351933D0` seleciona por `_s_stage+0x360`: `0=time attack`, `1=butchery`,
`2=survival`, `3=guard`. `FUN_35192F40` percorre as cinco linhas S→D em stride `0x18`, lendo o
threshold em `+8`. Time attack usa a primeira linha cujo threshold é maior ou igual ao tempo
truncado; os demais usam a primeira linha menor ou igual à métrica. Butchery lê o contador em
`+0x36C`. Survival e guard calculam percentuais com `100.0f`; os calls virtuais `+0x154/+0x158`
foram resolvidos na vtable de `CPlayer` como `GetMaxHP/GetHP`. A fórmula foi portada para
`StageRankPolicy`, mas não aplicada como autoridade porque essas métricas não existem no `0x53`
e o World ainda não simula o PvE.

`tools/extract_stage_catalog.py --flow-output` materializou o grafo dos 48 scripts ativos em
`docs/audits/evidence/stage-flow-v258.json`: 5.826 nós, 4.482 alcançáveis explicitamente, 48
triggers `win` e 47 ligados à raiz. O Stage 29/guard contém o único `win` sem caminho explícito.
Foram preservadas 29 referências ausentes e 31 duplicatas de nome por tipo; 21 stages possuem ao
menos uma inconsistência. O catálogo runtime leva contagem/alcançabilidade/flag de consistência e
o World avisa no boot, mas não altera os assets sem fechar antes a política do `CLevelScriptor`.

### Catálogo estrutural de cells e criaturas

`tools/extract_cell_catalog.py --data-setup-xfs` cruzou as 47 entradas configuradas da golden source
do cliente com registros reais de `items.dat`. O marcador `u32 id, u16 0, u32 id`, nome e modelo
prova, por exemplo,
`8000/Nak/NpcNak`, `8013/BloodNak/NpcNak3`, `8016/AssaultPanzer/NpcPanzer3` e
`8025/IronGolem/NpcGolem3`. Destas, 43 têm manifest/classe carregável; os quatro caminhos
`NpcBlackDragon*` não existem em `Classes.xfs`/exports. O índice é exatamente `itemId-8000`, o mesmo tipo usado por
`FUN_0040B940` em `npcinfo`. As 14 classes dos stages SQL ativos foram resolvidas. Nos stages
inativos, `blacknak`, `blackicewind` e `blackdragon` têm `.ecl`, mas não item correspondente no
`items.dat` ativo.

O loader runtime `0x35228D10` e o extrator fecharam 47 blocos de 8.118 bytes, 99 níveis por tipo,
registros runtime de 160 bytes e tabela secundária final `47 × 4 × 33`, totalizando 387.750 bytes.
SHA-256: `items.dat=57cbab82c3eaf2ff7789a674d8e60121f0ccf5923d0da8f37fbedc86a0297372`
e `creatures.dat=de97e9aa6fa47792fe9de38770d77b55bb6bc308db9444d8a314d6681d554e3f`.
A série `uint32 +0x18C` é EXP cumulativa da cell, `uint16 +0x166E` é ganho de CP por morte de NPC,
`uint16 +0x1734` é custo CP de summon por nível e `float32 +0x18C0` é custo GOLD de upgrade. O
snapshot RaGEZONE de 51 tipos é de outra revisão e não representa o cliente ativo.

No estado de CP, `CPlayer+0x2714` é o atual, `+0xB8C` é o máximo, `SetCP` faz clamp e a morte
reduz 10%. `FUN_004525F0` mostra o modificador agregado “CP consumption” em `GetItemInfo+0xB8`.
`FUN_351DBFF0` resolve os três slots equipados e combina esse percentual com o custo base runtime
`NpcSetup+0x70`, carregado de `creatures.dat+0x1734`, com piso de 1 CP. `FUN_351DBF00` recebe os
bits `0x200/0x400/0x800`, seleciona um dos três registros de `0x20` bytes em `CPlayer+0x26B0`,
rejeita estado diferente de `1` e chama `FUN_351DBE90`. O débito original é direto:
`CPlayer+0x2714 -= slotCost`; não passa por `ReduceCP`. Estados `0/1/2` significam, no seletor,
saldo insuficiente, ready e selecionado/spawnado. O executor seguinte envia `0x0307`. Não há
consulta de timer. `FUN_350E9AE0` é chamado pelos caminhos terminais `FUN_350EE230`,
`FUN_350F7AB0` e `FUN_350E9B30`; ele grava `0` em `owner+0x26C0+slot*0x20`, permitindo nova
promoção para `1` quando houver saldo. No desaparecimento, `FUN_350E9B30` ainda devolve
`AddCP(custoEfetivo × 0,3)` antes de ocultar a entidade; morte usa separadamente o ganho de CP
de `NpcSetup+0x64`.

Na morte válida de NPC, `FUN_350EE230` lê
`NpcSetup+0x64`, carregado de `creatures.dat+0x166E`, chama `AddCP` e emite a mensagem para o
jogador local. A série posterior `+0x1A4C`/runtime `+0x8C` não é custo CP. A varredura dos 61
chamadores dos accessors de `NpcSetup` não encontrou consumidor direto; ela fica catalogada como
`unconsumed_field_1a4c`, sem regra inferida.

O construtor de `CPlayer` inicializa CP atual em zero; `GetInitData` serializa `+0x2714` e
`ApplyInitData` restaura o mesmo `float32` no peer remoto. A varredura das 130 entradas do
`Scripts.xfs` encontrou APIs de CP somente em `scripts\item\12070.lua`, que adiciona 30% do Max CP.
Não há `ReduceCP`, `SetCP` ou outro `AddCP` em Lua, fechando a hipótese de regeneração passiva
indireta nesta build.

No runtime de entidades, `IsEnemy` usa o seat/time em `MovableEntity+0x264`: vermelho `0..9`, azul
`10..19` e cinza `20+`. Deathmatch considera inimigo todo seat diferente; nos demais modos, mesmo
time e dois cinzas são aliados. `CNpcBase::IsValidForEnemy/IsValidReceiveDamage` aplicam essa
relação, excluem master, morto, `MapItem`/`BoxItem` e `NoDamage_Switch`. `CNpcWatcher` filtra
owner/master, flags, cone, visibilidade e alcance antes de escolher mínimo/máximo por distância ou
propriedade. A política comum de owner, friendly fire e targeting está fechada; ataques específicos
de cada classe permanecem na validação dinâmica.

`Classes.xfs` confirmou que os `.ecl` são manifests apontando para classes compiladas em
`Entities.dll`, não scripts externos. A tabela compilada de eventos em `entitiesmp.dll` associa
`0x30=Summoner star explosion`, `0x44=Weapon Cell`, `0x48=Summon Npc`,
`0x49=Disappear Npc`, `0x53=HP Charge Effect`, `0x57=AP Charge Effect` e
`0x59=CP Charge Effect`. Os três layouts polimórficos de init e seus tipos escalares estão
identificados. O catálogo de eventos fecha os 269 pares ID/tamanho e a estrutura física da família
NPC `0x044D`; permanecem pendentes golden bytes em tráfego real e nomes de domínio de escalares
sem consumidor comprovado.

### Prova local de Deathmatch — 2026-07-15

`tools/world_deathmatch_probe.py` atravessou create/join/ready/start/spawn com duas sessões em
modo `2` e frag limit `2`. O reporte `0x4F [cause=8,killer=1]` chegou aos dois peers como
`4F 00 00 08 01 00 02`; em seguida, ambos receberam `4A 00 01 00 00 00`. Isso confirma score
individual do killer, `reason=1`, ausência de incremento em `Wins0/Wins1` e que o fim individual
não fabrica um lado vencedor para persistência.

O sample `0C839A00000000000100002A0091010400000001000000` confirma a composição: raw `0x830C`,
sequence `0x9A`, source `0`, e corpo `0x030C` com entity class `1` (player), event type
`0x0191002A`, tamanho `4` e payload `01000000`. Portanto, ele não deve mais ser descrito apenas
como “state sync opaco”.

O dump runtime de `entitiesmp.dll` fecha ainda `EPlayerDamage=0x0191000B/40 B`,
`EPlayerRemainHP=0x0191000C/12 B`, `EPlayerDeath=0x01910016/12 B` e
`ERespawn=0x01910017/0 B`. O segundo sample de 31 B decodifica
`EPlayerRemainHP [player=0][hp=97.0][ap=97.0]`.

O layout variável do roster foi extraído de `FUN_00406F40` e `FUN_0040B7F0` no
`worldserv.exe`. `RoomRosterFrameGoldenTests` protege o cabeçalho, as três strings e a regra de
20 estados; o probe protege a entrega real de `0x38/0x37`. A estrutura de `0xDE` bytes vista em
`FUN_0047A370` é representação interna do cliente e não foi copiada como wire. Os campos
`+0x1450/+0x1454/+0x1458/+0x145A` do registro separam IPv4/portas observados e anunciados,
conforme `FUN_0040AB90`; o codec envia zero enquanto o handshake não ocorreu.

### Prova local da economia da loja — 2026-07-14

`FindWorldShopEconomy.py` encontrou 12 strings e 14 consumidores econômicos no
`worldserv.exe`, sem qualquer ocorrência ou referência a `buyinfo`. A compra em
`FUN_00419A40` grava `LogUserItem.kind=0` para Gold ou `LogBuyCashItem` para Cash; o ID inserido
vira o serial original com `sn_type=1/2`. A venda em `FUN_0041A900` apaga o row ID de
`UserItemInfo` e grava `LogUserItem.kind=1`, incluindo saldo anterior/atual, nível e exp.

Na reconstrução, `WorldDatabase.EconomyLedger` centraliza esses inserts e participa da mesma
transação de wallet e inventário. Uma prova MariaDB comprou `1001` por `2700` Gold
(`999961266→999958566`, `kind=0`) e vendeu o mesmo row por `1080`
(`999958566→999959646`, `kind=1`). Item, logs e saldo da fixture foram restaurados ao final.

`DecompileClientShopPurchase.py` confirmou `SendInventoryBuy @ 0x36191740` como
`(u16 item,u8 currency,u8 useCoupon,u16 couponSlot)`; o `u16` só cruza o wire quando o flag é
diferente de zero. No World, `FUN_00421210` resolve a célula e `FUN_0040CB10` exige item
`11000..11999`, procura `couponinfo`, cruza `for_cash` com a moeda e produz `0x14/0x15/0x16`.
Logo, esse campo não é duração/count nem chave de idempotência.

Uma segunda prova MariaDB criou um cupom Gold temporário `11000` de 50% na célula 16. A compra de
`1001` consumiu a linha, debitou `1300` (`999961266→999959966`), gravou
`logcoupon.discount_amount=1400`, vinculou o ID no `loguseritem.kind=0` e colocou o item na célula
17. A definição, item, logs e saldo temporários foram removidos/restaurados ao final.

Em 2026-07-18, o assembly completo de `FUN_00419A40` fechou o ledger de sets: o preço permanece
no mesmo slot de stack antes e durante o loop `0x0041A340→0x0041A690`. Cada membro passa por um
insert de ledger separado em `0x0041A5DA..0x0041A712`, repetindo preço total e saldos da operação.
O E2E obrigatório comprou `1009`, o mesmo item com cupom Cash de 50% e o bundle `9012`: confirmou
`100000→95200→92800→83500`, seis grants, oito rows/seriais/ledgers, reconnect e rollback.

Para expiração online, `world_character_probe.py hold 22` manteve o personagem selecionado após o
login. Um item `1001` já carregado no box teve `limittime` alterado para `agora-1`; na varredura
seguinte o World removeu exatamente uma linha e enviou `31 00` com item/count zerados para a célula
0. A consulta posterior retornou zero linhas para o serial temporário, sem derrubar a sessão.

### Prova local do refino em duas fases — 2026-07-15

`DecompileWorldEnchant.py` confirmou `FUN_00421E10` como preview `0x74→0x28` e
`FUN_0041DE40` como commit `0x28→0x74`. O commit tem oito bytes fixos e carrega o result calculado
pelo cliente; a reconstrução ignora esse byte e usa o resultado já sorteado pelo backend. A tabela
de exports de `engine.dll` não contém o antigo alias `SendInventoryEnchantReinforce`.

`world_enchant_probe.py` colocou arma `1001 +4`, catalisador `13001` e joia `14001` nas células
`0/1/2`. O preview publicou os seriais reais `9912001..3`. Mesmo recebendo `clientResult=5`, o
servidor escolheu `3`, persistiu `+4→+2`, consumiu as duas linhas e gravou um único `logenchant`
com chance `0.3608577`, config `2` e result `3`. Repetir o commit retornou o mesmo `0x74` sem
segunda mutação. `FindEnchantCoefficientWriters.py` confirmou os cinco pares base/decay no
inicializador original. A fixture e o ledger foram removidos ao final.

| `6A` | variável | count, item IDs `u32` e nome da conta | `Notification_ContainsItemsAndAccountName` |
| `6B` | 13 | status, pending ID, item ID e opção `u16` | dois goldens Peek |
| `6C` | 7 | status e `u32` de resposta | goldens Accept + integração MySQL |
| `6D` | 3 | status | goldens Dispose + integração MySQL |

### Prova do gate GM `0x64` — 2026-07-15

`DecompileClientGmOperation.py` confirmou
`IScavengerWorldNet::SendGMOperation @ engine.dll:0x36194E00`: o builder escreve apenas `u16 0x64`
e finaliza o pacote. `DecompileWorldGmOperation.py` confirmou `FUN_004283A0`: compara
`user+0x146C` com ASCII `'4'` (`0x34`), usa `FUN_0040ABE0` para obter o endpoint e compara o IPv4
com quatro globais inicializados por chamadas a `inet_addr`. As quatro strings da build analisada
são `192.168.1.6`. Falha de substatus desconecta com `0xB9`, falha de IP com `0xBA`, e sucesso não
produz resposta.

`world_gm_operation_probe.py` atravessou o dispatcher real e confirmou os três ramos: `DISC B9`,
`DISC BA` com allowlist vazia e conexão preservada sem resposta quando `127.0.0.1` foi permitido
temporariamente. O deploy foi restaurado para `AllowedIPs=` vazio após a prova.

### Prova do ChCode `0x65` — 2026-07-15

`DecompileClientChCode.py` confirmou o builder
`IScavengerWorldNet::SendChCode @ engine.dll:0x36192A90` como `[u16 0x65][cstr md5]`.
`DecompileWorldChCode.py` fechou `FUN_00428430`: 32 bytes comparados case-sensitive com `MD5_2`
no modo `1` ou `MD5_1` nos demais, bypass nos modos `4/5`, `DISC BB` fora do field, `DISC BC` por
divergência e nenhuma resposta no sucesso. Os hashes ficam no objeto World (`+0x12C/+0x14D`), não
na sessão. O modo é definido no primeiro byte do login e o mesmo MD5 já é validado ali; modo `4`
pula a verificação de login.

Com `EnforceMD5=1` e hashes temporários, `world_ch_code_probe.py` confirmou `BB`, sucesso sem
resposta e `BC`. O login de compatibilidade também foi validado com enforcement desligado, e o
deploy foi restaurado para `EnforceMD5=0` e hashes vazios.

### Prova dos exports dormentes `0x66/0x69` — 2026-07-15

`DecompileClientWorldEvents.py` confirmou que
`IScavengerWorldNet::SendEvent1 @ engine.dll:0x36192C40` escreve somente `u16 0x66`, enquanto
`SendEvent4 @ engine.dll:0x36192C80` escreve somente `u16 0x69`. Os slots correspondentes da
vtable são `+0x2CC/+0x2D0`. A análise de `rakion.bin` encontrou apenas IAT e jump thunks, sem
chamada ativa a qualquer um dos exports.

A jump table de `worldserv.exe` pula de `0x65` para `0x6B`; os dois valores caem no default.
`world_dormant_event_probe.py`, em sessões separadas após seleção de personagem, confirmou
`opcode-66=disconnect-c9` e `opcode-69=disconnect-c9`. O servidor .NET preserva esse resultado e
há regressão garantindo que nenhum nome/handler canônico seja inventado para esses opcodes.

### Call sites dos exports rejeitados de canal/field — 2026-07-15

`TraceRakionWorldNetAccessor.py` decompilou os 143 callers alcançados pelas 438 referências a
`FUN_00471B70`, o accessor WorldNet usado pela UI. `DecompileDormantWorldCallsites.py` separou as
chamadas virtuais cujos offsets coincidem com os slots rejeitados. A única chamada atribuível ao
WorldNet nesse conjunto é `FUN_0046A0F0`, `case 0x174`, em `0x0046AA7C`: ela obtém o objeto pelo
accessor e chama o slot `+0x60`, mapeado para `SendChannelList` (`0x1D`).

Não foi encontrada chamada WorldNet aos slots `+0x68`, `+0x6C`, `+0x74..+0x88`, `+0xE0` ou
`+0x100`, correspondentes a `0x1F`, `0x21`, `0x23..0x28`, `0x3C` e `0x44`. Chamadas virtuais
brutas nos mesmos offsets pertencem a outras classes e não constituem evidência de envio. O
resultado fecha a busca estática desta build: `0x1D` possui rota UI completa; os demais não têm
consumidor Rakion identificado. Todos continuam caindo no default `DISC C9` do World original.

`DecompileClientChannelListRequest.py` fechou também os argumentos omitidos pelo decompiler no
call virtual. O assembly `rakion.bin:0x0046AA63..0x0046AA7C` carrega
`global+0x444A`, empilha `1` e chama o slot `+0x60`. O builder
`engine.dll:0x361911D0` confirma o corpo lógico `[0x1D:u16][primeiroId:u8][1:u8]`.
O primeiro ID é produzido pelo callback `0x00474260`: a resposta `0x1D` é lida em
`engine.dll:0x361931E0` como `[count]`, seguida por entradas com quatro bytes e C-string, e
materializada em registros de `0x2D` bytes; `0x0040A3C0` preserva os IDs da primeira e última
entrada em `+0x444A/+0x444B`. Assim, “page/type” e “filter” eram nomes especulativos: o contrato
comprovado é primeiro ID + flag literal `1`, embora o World v258 não implemente o request.

### Prova de AdminBan/AdminNotice `0x04/0x05` — 2026-07-15

`DecompileClientAdminBanNotice.py` confirmou os builders
`SendAdminBan @ engine.dll:0x361909C0` como `[u16 0x04][u8 flag][cstr text]` e
`SendAdminNotice @ engine.dll:0x36190A30` como `[u16 0x05][u8 scope][cstr target][cstr text]`.
O primeiro `char*` de Notice contém o byte de escopo seguido do nome na mesma string.

`DecompileWorldAdminBanNotice.py` fechou `FUN_0041F1A0/0041F290`. `0x04` devolve ao remetente
subtipo `1`, flag e texto de até 12 bytes; não altera banco nem estado de ban. `0x05` envia subtipo
`99` e texto somente a sessões com os dois handles de field ativos: escopo `0` aceita qualquer
status, `2` exige field-lobby e `3` exige in-field. Nome não vazio é comparado case-sensitive e
produz ack subtipo `5` (`0` entregue, `1` ausente); nome vazio faz broadcast sem ack.

### Prova de GmQueryEntry `0x09` — 2026-07-16

`DecompileWorldGmQueryEntry.py` extraiu `FUN_0041F5C0`, `FUN_004058E0` e o inicializador
`FUN_00405440`. O handler exige `Status=5` (`DISC 0x11`), lê `fieldId:u16` e sempre devolve
`[u16 9][u8 status][u16 fieldId]`. ID fora de `DAT_00455824/MaxField` produz status `1`; uma
entrada com `field+8 == 0` produz `2`; uma entrada ocupada produz `0` e anexa duas C-strings.

O serializer copia primeiro `field+0x16` e depois `field+0x09`. O inicializador prova os nomes:
`+0x16` recebe o nome da sala e `+0x09` recebe `user+0x14A8`, personagem do criador. Assim, a
segunda string é identidade imutável de criação, não o host atual. Os goldens .NET fixam os frames
de cinco bytes para status `1/2` e o frame variável para sucesso.

### Fechamento por estado de `0x3D/0x3E` — 2026-07-15

Os builders do cliente são `SendFieldReady(ready:u8)` e `SendFieldChangeTeam()`; o World original
roteia os mesmos opcodes em `Status=3` para `FUN_00423AD0/00423B70`. O primeiro aplica o byte como
transição de arma `1<->2` e publica `[seat][dir]`; o segundo move o player-record entre os blocos
`0..9` e `10..19` e publica `[status][oldSeat][newSeat]`.

Na reconstrução, `ClientSession` intercepta os dois opcodes somente em `Status=2`, onde ready e
troca de time já foram validados com duas sessões headless. Em `Status=3`, o interceptador retorna
false e a tabela executa `FieldWeaponChange/FieldChangeTeam`, também exercitados pelos probes de
combate. Assim, nenhuma das duas semânticas é combinada no mesmo estado.

### Prova de votação `0x5D/0x5E/0x5F` — 2026-07-15

`DecompileClientFieldVote.py` fixou os builders em `engine.dll:0x361929D0/0x36192A40`:
`0x5D [targetSeat][reason cstr]` e `0x5E [vote]`. No World,
`FUN_00425A70/00425BB0` chamam `FUN_0040A420`; o disassembly eliminou a perda de tipo do
decompilador e confirmou retornos `1`, `2`, `6`, `7` e `9` na abertura e `2`, `3`, `4`, `5` no
helper de voto. Sucesso do cast sempre volta com `AL=0`; o `8` de apuração pendente não é ACK.

No retorno, `FUN_36197320` despacha `0x5D` para `FUN_36194360` e `0x5F` para
`FUN_361943D0`. O primeiro repassa target e C-string ao callback `+0x28C`. O segundo sempre repassa
status ao callback `+0x290`, mas só carrega os seis campos de resultado quando status é zero; em
erro, zera esses argumentos. Isso confirma o frame final e a resposta curta sem depender do World.

O agregado usa `field+0x2D0` como ativo, `+0x2D1` como índice em dez slots de penalidade,
`+0x2D2` como alvo, `+0x2D3` como motivo e `+0x354` como deadline de 60 s. Cada player usa
`record+0x134`. `FUN_00409810` conta `state=4`; `FUN_004090B0` envia exatamente nove bytes e
zera os votos. Maioria requer participação sim/não de metade dos elegíveis e `yes>no`.

`FindWorldFieldVotePenalty.py` localizou `FUN_004068C0`, que grava a identidade do alvo em
`field+0x358[index]` e `GetTickCount()+1800000` em `+0x35C[index]`. `FUN_00406F40` compara essa
identidade no join e responde status `8`. Portanto o efeito original é resultado ao cliente mais
penalidade de reentrada, não chamada direta ao remover membro.

`world_vote_probe.py` atravessou o dispatcher com `test/test2/test3`. A abertura chegou apenas ao
terceiro eleitor como `5D 00 01 41 46 4B 00`; o alvo recebeu `5F 00 05`; o segundo voto sim
finalizou para os três como `5F 00 00 00 03 02 00 00 01`. A prova também revelou e corrigiu um
overflow antigo em `PacketReader.CString()`: `position + int.MaxValue` transbordava e apagava
C-strings precedidas por outro campo. Há regressão específica e 282 testes .NET verdes.

### Catálogo IScavengerWorldNet de respostas S→C — 2026-07-16

`DumpClientWorldResponseCatalog.py` percorreu o switch completo de
`engine.dll:0x36197320` na build de SHA-256
`83b20d6c32cd66b95c8f8e41ad6de13a58e8f5f948cd21cbd118d42ef8cf88f2`. Foram extraídos
88 cases distintos, de `0x00` a `0x74`, sem case sem handler e sem opcode duplicado. O TSV bruto
é `C:\temp\client_world_response_catalog.tsv`; o segundo passe validado gera
[`world-response-dispatch.md`](world-response-dispatch.md).

`TraceClientWorldResponseDispatcher.py` encontrou uma única referência ao switch dentro dessa
fila: a chamada em
`ProcessWorldRecvBuffer @ 0x36197A40`. Esse export percorre toda a fila de mensagens World e chama
`0x36197320(subtype,payload,length)`. No `rakion.bin`, há um único caller do export, em
`FUN_004125E0:0x004126BD`, executado na iteração principal. Isso fecha a fila WorldNet; o CNet/P2P
é uma frente distinta e está catalogado separadamente. Os cases FIELD desta tabela são respostas de
controle enviadas pelo caminho WorldNet e terminam em callbacks de partida.

O mesmo passe decompilou os 87 consumidores concretos. Foram resolvidos 86 slots de callback;
`0x00` chama diretamente `callback+0x15C`, e `0x61` é a única exceção sem callback: seu consumidor
remonta `[u16 0x61][i32 value]` e chama o builder de envio. Isso prova o eco automático que chega
ao handler World `FUN_0041C270`, onde o valor é gravado em `user+0x2380`, comparado com
`world+0x51B4` e, se igual, incrementa `world+0x51BC`.

`DecompileRakionWorldCallbackTable.py` cruzou os slots com a vtable final
`rakion.bin:0x004DDC08` e resolveu as 87 implementações, além da ação interna de `0x61`. O resultado
reprodutível está no [`world-response-dispatch.md`](world-response-dispatch.md). Os callbacks finais
de `0x04`, `0x05`, `0x29`, `0x2A`, `0x59`, `0x5A`, `0x5C` e `0x67..0x6A` são funções vazias. Os
últimos quatro ficam em `0x004734F0/00473500/00473510/00473520`; em especial, `0x6A` não aciona
notificação visual nesta build.

O passe direcionado dos seis cases simples corrigiu a tipagem residual. `0x5C` e `0x63` copiam
uma `cstr`; `0x67/0x68` ignoram o corpo e chamam callbacks sem argumentos; `0x69/0x6A` passam o
ponteiro do corpo sem dereferenciá-lo. `FUN_0041F290` produz `0x63 [text:cstr]` com comprimento
`strlen(text)+3` incluindo opcode e terminador. `FUN_0041C330/0041D650` produzem `0x6A`; no fluxo
de presentes o corpo é `[count:u8][itemId:u32*count][accountName:cstr]`. A busca reproduzível
`FindWorldSimpleResponseProducers.py` agora segue até quatro chamadas até os senders e classifica
os escalares encontrados. Para `0x5C`, todas as ocorrências alcançáveis são offsets de
stack/estrutura; não existe escrita literal desse opcode no `worldserv.exe` v258 analisado. Assim,
o consumidor `cstr` permanece documentado, mas a API é dormente sem produtor nessa build.
Para `0x67/0x69`, os únicos literais encontrados estão em `FUN_00423CC0` e são razões de
disconnect passadas a `FUN_0041EB20`, não frames S→C. Em `0x68`, há essa mesma razão e o stride de
um `IMUL` dentro do sender. Portanto `0x5C/0x67..0x69` são APIs consumidoras dormentes sem produtor
estático nessa build; não são contratos pendentes do World.

### Dispatcher CNet/P2P de gameplay — 2026-07-16

`DecompileClientFieldMessagePump.py` corrigiu uma raiz provisória incorreta e fechou o pump real:
`rakion.bin:0x004124A0` é o único caller de `CNet::RecvData`, drena sua fila e entrega cada item a
`rakion.bin:0x00411760`. Esse dispatcher trata 15 tipos diretamente e encaminha os demais, quando o
estado é válido, a `CSessionState::HandleMessage @ engine.dll:0x3610D7C0`. O segundo switch possui
nove cases exatos (`0x0307..0x0312`, com lacunas), reproduzidos em
[`field-message-dispatch.md`](field-message-dispatch.md).

`DecompileWorldDbQueues.py` refutou a associação anterior de `FUN_0041B940` com esse canal. Ela
enfileira requests `[u16 requestSequence][u16 commandType][data]`; `FUN_0041B3F0` drena a fila e
`FUN_0041AE50` despacha o worker DB. O comando `0x0C [characterId][remainingExp]` montado por
`FUN_00424350` chega a `FUN_004138B0`, que executa
`UPDATE CharacterInfo SET exp=%u WHERE id=%u`. A resposta do worker retorna por outra fila,
drenada em `FUN_0042BD70`; apenas o ACK interno `0x0C` não possui case em `FUN_004295C0`.
Em paralelo, `FUN_00424350` envia ao cliente por `FUN_004038E0`
`WorldNet 0x58 [i32 remainingExp]`. O `.NET` foi alinhado com persistência assíncrona e golden do
dword little-endian. Logo comando DB `0x0C`, response WorldNet `0x0C` e evento CNet `0x030C` são
três contratos distintos.

O mesmo pipeline corrige `0x78`: `FUN_0041BDE0` envia ao worker o comando `0x2C` com
`usergameinfo.id` e `clanid`; `FUN_0040F610` consulta até 99 outros membros por `id`, selecionando
`name,buddyname`, e o callback `FUN_0041E1A0` monta S→C `0x78`. Status `0` leva
`[u16 count][count*(account\0,buddy\0)]`; `1` representa lista vazia e `2`, falha DB. O nome antigo
O nome legado da consulta de estado de field e o corpo sintético `0x2C [fieldId][0]` estavam
substituídos pela consulta parametrizada. Como o catálogo cliente de 88 respostas não possui case
`0x78` e nenhum produtor foi localizado nesta build, o contrato fica dormente para compatibilidade,
sem ser anunciado como UI funcional.

### Gates de fase dos requests interceptados — 2026-07-16

`DecompileWorldRequestStateGates.py` extrai em uma passagem os handlers originais que hoje são
interceptados antes de `WorldHandlers`. A leitura de `user+0x1460`, `+0x14A4` e `+0x1440`
separa identidade de conta, personagem selecionado e fase da sessão:

| Requisito original | Opcodes comprovados |
|---|---|
| conta autenticada, sem personagem selecionado | `0x0E`, `0x12`, `0x13`, `0x14` |
| conta autenticada; fase não comparada diretamente | `0x0F`, `0x15`, `0x19`, `0x1A`, `0x1B`, `0x1C` |
| conta + personagem; fase não comparada diretamente | `0x6B`, `0x6C`, `0x6D` |
| conta + personagem; `Status=2` | `0x2C`, `0x2D`, `0x2E`, `0x2F`, `0x31`, `0x32`, `0x35`, `0x36`, `0x38`, `0x39`, `0x3B`, `0x6F`, `0x70`, `0x71`, `0x73` |
| conta + personagem; `Status=3` | `0x3A`, `0x48`, `0x4A`, `0x4B`, `0x4F`, `0x53` |

### KeepAlive `0x0F` — 2026-07-16

`tools/ghidra/DecompileKeepAlive.py` extrai o export
`IScavengerWorldNet::SendAlive @ engine.dll:0x36190C70`, que envia somente o opcode lógico
`0x0F`, e `World:0x0041FB30`. O request é isento da sequência normal. O único gate do handler é
`GameInfoId != 0`; falha encerra com `DISC 1A`, sem exigir personagem ou field.

`FUN_0040BBB0` lê o tick anterior em `user+0x1480`, grava `GetTickCount()` atual e retorna a
diferença. O World apenas registra `ALTO` quando o intervalo é estritamente maior que 90.000 ms e
não envia resposta. A rota .NET agora chega ao handler canônico, atualiza o tick por sessão e usa o
mesmo limiar; o interceptor vazio e o gate artificial `InField` foram removidos.

### CharacterGetUserName `0x19` — 2026-07-16

`tools/ghidra/DecompileCharacterGetUserName.py` comprovou que o nome do export não implica lookup
no banco. `engine.dll:0x36191020` envia `[0x19:u16][value:cstr]`. `World:0x00420760` exige apenas
GameInfoId (`DISC 28` na ausência), aceita `strlen(value) < 13`, copia a mesma C-string e chama
`FUN_0041B940` com msgType interno `0x0D`; comprimento inválido causa `DISC 29`.

O parser associado em `engine.dll:0x36193170` lê o primeiro byte como status e duas C-strings; o
callback `rakion.bin:0x00476450` encerra a espera e trata status `0/1/2`. Essa cadeia é uma ponte
cliente dirigida pelo buffer de entrada, não `characterinfo JOIN usergameinfo`. A rota .NET antiga
consultava o DB e fabricava `[0x19][0x0D][status][account][buddy]`, envelope que não existe no
handler original. Ela foi removida junto com o DTO/repositório exclusivos dessa hipótese; a rota
canônica agora preserva o eco no canal de mensagem.

### InventoryMove `0x31` — 2026-07-16

`tools/ghidra/DecompileInventoryMove.py` extrai builder, handler, helper, parser e callback. O
builder `engine.dll:0x36191810` envia exatamente quatro bytes de coordenadas. O handler
`World:0x00421870` aplica `DISC 3C/3D`, limita box a 120 slots e zona ativa a 19, chama
`FUN_0040CF10` e sempre publica 21 bytes lógicos. `engine.dll:0x36193810` consome os onze campos
na mesma ordem; `rakion.bin:0x0047D1D0` trata status `1..4`, atualiza ambos os descritores e
recalcula equipamento/modelo 3D.

`FUN_0040CF10` comprovou que os três arrays paralelos de item, metadata e valor são trocados junto
com a projeção do slot. Ele não soma stacks. Status `1` representa inventário fechado, `2` mutação
ocupada, `3` origem vazia e `4` incompatibilidade retornada por `FUN_0040BC10`; este último delega
ao catálogo com slot, classe e nível. A implementação anterior fundia poções iguais e mantinha um
handler alternativo chamado `RoomSetMode`. Ambos divergiam do binário e foram substituídos pela
rota única `Op_InventoryMove`.

`0x34` não compara `+0x1440`: `FUN_0040B2C0` bloqueia o subestado de inventário `+0x144C == 2`
e valida pagamento/cupom. Portanto ele não deve receber artificialmente um gate de fase da sessão.
`0x28 @ FUN_0041DE40` foi incluído para registrar a colisão do fluxo de enchant, mas não é case da
jump table principal desta build.

Essa extração também comprovou a divergência do catch-all `.NET`: ele aceitava qualquer opcode
quando `InField=true`, inclusive lacunas que o original envia a `DISC C9`. Uma restrição inicial à
jump table ainda engolia cases implementados como `0x61`, `0x62`, `0x75` e `0x76`; por isso o
catch-all foi removido. Requests não interceptados agora sempre chegam a `WorldHandlers`.

O inventário da tabela canônica não deixa delegates `Stub`: os fallbacks
duplicados de inventário, entitlements e presentes foram substituídos por handlers canônicos. O teste
`FinalDispatcherHasNoStubDelegate` percorre `0x00..0x79` e impede a reintrodução de um stub na tabela
final.

Os contratos foram materializados em `WorldRequestGatePolicy` com as razões originais de identity e
fase. O probe de sala com duas sessões validou `0x0E → 0x14 → 0x3B/0x38 → 0x43 → 0x48 → 0x4B`;
em particular, o `0x48` anterior ao spawn provou que `PrepareRoomMatch` promove todos os membros a
`Status=3` no start. O mesmo probe preservou ready, rejeição de não-host, relays e transferência de
host. A fixture `test2/9001`, ausente no banco local, precisou ser restaurada para o teste.

O probe UDP foi corrigido para a ordem real observada: login, handshakes UDP, `0x0E`, seleção
`0x14`. Com essa ordem, o Release confirmou `0x62 targetSeat→senderSeat`. A sonda
`world_tail_dispatch_probe.py` comprovou no mesmo runtime as rotas finais `0x61`, `0x75..0x79`:
eco e diagnóstico dormente não encerram a sessão, consulta de loteria e clã respondem, compra
inválida para em `E7` sem mutação e disconnect sem texto usa razão `1`.

### CharacterSelect `0x14` canônico — 2026-07-16

`DecompileCharacterSelect.py` reproduz a cadeia completa nas três imagens. O builder
`engine.dll:0x36190E20` finaliza seis bytes lógicos, `[u16 0x14][u32 characterId]`; o handler
`FUN_0041FEF0` lê somente o primeiro `u32` após o cabeçalho. Conta ausente ou personagem já
selecionado encerram com `DISC 0x1D`; id zero encerra com `DISC 0x1E`.

O World percorre no máximo seis registros de personagem já carregados na sessão. Em caso de match,
`FUN_0040BE30` copia nome, equipamento e progressão, `FUN_0040D3F0` limita o valor derivado pela
tabela de classe/nível e `FUN_0040AC30` grava o id e os atributos ativos. Não existe comando DB no
select original. O retorno lógico é sempre `[u16 0x14][u8 status]`: `0` no match e `2` quando os seis
slots não contêm o id. Depois, `FUN_0041B8B0` procura a associação de canal e atualiza a presença.

O parser `engine.dll:0x36192F90` consome somente o primeiro byte. O callback
`rakion.bin:0x0047CB40` mostra mensagens distintas para status `1` (erro do sistema do servidor) e
`2` (personagem inexistente); no status `0`, seleciona o registro local correspondente e conclui a
transição de tela. No `.NET`, o antigo interceptor e o delegate incorreto `Op_FieldGameStart` foram
substituídos por uma única entrada `Op_CharacterSelect`. Como a arquitetura atual relê o banco em
vez de manter os seis registros legados residentes, resultado ausente mapeia para `2`, exceção de
infraestrutura para `1`, e a presença `0x1F/0x1E` só é publicada após `0`.

### InventoryLeave `0x2D` e fila DB `0x13` — 2026-07-16

`DecompileInventoryLeave.py` fecha a cadeia que antes estava misturada com roster e com o opcode
WorldNet `0x13`. O builder `engine.dll:0x36191700` envia somente o `u16 0x2D`. Na abertura, o
comando DB `0x12` termina em `FUN_00427A80`, que envia sete bytes lógicos de `0x2C`; o parser
`engine.dll:0x36193650` consome exatamente `status:u8 + sessionRef:u32`.
`FUN_00420F10` exige conta, personagem e `Status=2`, com razões `0x34/0x35`, e chama
`FUN_0040C960`. Esse helper lê `user+0x144C`: zero retorna `1`, dois retorna `2`; no estado um,
coleta deltas por `FUN_0040BCB0/FUN_0040BC50` e fecha a UI.

Sem delta, o World chama `FUN_004038E0` com comprimento lógico `3` e
`[0x2D:u16][status:u8]`. Com delta, `FUN_0041B940` enfileira o comando DB interno `0x13`;
`FUN_00419730` persiste diferenças de itens e, quando necessário, atributos de `CharacterInfo` e
`powerlevelpoint`. O callback `FUN_0041CCA0` só então envia `0x2D status=0`. Portanto não existe
lista S→C `0x13` nessa operação.

O parser `engine.dll:0x36193680` encaminha somente `param[0]` ao callback
`rakion.bin:0x00474DE0`. A cauda variável observada após esse byte está fora do comprimento lógico
e não deve ser copiada. No `.NET`, `InventoryUiState` representa abertura/fechamento, a mutação
transacional já é persistida em sua própria rota e `Op_InventoryEnter/Op_InventoryLeave` são a única
entrada final da tabela; o interceptor e `Op_RoomRosterSync` foram removidos.

### InventoryBuy `0x2E` e InventorySell `0x2F` canônicos — 2026-07-16

Uma extração limpa com `DecompileWorldShopRequest.py`, `DecompileClientShopPurchase.py` e
`DecompileRakionShopCallbacks.py` confirmou os dois contratos completos. O builder de compra
`engine.dll:0x36191740` produz
`[itemId:u16][currency:u8][couponFlag:u8][couponSlot:u16 se couponFlag=1]`; `currency=0` significa
Cash e qualquer valor não zero significa Gold. Somente o flag exatamente igual a `1` ativa cupom.
O handler `World:0x00421210` exige identidade e `Status=2` (`DISC 36/37`) e rejeita item fora do
catálogo com `DISC 37`.

O helper `FUN_0040CB10` fecha os retornos de compra: `1` UI fechada, `2` mutação ocupada, `3`
quote/saldo/criação, `4` falta de espaço e `0x14/0x15/0x16` para item de cupom inválido, moeda
incompatível e definição ausente. Sucesso segue pelo message subtype `0x14`; erros usam o frame
direto `[0x2E:u16][status:u8]`.

O builder de venda envia `[slot:u8]`. `World:0x004215A0` exige identidade e `Status=2`
(`DISC 39/3A`) e limita o slot a `0..119` (`DISC 3B`). `FUN_0040CD70` devolve `1` para UI fechada,
`2` para mutação ocupada e `3` para origem vazia; sucesso segue pelo subtype `0x15` e erros pelo
frame direto `0x2F`. Não há gate de `InField/FieldSecondary` no original.

No `.NET`, os cases paralelos e `Op_0x2E_Recon/Op_0x2F_Recon` foram removidos. A tabela final agora
aponta diretamente para `Op_InventoryBuy/Op_InventorySell`, os parsers preservam as comparações
não binárias originais e todas as mutações de inventário compartilham o mesmo lock atômico de
sessão. Persistência, callbacks e saldos continuam transacionais em `ClientSession.Shop.cs`.

### InventoryBuyBag `0x32` e InventoryBuyCharacterSlot `0x35` canônicos — 2026-07-16

`FindInventoryEntitlementFlows.py` confirmou que `World:0x004226B0/0x00422850` exigem conta,
personagem e `Status=2`, com `DISC 3F/40` e `DISC 41/42`. Não há leitura de
`InField/FieldSecondary`. Ambos leem `mode:u8` e, quando ele é diferente de zero, também
`couponSlot:u16`; somente `mode==1` é interpretado como cupom pelos helpers
`FUN_0040B080/FUN_0040B1A0`.

Os helpers devolvem `1` com a UI fechada, `2` com mutação ocupada, `3` no limite e `4` sem saldo;
`0x14/0x15/0x16` preservam os erros de cupom. Bag custa `8000` e limita `user+0x1540` a `3`;
character slot custa `12000` e limita `user+0x1541` a `6`. Sucesso segue pela fila DB interna com
subtypes `0x16/0x18`, enquanto erro é enviado diretamente em `0x32/0x35`.

No `.NET`, os dois cases do interceptor e as sobrescritas `Op_InterceptedRoute` foram removidos.
Os parsers aceitam a semântica não binária original, os handlers canônicos compartilham o lock de
inventário e as transações existentes continuam sendo a única fonte para carteira, entitlement,
ledger, cupom e random present.

### InventoryBuyPowerUser `0x34` canônico — 2026-07-16

`FindInventoryEntitlementFlows.py` confirmou que `World:0x00422B10` lê
`[mode:u8][couponFlag:u8][couponSlot:u16 se flag!=0]`. Apenas `mode=0/1` é válido; outro valor
causa `DISC 45`. O slot é lido para qualquer flag não zero, mas `FUN_0040B2C0` só considera cupom
quando o flag é exatamente `1`; os demais valores seguem pelo pagamento Cash.

Ao contrário de `0x32/0x35`, esse handler não testa identidade nem `user+0x1440`. O helper apenas
rejeita `user+0x144C==2` com status `2`, aplica preços `8000/6000`, retorna `3` sem saldo e
`0x14/0x15/0x16` nos erros de cupom. Sucesso enfileira o comando DB `0x17`; o callback externo
mantém gold, cash, marcador de validade, bonus points e presentes.

No `.NET`, o case antecipado, `Op_InterceptedRoute`, `Op_0x34_Recon` e o gate legado de
`InField` foram removidos. `Op_InventoryBuyPowerUser` é a única entrada final. O lock atômico de
inventário produz status `2` imediatamente em concorrência, enquanto o semaphore de storage ainda
protege a transação contra enchant e outras mutações persistentes.

### PresentPeek/Accept/Dispose `0x6B..0x6D` canônicos — 2026-07-16

`FindPresentInboxFlow.py` confirmou que `World:0x004286A0/0x00428750/0x00428A10` exigem somente
conta e personagem selecionado, com `DISC C2/C3/C4`; nenhum deles compara fase ou flags de field.
Peek não recebe corpo. Accept lê apenas `[pendingId:i32][slot:u16]` e Dispose lê
`[pendingId:i32]`; bytes posteriores ao prefixo lógico não são inspecionados.

Accept serializa o snapshot de inventário e enfileira o comando DB `0x1F`; Dispose enfileira
`0x20`. Os callbacks mantêm `DISC C5/C6` quando o item indicado não é a cabeça válida da fila e
publicam os frames externos `0x6C/0x6D` nos demais resultados. Peek usa o comando `0x1E` e retorna
o registro atual em `0x6B`.

No `.NET`, os cases do interceptor, as três sobrescritas `Op_InterceptedRoute` e os gates legados
de `InField/FieldSecondary` foram removidos. `Op_PresentPeek/Accept/Dispose` são as únicas entradas;
os parsers mínimos ignoram a cauda de transporte e a persistência FIFO transacional existente foi
preservada.

### Entitlements vazios `0x6F/0x70/0x71` canônicos — 2026-07-16

`FindInventoryEntitlementFlows.py` confirmou que os três requests não possuem corpo. Os handlers
exigem conta, personagem e `Status=2`: `0x6F` usa `DISC D3/D4`, `0x70` usa `D9/DA` e `0x71` usa
`DB/DC`. Nenhum lê padding, `InField/FieldSecondary` ou estado de compra ocupada.

`0x6F @ 0x00428D80` seleciona produtos `10008/10009/10010` apenas quando `potionslot` é `3/4/5`;
qualquer outro total termina em `DISC D5`. `0x70 @ 0x004292B0` escolhe `10011/10012/10013` pelas
faixas de nível `10..20`, `21..40` e `>40`, deixando produto zero fora delas. `0x71 @ 0x004293F0`
sempre enfileira o produto `10014`. Os comandos DB internos são `0x21/0x23/0x24`.

No `.NET`, os cases antecipados e as últimas três sobrescritas `Op_InterceptedRoute` de entitlement
foram removidos. `Op_InventoryBuyPotionSlot`, `Op_InventoryBuyStageRankClear` e
`Op_InventoryBuyStageLevelFree` são as entradas únicas; o `DISC D5` foi restaurado e os gates
artificiais de padding, field e busy foram eliminados.

### Whisper e localização `0x16/0x17/0x18` — 2026-07-16

`DecompileWorldWhisperLocation.py` fecha em conjunto os handlers do World, parsers da engine e
callbacks da UI. `FUN_00420200` exige apenas `user+0x1460` e `user+0x14A4`, limita o nome a 12 e o
texto a 128 bytes e percorre todos os slots. O alvo exige `+0x1440 != 0`, as mesmas duas identidades,
`+0x146C != 1` e `FUN_0040AF20`; esta última chama `lstrcmpA` sobre `CharName`, logo a busca é global
e case-sensitive. O retorno de sucesso `0x16` contém status zero, nome do remetente e texto, e é
enviado ao alvo e ecoado ao remetente. Status um não possui cauda.

`FUN_00420410` trata `Status=2` com `FUN_0040AF90`, serializando `user+0x148C` como `kind=0`; para
`Status=3`, `FUN_0040B7D0` fornece `user+0x14A0` como `kind=1`. Slot de canal `+0x148D` e seat de
field `+0x14A2` são lidos pelos helpers, mas não entram no frame. O primeiro byte da resposta não é
status: `FUN_0042CEE0` carrega `[Server].ServerId` em `World+0x54`, lido pelo handler. O layout é
`[0x17:u16][ServerId:u8][kind:u8][locationId:u16]`.

`FUN_00420520` usa a mesma busca exata e produz status zero, nome solicitado, kind e ID; alvo
encontrado fora dos estados 2/3 cai no mesmo status um de não encontrado. Os parsers
`engine.dll:0x36193000/0x361930B0/0x361930F0` confirmam os três layouts. Os callbacks
`rakion.bin:0x00475A30/0x00475D80/0x00475F10` confirmam que o whisper é exibido em canal, sala e
field e que os dois kinds selecionam textos de localização diferentes.

### Prova de `/roominfo` — 2026-07-16

`DecompileWorldRoomInfo.py` extraiu `FUN_0041BCA0` e `FUN_00406B10`. O primeiro procura o primeiro
`':'`, compara `"/roominfo"` em `colon+2`, aplica `atol` em `colon+12` e consome o comando sem
broadcast. Somente IDs no intervalo `0..MaxField-1` chamam o serializer; não existe gate de field
ocupado.

`FUN_00406B10` faz 26 chamadas a `FUN_0041B8A0`: seis linhas fixas e vinte registros. Cada buffer
começa por `[0x22:u16][sender:u8=0]`; o comprimento é `strlen(text)+3`, logo a mensagem diagnóstica
não carrega NUL. Os offsets confirmados são ID `+0`, state `+8`, creator `+9`, title `+0x16`, senha
`+0x3F`, níveis/basic `+0x111..113`, map/mode `+0x118/119`, master `+0x121`, tunneling `+0x2CC`,
voto `+0x2D0..2D2` e records `+0x124`, stride `0x14`.

## Variações que não podem ser copiadas

- handles/ponteiros vistos em `0x0E` e `0x2C` pertencem à sessão; `0x14` leva apenas o personagem,
  enquanto `0x2D` não tem corpo e `0x36` contém somente a consulta de lista;
- a cauda dos blocos `0x0E`, `0x14`, `0x2D`, `0x36`, `0x3B`, `0x43`, `0x48` e `0x4A` variou entre
  capturas e não integra o request lógico; no retorno W→C `0x48`, `FUN_00408440/00409940`
  fechou nove bytes lógicos;
- slot global e nome em `0x1E/0x1F` pertencem à sessão/personagem atual;
- portas `0x0E` foram reescritas pelo MITM para `51708/51709`; o contrato exige endpoint observado
  e endpoint P2P anunciado, não esses números fixos;
- o `0x36` extra ocorre na primeira armação, não em todo polling.

## Lacunas ainda abertas nesta frente

1. capturar no cliente gráfico lista com duas salas e dois jogadores para comparar o registro
   variável de `0x36`; a forma gerada pelo .NET já foi exercitada por duas sessões headless;
2. capturar visualmente a conexão P2P direta e o corpo reliable de entidades em `2300..2399`;
3. obter captures dos opcodes rejeitados/inativos apenas se uma UI dessa build realmente os emitir.
