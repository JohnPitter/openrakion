# Engenharia reversa de Power User, bonus points e slots — Rakion v258

## Escopo e veredito

Cobre compra `0x34`, validade, PU Bonus Points, multiplicadores, alocação `0x33`, bag `0x32`,
character slot `0x35`, potion slot `0x6F` e seus logs.

**Veredito:** Power User foi fechado para operação offline com compra, renovação, cupom, wallets,
validade, bonus points e ledger em uma única transação. O callback externo real foi reconstruído nos
binários World e cliente, eliminando o status falso e o patch de mensagem. O `0x33` aloca level point
ou PU point sob lock e só atualiza a sessão após commit. A validade é relida durante a conexão e os
multiplicadores ficam congelados por partida. O resultado do cliente foi fechado estaticamente:
Power User concede EXP `×1,5`, mantém Gold bruto e reutiliza o asset histórico `Img_PCBang`. Bag,
character slot e potion slot estão implementados de ponta a ponta. Resta observação no cliente gráfico.

## Power User atual

`pu_config` define preço inicial `8000`, renovação `6000`, 5 bonus points por padrão, dias,
EXP `1.5` e Gold neutro `1.0`. Multiplicadores de promoção e Gold são extensões administrativas,
neutras por padrão. O World original recebe `[mode][couponFlag][couponSlot?]`; `mode=0/1` escolhe
os preços `8000/6000`. O helper `FUN_0040B2C0` confirmou os status `2`, `3`, `0x14`, `0x15` e
`0x16` para operação em andamento, saldo e validação de cupom.

O slot é lido quando `couponFlag!=0`, mas somente o valor exatamente igual a `1` ativa cupom;
outros valores seguem como pagamento Cash. `0x34` não compara a fase `user+0x1440` nem exige
`InField/FieldSecondary`: apenas o subestado ocupado `user+0x144C==2` produz status `2`. A rota
canônica `Op_InventoryBuyPowerUser` agora responde busy imediatamente em vez de enfileirar uma
segunda compra atrás do lock.

- cash e `usergameinfo` são bloqueados com `FOR UPDATE`;
- cupom Cash opcional é validado, consumido e logado;
- `powerlevelpoint`, `powertime` e `powertimedate` são atualizados juntos;
- `logbuypoweruser` registra custo, modo, estado anterior/final e cupom;
- a transação commita antes de `Cash`, `PowerLevelPoint`, `PuActive` e a UI mudarem.

No `logcoupon`, compra inicial usa produto `10000` e renovação usa `10001`, como em
`FUN_00415CB0`; o opcode wire `0x34` não é identidade de produto.

Falhas retornam o frame original curto `[u16 0x34][status]`. No sucesso, a operação interna `0x17`
é convertida por `FUN_004281B0` no frame externo:

```text
[u16 0x34][u8 status=0][u32 gold][u32 cash][u32 powertime]
[u16 powerlevelpoint][u8 presentCount][presentCount * u32 itemId]
```

`FUN_00474F50` recebe esses campos no cliente, atualiza gold, cash, validade e pontos, ativa
`SetPowerUser` e repinta as telas de inventário. `powertime` usa o mesmo marcador do original:
`TO_DAYS(NOW())*1440 + HOUR(NOW())*60 + MINUTE(NOW())`, acrescido da duração. Compra inicial parte
do horário atual; renovação soma a duração ao marcador existente.

## Bônus e validade

PU ativo é `powertimedate > NOW()`. No cliente, `FUN_351EBE50` lê o campo gravado por
`AccountInfo_s::SetPowerUser` e calcula `exp + exp/2`, com truncamento inteiro. Gold não é alterado.
O World original confirma a mesma regra no caminho `0x50`. O servidor usa EXP `1.5` e Gold `1.0`
por padrão; valores de Gold diferentes de `1.0` são extensão configurável, não comportamento v258.

Reload a cada 15 s troca a config para compras/partidas futuras. No início da partida, a sessão fixa
os multiplicadores e a elegibilidade; mudanças de promoção ou expiração não alteram uma liquidação
já iniciada. Em paralelo, a cada 5 s o World relê `powertimedate` por conta online. Isso ativa ou
desativa `PuActive`/`ExpBonusActive` sem relog e preserva o snapshot da partida corrente.

## Bonus Points e `0x33`

Stats `0..9` têm cap `50`. O backend bloqueia personagem e conta, gasta level point primeiro e PU
Bonus Point depois, incrementa o stat e debita a carteira na mesma transação. A resposta e a sessão
usam os saldos relidos/commitados; duas sessões não conseguem gastar o último ponto duas vezes.

O callback `0x34` já atualiza o saldo de pontos. O antigo push artificial `0x33` com `statIdx=0x0A`
foi removido; `0x33` ficou reservado à alocação real de stats.

## Entitlements de slots

| Op | Export | Estado atual |
|---:|---|---|
| `0x32` | `SendInventoryBuyBag [mode,couponSlot?]` | handler canônico transacional |
| `0x35` | `SendInventoryBuyCharacterSlot [mode,couponSlot?]` | handler canônico transacional |
| `0x6F` | `SendInventoryBuyPotionSlot` | handler canônico transacional |

Bag usa preço `8000`, produto `10006` e máximo `3`; character slot usa `12000`, produto `10007` e
máximo `6`. Ambos aceitam cash/cupom, persistem wallet + entitlement + logs/presente no mesmo
commit e retornam o callback original com saldos e novo limite. O login projeta `usergameinfo.slot`
no `0x0C`.

Os builders enviam o slot quando `mode!=0`, mas os helpers `FUN_0040B080/FUN_0040B1A0` só tratam
`mode==1` como cupom; outros valores não zero seguem como compra Cash. Os handlers exigem conta,
personagem e `Status=2` (`DISC 3F/40` para bag, `DISC 41/42` para character slot), sem gate adicional
de `InField/FieldSecondary`. UI fechada retorna status `1`, mutação ocupada retorna `2`, limite
retorna `3`, saldo insuficiente `4` e erros de cupom usam `0x14/0x15/0x16`. `0x32/0x35` agora entram
somente por `Op_InventoryBuyBag/Op_InventoryBuyCharacterSlot` e compartilham o lock atômico das
demais mutações de inventário.

`characterinfo.potionslot` começa em `3`. Produtos `10008..10010` liberam slots 4–6 por,
respectivamente, `8000 cash`, `100000 gold` e `31000 cash`. O backend lê moeda/preço do catálogo,
commita entitlement + wallet + ledger/presente e limita quickslot às células compradas `13..18`.
O handler original aceita request vazio, exige identidade e `Status=2` (`DISC D3/D4`) e seleciona
produto apenas para totais `3/4/5`; outro total usa `DISC D5`. Não existe gate adicional de field,
padding ou operação ocupada.

## Modelo recomendado

```text
Subscription(PowerUser, StartsAt, ExpiresAt, PurchaseId)
Entitlements(BagCount, CharacterSlots, PotionSlots)
BonusPointWallet(Balance, Version)
BenefitPolicy(ConfigVersion, ExpMultiplier, OptionalGoldExtension, EnchantMultiplier)
```

Compra de PU: cash debit + subscription extension + points + ledger numa transação serializável.
Compra de slot: quote/produto + débito + entitlement + ledger na mesma transação. O servidor deve
responder status real, com saldo/validade/limite resultantes.

## Implementação e ativação

1. preservar os contratos e testes de `0x32/0x35/0x6F/0x34`;
2. manter o callback `0x34` como golden source, sem patch de XFS;
3. manter PU e bonus points na transação implementada;
4. manter expiração durante sessão e snapshot por match habilitados;
5. preservar a alocação transacional de stats;
6. manter quickslot/runtime respeitando o limite e adicionar testes visuais;
7. escrever ledger e testes de duas sessões.

```ini
[PowerUser]
Enabled=true
Transactional=true
[Entitlements]
Bag=true
CharacterSlots=true
PotionSlots=true
```

## Testes mínimos

- cash insuficiente, compra exata, recompra acumulando validade e falha entre statements;
- expiração durante sessão e fronteiras de promoção/timezone;
- multiplicador aplicado exatamente uma vez no mesmo match;
- alocação concorrente do último bonus point e cap 50;
- compra repetida de bag/char/potion slot e limites máximos;
- slots bloqueados não utilizáveis antes da compra;
- reconnect preservando cash, validade, pontos e entitlements;
- ledger conciliando toda alteração.

## Idempotência e critério de conclusão

O protocolo legado não envia `purchaseId` nem token de retry. O sequence number pertence à conexão,
reinicia no login e não distingue uma tentativa repetida após reconnect de uma nova compra legítima.
Logo, idempotência distribuída após queda de conexão não pode ser adicionada com fidelidade apenas no
backend; exigiria extensão coordenada do cliente e do servidor. A implementação preserva a semântica
original e garante atomicidade/serialização no banco. A frente está concluída em RE e validação
headless; a única fronteira ainda aberta é observar popup, saldos e validade no cliente gráfico.

## Evidência executada em 2026-07-15

- `FUN_00422B10` e `FUN_0040B2C0` decompilados junto dos demais entitlements;
- `FUN_00415CB0`, `FUN_004281B0` e `FUN_00474F50` fecharam worker, callback World e consumidor cliente;
- o bônus padrão foi corrigido de 51 provisório para 5, com migração `config_version=2`;
- `config_version=3` alinhou Gold a `1.0`, preservando valores customizados diferentes das sementes;
- entities/engine fecharam `AccountInfo_s::SetPowerUser`, EXP `×1,5` e o reuso de `Img_PCBang`;
- golden tests cobrem sucesso de 18 bytes e falha curta do `0x34`;
- probe wire confirmou gold `10113`, cash `12000`, `powertime=1065898655` e pontos `10→15`;
- ao alterar `powertimedate` para o passado com o socket aberto, o ciclo de 5 s recarregou a data e
  registrou a expiração sem desconectar;
- 299/299 testes .NET, quatro testes Python, probe transacional e build sem warnings aprovados;
- fixture financeira, stat e ledger foram restaurados ao final.
