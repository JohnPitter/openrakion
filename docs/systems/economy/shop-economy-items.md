# Engenharia reversa de loja, economia e itens — Rakion v258

## Escopo e veredito

Cobre catálogo `iteminfo`, preços, gold/cash, compra `0x2E`, venda `0x2F`, box, sets,
itens temporários/serial e logs econômicos. Equipamento e quickslot estão em
[`../core/inventory-equipment-storage.md`](../core/inventory-equipment-storage.md); pagamentos em
[`cash-payments-local-sales.md`](cash-payments-local-sales.md).

**Veredito:** o contrato headless está fechado. Saldo, item e ledger de compra/venda formam uma
única transação e o cliente só recebe sucesso após o commit. Células físicas são persistidas em
`useriteminfo.slot` com `characterid=0`, gear é vendido por row ID e compras usam o mesmo namespace
de serial do original. O catálogo ativo desta build é `iteminfo`; uma busca estática completa no
`worldserv.exe` não encontrou referência a `buyinfo`. Resta validação gráfica no cliente real.

## Catálogo e moedas

O boot carrega `iteminfo` para cache por `id`, incluindo `type`, `Class`, `level`, `shop`, `gold`,
`cash`, stats e `power`. A compra aceita somente `0=CASH` ou `1=GOLD` e cruza a moeda com o
catálogo: `shop=1` exige Gold e preço `gold>0`; `shop=2` exige Cash e preço `cash>0`; `shop=0` não
é comprável. A tentativa de trocar a moeda para obter o preço zero é rejeitada no backend.

Gold vive em `usergameinfo.gold`, indexado pelo id interno. Cash vive na tabela `cash`, indexada
pelo nome da conta. A sessão mantém ambos como `uint` e os serializa no login/HUD. Essa divisão
de chaves exige uma identidade de conta canônica; hoje há comentários indicando artefatos no
campo de login e fallback de nome.

`FindWorldShopEconomy.py` encontrou 12 strings econômicas e 14 consumidores no World original,
mas nenhuma string ou referência a `buyinfo`. Assim, tratar essa tabela vazia do dump como catálogo
obrigatório seria uma inferência incorreta. Descontos, região, PU e disponibilidade temporal ainda
não participam da quote geral de `0x2E`.

## Compra `0x2E`

Payload atual:

```text
[u16 itemId][u8 currency][u8 useCoupon][u16 couponSlot quando useCoupon=1]
```

`IScavengerWorldNet::SendInventoryBuy @ 0x36191740` confirma os quatro parâmetros. O campo final
não é token/idempotency key: é a célula visual de um cupom. `FUN_00421210` entrega a seleção ao
helper `FUN_0040CB10`, que valida faixa `11000..11999`, definição e moeda. O backend valida item,
moeda, preço e cupom. Como no original, `currency=0` seleciona Cash e qualquer valor não zero
seleciona Gold; somente `useCoupon=1` acrescenta e ativa `couponSlot`. Em seguida:

1. serializa a conta pelo `GameInfoId`;
2. bloqueia a carteira e valida saldo/capacidade;
3. consome/loga o cupom, insere todos os grants, grava um ledger por grant e debita na mesma transação;
4. após commit, atualiza a sessão, responde `0x14`, repinta e envia saldo `0x2E`.

No original, `FUN_00419A40` grava `LogUserItem.kind=0` para Gold ou `LogBuyCashItem` para Cash e
usa o `mysql_insert_id` do ledger como `item_sn`, com `sn_type=1/2`. A reconstrução agora faz a
mesma vinculação dentro da transação: `1=Gold`, `2=Cash`. Seriais de fontes sem ledger de compra
continuam no namespace local `3`, como `8000000 + useriteminfo.id`. A unicidade é composta por
`(sn_type,item_sn)`, pois os IDs dos dois ledgers pertencem a namespaces distintos.

Cupons Cash e Gold são aceitos somente quando `couponinfo.for_cash` corresponde à moeda. O desconto
usa o arredondamento original, `logcoupon` é ligado ao ledger por `coupon_log_id`, e o callback
`0x14` devolve row ID, item, desconto e célula. A compra Cash sem cupom também participa do sorteio
de random present. O wire não contém idempotency key. `FUN_0040CB10` usa estado da sessão para
barrar uma segunda operação enquanto a primeira está pendente; a reconstrução reproduz isso e
também serializa a conta e bloqueia a wallet no banco. Um retry depois de commit sem callback é uma
nova compra indistinguível da anterior, comportamento inerente ao protocolo original, não um campo
faltante no RE.

O dispatcher aplica os gates originais `DISC 36` para identidade e `DISC 37` para fase, catálogo
ou payload inválido. Com a UI de inventário fechada/ocupada, o retorno é `0x2E status 1/2`; quote,
saldo e criação falhos usam `3`, falta de espaço usa `4`, e cupom usa `0x14/0x15/0x16`. A rota
canônica `Op_InventoryBuy` substitui o interceptor anterior.

## Venda `0x2F`

Recebe `[u8 boxSlot]`. A fórmula reconstruída é:

- item de loja gold: `round(gold * 0,4)`;
- item de loja cash: `round(cash * 1,5)`, creditado em gold;
- poção `12xxx` ou item fora de loja: zero.

O servidor bloqueia a wallet, remove a linha exata pelo `BoxRowId`, credita Gold e grava
`loguseritem.kind=1` na mesma transação, preservando também nível e EXP da instância vendida.
Poções removem a pilha fungível daquela célula. Só depois do commit limpa a célula e publica o
snapshot `0x15` com item, crédito, célula, row handle, nível e EXP. Erros usam `0x2F`; célula vazia
é status `3`. Isso reproduz `FUN_0041A900`, que apaga `UserItemInfo.id`, registra saldo
anterior/atual e usa `kind=1`, e `FUN_004215A0`, que serializa a resposta.

O gate de venda usa `DISC 39` para identidade, `DISC 3A` para fase e `DISC 3B` para slot fora de
`0..119`. Não existe exigência original de `InField/FieldSecondary`. UI fechada, mutação ocupada e
célula vazia retornam respectivamente `0x2F status 1/2/3`; o fluxo agora entra unicamente por
`Op_InventorySell`.

## Sets

Type `10` é bundle. Em `FUN_0040CB10`, o branch `*itemDef == 10` lê os membros da área de composição
da definição, procura células livres no box e devolve os arrays de IDs e células no callback `0x14`;
itens comuns devolvem apenas o próprio ID. Nesta base, `hit1..hit4/chit/ap` são os seis membros
conhecidos e a compra entrega as peças diretamente, com capacidade validada antes do commit.

O login só desempacota linhas type-10 legadas criadas por versões anteriores da reconstrução. Cada
row é processado separadamente, inclusive quando existem dois sets iguais. A transação bloqueia a
linha exata, reutiliza sua primeira célula, encontra espaço para as demais peças, propaga nível e
`limittime` e faz rollback integral se faltar espaço. Compras novas nunca persistem o bundle.

## Identidade, duração e serial

`useriteminfo` é a fonte única e possui `item_sn`, `sn_type`, `level`, `limittime`, `slot` e `exp`.
A compra cria a linha com `characterid=0`, célula física e serial de ledger; gear é vendido pelo
row id exato e poções são removidas como pilha fungível. Itens de outras origens recebem serial
local type 3. No login, itens vencidos são apagados com
a mesma fórmula/minuto e fronteira estrita do original. Em sessão conectada, uma varredura de 15
segundos remove a linha vencida, recarrega as projeções e publica os deltas de box/slots ativos. A
remoção e o `0x31` de box foram validados headless; o efeito gráfico ainda precisa ser observado.

O item deve ser uma instância explícita:

```text
ItemInstance(Id, OwnerAccountId, CharacterId?, DefinitionId,
             Serial, Level, AcquiredAt, ExpiresAt?, Location, Slot)
```

## Logs e auditoria

`WorldDatabase.EconomyLedger` é a golden source do ledger. Compras Cash escrevem
`logbuycashitem`; compras Gold escrevem `loguseritem.kind=0`; vendas escrevem
`loguseritem.kind=1`. Conta, personagem ativo, item, valor e saldos anterior/atual são gravados na
mesma transação da wallet e do inventário. O ID criado no ledger também vira o serial da instância
comprada. Cupons continuam ligados por `coupon_log_id` nos fluxos que os suportam. O protocolo não
oferece correlation/idempotency key; senha e token sensível não devem entrar no log.

## Arquitetura e implementação

Use um serviço de aplicação transacional:

```text
ShopCatalog -> Quote
PurchaseService -> Wallet + Inventory + EconomyLedger
SellService -> Inventory + Wallet + EconomyLedger
```

Implementado:

1. quote por `iteminfo.shop` e moeda esperada;
2. lock da wallet e validação de capacidade;
3. saldo + item + ledger em uma transação;
4. resposta somente após commit;
5. venda por row ID e ownership;
6. capacidade física, serial original por ledger e expiração no login e online;
7. bundle type-10 expandido antes da persistência e migração segura de rows legadas.

Próximo passo de validação: observar no cliente real compra Gold/Cash, cupom, set com seis peças,
venda e expiração online. Um journal idempotente só pode ser extensão do servidor, com uma chave
nova fora do wire v258; não é requisito para fidelidade ao original.

O caminho transacional está ativo para gold e cash. Rollback de código deve desabilitar novas
operações, nunca tentar compensar automaticamente transações já comprometidas.

## Testes mínimos

- item inexistente/indisponível, moeda inválida, saldo exato/insuficiente e box cheio;
- duplo clique enquanto a operação está pendente, duas sessões da mesma conta e rollback por falha;
- set com seis peças e falha no N-ésimo insert;
- venda por instância correta entre duplicatas com nível/duração diferentes;
- expiração antes/depois de login e compra;
- saldo e item convergentes após reconnect;
- ledger totalizando exatamente a variação da wallet;
- golden frames de sucesso/erro e atualização visual.

Em 2026-07-14, a integração MariaDB comprou o item `1001` por `2700` Gold, criou a linha física e
o ledger `kind=0`; a venda da mesma célula removeu o row ID, creditou `1080` Gold e criou
`kind=1`. Outra prova manteve uma sessão aberta, venceu um item já carregado, confirmou o delete no
banco e recebeu `0x31` zerando sua célula. As fixtures foram restauradas após as provas.

## Critério de conclusão

O RE e a implementação headless de loja estão completos: catálogo, quote, cupom, bundle, wallet,
inventário, serial, ledger, venda, expiração e callbacks foram fechados. A conclusão visual depende
de executar os cenários acima no cliente real. Idempotência distribuída exigiria extensão de
protocolo ou heurística que não existe na build v258.
