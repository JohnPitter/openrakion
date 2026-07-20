# Sistema de clã — RE, implementação e ativação

Este é o documento canônico do sistema de clã do Rakion v258. Ele separa o que foi observado no
cliente/servidor original, o que está implementado no servidor .NET e o que ainda exige validação no
cliente real.

## Estado atual

O ciclo básico de clã está implementado de ponta a ponta no backend:

- snapshot de clã no login World `0x0C`, com layout obtido por captura diferencial do servidor
  original;
- leitura das tabelas legadas `claninfo` e `usergameinfo`;
- criação, inclusão, remoção, transferência de mestre, árvore e dissolução pelo Admin;
- transações InnoDB, locks, reconciliação de membros, auditoria encadeada e logs críticos;
- presença de canal com `clanId` real;
- perfil guild do Buddy em `0x3102/0x3103`;
- ranking de membros e clãs pelo job separado de ranking;
- feature flag `[Clan]`, desligada por padrão.

O sistema está **fechado em RE e validado headless**. O uso visual da presença também foi fechado
estaticamente: no canal, `clanId` escolhe a cor do nome do personagem; ele não transporta nome nem
brasão. Ao entrar na sala, a tela do canal e suas linhas são destruídas, e o roster da sala/campo não
possui campo de clã. A aparência efetivamente renderizada ainda precisa ser conferida com dois
clientes; nenhum byte foi inventado para criar uma tag remota que esta build não recebe.

## Evidência do original

A referência dinâmica foi uma cópia descartável do servidor original, com banco isolado. A mesma
conta foi capturada no login sem clã e depois com alterações unitárias de nome, mestre, buddy name,
rank, pontos, árvore e filhos. O pacote `0x0C` foi descriptografado e comparado byte a byte.

Resultados confirmados:

- `clanId` fica no offset fixo `+0x11` do corpo `0x0C`;
- o nome do clã inicia em `+0x15` e torna o restante do cabeçalho variável;
- o nome exibido é `buddyname`, com fallback para `charname`, não o ID da conta;
- o personagem do mestre vem da conta indicada por `claninfo.masterid`;
- a árvore contém pares conta/personagem e serializa no máximo sete filhos; o oitavo é omitido pelo
  original;
- a presença de canal `0x1E/0x1F` termina com `user+0x14D0`, o `clanId` carregado no login;
- `engine.dll` monta cada presença em uma estrutura local de `0x24` bytes, com `clanId` em `+0x14`;
- `rakion.bin:FUN_0040CBF0/FUN_0040CC40` escolhem `0x8080C0FF` para ID zero e `0xFFFF80FF`
  para ID não zero; a linha copia os `0x24` bytes para `this+0x180`, mas seu draw
  `FUN_0043FEE0` imprime somente o nome em `this+0x184` usando a cor em `this+0x17C`;
- `FUN_0040B7F0`, o serializer completo do jogador da sala, não lê `user+0x14D0` nem outro campo
  de clã. `GroupId` não deve ser reinterpretado como clã.
- `FUN_0047A370`, na transição para a sala, destrói o componente ativo em
  `DAT_004FEED0+0x17C` antes de criar a tela de sala; portanto, a linha de presença não funciona
  como cache de clã para o roster.

A decompilação principal do login original é `FUN_004107d0`. A consulta de membros `0x78` vem de
`FUN_0041BDE0`, `FUN_0040F610` e `FUN_0041E1A0`.

## Contrato World `0x0C`

O início do corpo é fixo:

| Offset | Tipo | Campo |
|---:|---|---|
| `+0x00` | `u8` | comando `0x0C` |
| `+0x03` | `u8` | sucesso `1` |
| `+0x07` | `u16 LE` | slot de rede |
| `+0x09` | `u32 LE` | chave da sessão UDP |
| `+0x0D` | `u32 LE` | marcador corrente do servidor em minutos desde `TO_DAYS(0)` |
| `+0x11` | `i32 LE` | clan ID; zero quando não há clã |
| `+0x15` | variável | bloco abaixo |

Depois de `+0x15`, os campos aparecem exatamente nesta ordem:

```text
clanName\0
u32 clanRank
u16 memberCount
u32 clanPoint
u32 personalClanPoint
u32 personalClanRank
masterCharacterName\0
displayName\0
u32 powerTimeMarker
u16 powerLevelPoint
u16 country
treeUpperAccount\0
treeUpperCharacter\0
u8 treeRank
u8 childCount
childCount * (childAccount\0, childCharacter\0)
u32 gold
u32 cash
u8 characterSlotCount
character records
20-byte trailer, iniciado por 0x03
```

Limites provados pelo layout: clã, display e personagem têm até 12 bytes ASCII; conta tem até 16;
`childCount <= 7`. O codec rejeita valores fora do contrato. Goldens preservam também o frame sem
clã já aceito pelo cliente.

A implementação está em
[`LoginCharListWriter.cs`](../../../server/RakionServer/src/RakionServer.World/CharSelect/LoginCharListWriter.cs),
o DTO imutável em
[`ClanLoginSnapshot.cs`](../../../server/RakionServer/src/RakionServer.World/Domain/ClanLoginSnapshot.cs)
e a leitura em
[`WorldDatabase.Clans.cs`](../../../server/RakionServer/src/RakionServer.World/Database/WorldDatabase.Clans.cs).

## Outros contratos

### Presença de canal

O registro confirmado é:

```text
characterName\0, u8 class, u8 subStatus, u32 clanId
```

Depois do parse, o cliente usa esta estrutura alinhada:

| Offset | Tipo | Campo |
|---:|---|---|
| `+0x00` | `u8` | slot local do canal |
| `+0x02` | `u16` | slot global da sessão |
| `+0x04` | `char[13]` | personagem |
| `+0x11` | `u8` | classe |
| `+0x12` | `u8` | substatus |
| `+0x14` | `u32` | clan ID |

O World .NET já publica o ID real. A UI não resolve o ID para nome/brasão nesse fluxo: ela apenas
pré-calcula a cor da linha (`0x8080C0FF` sem clã, `0xFFFF80FF` com clã) e desenha o nome do
personagem. Os literais são documentados sem impor ARGB/RGBA, pois a ordenação dos canais do
renderer não foi validada visualmente.

### Consulta World `0x78`

O original consulta até 99 outros membros do mesmo clã e responde:

```text
sucesso: u16 0x78, u8 0, u16 count, count * (account\0, buddyName\0)
vazio:   u16 0x78, u8 1
falha:   u16 0x78, u8 2
```

O codec e a consulta estão implementados. Porém, a build v258 analisada não possui produtor conhecido
nem case de resposta `0x78`; essa compatibilidade permanece dormente e não é apresentada como tela
funcional.

### Buddy `0x3102/0x3103`

O Buddy aceita `SVC_SET_GUILD = 0x3102`, persiste o nome de guild do perfil e responde
`RET_SET_GUILD = 0x3103` com `u16 result`. Nome vazio limpa o campo. Isso pertence ao perfil do
messenger; `usergameinfo.clanid` continua sendo a fonte de verdade do clã. A gestão no Admin não
depende desse texto e não o usa para autorização.

### Sala e campo

O serializer original completo `FUN_0040B7F0` não transporta clã: ele lê os dois nomes,
estado/grupo, endpoints, classe, nível, equipamentos e quickslots, sem acessar `user+0x14D0`.
No cliente, `0x37/0x38` constroem registros `FieldInfo` de `0x378` bytes a partir desse mesmo
contrato. A transição destrói a tela do canal, que era a proprietária das linhas de presença; não há
reuso do `clanId` no roster, loading ou HUD por esse caminho.

Assim, a build v258 analisada não tem dados remotos suficientes para exibir tag/brasão de clã na
sala ou partida. Isso não impede a tela de informações do próprio usuário, alimentada pelo login
`0x0C`. O servidor não deve alterar `GroupId`, acrescentar trailer especulativo nem prometer uma tag
remota sem criar uma extensão cliente/servidor explícita.

## Fonte de verdade e regras

| Dado | Fonte |
|---|---|
| associação | `usergameinfo.clanid` |
| mestre | `claninfo.masterid` |
| nome, pontos, rank e país | `claninfo` |
| pontos/rank do membro | `usergameinfo.clanpoint/clanrank` |
| superior e rank da árvore | `usergameinfo.treeuppername/treerank` |
| quantidade de membros | projeção `claninfo.members`, reconciliada na transação |
| snapshots de ranking | `clanrankp`, reconstruível pelo Ranking |

As mutações aplicam estas regras no backend:

- nome único com 1..12 caracteres ASCII (`A-Z`, `a-z`, dígitos, espaço, `_` e `-`);
- identificador de conta com 1..16 caracteres ASCII imprimíveis, sem espaços;
- no máximo 99 membros e sete filhos por superior;
- uma conta só pertence a um clã;
- superior e filho pertencem ao mesmo clã, sem autorreferência ou ciclo;
- o mestre não pode ser removido: primeiro deve transferir a liderança ou dissolver;
- remoção/dissolução limpa associação, pontos, rank, grade e árvore;
- `clangrade` não concede autoridade: sua enumeração não foi provada. A liderança é definida por
  `masterid`.

## Implementação administrativa

A página `/clans` do Admin permite listar e selecionar clãs, criar, adicionar/remover membro,
transferir liderança, alterar o superior da árvore e dissolver com confirmação pelo nome. Somente o
papel `Owner` possui `ClanWrite`; `Operator` e `Viewer` são rejeitados antes do I/O.

Cada escrita:

1. valida entrada e justificativa;
2. abre transação `Serializable`;
3. exige `claninfo` e `usergameinfo` em InnoDB;
4. bloqueia clã e contas com `FOR UPDATE`;
5. aplica a regra e reconcilia `members`;
6. grava hashes antes/depois na cadeia `admin_audit`;
7. confirma e registra o fluxo crítico.

O cliente não demonstrou CRUD de clã no protocolo World. Por isso o Admin é deliberadamente a borda
de escrita, e o cliente reflete alterações no próximo login. Não existe push entre processos para
uma sessão World já conectada.

## Configuração

Em `worldserver.ini`:

```ini
[Clan]
Enabled=0
MaxMembers=99
TreeMaxChildren=7
```

Os limites são presos a `1..99` e `1..7`. Com `Enabled=0`, o login recebe snapshot vazio mesmo que
existam dados no banco. Isso permite rollback operacional sem apagar clãs.

## Como implantar e ativar

### 1. Parar e fazer backup

Pare World, Buddy, Ranking e Admin. Faça backup completo antes de converter engines; `claninfo`,
`clanrankp` e `clanschedule` podem estar em MyISAM no dump legado.

Exemplo com cliente MariaDB disponível no `PATH`:

```powershell
mariadb-dump -h 127.0.0.1 -u root -p --lock-tables rakion > rakion-before-clan.sql
```

### 2. Executar o preflight

Estas consultas devem retornar zero linhas:

```sql
SELECT name, COUNT(*) FROM claninfo GROUP BY name HAVING COUNT(*) > 1;

SELECT g.name, g.clanid
FROM usergameinfo g LEFT JOIN claninfo c ON c.id = g.clanid
WHERE g.clanid <> 0 AND c.id IS NULL;

SELECT c.id, c.masterid
FROM claninfo c LEFT JOIN usergameinfo g ON g.id = c.masterid AND g.clanid = c.id
WHERE g.id IS NULL;

SELECT c.id, c.members, COUNT(g.id) AS actual
FROM claninfo c LEFT JOIN usergameinfo g ON g.clanid = c.id
GROUP BY c.id, c.members HAVING c.members <> actual;
```

Corrija duplicatas, órfãos, mestre inválido e contagem divergente antes da migração.

### 3. Aplicar a migração explícita

Use
[`001_clan_innodb.sql`](../../../server/RakionServer/deploy/migrations/001_clan_innodb.sql):

```powershell
mariadb -h 127.0.0.1 -u root -p rakion -e `
  "source server/RakionServer/deploy/migrations/001_clan_innodb.sql"
```

Ela converte as tabelas de clã e cria os índices de nome, associação, árvore e ranking. O Admin se
recusa a alterar clãs se `claninfo` ou `usergameinfo` não for InnoDB.

### 4. Compilar e testar

```powershell
dotnet build `
  server/RakionServer/RakionServer.sln -c Release
dotnet test `
  server/RakionServer/tests/RakionServer.World.Tests/RakionServer.World.Tests.csproj -c Release
```

Para incluir os smokes isolados de banco:

```powershell
$env:RAKION_MYSQL_SMOKE_CONNECTION = `
  'Server=127.0.0.1;Port=3306;User ID=root;Password=123456;SslMode=None'
```

Os testes criam e removem schemas temporários; não modificam o schema `rakion` informado.

### 5. Fazer um piloto ainda desativado

Mantenha `Enabled=0`, suba a stack e confirme que contas sem clã continuam entrando. Pelo Admin,
crie um clã piloto, adicione duas contas e configure uma relação de árvore. Confirme a auditoria e os
dados no banco.

### 6. Ativar e validar o cliente

Mude `Enabled=1` e reinicie o World. Faça novo login — sessão já conectada não recebe atualização.
Valide nesta ordem:

1. nome, rank, pontos, mestre e membros na conta com clã;
2. buddy name e fallback para personagem;
3. árvore com zero, um e sete filhos;
4. conta sem clã e troca de clã após relog;
5. diferença de cor da linha no canal entre conta sem clã e conta com clã;
6. confirmar a ausência esperada de tag remota em sala, loading, partida e resultado nesta build.

Todos possuem contrato/implementação estática; o gate restante é confirmar o resultado gráfico e a
interpretação visual dos dois literais de cor.

## Rollback

1. Defina `[Clan] Enabled=0` e reinicie o World.
2. Preserve os dados: o login volta a emitir snapshot vazio.
3. Se a migração precisar ser revertida, pare toda a stack e restaure o backup completo. Não restaure
   somente `usergameinfo` ou somente `claninfo`, pois isso cria associações órfãs.

Não é necessário voltar as tabelas para MyISAM para desativar o recurso.

## Cobertura e limites conhecidos

Cobertura automatizada atual:

- goldens exatos do login sem clã e com clã;
- limites de strings e sete filhos;
- leitura do snapshot em MariaDB isolado;
- ciclo Admin completo, autorização, auditoria, ciclos e idempotência no limite da árvore;
- migração validada em schema isolado;
- Buddy guild e consulta `0x78` cobertos por codec/banco.

Pendências explícitas:

- teste visual multi-cliente da cor de presença no canal e da ausência esperada de tag remota em
  sala/partida;
- se o produto exigir tag/brasão remoto em sala ou partida, desenhar e versionar uma extensão de
  protocolo e do cliente; ela não existe no contrato original mapeado;
- push de alteração para sessões já conectadas, se isso for necessário ao produto;
- guerra de clãs: `clanschedule` existe, mas não foi encontrado transporte ativo nessa build. Não faz
  parte do caminho funcional comprovado da v258;
- a finalidade visual exata de `country`/brasão precisa de validação no cliente; o wire envia o campo
  confirmado sem renomeá-lo para `emblemId`.

Essas pendências não invalidam membership, login, árvore, Admin ou ranking headless; elas delimitam o
que ainda não foi observado visualmente.
