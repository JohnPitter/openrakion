# Engenharia reversa de ranking — Rakion v258

## Veredito

O ranking original não nasce do resultado de uma partida e não concede prêmio. Ele é recalculado
por um executável one-shot separado, `RankUpdate.exe`, usando EXP, `clanpoint` e pontos do clã já
persistidos. O World carrega `totalrank`, `classrank` e `rankgrade` no login e os serializa no
registro de personagem do `0x0C`.

A reimplementação está em
[`RakionServer.Ranking`](../../../server/RakionServer/src/RakionServer.Ranking). Ela reproduz o job
original, atualiza os campos canônicos e publica as sete tabelas `*rankp`. Não foi criado um sistema
de reward porque nenhum grant, item, moeda, temporada ou tabela de prêmio existe no binário
analisado.

## Fontes e cadeia de evidência

- original: `server/RIS_RakionUpdate/RankUpdate.exe`;
- SHA-256: `5B1047AC0691C161DA9565019021F05CF621292031A06AA54683B0147D8EA7F3`;
- configuração original: `server/RIS_RakionUpdate/Config.ini`;
- DDL: `server/DB/rakion_all.sql`;
- RE reproduzível:
  [`DecompileRankUpdate.py`](../../../tools/ghidra/DecompileRankUpdate.py) e
  [`FindWorldRankingFlows.py`](../../../tools/ghidra/FindWorldRankingFlows.py).

O `Config.ini` original define `ProcessTime=24` e `RankMonth=2`. O executável é executado uma vez e
encerra; a periodicidade de 24 horas pertence ao agendador externo.

## Contrato original

### População elegível

Para cada país de `1` a `254`, entram personagens cujo usuário conectou nos últimos `RankMonth`
meses. Personagens com `characterinfo.auth` igual a `2`, `10` ou `52` são excluídos.

O ranking total é independente por país, ordenado por EXP decrescente. A regra é competition rank:
se a sequência de EXP for `100, 100, 90`, as posições são `1, 1, 3`. A implementação adiciona ID
crescente como segunda ordenação; isso não altera posições empatadas e elimina a ordem instável do
MySQL original na fronteira das grades.

### Grades

EXP abaixo de 32.000 usa faixas fixas:

| EXP | Grade |
|---:|---:|
| `0–999` | 26 |
| `1.000–2.499` | 25 |
| `2.500–3.999` | 24 |
| `4.000–5.999` | 23 |
| `6.000–7.999` | 22 |
| `8.000–10.499` | 21 |
| `10.500–12.999` | 20 |
| `13.000–16.999` | 19 |
| `17.000–19.999` | 18 |
| `20.000–23.999` | 17 |
| `24.000–27.999` | 16 |
| `28.000–31.999` | 15 |

Para EXP a partir de 32.000, a população é contada por país. O original reserva 1 jogador na grade
1, 4 na grade 2 e 16 na grade 3. As grades 4–13 usam cumulativos inteiros próximos de 0,1%, 1%, 3%,
6%, 10%, 17%, 27%, 40%, 56% e 76%; grade 14 recebe o restante. A implementação preserva a máquina
de estados exata, inclusive o comportamento peculiar quando uma faixa calculada tem tamanho zero.

### Ranking por classe

Depois do ranking total, os personagens são agrupados globalmente, sem separar país, por classe:

| Classe | Snapshot |
|---:|---|
| 0 | `swordmanrankp` |
| 1 | `archerrankp` |
| 2 | `blacksmithrankp` |
| 3 | `magerankp` |
| 4 | `ninjarankp` |

Cada classe usa EXP decrescente e competition rank. `characterinfo.classrank` anterior vira
`lastrank` no snapshot da classe e permanece em `totalrankp.classrank`; o rank novo vai para a
tabela da classe e para `characterinfo`. `totalrank` anterior vira `totalrankp.lastrank`.

### Clãs

- membros com `clanid > 0` são ordenados por `clanid` e `clanpoint` decrescente;
- `usergameinfo.clanrank` é competition rank reiniciado dentro de cada clã;
- clãs são ordenados globalmente por `claninfo.point` decrescente;
- `claninfo.rank` anterior é publicado como `clanrankp.lastrank`.

O binário antigo truncava o `clanid` ao comparar a troca de grupo. A implementação usa o inteiro
completo, evitando misturar clãs cujos IDs tenham o mesmo byte baixo.

## Persistência e publicação

O job lê `characterinfo`, `usergameinfo` e `claninfo`, calcula tudo em memória e monta tabelas de
staging clonadas dos snapshots atuais. Só depois atualiza os campos canônicos:

- `characterinfo.rankgrade`, `totalrank`, `classrank`;
- `usergameinfo.clanrank`;
- `claninfo.rank`.

Por fim, um único `RENAME TABLE` troca os sete snapshots:

- `totalrankp`;
- os cinco `*class*rankp` listados acima;
- `clanrankp`.

Um named lock MySQL impede duas execuções concorrentes. Falha antes da troca mantém o snapshot
anterior. Como fonte e projeção podem estar em conexões/bancos diferentes, não existe transação
distribuída entre a atualização canônica e o rename; essa é também uma limitação estrutural do
original. Alertas e falhas do job são logados.

## Compilar e executar manualmente

Pré-requisitos: .NET 9, MariaDB acessível e as sete tabelas `*rankp` do dump importadas no banco de
projeção.

```powershell
dotnet --info
$env:ConnectionStrings__Rakion = 'Server=127.0.0.1;Database=rakion;Uid=rakion;Pwd=troque;'
$env:ConnectionStrings__RakionRank = 'Server=127.0.0.1;Database=rakionrank;Uid=rakion;Pwd=troque;'
$env:Ranking__ActiveMonths = '2'

dotnet run --project .\server\RakionServer\src\RakionServer.Ranking -c Release
```

`ConnectionStrings__RakionRank` é opcional e usa `ConnectionStrings__Rakion` quando omitida. Isso
suporta instalações que mantêm os snapshots no mesmo schema. Senhas não devem ser versionadas nem
colocadas na linha de comando.

Uma execução bem-sucedida termina com código `0`; cancelamento retorna `2`; falha retorna `1` e
mantém o snapshot anterior sempre que a troca ainda não ocorreu.

## Ativar a atualização diária no Windows

Publique o executável:

```powershell
dotnet publish .\server\RakionServer\src\RakionServer.Ranking\RakionServer.Ranking.csproj `
  -c Release -o $env:RAKION_RANKING_DIR
```

Defina as três variáveis no ambiente da conta de serviço do agendador. Depois registre a tarefa para
rodar uma vez por dia; o processo não deve ficar residente:

```powershell
$rankingExe = Join-Path $env:RAKION_RANKING_DIR 'RakionRankUpdate.exe'
$action = New-ScheduledTaskAction -Execute $rankingExe
$trigger = New-ScheduledTaskTrigger -Daily -At '00:05'
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName 'OpenRakion-Ranking' -Action $action -Trigger $trigger `
  -Settings $settings -Description 'Atualiza os rankings do Rakion uma vez por dia'
```

Valide primeiro com `Start-ScheduledTask -TaskName 'OpenRakion-Ranking'` e confira o log e as datas
dos snapshots. Não inclua esse executável no `start-stack.ps1`: ele é batch diário, não servidor.

## Validação

Os testes cobrem ranking total por país, ranking global por classe, empates, limites fixos de EXP,
distribuição proporcional, a peculiaridade de população pequena, rank de membro por clã e rank
global de clãs:

```powershell
dotnet test .\server\RakionServer\tests\RakionServer.World.Tests\RakionServer.World.Tests.csproj `
  -c Release
```

Antes de produção, execute o job contra uma cópia do banco e confira:

1. contagem de elegíveis dos últimos dois meses;
2. empates `1, 1, 3` em total/classe/clã;
3. `lastrank` igual ao valor anterior;
4. ausência dos auth `2`, `10` e `52`;
5. troca conjunta dos sete snapshots;
6. login de um cliente exibindo os novos `totalrank`, `classrank` e `rankgrade`.

O smoke test local em banco temporário cobriu dois países, duas classes, dois clãs, empates e a troca
dos sete snapshots. O resultado confirmou total rank `1, 1, 3`, reinício por país/clã, classe global
e grades fixa/proporcional sem alterar o banco de desenvolvimento.

### E2E job→login — 2026-07-18

`RankingJobWireE2ETests` executa o `RankingJob` contra o MariaDB do harness, preserva os campos
canônicos e faz backup físico dos sete snapshots antes da jornada. O teste confirma grade fixa,
rank total por país, rank global de classe, `lastrank` total/classe, troca dos sete snapshots e
ausência de tabelas `_next/_previous`. Em seguida conecta um cliente headless e valida os mesmos
valores no DTO e no frame `0x0C` real.

Essa jornada encontrou uma lacuna na projeção: `WorldDatabase` lia `totalrank/classrank`, mas não
lia `rankgrade`, e `LoginCharListWriter` descartava todos os três. A borda foi alinhada à
decompilação do produtor original: no bloco de campos após `name\0`, `classrank` ocupa `+12` em um
byte, `auth` ocupa `+13` em quatro bytes, `totalrank` ocupa `+17` em quatro bytes e `rankgrade`
ocupa `+21`. Goldens preservam os registros anteriores e um teste dedicado fixa esses offsets.

## O que não pertence a este sistema

- `0x70` é compra de Stage Rank Clear;
- `0x71` é Stage Level Free;
- placar/MVP da sala é estado efêmero de partida;
- não há temporada, liquidação ou reward de ranking comprovado nesta build.

Criar prêmio sazonal pode ser uma extensão futura, mas precisa ser documentada como funcionalidade
nova, com grant transacional e idempotente, e não como reconstrução do Rakion v258.
