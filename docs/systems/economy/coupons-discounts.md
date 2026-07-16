# Engenharia reversa de cupons e descontos — Rakion v258

## Escopo e veredito

Cobre o item-cupom, `couponinfo`, `logcoupon`, seleção pelo box, desconto e consumo. O contrato
headless foi fechado e implementado para reset de stats `0x1B`, rename `0x1C`, loja geral `0x2E`,
bag `0x32`, character slot `0x35` e Power User `0x34`. Potion slot `0x6F` não carrega campos de
pagamento e não oferece seleção de cupom nesta build.

## Modelo original

`couponinfo` é carregada no boot do World:

```text
id, discount_rate, expire_days, min_level, max_level, for_cash
```

`id` é também o item do cupom, na faixa `11000..11999`. O ownership não fica em `couponinfo`: o
cupom é uma linha consumível do storage. Na build original, storage de conta usa
`useriteminfo.characterid=0`; o `.NET` atual preserva esse mesmo modelo canônico.

O dump distribuído define a estrutura de `couponinfo`, mas não traz registros. Portanto, taxas e
ativação são conteúdo operacional e não devem ser inventadas no boot. Os probes de 50% usam uma
definição temporária explícita, removida junto com a fixture ao final.

`logcoupon` registra:

```text
coupon_id, item_id, user_id, use_time, discount_amount
```

`item_id` é o produto/operação comprada, não a linha do storage:

| Operação | `item_id` |
|---|---:|
| Power User inicial | 10000 |
| renovação Power User | 10001 |
| reset level `<16` | 10002 |
| reset level `16..40` | 10003 |
| reset level `>=41` | 10004 |
| rename | 10005 |
| bag | 10006 |
| character slot | 10007 |

`FUN_00415CB0` confirma que Power User escolhe `10000/10001` conforme o modo inicial/renovação.
A implementação antes registrava o opcode `0x34` como produto; isso foi corrigido para os IDs do
ledger original.

O dump não dá identidade a `logcoupon`, embora o binário chame `mysql_insert_id()` e tente gravar o
retorno em `coupon_log_id`. O boot moderno adiciona `id BIGINT AUTO_INCREMENT`, permitindo vínculo
auditável sem alterar os INSERTs legados por lista de colunas.

## Contrato wire e validação

Não existe opcode exclusivo. Operações compatíveis carregam:

```text
[paymentType:u8]
paymentType=0 -> nenhum campo adicional
paymentType=1 -> [boxSlot:u16]
```

O `u16` é a célula visual do box, não `couponinfo.id` nem o id da linha. `FUN_0040bd80` resolve os
arrays paralelos da sessão:

```text
rowId  = user + 0x1BC4 + boxSlot*4
itemId = user + 0x1E2C + boxSlot*2
```

Validações e códigos observados ao vivo:

| Status | Condição |
|---:|---|
| `0x14` | célula vazia ou item fora de `11000..11999` |
| `0x15` | definição encontrada, mas `for_cash` não corresponde à moeda da operação |
| `0x16` | não existe `couponinfo.id == itemId` |

`expire_days`, `min_level` e `max_level` são carregados em memória, porém esse helper consulta apenas
`id`, `discount_rate` e `for_cash`. Logo, não é correto afirmar que validade e faixa de nível são
aplicadas nessa build. Essas colunas podem pertencer a outra rota ou estar inativas.

## Preço e arredondamento

Para preço base `P` e `discount_rate=R`:

```text
rawDiscount  = floor(P * R / 100)
finalCost    = floor((P - rawDiscount) / 100) * 100
loggedAmount = ceil(rawDiscount / 100) * 100
```

O backend calcula tudo; o cliente informa somente tipo e célula. Exemplos capturados:

| Fluxo | Base | Cupom | Cobrado | Logado como desconto |
|---|---:|---:|---:|---:|
| reset level 1 | 7.000 | 50% | 3.500 | 3.500 |
| reset level 40 | 12.000 | 50% | 6.000 | 6.000 |
| rename | 3.000 | 50% | 1.500 | 1.500 |
| bag | 8.000 | 50% | 4.000 | 4.000 |

Nos probes, sucesso removeu exatamente a linha selecionada, debitou `cash`, gravou `logcoupon` e o
log da operação. Cupom não gera random present; esse sorteio ocorre apenas no pagamento cash direto.

## Implementação atual

O wire slot é traduzido para `BoxRowId` e `BoxItems` da sessão. A transação serializable:

1. trava personagem/conta e a linha exata de `useriteminfo`;
2. revalida ownership, `characterid=0`, row id e item id;
3. trava a definição e calcula o preço no backend;
4. valida saldo e executa a operação;
5. remove o cupom e grava `logcoupon`;
6. grava o log da operação com `coupon_log_id`;
7. commita antes de alterar a sessão ou responder.

`useriteminfo`, `cash`, `couponinfo`, `logcoupon` e os logs envolvidos são convertidos para InnoDB no
boot. Isso evita o falso rollback do schema MyISAM original.

Testes cobrem arredondamento, thresholds, códigos `0x14..0x16`, produtos `10000/10001`, golden
frames e integração real de reset/rename/bag/character slot/Power User com consumo e restauração
da fixture. Em 2026-07-14, bag com cupom
temporário `11000` a 50% confirmou `bag 1→2`, cash `39350→35350`, remoção da linha canônica,
`logcoupon.discount_amount=4000` e vínculo `logbuycashitem.coupon_log_id=4` no mesmo commit.

## Validações adicionais

- captura da UI real escolhendo o cupom;
- eventual descoberta de outra rota que consulte `expire_days`, `min_level` ou `max_level`;
- concorrência com duas sessões da mesma conta em teste de integração;
- validação visual do desaparecimento da célula após sucesso.

## Critério de conclusão global

O núcleo de desconto está encerrado para `0x1B/0x1C/0x2E/0x32/0x34/0x35`. Todas as operações que
chamam o helper original compartilham quote, consumo, wallet e ledger atômicos. A fronteira aberta
é a UI real e o teste concorrente; colunas carregadas mas não lidas pelo helper não devem ganhar
regras inventadas.
