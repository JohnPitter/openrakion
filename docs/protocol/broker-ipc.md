# RE do Broker, IPC e descoberta de mundos

## Veredito

O caminho Broker → lista de mundos e World → heartbeat está implementado e validado headless. A
lista agora anuncia somente registros online completos; heartbeat vazio ou cifrado é decodificado,
validado por tamanho/contrato/BCRC/origem/server id e então renova a lease. O wire legado ainda usa
XOR/CRC sem autenticação forte. O RE do `engine.dll` provou que o cliente v258 não possui envio de
credenciais ao Broker: o login segue diretamente para o World.

## Configuração e descoberta

O Broker lê `Settings/Settings.ini` para TCP e UDP IPC e `Settings/GameServers.ini` para:
`id`, `name`, `ip`, `wan`, `port`, `ipcport`, `code`, `lan_wan` e `version`.
`lan_wan=0` anuncia `ip`; `lan_wan=1` anuncia `wan`. Não há detecção por IP do cliente, DNS ou
health-check de rota. É uma chave global por mundo.

Ao receber opcode TCP `0x0101`, `ServerListPacket` responde:

```text
[frame 0x0101][u8 quantidade]
por mundo online:
  [IPv4 4B][porta u16 big-endian]
  [usedRooms u16][maxRooms u16][usedUsers u16][maxUsers u16]
```

Probe ao vivo do servidor original em 2026-07-14, com um mundo online:

```text
13 00 01 01 01 7f 00 00 01 a2 ec 00 00 d0 07 00 00 f4 01
```

Interpretação: tamanho `19`, opcode `0x0101`, quantidade `1`, `127.0.0.1:41708`, zero salas
e usuários, máximos `2000/500`. O contrato está protegido por
`BrokerServerListGoldenTests.OnlineServer_MatchesOriginalLiveProbe`.

O port original escrevia apenas `u16 0` para mundo offline, embora `quantidade` fosse
`GSList.Count`. Como o layout histórico offline não foi capturado, a política segura local é emitir
somente mundos online e contar exatamente esses registros. Goldens cobrem zero, um, lista mista e
seleção LAN/WAN. `cliVersion` e o campo `version` continuam sem filtro porque não há evidência da
política original.

## IPC Broker ↔ World

Envelope observado:

```text
[u16 sourcePort][u8 random][u8 command][u16 payloadLength][payload][u8 BCRC]
```

Comandos: `0=RequestServerInfo`, `1=RequestLogin`, `2=ResponseServerInfo` e `3=ResponseLogin`.
`BCRC` é XOR de todos os bytes; XOR total igual a zero detecta corrupção acidental, não autentica.
`code` aplica cifra XOR simétrica. O mesmo valor deve existir em `GameServers.ini` e
`worldserver.ini [Broker] Code`. O Broker agora clona e decodifica o datagrama antes de validar BCRC,
opcode `257`, comando `2`, payload `9` e tamanho total `16`. Código vazio e não vazio estão cobertos;
um smoke com processos reais e `shared-code` colocou o World online.

`ResponseServerInfo` do World usa opcode `257`, server id, máximo/uso de salas e usuários. O World
anuncia ao iniciar e a cada 60 segundos. O Broker marca online ao receber e expira após cinco minutos.
O Broker casa o remetente exatamente por `ip + ipcport`; NAT, mudança de endpoint ou hostname não
são tratados.

O decompile do BrokenServer contém builders dormentes de `RequestLogin`/`ResponseLogin`, usuário,
senha e `ipcId`, mas nenhuma chamada alcança esses builders. A superfície exportada de
`IScavengerBrokerNet` no `engine.dll` v258 é somente:

| Função | Endereço | Efeito |
|---|---:|---|
| `Connect(IP, port)` | `0x3618D1D0` | abre TCP e inicia threads de send/receive |
| `SendWorldList()` | `0x3618D3A0` | enfileira somente `u16 0x0101` |
| `SendDisconnect()` | `0x3618D3E0` | enfileira somente `u16 0x0102` |
| `Disconnect()` | `0x3618CE70` | fecha o socket |

Não existe export Broker que receba conta ou senha. Em contraste, o mesmo binário exporta
`IScavengerWorldNet::SendLogin`, e a captura World contém conta/senha no opcode `0x0C`. Assim,
login Broker→IPC não é uma lacuna de implementação desta build; é código morto herdado no
BrokenServer. Os builders, estruturas de correlação e handler World correspondentes foram removidos
da implementação ativa. O resultado reproduzível está em
`<diretorio-de-evidencias>/engine_broker_protocol.txt` e o
script em `tools/ghidra/DecompileEngineBrokerProtocol.py`.

## Auditoria da implementação

- lista contém somente mundos online e nunca emite placeholder parcial;
- `CurRoom` reportado pelo World está fixo em zero;
- timeout de cinco minutos mantém mundo morto visível por tempo excessivo;
- sem sequence, timestamp, HMAC ou replay window no IPC;
- `code` permanece ofuscação compatível, não MAC; o caminho é agora simétrico;
- heartbeat usa parser estrito, sem over-read nem captura ampla;
- a porta IPC do World também recebe ping UDP de gameplay; há classificação por marcador e BCRC,
  coberta por testes, mas os dois protocolos continuam acoplados ao mesmo socket;
- alteração de `GameServers.ini` exige reinício; não há reconciliação dinâmica.

## Arquitetura recomendada

Preservar o wire legado em um adapter e criar domínio explícito:

```text
BrokerTcpAdapter -> WorldDirectory -> WorldLease
LegacyIpcAdapter -> HealthReport / LoginRequest
```

`WorldLease` deve guardar identidade estável, endpoint anunciado, build, capacidade, último heartbeat
e estado. O adapter valida tamanho exato, origem permitida, HMAC e monotonic sequence quando ambos os
lados forem atualizados. Para clientes legados, a lista deve emitir somente mundos online ou um
registro completo para cada posição, conforme captura dourada.

## Ativação, rollback e testes

1. serialização online-only e goldens de 0/1/misto/LAN/WAN — concluído;
2. medir heartbeat e reduzir lease gradualmente;
3. login direto Launcher→World confirmado; não ativar o IPC dormente — concluído;
4. introduzir envelope autenticado versionado, aceitando legado durante migração;
5. separar IPC e gameplay em portas distintas quando a compatibilidade permitir.

Rollback: manter leitura do envelope legado e configuração estática; nunca aceitar pacote novo sem
autenticação só porque o endpoint coincide.

Coberto: CRC adulterado, código vazio/não vazio, payload truncado, mundo offline no meio da lista,
zero mundos, LAN/WAN, golden online, origem IP/porta exata e limite temporal da lease. Um smoke com
Broker+World reais comprovou boot/heartbeat cifrado; a capacidade `ushort` extrema também possui
golden. Ainda faltam replay autenticado sem quebrar o wire legado e captura de uma lista original
mista.

## Estado da evidência

- **Confirmado:** layout online por captura ao vivo/golden, política local online-only, heartbeat
  60 s, expiração 5 min, LAN/WAN, BCRC e cifra `code` simétrica em smoke real.
- **Confirmado ausente:** login TCP Broker→IPC no cliente v258; os builders do servidor estão
  dormentes e sem caller.
- **Não resolvido:** layout original offline, política de versão e autenticação forte/replay sem
  alterar o wire legado.
