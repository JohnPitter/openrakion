# Servidor

Servidor do Rakion v258 **reescrito do zero em .NET** — não usa os executáveis proprietários da SoftNyx. Código-fonte em [`RakionServer/`](RakionServer/).

## Serviços

| Projeto | Binário | Porta | Função |
|---|---|---|---|
| `RakionServer.Broker` | BrokenServer | 40706/TCP | Lista de servidores/canais, anuncia o world (advertised IP), ponte de login |
| `RakionServer.World` | RakionWorldServer | 40708/TCP, 40708-40709/UDP | Login, lobby, salas, personagem, **inventário/box**, **loja/ouro**, chat, partida (UDP) |
| `RakionServer.Buddy` | RakionServer.Buddy | — | Lista de amigos/mensageiro (opcional) |
| `RakionServer.Common` | (lib) | — | Cripto (AES-128-ECB do protocolo), leitura/escrita de pacotes, IPC |
| `RakionServer.LauncherWeb` | RakionLauncherWeb | 80/TCP | Auth web do launcher (login + auto-update `fetch`); **aposentou** o `web/launcher_web.py` |
| `RakionServer.Admin` | RakionAdmin | 8080/TCP | Painel admin (Blazor): contas, gold/cash, itens, config do Power User, publicar updates |

> A cifra de fio é **AES-128-ECB** *de propósito* — é a réplica fiel da cripto do jogo original (não troque por GCM/CBC).

## Build

Pré-requisitos: **.NET 9 SDK** e **MariaDB**.

```bash
cd RakionServer
dotnet build -c Release RakionServer.sln
```

## Configs

Templates em [`config/`](config/):
- `Settings.ini`, `GameServers.ini` → vão em `RakionServer.Broker/Settings/`
- `worldserver.ini` → fica junto do World

Ajuste IPs e credenciais de DB. O ponto mais sensível é o **advertised IP** no `GameServers.ini` (o IP que o broker anuncia para o cliente conectar no world — `127.0.0.1` para local, o IP/host público para acesso externo).

## Rodar

Tudo de uma vez (Windows): `cd RakionServer && ./start-stack.ps1` (já exporta `DOTNET_ROOT` p/ os web apps). Ou cada serviço em seu processo (ou via Docker — veja [`RakionServer/Dockerfile`](RakionServer/Dockerfile)):

1. **MariaDB** (`lower_case_table_names=1`), importe o schema (`../database/`), crie a conta de teste.
2. **LauncherWeb:** rode o `RakionServer.LauncherWeb` (auth do launcher, :80).
3. **Broker:** rode o `RakionServer.Broker` (lê `Settings/`).
4. **World:** rode o `RakionServer.World` (lê `worldserver.ini`).
5. **Admin** (opcional): rode o `RakionServer.Admin` → painel em **http://localhost:8080**.

Passo a passo detalhado: [`RakionServer/TUTORIAL.md`](RakionServer/TUTORIAL.md). Protocolo: [`docs/protocol-world.md`](../docs/protocol-world.md).

> **Credenciais de amostra (dev, à mostra de propósito — troque se for expor à rede):** MariaDB `root`/`123456`; painel admin senha `rakion` (`src/RakionServer.Admin/appsettings.json` → `Admin:Password`). Não exponha 3306/8080 públicos sem trocar.

## Notas

- O World loga fluxos críticos (login, transações, integrações) no console — redirecione para arquivo se quiser persistir.
- Regras de negócio (loja, inventário, ouro) ficam **no servidor**; o cliente só renderiza.
