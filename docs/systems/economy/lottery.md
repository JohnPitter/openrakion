# Engenharia reversa da loteria — Rakion v258

## Estado atual

O World está reconstruído para as duas operações comprovadas no executável original:

- compra de bilhete `0x75`, em gold ou cash, com débito e persistência atômicos;
- consulta paginada de bilhetes `0x76`, dez registros por página.

Essas rotas são compatibilidade dormente para o cliente v258 disponível. O `engine.dll` encerra o
dispatcher World S→C em `0x74`: não aceita as respostas `0x75/0x76`, e também não foram encontrados
builders desses requests no `engine.dll` ou no `rakion.bin`. Portanto, a UI não pode ser ativada
fielmente com este conjunto de binários, embora o World original contenha o backend.

O sorteio e o pagamento dos vencedores **não estão implementados**. Nenhum dos executáveis
disponíveis produz linhas em `loglottery` ou liquida prêmios. O World apenas lê resultados que outro
componente gravava. Inventar esse componente seria incompatível com um RE fiel.

Validação atual: testes golden e de regra aprovados, build sem warnings e smoke MariaDB isolado com
compra em gold, compra em cash, saldo insuficiente e paginação. Esses testes validam o backend; não
representam uma UI utilizável no cliente v258.

## Fontes e funções reconstruídas

Fonte primária: `World/WorldServ.exe` da distribuição v258 e o DDL `server/DB/rakion_all.sql`.

| Endereço | Responsabilidade |
|---|---|
| `0x4222A0` | recebe compra `0x75`, valida pagamento, saldo e duplicidade |
| `0x40EC50` | consulta wallets, obtém rodada, insere `lotto` e debita |
| `0x41DFB0` | callback da compra e resposta ao cliente |
| `0x4225D0` | recebe página da consulta `0x76` |
| `0x40F0A0` | carrega até dez bilhetes por página |
| `0x41E0C0` | serializa a página ao cliente |
| `0x40F2F0` | carrega as últimas sete linhas de `loglottery` e o pool de gold |

As evidências reproduzíveis são geradas por:

```powershell
py tools/ghidra/FindWorldLotteryFlows.py
py tools/ghidra/AuditClientWorldLotterySupport.py
```

O primeiro script roda no projeto do `worldserv.exe`. O segundo roda no projeto do `engine.dll` e
gera `<diretorio-de-evidencias>/client_world_lottery_support.txt`, comprovando `0x75=absent`,
`0x76=absent` e maior
case `0x74`.

## Wire protocol

### Compra `0x75`

Request:

```text
[u8 paymentType][u8 no1][u8 no2][u8 no3][u8 no4][u8 no5]
paymentType 0 = 1000 gold
paymentType 1 = 100 cash
```

Erro detectado antes do banco:

```text
[u16 0x75][u8 result][u32 gold][u32 cash]
result 1 = saldo insuficiente
result 2 = número repetido/rejeitado
```

Resposta após a operação persistente:

```text
[u16 0x75][u8 result][u32 round][u32 gold][u32 cash]
result 0 = sucesso
```

O subtipo interno de DB `0x29` visto no original não é uma resposta ao cliente. O handler antigo o
enviava equivocadamente pelo canal field e foi removido.

O original compara os cinco números entre si, mas não faz range check. Portanto, o servidor atual
rejeita duplicatas em qualquer posição e não inventa uma faixa ainda não comprovada.

### Consulta `0x76`

Request:

```text
[u8 page]
offset = page * 10
```

Página com registros:

```text
[u16 0x76][u8 0][u32 count]
count * [u32 round][u8 no1][u8 no2][u8 no3][u8 no4][u8 no5]
```

Lista vazia ou falha retorna somente `[u16 0x76][u8 result]`, com `1` para vazio e `2` para falha.
O nome antigo `RoomReadyState` estava incorreto: `0x76` é `AskLotto`.

## Persistência e atomicidade

`lotto` guarda `userid`, rodada, instante, cinco números e os campos de auditoria `gold`/`cash`.
`loglottery` guarda o resultado de cada rodada: cinco números, bônus e instante do sorteio.

Na compra, o backend:

1. abre uma transação `Serializable`;
2. bloqueia as wallets `usergameinfo.gold` e `cash.cash`;
3. calcula a rodada como `MAX(loglottery.no) + 1`;
4. insere o bilhete em `lotto`;
5. debita a moeda escolhida;
6. commita e só então atualiza o saldo da sessão.

O boot converte `lotto` para InnoDB. Isso melhora a segurança do original, cujo dump usava MyISAM e
executava insert e débito em statements separados. Logs críticos registram compra commitada e falhas.

Não há request ID no wire original. O bloqueio por sessão evita clique concorrente, mas um retry feito
depois de uma resposta perdida pode comprar outro bilhete. Idempotência perfeita exigiria uma extensão
de protocolo e não deve ser apresentada como compatibilidade v258.

## Resultado, pool e prêmio

No boot, o World original carrega até sete resultados:

```sql
SELECT no,no1,no2,no3,no4,no5,bonus
FROM loglottery ORDER BY no DESC LIMIT 7;
```

Também calcula o pool com `SUM(lotto.gold)`, separando bilhetes anteriores ou posteriores a
`CURDATE()` conforme já exista resultado no dia. Não foi encontrada geração aleatória, insert em
`loglottery`, matching de bilhetes ou grant de prêmio em `WorldServ.exe`, `RankUpdate.exe` ou
`BrokenServer.exe`.

A associação anterior dos IDs de idioma `829..864` com a loteria não se sustenta: a varredura encontra
esses escalares em dezenas de funções genéricas, inclusive entrada de UI, render e estruturas sem
relação com rede. Sem as strings localizadas e sem um call site ligado a `0x75/0x76`, esses números não
provam tela, regras nem pagamento. Os itens `11000..11004` continuam sendo apenas uma categoria de
loja associada à loteria e não provam tiers de prêmio.

## Compatibilidade com o cliente v258

`IScavengerWorldNet` entrega todas as respostas pelo dispatcher `engine.dll:0x36197320`, chamado por
`ProcessWorldRecvBuffer`. A tabela possui 88 cases, termina em `0x74` e não contém `0x75` nem `0x76`.
Uma resposta do backend de loteria chega ao transporte, mas não possui consumidor de UI nesta build.

A busca por escalares `0x75/0x76` no `rakion.bin` encontrou somente códigos de eventos de interface,
offsets e dados auxiliares; no `engine.dll`, somente offsets/dados gráficos. Nenhuma ocorrência monta
um pacote World. Logo, não há caminho C→S nem S→UI comprovado no cliente entregue.

O cache carregado por `FUN_0040F2F0` permanece comportamento interno do World original. Replicá-lo no
.NET sem produtor externo e sem consumidor no cliente criaria código morto, por isso ele está mapeado,
mas não materializado como serviço ativo.

## Implementação

Arquivos canônicos:

- `Domain/LotteryRules.cs`: preços, tipos de pagamento e duplicidade;
- `Database/WorldDatabase.Lottery.cs`: compra transacional e paginação;
- `Database/LotteryModels.cs`: contratos explícitos da borda de persistência;
- `Network/LotteryFrames.cs`: layouts binários golden;
- `Network/ClientSession.Lottery.cs`: validação, concorrência e sincronização da sessão;
- `WorldServer.Lottery.cs`: coordenação por conta;
- `WorldHandlers.Generated.Room.cs`: dispatch comprovado de `0x75` e `0x76`.

## Ativação e verificação

Não existe feature flag de loteria. O backend fica disponível quando o World usa o schema v258 com as
tabelas `lotto` e `loglottery`; `EnsureSchemaAsync` prepara `lotto` para a transação no boot. Isso não
ativa a UI no cliente v258.

```powershell
cd server/RakionServer
dotnet build RakionServer.sln -c Release
dotnet test tests/RakionServer.World.Tests/RakionServer.World.Tests.csproj -c Release
```

Uma validação visual exige outro cliente original que comprovadamente possua builders e consumers de
`0x75/0x76`, ou uma extensão nativa nova. A extensão seria desenvolvimento autoral, não RE fiel da
build atual, e precisa ser tratada como projeto separado.

## Lacuna para concluir o domínio inteiro

É necessário localizar o executável/serviço externo que escrevia `loglottery` e pagava o prêmio, ou
capturar uma instalação original executando o fechamento diário. Sem essa fonte, continuam abertos:

- horário e timezone do fechamento;
- faixa válida e ordenação dos números;
- RNG e escolha do bônus;
- cálculo/divisão do pool e tiers;
- grant automático no login e prevenção de pagamento duplicado;
- retenção de sete dias e tratamento de sorteio sem vencedor.

Compra e consulta estão concluídas no backend headless, mas são dormentes no cliente v258. Sorteio,
liquidação e uma build cliente compatível permanecem fronteiras comprovadamente ausentes dos
artefatos disponíveis.
