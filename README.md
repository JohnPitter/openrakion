# OpenRakion

Servidor privado **open source** para **Rakion** (SoftNyx, versão *XfsVer258*) — com um **servidor reescrito do zero em .NET**: broker, world (lobby/salas/partida) e buddy, mais o backend de autenticação. Você loga, entra no lobby, abre o inventário/armazém, **compra e vende** na loja e entra em partida — **sem precisar dos executáveis de servidor proprietários da SoftNyx**. Roda nativo em **Windows, Linux e Mac** (.NET 9, sem Wine).

> ⚠️ **Legal:** Este repositório **NÃO contém** os arquivos proprietários do **cliente** Rakion (`rakion.bin`/`rakion.exe`, `engine.dll`, `*.xfs`, `NyxLauncher`, GameGuard). Esses são **copyright da SoftNyx** — obtenha de uma cópia legítima do jogo. Aqui está apenas **trabalho original** (servidor .NET, auth web, tools, docs) e componentes open source de terceiros. Veja [NOTICE.md](NOTICE.md).

---

## O que mudou

Servidores privados de Rakion existiam há mais de uma década, mas dependiam dos **binários de servidor da SoftNyx** (rodando sob Wine) e quase sempre travavam no **"stuck at login"**. O OpenRakion agora vai muito além:

- 🟢 **Servidor 100% próprio em .NET** (não usa os executáveis da SoftNyx). Serviços:
  - **Broker** (`RakionServer.Broker`) — lista de servidores/canais, anuncia o world (advertised IP) e faz a ponte de login.
  - **World** (`RakionServer.World`) — login completo, lobby, lista de canais/salas, seleção de personagem, **inventário + armazém (box) persistente**, **loja (compra e venda) com saldo em tempo real**, **Power User** (compra + bônus configurável de XP/gold), chat, handshake **UDP de gameplay** e motor de partida.
  - **Buddy** (`RakionServer.Buddy`) — lista de amigos/mensageiro.
  - **LauncherWeb** (`RakionServer.LauncherWeb`) — auth web do launcher (login + auto-update `fetch`) em ASP.NET; reimplementa o backend PHP de auth/`fetch` do [RakionLauncher do CarlosX](https://github.com/CarlosX/RakionLauncher) (ver [CREDITS.md](CREDITS.md); o PHP original não é redistribuído aqui).
  - **Admin** (`RakionServer.Admin`) — painel web (Blazor) pra gerenciar **contas, gold/cash, itens no inventário** (visual estilo jogo, com nomes), a **config do Power User** (preço/bônus/multiplicadores/promoção) e **publicar updates** do launcher.
- 🟢 **Login resolvido** — a peça que travava a comunidade. A conta loga, o world aceita, o banco carrega personagem e itens.

## Status honesto (o que funciona / o que não)

| Área | Status |
|---|---|
| Login completo (broker → world → DB) | ✅ funciona |
| Lobby, canais, lista de salas | ✅ |
| Seleção de personagem, inventário, **armazém (box) entre sessões** | ✅ |
| **Loja: compra e venda**, com saldo de ouro/cash em tempo real | ✅ |
| Handshake UDP + entrada no campo + motor de partida | ✅ |
| **Power User** (compra + bônus de XP/gold configurável + bonus points) | ✅ |
| **Painel admin** (contas, gold/cash, itens, config do PU, updates) | ✅ |
| **Stack roda nativo em Windows/Linux/Mac** (.NET 9, sem Wine/P-Invoke) | ✅ |
| Modos PvP/deathmatch completos | 🟡 motor de round server-side implementado (timer, fim de round por placar, win/lose/draw persistido); falta validar com 2 clientes |
| **GameGuard** original | ❌ morto (servidor nProtect offline desde ~2007) — exige client no-GG |
| Navegação inventário/loja ↔ lista de salas (botão **Previous**) | ✅ |

> O único ❌ é o **GameGuard** — não é limitação do nosso servidor, e sim de um serviço externo da nProtect offline há anos ([detalhe](#gameguard-veredito-honesto)).

Documentação técnica, mapas de RE e lacunas de validação: [`docs/README.md`](docs/README.md).

---

## Estrutura do repositório

```
openrakion/
├── server/
│   ├── RakionServer/     Servidor .NET — código-fonte
│   │   ├── src/          .Broker, .World, .Buddy, .Common, .LauncherWeb (auth :80), .Admin (painel :8080)
│   │   ├── tools/        OracleDiff (diff de blobs de login)
│   │   ├── deploy/        worldserver.ini (template)
│   │   ├── start-stack.ps1  Sobe os 4 serviços .NET de uma vez
│   │   ├── Dockerfile     Container (Linux + .NET)
│   │   └── TUTORIAL.md    Setup passo a passo
│   ├── config/           Templates (Settings.ini, GameServers.ini, worldserver.ini)
│   └── README.md
├── tools/                xfs_read/repack (XFS2), worldprobe/listprobe (sondas), difftest (teste diferencial)
├── database/             Schema do banco (MariaDB)
├── docs/                 Índice, protocolos, sistemas, guias, auditorias e histórico
├── CREDITS.md
└── NOTICE.md
```

## Servidor .NET — começando

Pré-requisitos: **.NET 9 SDK**, **MariaDB** (localhost). Detalhes completos em [server/RakionServer/TUTORIAL.md](server/RakionServer/TUTORIAL.md).

```bash
# 1) banco: importe o schema (veja database/README.md — o dump vem do RakionLauncher do CarlosX)
mysql -uroot -p123456 rakion < rakion_all.sql        # ajuste a senha; crie a conta de teste

# 2) build
cd server/RakionServer
dotnet build -c Release RakionServer.sln

# 3) configs: ajuste IPs/credenciais em deploy/worldserver.ini e src/RakionServer.Broker/Settings/

# 4) rodar — tudo de uma vez (Windows):
cd server/RakionServer && ./start-stack.ps1
#    ou cada serviço em seu processo:
#    - launcher web: RakionServer.LauncherWeb (RakionLauncherWeb)  porta 80
#    - broker:       RakionServer.Broker      (BrokenServer)       porta 40706
#    - world:        RakionServer.World       (RakionWorldServer)  TCP 40708 / UDP 40708-40709
#    - admin:        RakionServer.Admin       (RakionAdmin)        porta 8080
#    - buddy:        RakionServer.Buddy       (opcional)
```

> Os **web apps** (LauncherWeb/Admin) usam o runtime ASP.NET. Se o .NET for user-local (fora de
> `C:\Program Files\dotnet`), exporte `DOTNET_ROOT` — o `start-stack.ps1` já cuida disso.

### Credenciais de amostra (open source — troque se precisar)

Tudo abaixo é **dev default**, deixado **à mostra de propósito** para facilitar rodar local. Quem for usar muda se quiser — em especial antes de expor à rede:

| Onde | Valor de amostra | Arquivo |
|---|---|---|
| MariaDB | `root` / `123456` | `deploy/worldserver.ini`, `src/*/appsettings.json` |
| Painel admin | variável `Admin__Password` (mín. 16 caracteres) | não há senha versionada |
| Conta de teste | crie a sua (no painel admin ou direto no DB) | — |

O Admin também exige `ConnectionStrings__Rakion` e escuta apenas `127.0.0.1` por padrão. Não exponha
as portas **3306** e **8080** publicamente sem TLS, proxy restrito e credenciais próprias.

### Painel admin

Com `Admin__Password` e `ConnectionStrings__Rakion` definidos, sobe em **http://127.0.0.1:8080**.
Dali dá pra criar/editar **contas**, ajustar **gold/cash**, **adicionar itens** ao inventário,
configurar o **Power User** e **publicar updates** do launcher.

## O cliente (proprietário)

O OpenRakion **não distribui** o cliente. Você precisa de uma cópia legítima do Rakion v258 e, para conectar a um servidor próprio:

1. Gerar o **`config.xfs`** apontando para a SUA URL web (senão o client trava em *"Config.xfs File not found or changed"*) — veja [docs/guides/config-xfs.md](docs/guides/config-xfs.md).
2. Usar um cliente **no-GG** (o GameGuard original não inicializa mais — veja abaixo).

## GameGuard (veredito honesto)

O GameGuard (nProtect, 2007) **não inicializa mais**: o GameMon requisita o servidor de update/auth do nProtect, que está **offline** há anos (*"Game guard error : 0"*). **Não** é problema de Windows 11/driver; VM Win7/10 **não resolve**. Saídas: jogar pelo fluxo do launcher (em alguns builds o `rakion.bin` conecta com o GG falhando de forma não-fatal) ou aplicar um patch *no-GG* no client. Detalhes em [docs/guides/gameguard.md](docs/guides/gameguard.md).

## Tools

- **`tools/xfs_repack.py`** / **`xfs_read.py`** — leitor/repacker do formato **XFS2** da SoftNyx (Python puro). Edita arquivos dentro de `DataSetup.xfs` (ex.: renomear o servidor no `locale.ini`) sem o iXFS. Round-trip validado.
- **`tools/worldprobe.py`** / **`listprobe.py`** — sondas headless do protocolo do world (login, inventário, loja) — conectam, decifram o AES e validam os pacotes sem abrir o jogo.
- **`tools/extract_item_names.py`** / **`finalize_item_names.py`** — extraem o nome de cada item dos labels do `items.dat` (o `iteminfo` do DB não tem nome) e geram um fallback "categoria+nível" para os ausentes; saída usada pelo painel admin (`item_names.tsv`).

## Créditos

Apoiado em mais de uma década de trabalho da comunidade — em especial **CarlosX** ([RakionLauncher](https://github.com/CarlosX/RakionLauncher), GPL-3.0: BrokenServer, GConfig, auth web PHP), **SirMaster**, **jdastridge** (iXFS) e a comunidade do **RaGEZONE**. O servidor .NET deste repo foi **reescrito do zero** a partir da engenharia reversa do protocolo, mas se apoia nesse conhecimento. Veja **[CREDITS.md](CREDITS.md)**.

## Licença

Material original deste repositório (servidor .NET, tools, docs) sob **GPL-3.0**; componentes de terceiros mantêm suas licenças GPL originais. Veja [LICENSE](LICENSE) e [NOTICE.md](NOTICE.md).
