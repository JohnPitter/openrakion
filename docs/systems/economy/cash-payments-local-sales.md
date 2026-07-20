# Engenharia reversa de cash e vendas locais — Rakion v258

## Veredito

O cash usado pelo jogo é uma wallet simples de EX points, indexada pelo nome da conta em
`cash(id,cash)`. O World original lê e debita essa wallet diretamente. Compras registram o produto e
os saldos anterior/atual em logs específicos.

Não existe recarga, checkout, provedor, webhook, refund ou chargeback nos executáveis entregues. As
tabelas `localsales`, `logaddgoldcash`, `logincash` e `logspendgoldcash` também não possuem nenhuma
referência no World. Elas pertenciam à infraestrutura externa de operação/relatórios, ausente do
pacote. `LauncherWeb` serve autenticação e atualização do launcher; não é um servidor de pagamento.

Assim, o RE do runtime fornecido está fechado. Uma integração com dinheiro real seria um produto
novo, não uma função recuperável desses binários.

## Página de compra de créditos

O `LauncherWeb` agora publica `/cash/`, uma página responsiva com os pacotes disponíveis e o estado
explícito do checkout. Quando falta Cash na compra de Power User (status `3`), a
`RakionClientPatch.dll` abre essa página usando a URL de `cash-shop.url`; a URL padrão de validação é
`http://127.0.0.1/cash`.

O cliente original também contém uma rota de UI para compra de créditos: o command `0x15` fecha o
estado corrente e chamava `ShellExecuteA` com a URL histórica da Softnyx. A DLL preserva essa rota,
troca somente o destino pela `cash-shop.url` e cria um `csButton` nativo `Buy Cash` ao lado do botão
`Potion slot` (command `0x20`). O botão herda bitmap, fonte, posição vertical e tamanho do controle
original; não usa janela sobreposta nem duplica a configuração da URL.

A página está pronta como entrada operacional, mas os botões permanecem desabilitados enquanto não
há provedor autorizado. Isso é intencional: navegar, atualizar a página ou retornar ao jogo nunca
pode creditar a wallet. A concessão futura deverá ocorrer somente após webhook assinado e idempotente,
processado pelo backend em transação com pedido e ledger.

A abertura automática permanece uma responsabilidade mínima da DLL: o protocolo `0x34` entrega ao
cliente apenas o status numérico de saldo insuficiente e não possui mensagem server-side capaz de
pedir ao Windows que abra uma URL. Remover esse hook exige aceitar navegação manual ou estender
coordenadamente o protocolo/launcher; o servidor continua responsável apenas pela decisão econômica.

## Evidência reproduzível

World original analisado:

```text
RakionWorldServ.exe
SHA-256 1B8B5EB1AF36F414D7B2C4D58196E63C7D6918C403741A5DBA40D5EB9C8EE0E5
```

O script abaixo encontrou 16 strings de cash e 13 funções consumidoras:

```powershell
py tools/ghidra/FindWorldCashAccounting.py
```

Relatório: `C:\temp\world_cash_accounting.txt`.

As strings comprovadas incluem:

```sql
SELECT cash FROM Cash WHERE id = '%s';
UPDATE Cash SET cash=cash-%u WHERE id='%s' AND cash>=%u;
INSERT INTO LogBuyCashItem
  (userid,itemid,price,cash_prev,cash_cur,createtime,coupon_log_id) ...;
```

Entre os consumidores estão `FUN_0040EC50` (loteria), `FUN_00413CD0`/`FUN_004144F0`
(operações de personagem), `FUN_00417800`/`00417F10`/`004184A0` (entitlements) e
`FUN_00419A40` (loja). Não há string nem xref para `localsales`, `localserverstatus`,
`logaddgoldcash`, `logincash` ou `logspendgoldcash`.

## DDL legado

### Wallet

```sql
CREATE TABLE cash (
  id CHAR(16) NOT NULL,
  cash INT DEFAULT 0,
  PRIMARY KEY (id)
);
```

`id` é `user.id`/`usergameinfo.name`, não o `usergameinfo.id` numérico. O World obtém o nome pelo
perfil e o usa nas queries da wallet.

### `localsales`

```text
id, local_id, server_id, product_id, user_id, cash, gold, log_time
```

O formato parece telemetria regional de venda por produto, servidor e local. Isso é inferência do
DDL, não comportamento executável. Como nenhum binário disponível lê ou grava a tabela, ela não é
uma fonte canônica do runtime reconstruído.

### Logs sem consumidor

- `logaddgoldcash`: snapshot por `userid`, com deltas e saldos; a PK permite apenas uma linha por
  usuário, portanto não é um ledger imutável;
- `logincash`/`logingold`: conta e data, sem uso no World;
- `logspendgoldcash`: agregado por desconexão, local/server/spendtype, sem xref;
- `localserverstatus`: telemetria de população por local/server, também externa.

## Gastos dentro do jogo

Os fluxos cash comprovados estão implementados no backend com lock de wallet e transação conjunta
com a concessão:

- compra de item e bundle;
- reset de stats e troca de nome;
- bag e slot de personagem;
- Power User;
- slots de poção;
- limpeza de ranking de stage e stage-level-free;
- loteria.

`logbuycashitem` é o ledger de compras por produto do original. Reset/rename/Power User possuem seus
próprios logs. Cupons gravam `logcoupon` e vinculam seu ID ao lançamento correspondente. O mutador
genérico `AddCashAsync`, que fazia clamp silencioso e não possuía chamadores, foi removido.

## Ajustes administrativos

O painel não sobrescreve mais gold/cash sem rastro. Cada ajuste agora:

1. exige saldo alvo válido e motivo de 4 a 200 caracteres;
2. abre transação `Serializable` e bloqueia a wallet;
3. calcula o delta no backend;
4. atualiza o saldo e insere `admin_currency_adjustment` na mesma transação;
5. registra conta, moeda, antes/depois, delta, motivo, operador e timestamp;
6. trata repetição do mesmo saldo alvo como no-op, sem lançamento duplicado;
7. exibe o histórico recente na tela da conta.

O painel continua exigindo senha forte, cookie HttpOnly/SameSite, antiforgery, autenticação e rate
limit no login. Ajustes são fluxos críticos e também produzem log operacional.

Contas já conectadas veem o novo saldo no próximo callback econômico ou relog. O servidor nunca
aceita saldo informado pelo cliente; cada compra relê e bloqueia o valor persistido.

## Ativação

No primeiro boot do Admin, `EnsureCurrencySchemaAsync` cria automaticamente
`admin_currency_adjustment`. É obrigatório configurar:

```powershell
$env:ConnectionStrings__Rakion='Server=127.0.0.1;Port=3306;Database=rakion;Uid=...;Pwd=...;'
$env:Admin__Password='uma-senha-forte-com-16-ou-mais'
```

Depois, abra a conta no painel, informe o saldo alvo e um motivo/ticket. O histórico aparece abaixo
dos saldos.

## Validação

- regras de input cobertas por testes unitários;
- build Release sem warnings;
- smoke MariaDB isolado confirmou `gold 1000→1750`, `cash 500→800`, dois lançamentos com deltas
  `+750/+300` e replay do mesmo saldo sem terceira linha;
- schema temporário removido após o teste.

```powershell
& C:\Users\joaop\.dotnet\dotnet.exe build server/RakionServer/RakionServer.sln -c Release
& C:\Users\joaop\.dotnet\dotnet.exe test server/RakionServer/tests/RakionServer.World.Tests/RakionServer.World.Tests.csproj -c Release
```

## Fronteira externa

Para vender cash por dinheiro real ainda seria necessário escolher e autorizar explicitamente um
provedor. Esse sistema novo precisaria de order state machine, assinatura de webhook, idempotência,
refund/chargeback compensatório, reconciliação e proteção de segredos. Nada disso deve ser inventado
ou ativado como se fosse parte do Rakion v258 original. A landing `/cash/` e o redirecionamento do
cliente já existem; checkout, webhook e liquidação da wallet continuam nessa fronteira externa.
