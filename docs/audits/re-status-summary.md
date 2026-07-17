# Resumo do RE completo — estado e objetivos restantes

Atualizado em 17 de julho de 2026. Este é o resumo executivo; a matriz detalhada e as evidências
continuam em [`re-coverage.md`](re-coverage.md).

## Objetivo geral

Mapear a superfície do Rakion v258 encontrada no cliente, servidores originais, banco e assets;
comparar cada contrato com o servidor .NET; implementar o que for necessário para compatibilidade;
e separar três níveis de conclusão:

1. **RE estático**: contrato, regra, layout ou ausência comprovados nos artefatos;
2. **headless**: implementação validada por build, testes e probes sem interface gráfica;
3. **runtime visual**: comportamento observado no cliente real, inclusive com dois clientes e redes
   diferentes quando aplicável.

O projeto já concluiu a catalogação estática dos **29 domínios** da auditoria. Isso não significa que
o jogo inteiro esteja validado visualmente: a principal fronteira restante é dinâmica.

## Objetivos já concluídos

| Objetivo | Estado atual |
|---|---|
| Organizar a documentação | Concluído: documentos canônicos estão separados em `protocol/`, `systems/`, `guides/`, `audits/` e `archive/` |
| Auditar a superfície completa do jogo | Concluído estaticamente: 29/29 domínios possuem passe dedicado e lacunas explícitas |
| Clã | RE e ciclo básico headless concluídos; login, árvore, persistência, Admin, Buddy e presença de canal documentados |
| Amigos/Buddy | RE e implementação headless concluídos; lista, add/remove, apelido, grupos, presença, SMS e túnel documentados |
| Natal e presentes | Gift Box implementada e validada headless; conteúdo natalino ausente/dormente nesta build foi comprovado |
| Protocolo e fundação World | Dispatches, opcodes, estados, lobby, sala, field, P2P/túnel e ciclo de personagem mapeados e testados headless |
| Economia e progressão | Loja, inventário, storage, coupons, Power User, enchant, ranking e vendas locais fechados no limite headless documentado |
| Gameplay comum | PvP, stage, combate, potions, Chaos, objetivos, votes e fluxo de Cells/NPC possuem contratos canônicos |
| Curvas e infraestrutura de NPC | 47 tipos, 99 níveis, 43 classes carregáveis, atributos, CP, targeting, ownership, dano, late join e catálogo de eventos fechados estaticamente |
| Famílias-base de NPC completas | Nak, Panzer, CrossBow, Blazer, Golem, SoulCannon, LongBow, Taurus, Dragon e IceWind possuem documentos e extratores reproduzíveis; IceWind fecha inclusive o projétil dedicado `CIceWind` |
| Classes especiais e censo | MasterGolem, GoldGolem e ChocolateCake fechados em `npc-special-classes.md`; censo de 116 descritores com veredito por classe em `entity-class-census.md` |
| Ferramentas de análise | SDK .NET e fluxo headless do Ghidra estão disponíveis para reproduzir extrações, testes e builds |

O passe residual também está concluído:
[`npc-special-classes.md`](../systems/gameplay/npc-special-classes.md) fecha MasterGolem,
GoldGolem e ChocolateCake, e o censo
[`entity-class-census.md`](entity-class-census.md) enumera os **116 descritores concretos** do
`entitiesmp.dll` com veredito para cada um (família documentada, especial, player, skill,
efeito/infra, item/evento ou herança SE1). Os quatro `NpcBlackDragon*` estão formalmente
ausentes por três fontes independentes. Nenhuma classe, alias ou variante ficou sem
classificação — **o RE estático por classe está encerrado**.

## Objetivos que ainda faltam

Nenhum marco estático em aberto: o RE estático está **completo** pelo critério de encerramento.
A fronteira do projeto agora é dinâmica.

### 1. Validação dinâmica via backend (headless, 2 clientes) — AMPLA COBERTURA

A validação **dinâmica** dirige o `WorldServer` real por clientes headless no fio
(TCP + AES + dispatch + motor de partida + banco), registrada em
[`dynamic-validation.md`](dynamic-validation.md). Verde ponta a ponta com dois clientes:

- login concorrente + rejeição de credencial inválida;
- char-select, criar sala, join do 2º jogador (coabitação no mesmo field, assentos distintos);
- ready + start da partida (armada em fase Pre, ambos os assentos promovidos a combatente);
- handshake UDP dos dois peers + relay de movimento `0x030A` e de combate (`0x0311`/`0x030F`) byte a byte;
- chat de canal (0x22) broadcast entre os dois clientes;
- **settlement PvP persistido no DB real** pelo motor da partida vivo (WIN/LOSE em `characterinfo`);
- **matriz dos 4 modos** (Golem/Deathmatch/TeamDeath/Boss) armando a partida + rejeição de fragLimit inválido;
- **entrada em stage PvE solo** (`BeginStageRun` via `0x4b`).

São 14 testes E2E, **782 verdes**. Detalhe em [`dynamic-validation.md`](dynamic-validation.md).

Próximos alvos headless (ainda abertos):

- ciclo de partida ao vivo pelo tick real (engage por deadline, rounds, morte `0x4f` no fio, placar);
- liquidação `0x53` com reward exato (coberta hoje por testes de domínio/DB);
- matriz P2P (direto/Tunnel, mesma máquina/LAN/NAT/UDP bloqueado);
- economia/UI ao vivo: loja, inventário, enchant, presentes, Power User, ranking.

### 2. Validação gráfica com o cliente real

Camada que **exige o cliente v258** e não é atingível só pelo backend:

- render de personagem, inventário, loja, clã, amigos e presentes;
- PvP/PvE visual: animação, frames de ataque, hitbox, colisão, trajetória de projétil, efeitos;
- HUD de placar, enchant, compras, Power User e ranking;
- fluxo completo de launcher/update no cliente.

Cada validação precisa registrar build/hash, configuração, captura ou log, resultado esperado e
resultado observado. Build verde não substitui a prova visual.

### 3. Decidir extensões que não pertencem ao v258 original

Somente depois do fechamento compatível devem ser priorizadas features autorais: servidor de combate
autoritativo, bots/puppets, checkout/recarga, liquidação de loteria, política moderna de PC Bang,
SMTP e eventual evento de Natal novo. Elas não devem ser contabilizadas como lacunas do RE original.

## Ordem recomendada

1. estender a validação **headless** (2 clientes no fio): gameplay UDP → ciclo de partida →
   settlement → PvE stage → matrizes de modo/P2P → economia/UI;
2. smoke **visual** da jornada básica com dois clientes;
3. matriz PvP/P2P visual;
4. matriz PvE/NPC visual;
5. economia, launcher e integrações externas;
6. extensões autorais escolhidas para o lançamento.

## Critério de encerramento

O **RE estático completo** — passe residual e auditoria final sem classe ou domínio sem veredito —
foi **atingido em 17 de julho de 2026**: 29/29 domínios, 10 famílias, 3 especiais e 116
descritores classificados. O **jogo completo para lançamento** exige, além disso, os goldens
runtime dos fluxos críticos. Enquanto esses testes visuais não forem executados, o estado correto
do projeto é: **RE estático completo e ampla compatibilidade headless, com validação dinâmica
pendente**.
