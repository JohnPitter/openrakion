# Documentação do OpenRakion

Este é o ponto de entrada da documentação. Use os documentos **canônicos** para implementar e os
arquivos em `archive/` somente para contexto histórico.

## Comece aqui

| Objetivo | Documento |
|---|---|
| instalar e executar o servidor | [`server/RakionServer/TUTORIAL.md`](../server/RakionServer/TUTORIAL.md) |
| entender o protocolo World | [`protocol/world.md`](protocol/world.md) |
| entender amigos/Buddy | [`protocol/buddy.md`](protocol/buddy.md) |
| ver cobertura e próxima prioridade | [`audits/re-coverage.md`](audits/re-coverage.md) |
| revisar qualidade estrutural | [`audits/code-quality.md`](audits/code-quality.md) |
| configurar o cliente | [`guides/config-xfs.md`](guides/config-xfs.md) |
| entender o GameGuard | [`guides/gameguard.md`](guides/gameguard.md) |

## Protocolos e bordas

- [`protocol/world.md`](protocol/world.md) — fonte canônica de frames, opcodes e estados World.
- [`protocol/world-evidence.md`](protocol/world-evidence.md) — hashes, capturas e cadeia dourada World.
- [`protocol/world-response-dispatch.md`](protocol/world-response-dispatch.md) — 88/88 cases da fila IScavengerWorldNet S→C.
- [`protocol/field-message-dispatch.md`](protocol/field-message-dispatch.md) — pump CNet/P2P e 24 cases explícitos de gameplay/transporte.
- [`protocol/entity-event-catalog.md`](protocol/entity-event-catalog.md) — 269 event IDs e tamanhos compilados de `entitiesmp.dll`.
- [`protocol/buddy.md`](protocol/buddy.md) — login, presença, amigos, grupos e tunnel Buddy.
- [`protocol/broker-ipc.md`](protocol/broker-ipc.md) — descoberta de mundos, heartbeat e IPC.
- [`protocol/launcher-auth-update.md`](protocol/launcher-auth-update.md) — login web, ticket e update.
- [`protocol/client-integrity.md`](protocol/client-integrity.md) — hashes, ChCode e modo sem GameGuard.

## Sistemas centrais

- [`systems/core/character-lifecycle.md`](systems/core/character-lifecycle.md)
- [`systems/core/inventory-equipment-storage.md`](systems/core/inventory-equipment-storage.md)
- [`systems/core/channel-lobby.md`](systems/core/channel-lobby.md)
- [`systems/core/room-management.md`](systems/core/room-management.md)
- [`systems/core/field-match-lifecycle.md`](systems/core/field-match-lifecycle.md)
- [`systems/core/udp-p2p-tunneling.md`](systems/core/udp-p2p-tunneling.md)

## Gameplay

- [`systems/gameplay/pvp-modes-combat.md`](systems/gameplay/pvp-modes-combat.md)
- [`systems/gameplay/stage-pve-progression.md`](systems/gameplay/stage-pve-progression.md)
- [`systems/gameplay/cells-creatures-npc.md`](systems/gameplay/cells-creatures-npc.md)
- [`systems/gameplay/npc-stat-curves.md`](systems/gameplay/npc-stat-curves.md)
- [`systems/gameplay/npc-family-nak.md`](systems/gameplay/npc-family-nak.md)
- [`systems/gameplay/npc-family-panzer.md`](systems/gameplay/npc-family-panzer.md)
- [`systems/gameplay/npc-family-crossbow.md`](systems/gameplay/npc-family-crossbow.md)
- [`systems/gameplay/npc-family-blazer.md`](systems/gameplay/npc-family-blazer.md)
- [`systems/gameplay/npc-family-golem.md`](systems/gameplay/npc-family-golem.md)
- [`systems/gameplay/npc-family-soulcannon.md`](systems/gameplay/npc-family-soulcannon.md)
- [`systems/gameplay/npc-family-longbow.md`](systems/gameplay/npc-family-longbow.md)
- [`systems/gameplay/npc-family-taurus.md`](systems/gameplay/npc-family-taurus.md)
- [`systems/gameplay/npc-family-dragon.md`](systems/gameplay/npc-family-dragon.md)
- [`systems/gameplay/combat-actions-status.md`](systems/gameplay/combat-actions-status.md)
- [`systems/gameplay/potions-chaos-effects.md`](systems/gameplay/potions-chaos-effects.md)
- [`systems/gameplay/votes-invites-kicks.md`](systems/gameplay/votes-invites-kicks.md)
- [`systems/gameplay/golem-boss-objectives.md`](systems/gameplay/golem-boss-objectives.md)

## Economia e progressão

- [`systems/economy/shop-economy-items.md`](systems/economy/shop-economy-items.md)
- [`systems/economy/enchant-reinforce.md`](systems/economy/enchant-reinforce.md)
- [`systems/economy/power-user-slots.md`](systems/economy/power-user-slots.md)
- [`systems/economy/coupons-discounts.md`](systems/economy/coupons-discounts.md)
- [`systems/economy/ranking-rewards.md`](systems/economy/ranking-rewards.md)
- [`systems/economy/lottery.md`](systems/economy/lottery.md)
- [`systems/economy/cash-payments-local-sales.md`](systems/economy/cash-payments-local-sales.md)

## Social, eventos e operação

- [`systems/social/clan.md`](systems/social/clan.md)
- [`systems/social/chat-moderation-abuse.md`](systems/social/chat-moderation-abuse.md)
- [`systems/events/christmas-and-gifts.md`](systems/events/christmas-and-gifts.md)
- [`systems/events/valentine-and-generic-events.md`](systems/events/valentine-and-generic-events.md)
- [`systems/operations/gm-admin-commands.md`](systems/operations/gm-admin-commands.md)
- [`systems/operations/pcbang-region-service.md`](systems/operations/pcbang-region-service.md)
- [`systems/operations/replay-demo-diagnostics.md`](systems/operations/replay-demo-diagnostics.md)

## Convenção

- `protocol/`: contratos de rede e integração entre processos;
- `systems/`: regras e fluxos por domínio do jogo;
- `guides/`: instruções operacionais atuais;
- `audits/`: inventários, riscos e trabalho pendente;
- `archive/`: material desatualizado, mantido apenas como evidência histórica.

Um documento de RE não prova que a feature está pronta. “Mapeado com lacunas” significa que ainda
podem faltar captura dourada, implementação, teste multi-cliente ou validação visual.
