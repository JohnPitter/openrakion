# OpenRakion

Servidor privado **open source** para **Rakion** (SoftNyx, versão *XfsVer258*) — com um **servidor reescrito do zero em .NET**: broker, world (lobby/salas/partida) e buddy, mais o backend de autenticação. Você loga, entra no lobby, abre o inventário/armazém, compra na loja e entra em partida — **sem precisar dos executáveis de servidor proprietários da SoftNyx**.

> ⚠️ **Legal:** Este repositório **NÃO contém** os arquivos proprietários do **cliente** Rakion (`rakion.bin`/`rakion.exe`, `engine.dll`, `*.xfs`, `NyxLauncher`, GameGuard). Esses são **copyright da SoftNyx** — obtenha de uma cópia legítima do jogo. Aqui está apenas **trabalho original** (servidor .NET, auth web, tools, docs) e componentes open source de terceiros. Veja [NOTICE.md](NOTICE.md).

---

## O que mudou

Servidores privados de Rakion existiam há mais de uma década, mas dependiam dos **binários de servidor da SoftNyx** (rodando sob Wine) e quase sempre travavam no **"stuck at login"**. O OpenRakion agora vai muito além:

- 🟢 **Servidor 100% próprio em .NET** (não usa os executáveis da SoftNyx). Três serviços:
  - **Broker** (`RakionServer.Broker`) — lista de servidores/canais, anuncia o world (advertised IP) e faz a ponte de login.
  - **World** (`RakionServer.World`) — login completo, lobby, lista de canais/salas, seleção de personagem, **inventário + armazém (box) persistente**, **loja com débito de ouro em tempo real**, chat, handshake **UDP de gameplay** e motor de partida.
  - **Buddy** (`RakionServer.Buddy`) — lista de amigos/mensageiro.
- 🟢 **Login resolvido** — a peça que travava a comunidade. A conta loga, o world aceita, o banco carrega personagem e itens.
- 🟢 **Auth web em Python** (`web/launcher_web.py`) — reimplementação fiel das páginas PHP de login do launcher (sem precisar de PHP).

## Status honesto (o que funciona / o que não)

| Área | Status |
|---|---|
| Login completo (broker → world → DB) | ✅ funciona |
| Lobby, canais, lista de salas | ✅ |
| Seleção de personagem, inventário, **armazém (box) entre sessões** | ✅ |
| **Loja + débito de ouro/cash em tempo real** | ✅ |
| Handshake UDP + entrada no campo + motor de partida | ✅ |
| Modos PvP/deathmatch completos | 🟡 em progresso |
| **GameGuard** original | ❌ morto (servidor nProtect offline desde ~2007) — exige client no-GG |
| Botão **"Previous"** no client no-GG | ❌ crasha (parede de GG-removal estrutural; *workaround:* re-logar) |

> Os ❌ são limitações **do cliente proprietário sem GameGuard**, não do servidor.

---

## Estrutura do repositório

```
openrakion/
├── server/
│   ├── RakionServer/     Servidor .NET (broker + world + buddy + common) — código-fonte
│   │   ├── src/          Os 4 projetos (.Broker, .World, .Buddy, .Common)
│   │   ├── tools/        OracleDiff (diff de blobs de login)
│   │   ├── deploy/        worldserver.ini (template)
│   │   ├── Dockerfile     Container (Linux + .NET)
│   │   ├── README.md      Visão geral do solution (origem da RE, estrutura)
│   │   └── TUTORIAL.md    Setup passo a passo
│   ├── config/           Templates (Settings.ini, GameServers.ini, worldserver.ini)
│   └── README.md
├── web/
│   ├── launcher_web.py   Auth web (Python) — login do launcher
│   └── *.php             Versão PHP de referência (base CarlosX)
├── tools/                xfs_read/repack (XFS2), worldprobe/listprobe (sondas), difftest (teste diferencial)
├── database/             Schema do banco (MariaDB)
├── docs/                 Guias (setup, GameGuard, config.xfs) + protocolo (world/buddy)
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

# 4) rodar (cada um em seu processo)
#    - auth web:  python web/launcher_web.py              (porta 80)
#    - broker:    RakionServer.Broker (BrokenServer)      (porta 40706)
#    - world:     RakionServer.World  (RakionWorldServer) (TCP 40708 / UDP 40708-40709)
#    - buddy:     RakionServer.Buddy  (opcional)
```

> **Credenciais:** os templates usam MariaDB local `root` / `123456` (senha de desenvolvimento, documentada no tutorial). **Troque para produção** e nunca exponha a porta 3306 publicamente.

## O cliente (proprietário)

O OpenRakion **não distribui** o cliente. Você precisa de uma cópia legítima do Rakion v258 e, para conectar a um servidor próprio:

1. Gerar o **`config.xfs`** apontando para a SUA URL web (senão o client trava em *"Config.xfs File not found or changed"*) — veja [docs/config-xfs.md](docs/config-xfs.md).
2. Usar um cliente **no-GG** (o GameGuard original não inicializa mais — veja abaixo).

## GameGuard (veredito honesto)

O GameGuard (nProtect, 2007) **não inicializa mais**: o GameMon requisita o servidor de update/auth do nProtect, que está **offline** há anos (*"Game guard error : 0"*). **Não** é problema de Windows 11/driver; VM Win7/10 **não resolve**. Saídas: jogar pelo fluxo do launcher (em alguns builds o `rakion.bin` conecta com o GG falhando de forma não-fatal) ou aplicar um patch *no-GG* no client. Detalhes em [docs/gameguard.md](docs/gameguard.md).

## Tools

- **`tools/xfs_repack.py`** / **`xfs_read.py`** — leitor/repacker do formato **XFS2** da SoftNyx (Python puro). Edita arquivos dentro de `DataSetup.xfs` (ex.: renomear o servidor no `locale.ini`) sem o iXFS. Round-trip validado.
- **`tools/worldprobe.py`** / **`listprobe.py`** — sondas headless do protocolo do world (login, inventário, loja) — conectam, decifram o AES e validam os pacotes sem abrir o jogo.

## Créditos

Apoiado em mais de uma década de trabalho da comunidade — em especial **CarlosX** ([RakionLauncher](https://github.com/CarlosX/RakionLauncher), GPL-3.0: BrokenServer, GConfig, auth web PHP), **SirMaster**, **jdastridge** (iXFS) e a comunidade do **RaGEZONE**. O servidor .NET deste repo foi **reescrito do zero** a partir da engenharia reversa do protocolo, mas se apoia nesse conhecimento. Veja **[CREDITS.md](CREDITS.md)**.

## Licença

Material original deste repositório (servidor .NET, tools, docs) sob **GPL-3.0**; componentes de terceiros mantêm suas licenças GPL originais. Veja [LICENSE](LICENSE) e [NOTICE.md](NOTICE.md).
