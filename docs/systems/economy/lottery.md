# Engenharia reversa da loteria — Rakion v258

## Estado atual

O World está reconstruído para as duas operações comprovadas no executável original:

- compra de bilhete `0x75`, em gold ou cash, com débito e persistência atômicos;
- consulta paginada de bilhetes `0x76`, dez registros por página.

O sorteio e o pagamento dos vencedores **não estão implementados**. Nenhum dos executáveis
disponíveis produz linhas em `loglottery` ou liquida prêmios. O World apenas lê resultados que outro
componente gravava. Inventar esse componente seria incompatível com um RE fiel.

Validação atual: testes golden e de regra aprovados, build sem warnings e smoke MariaDB isolado com
compra em gold, compra em cash, saldo insuficiente e paginação. A UI ainda precisa ser validada no
cliente real.

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
py tools/ghidra/FindClientLotteryUi.py
```

Saídas padrão: `C:\temp\world_lottery_flows.txt` e `C:\temp\client_lottery_ui.txt`.

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

Os textos 829–864 do cliente confirmam cinco números mais bônus, histórico de uma semana e prêmio do
primeiro colocado em gold concedido no login. Eles não revelam algoritmo, calendário, divisão do pool
ou transação de pagamento. Os itens `11000..11004` são categoria de loja associada à loteria, mas não
provam tiers de prêmio.

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

Não existe feature flag de loteria. Ela fica ativa quando o World usa o schema v258 com as tabelas
`lotto` e `loglottery`; `EnsureSchemaAsync` prepara `lotto` para a transação no boot.

```powershell
cd server/RakionServer
& C:\Users\joaop\.dotnet\dotnet.exe build RakionServer.sln -c Release
& C:\Users\joaop\.dotnet\dotnet.exe test tests/RakionServer.World.Tests/RakionServer.World.Tests.csproj -c Release
```

Para validação visual, entre numa room (`Status=2`), compre uma combinação com gold e outra com cash,
reabra o histórico e confirme rodada, ordem, saldos e paginação. Faça isso em uma conta descartável.

## Lacuna para concluir o domínio inteiro

É necessário localizar o executável/serviço externo que escrevia `loglottery` e pagava o prêmio, ou
capturar uma instalação original executando o fechamento diário. Sem essa fonte, continuam abertos:

- horário e timezone do fechamento;
- faixa válida e ordenação dos números;
- RNG e escolha do bônus;
- cálculo/divisão do pool e tiers;
- grant automático no login e prevenção de pagamento duplicado;
- retenção de sete dias e tratamento de sorteio sem vencedor.

Compra e consulta estão concluídas headless; sorteio e liquidação permanecem uma fronteira externa
comprovadamente ausente dos artefatos disponíveis.
