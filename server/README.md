# Servidor

Servidor do Rakion v258 **reescrito do zero em .NET** — não usa os executáveis proprietários da SoftNyx. Código-fonte em [`RakionServer/`](RakionServer/).

## Serviços

| Projeto | Binário | Porta | Função |
|---|---|---|---|
| `RakionServer.Broker` | BrokenServer | 40706/TCP | Lista de servidores/canais, anuncia o world (advertised IP), ponte de login |
| `RakionServer.World` | RakionWorldServer | 40708/TCP, 40708-40709/UDP | Login, lobby, salas, personagem, **inventário/box**, **loja/ouro**, chat, partida (UDP) |
| `RakionServer.Buddy` | RakionServer.Buddy | — | Lista de amigos/mensageiro (opcional) |
| `RakionServer.Common` | (lib) | — | Cripto (AES-128-ECB do protocolo), leitura/escrita de pacotes, IPC |
| `web/launcher_web.py` | (Python) | 80/TCP | Auth web do launcher (login → token de sessão) |

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

Cada serviço em seu processo (ou via Docker — veja [`RakionServer/Dockerfile`](RakionServer/Dockerfile)):

1. **MariaDB** (`lower_case_table_names=1`), importe o schema (`../database/`), crie a conta de teste.
2. **Auth web:** `python ../web/launcher_web.py`
3. **Broker:** rode o `RakionServer.Broker` (lê `Settings/`).
4. **World:** rode o `RakionServer.World` (lê `worldserver.ini`).

Passo a passo detalhado: [`RakionServer/TUTORIAL.md`](RakionServer/TUTORIAL.md). Protocolo: [`RakionServer/PROTOCOL.md`](RakionServer/PROTOCOL.md).

## Notas

- O World loga fluxos críticos (login, transações, integrações) no console — redirecione para arquivo se quiser persistir.
- Regras de negócio (loja, inventário, ouro) ficam **no servidor**; o cliente só renderiza.
