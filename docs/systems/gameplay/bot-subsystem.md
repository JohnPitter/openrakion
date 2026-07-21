# Subsistema de Bots — peer sintético server-side

O bot é um peer sintético controlado pelo World: ocupa um assento do field, aparece no roster,
recebe movimento e animações no formato do cliente v258 e possui HP, morte e respawn autoritativos.
A DLL não implementa regra de combate. Ela apenas espelha ao World, pelo envelope autenticado
`0xB07A`, o movimento `0x030A` e o início do ataque local `0x0311` que o cliente normalmente envia
somente aos peers.

O retorno do bot usa a rota real de cada humano: `0x57` via TCP quando há tunneling e datagrama UDP
para uma rota direta autenticada. O servidor nunca inventa endpoint para o bot e não interfere no
canal humano↔humano.

## Limite confirmado no cliente

A entidade remota sintética não percorre o mesmo caminho local de colisão/dano de um peer real.
Por isso não existe uma confirmação nativa de vítima disponível para esse modelo e o contador
cosmético **HIT×N** não é produzido. A inspeção do endereço antes atribuído a esse gate mostrou que
ele pertence a outra transição de estado; essa atribuição foi removida da documentação.

Para manter o bot funcional para lançamento, o World resolve o golpe a partir de dados que o
cliente realmente fornece:

- início do ataque do humano (`0x0311 kind=Attack`);
- posição e rumo mais recentes do atacante (`0x030A`);
- bots inimigos vivos do field;
- alcance, cone frontal, alvo mais próximo e cooldown antirrepetição.

Esse contrato não transforma toda animação em dano: no máximo um bot inimigo é escolhido, precisa
estar a até 600 unidades wire e dentro de um cone de 150 graus centrado no rumo do atacante. O
cooldown de 250 ms elimina emissões duplicadas do hook durante o mesmo golpe.

## Camadas

| Camada | Arquivos | Responsabilidade |
|---|---|---|
| Domínio/IA | `Domain/BotProfile.cs`, `BotVector.cs`, `BotSteering.cs`, `BotPlayer.cs` | dificuldade, perseguição, HP, reação, morte e respawn |
| Combate | `Domain/BotCombat.cs` | alvo único, time, alcance, cone frontal, cooldown e dano |
| Field | `Domain/Field.Bots.cs` e `PlayerRec` | assentos, pose humana e estado efêmero do bot |
| Serviço | `BotManager.cs`, `BotManager.Tick.cs`, `WorldServer.ResolveBotMeleeAttack` | tick, feedback visual, lifecycle e placar |
| Rede | `Network/BotMovement.cs`, `BotTelemetryDatagram.cs`, `UdpGameplay.cs` | síntese, validação e transporte |
| Cliente | `client/RakionClientCompat/bot_telemetry.cpp` | espelho de movimento/ataque ao World, sem regra de dano |

## Fluxo

1. `BotManager.AddBotToField` aceita somente o host, antes da partida e em sala competitiva. O bot
   entra no time oposto, ocupa um assento do bloco `0..9` ou `10..19` e já fica pronto.
2. O roster recebe o member-join `0x38`; o primeiro snapshot `0x4B` humano é replicado com o seat do
   bot para o engine criar a entidade no stage.
3. A cada 150 ms em partida, `BotManager.TickField` encontra o humano inimigo vivo mais próximo,
   avança a IA e sintetiza o `0x030A` do bot.
4. Ao atacar, a DLL envia o `0x0311` local dentro do `0xB07A`. O World autentica endpoint e seat,
   aplica rate limit e chama `BotCombat.TryResolveMeleeAttack`.
5. Em um acerto, o World zera velocidade e alvo da IA, publica um `0x030A` de parada e depois a
   reação `0x0311 kind=Damage`. A IA permanece suspensa por 1,8 s, portanto o bot não persegue nem
   desliza enquanto está caído.
6. Ao zerar o HP, `Field.ApplyReportedDeath` liquida exatamente um kill, publica `0x4F`, a DLL chama
   o lifecycle nativo de morte e o bot permanece sem movimento até o respawn. Deathmatch, Team
   Death e Boss usam respawn autoritativo de sete segundos; Golem segue eliminação por round.
7. No fim da partida ou na saída do último humano do gameplay, o field retorna ao game room. Os
   humanos voltam a não-pronto, o bot é revivido, tem IA e pose efêmeras zeradas e permanece
   pronto para o rematch. A liderança da sala só muda quando o master realmente deixa a sala.

## Movimento e animação

No `0x030A`, o word `+17` contém heading absoluto. Os words `+20/+22/+24` são deltas acumuláveis de
câmera e ficam zerados; repetir heading nesses campos faz o modelo girar no próprio eixo. Antes de
cada reação de dano o servidor também publica a posição atual com velocidade lógica zerada, para
impedir que o último movimento continue sendo interpolado durante a queda.

O ataque do bot alterna as três animações observadas (`0x1B`, `0x1A`, `0x12`) e usa cooldown por
dificuldade. O dano bot→humano continua fora do modelo sintético: o cliente trata essa colisão como
client-authoritative e exige um peer real.

## Testes

- `BotCombatTests`: alvo inimigo mais próximo, rumo, cone frontal, alcance, time, cooldown, dano e
  morte de um único bot.
- `BotRespawnTests`: parada durante a reação, lifecycle, prazo e restauração do respawn.
- `BotMovementSynthTests`: formato, pose/heading, ausência de deltas de câmera e reação estendida.
- `E2E/BotStageValidationTests`: ataque `0x0311` autenticado reduz HP, publica parada/reação, mata o
  bot com `0x4F` e funciona também por tunneling TCP.
- `E2E/BotRematchE2ETests`: dois clientes e um bot saem do gameplay, reencontram a sala no filtro
  Available e iniciam outra partida no mesmo game room.
- `E2E/AddBotButtonCommandE2ETests` e `BotMovementE2ETests`: comando, roster e movimento no fio.

## Estado de validação

Build e testes automatizados validam protocolo e regra de negócio. O gate final permanece visual:
confirmar no cliente gráfico que cada golpe frontal próximo reduz HP, que a queda interrompe a
perseguição, que o bot morre uma vez, incrementa um kill e só volta a andar após o respawn.
