# RakionServer — stack de servidor do Rakion v258 em .NET

Reconstrução em **C# / .NET 9** dos três servidores do Rakion v258 — **WorldServer**,
**Broker** e **BuddyServer** — feita por **engenharia reversa dos binários** (os fontes
não existem na internet). Roda **nativo no Linux** (sem Wine), ao contrário dos `.exe`
originais. Objetivo: réplica fiel em comportamento, *como se fosse o original*.

## Origem (do que cada projeto foi reconstruído)
| Projeto | Binário de origem | Como |
|---|---|---|
| `RakionServer.Broker` (`BrokenServer`) | `BrokenServer.exe` (.NET) | decompile (dnSpy) portado p/ net9.0 |
| `RakionServer.World` (`RakionWorldServer`) | `RakionWorldServ.exe` / `worldserv.exe` (C++ nativo) | RE com Ghidra → reimplementação fiel |
| `RakionServer.Buddy` (`BuddyServer`) | `Buddy2.dll` (C++ nativo, client-side) | RE com Ghidra → 1ª impl. do lado servidor |
| `RakionServer.Ranking` (`RakionRankUpdate`) | `RankUpdate.exe` (C++ nativo) | RE com Ghidra → job one-shot fiel |
| `RakionServer.Common` | — | núcleo compartilhado (pacotes, cifra IPC, INI, log) |

Protocolo documentado em [`docs/protocol/world.md`](../../docs/protocol/world.md) (World) e
[`docs/protocol/buddy.md`](../../docs/protocol/buddy.md), ancorado nos endereços dos binários.

> **Quer subir o servidor?** Passo a passo completo (build, MariaDB, configs, rodar os 3
> servidores, Docker, cliente/GameGuard) em **[`TUTORIAL.md`](TUTORIAL.md)**.

## Estrutura
```
RakionServer.sln
src/
  RakionServer.Common/   PacketReader/Writer, IpcCodec (BCRC+cifra), IniFile, Log
  RakionServer.World/    WorldServer, ClientSession (framing+seq), LoginHandler (FUN_0041f6c0),
                         BrokerLink (IPC), WorldDatabase (MySQL), loja e loteria 0x75/0x76
  RakionServer.Broker/   BrokenServer portado (broker IPC UDP + lista de mundos)
  RakionServer.Buddy/    BuddyServer (:8500/:8504), BuddyProtocol (tabela CD)
  RakionServer.Ranking/  Job diário one-shot de total/class/clan rank e snapshots *rankp
  RakionServer.LauncherWeb/  Ticket de launch + update assinado + adaptadores legados
  RakionServer.Admin/        Painel admin (Blazor, :8080) — contas/gold/cash/itens/Power User/updates
```

> `LauncherWeb` e `Admin` são **adições originais** (não RE de binário): o launcher web (login + auto-update)
> reescrito em .NET e um painel de administração. `start-stack.ps1` sobe os serviços de uma vez.

## Deploy (Docker, stack completo)
`Dockerfile` (multi-stage: SDK builda → runtime + MariaDB + PHP + os 3 servidores) +
`docker-entrypoint.sh` sobem tudo numa imagem nativa Linux (sem Wine). Coloque o dump do
MySQL em `deploy/db/` e a auth web em `deploy/web/` (não versionados), então:
```bash
docker build -t rakion-net:latest .
docker run -p 40706:40706 -p 40708:40708 -p 40708:40708/udp -p 40709:40709/udp \
           -p 8500:8500 -p 8504:8504 -p 80:80 -v rakiondb:/var/lib/mysql rakion-net:latest
```
**Integração verificada (3 binários reais juntos, sem Docker):** o broker carrega o
`GameServers.ini`, recebe o IPC ServerInfo do world e loga *"Server: LuxView World change to
online"*; world sobe TCP 40708 + AES self-test OK; buddy ouve 8500/8504.

## Build e execução
```bash
dotnet build -c Release            # build limpo (0 warnings)
# World (lê worldserver.ini; mesmo formato do RakionWorldServ.exe):
dotnet run --project src/RakionServer.World -- /caminho/worldserver.ini
# Broker (lê Settings/Settings.ini + Settings/GameServers.ini no cwd):
dotnet run --project src/RakionServer.Broker
# Buddy (portas 8500,8504 por padrão):
dotnet run --project src/RakionServer.Buddy
```

## Estado da reconstrução
**Validado end-to-end (cliente simulado falando o wire real):**
- **World**: framing `[u16 size][u16 opcode][u16 seq][payload]` (size inclui-se), validação de
  sequência (wrap 65000; 0x0C/0x0F isentos), **login opcode 0x0C** (`FUN_0041f6c0`) com os guards
  e DISC (0x12/0x13/0x14/0x15) e erros (3/8, 3/10) fiéis, e resposta **LoginComplete** byte-perfeita.
  Heartbeat IPC `ServerInfo` (opcode 257) que marca o world "online" no broker. Camada MySQL
  (user/usergameinfo/loguserconnect).
- **Broker**: porta fiel do decompile .NET; sobe e fala o IPC UDP.
- **Buddy**: framing `[u16 size][u16 CD][payload]` e handshake `PRECREDENTIAL→LOGIN` (login OK,
  lista vazia → client prossegue). Tabela de CD completa.

**Framework completo e developável:**
- World: **os 87 opcodes** do dispatcher `FUN_0042ab40` **reconstruídos** em `Network/WorldHandlers*.cs`
  — 34 à mão + **53 via workflow multi-agente** (cada handler decompilado, transcrito e verificado
  adversarialmente contra o binário; em `WorldHandlers.Generated.cs`). Guards/DISC fiéis; payloads de
  resposta transcritos do struct do decompile (helpers `FUN_0040xxxx` profundos marcados `// TODO`).
- Cripto: **AES-128 quebrado** — chave hardcoded `E1 3A 7E F5 37 2C 10 4D 4E CE B3 0C 56 26 A4 8E`,
  IV `0xc47f` (`PacketCrypto.WorldKey/EnableWorldDefault()`).
- Modelo de domínio: `World` (canais, GmVars, Fields, Rooms, lock), `Field`/`Room` (membros+broadcast),
  `UserStatus`, sessão com campos nomeados (Status, GroupId, CharName, InField, FieldId, RoomId).
- DB: POCOs + repositório (`WorldDatabase`) para user/usergameinfo/characterinfo/useriteminfo/iteminfo/
  cash/claninfo/loguserconnect.
- Cripto: `PacketCrypto` (AES, do `FUN_00401670`) integrado no canal lobby (`SendLobby`), ligado quando
  o handshake habilitar (key-setup é a RE pendente).
- Buddy: superfície completa de comandos (login + add/remove buddy, grupos, SMS).

**Stubs (RE incremental por handler):** os ~69 opcodes restantes do world (gameplay/field/item/clã)
são logados com o nome real e referenciam o `FUN_xxxx` de origem. Pendente: key-setup do AES,
métodos profundos do `Field` (criar/entrar/ready/slots), UDP de gameplay e o canal P2P do buddy.

## Material de RE
- Binários e Ghidra: `rakion-work/ghidra-proj/` (worldserv.exe, Buddy2.dll, `*.out.txt`).
- Scripts: `rakion-work/ghidra_scripts/WSProto*.py`, `BuddyProto.py`.
- Broker original (decompile): `rakion-work/broker_src/`.
- Configs/schema de referência: `rakion-tutorial/server/` (worldserver.ini, DB/rakion_all.sql).
