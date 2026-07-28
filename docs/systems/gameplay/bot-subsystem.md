# Subsistema de Bots — roster e lifecycle no World

> O caminho de lançamento e de simulação é exclusivamente o
> [`Bot Engine Host`](bot-engine-host.md). Não existe fallback para peer sintético com física
> aproximada. Este documento cobre apenas o que o World ainda possui: roster, admissão, lifecycle
> e publicação de rede.

## Responsabilidades do World

| Camada | Arquivos | Responsabilidade |
|---|---|---|
| Domínio | `Domain/BotPlayer.cs`, `BotCombat.cs`, `Bot*DamagePolicy.cs`, `PlayerCombatVitals.cs` | HP, janela de ataque, hitbox, dano, morte e respawn |
| Field | `Domain/Field.Bots.cs`, `PlayerRec` | assentos, pose e vitais |
| Serviço | `BotManager.cs`, `WorldServer.BotEngine.cs`, `WorldServer.BotCombat.cs` | reserva/confirmação, tick nativo, combate autoritativo |
| Host | `BotEngine/*`, `client/RakionBotEngineHost` | física, colisão, animação e snapshots |
| Rede | `Network/BotMovement.cs`, `BotTelemetryDatagram.cs`, `ServerCombatDatagrams.cs`, `UdpGameplay.cs` | síntese de frames e transporte |
| Cliente | `client/RakionClientCompat` | espelho autenticado e apresentação; sem regra de dano |

## Fluxo de admissão

1. `/addbot` (ou botão nativo) chama `AddNativeBotsAsync`.
2. `BotManager.ReserveBot` aloca seat no time oposto sem publicar roster.
3. O Host do field cria a fonte nativa; só então `ConfirmReservation` marca pronto e envia `0x38`.
4. Falha de Host desfaz a reserva e remove os bots do field — sem troca para motor sintético.

## Tick

A cada ~150 ms em partida, `SyncNativeBotsAsync` avança o Host, copia snapshots, aplica intenções
do cérebro e resolve combate nas duas direções. Input de locomoção e ataque do bot só é enviado
quando o bot está vivo e fora de reação de HIT.

## Testes

- `BotCombatTests` / `ServerCombatDatagramTests`: janela, hitbox, armadura, morte e codecs.
- `BotEngineIsolationTests`: multi-bot sem estado compartilhado e ausência do tick sintético.
- `BotEngineWorkerIntegrationTests` e `E2E/NativeBotMovementE2ETests`: Host real (fixture
  `RAKION_BOT_ENGINE_HOST` + `RAKION_BOT_ENGINE_CLIENT_ROOT`) — sala, entrada, movimento,
  combate, morte, respawn, multi-bot e saída. O gate de movimento mede **só o plano X/Z**: medir
  também o eixo vertical faria a queda de spawn passar por locomoção, e foi exatamente o que
  mascarou o defeito por muito tempo.
- `BotManagerTests`: gates de roster (host, time oposto, rollback).
- `E2E/AddBotButtonCommandE2ETests` e `BotRematchE2ETests`: comando e rematch.

## Histórico

O planejador sintético (`BotSteering` / `BotNavigationPlanner` / `BotManager.Tick`) foi removido.
Documentação legada de peer sintético e de `RakionBotHost` peer-process permanece apenas como
arquivo histórico e não descreve o caminho shippado.
