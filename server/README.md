# Servidor

Servidor do Rakion v258 **reescrito do zero em .NET** — não usa os executáveis proprietários da SoftNyx. Código-fonte em [`RakionServer/`](RakionServer/).

## Serviços

| Projeto | Binário | Porta | Função |
|---|---|---|---|
| `RakionServer.Broker` | BrokenServer | 40706/TCP | Lista de servidores/canais, anuncia o world (advertised IP), ponte de login |
| `RakionServer.World` | RakionWorldServer | 40708/TCP, 40708-40709/UDP | Login, lobby, salas, personagem, inventário/box, loja, compra e histórico da loteria, chat, partida (UDP) |
| `RakionServer.Buddy` | RakionServer.Buddy | — | Lista de amigos/mensageiro (opcional) |
| `RakionServer.Ranking` | RakionRankUpdate | — | Job diário one-shot de ranking total, classe, membro e clã |
| `RakionServer.Common` | (lib) | — | Cripto (AES-128-ECB do protocolo), leitura/escrita de pacotes, IPC |
| `RakionServer.LauncherWeb` | RakionLauncherWeb | configurável | Ticket de launch, update assinado e adaptadores legados `launcherlogin/fetch` |
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
2. **LauncherWeb:** configure connection string/ticket/update e rode o serviço.
3. **Broker:** rode o `RakionServer.Broker` (lê `Settings/`).
4. **World:** rode o `RakionServer.World` (lê `worldserver.ini`).
5. **Admin** (opcional): rode o `RakionServer.Admin` → painel em **http://localhost:8080**.

Passo a passo detalhado: [`RakionServer/TUTORIAL.md`](RakionServer/TUTORIAL.md). Protocolo:
[`docs/protocol/world.md`](../docs/protocol/world.md).

> O Admin não contém senha nem connection string versionadas. Defina `Admin__Password` (mínimo 16
> caracteres) e `ConnectionStrings__Rakion`; sem ambas, `start-stack.ps1` não inicia o painel. Ele
> escuta `127.0.0.1:8080` por padrão. Não exponha 3306/8080 publicamente.

> O LauncherWeb também não contém credencial de banco versionada. O padrão legado exige
> `ConnectionStrings__Rakion`; bind externo exige HTTPS. A ativação segura está em
> [`docs/protocol/launcher-auth-update.md`](../docs/protocol/launcher-auth-update.md).

## Notas

- O World loga fluxos críticos (login, transações, integrações) no console — redirecione para arquivo se quiser persistir.
- Regras de negócio (loja, inventário, ouro) ficam **no servidor**; o cliente só renderiza.
