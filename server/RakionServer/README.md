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

- A tabela canônica do World cobre `0x00..0x79`; nenhum delegate final aponta para `Stub`,
  `ReconStub` ou rota genérica. Colisões por estado ficam nos interceptores documentados.
- Login, canais, salas, partida, inventário, economia, clã, amigos, presentes, eventos, GM/Admin,
  Broker, Buddy, Ranking e launcher possuem contratos e critérios de validação em
  [`docs/audits/re-coverage.md`](../../docs/audits/re-coverage.md).
- AES-128 está fechado: chave hardcoded, IV `0xc47f`, blocos lógicos de 12→16 bytes e ativação no
  setup da sessão por `Crypto.EnableWorldDefault()`.
- O RE estático e os fluxos headless estão cobertos por testes golden, probes TCP/UDP e smokes de
  banco. As pendências restantes são explicitamente classificadas como validação gráfica/P2P real,
  integrações externas ou extensões modernas opcionais; não são representadas como stubs do v258.

O inventário atual e as evidências reproduzíveis estão em
[`docs/protocol/world.md`](../../docs/protocol/world.md),
[`docs/protocol/world-evidence.md`](../../docs/protocol/world-evidence.md) e
[`docs/audits/re-coverage.md`](../../docs/audits/re-coverage.md). Esses documentos são a fonte
canônica; este README não duplica contagens de testes ou listas de handlers que envelhecem rápido.

## Material de RE

- Scripts reproduzíveis: [`tools/ghidra`](../../tools/ghidra) e
  [`tools/README.md`](../../tools/README.md).
- Evidências consolidadas: [`docs/protocol/world-evidence.md`](../../docs/protocol/world-evidence.md).
- Configuração e schema: `worldserver.ini`, `deploy/db/` e `TUTORIAL.md`.
