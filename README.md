# OpenRakion

Servidor privado **open source** para **Rakion** (SoftNyx, versão *XfsVer258*), com broker,
World, Buddy, launcher web e painel administrativo reescritos em **.NET 9**. O servidor roda
nativamente em Windows e Linux, sem os executáveis proprietários de servidor e sem Wine. O
launcher e a camada de compatibilidade do cliente são voltados para Windows.

> ⚠️ **Legal:** Este repositório **NÃO contém** os arquivos proprietários do **cliente** Rakion (`rakion.bin`/`rakion.exe`, `engine.dll`, `*.xfs`, `NyxLauncher`, GameGuard). Esses são **copyright da SoftNyx** — obtenha de uma cópia legítima do jogo. Aqui está apenas **trabalho original** (servidor .NET, auth web, tools, docs) e componentes open source de terceiros. Veja [NOTICE.md](NOTICE.md).

---

## O que mudou

Servidores privados de Rakion existiam há mais de uma década, mas dependiam dos **binários de servidor da SoftNyx** (rodando sob Wine) e quase sempre travavam no **"stuck at login"**. O OpenRakion agora vai muito além:

- 🟢 **Servidor 100% próprio em .NET** (não usa os executáveis da SoftNyx). Serviços:
  - **Broker** (`RakionServer.Broker`) — lista de servidores/canais, anuncia o world (advertised IP) e faz a ponte de login.
  - **World** (`RakionServer.World`) — login completo, lobby, lista de canais/salas, seleção de personagem, **inventário + armazém (box) persistente**, **loja (compra e venda) com saldo em tempo real**, **Power User** (compra + bônus configurável de XP/gold), chat, handshake **UDP de gameplay**, **motor de partida** (Golem/Deathmatch/TeamDeath/Boss com settlement persistido) e **bots PvP** server-side (`/addbot`).
  - **Buddy** (`RakionServer.Buddy`) — serviço **canônico** de amigos/mensageiro (F9): login, adicionar/remover amigo, grupos, apelido, **presença** (amigo acende online), **SMS/PM** e **brokering de tunnel P2P** para convite/mensagem direta.
  - **LauncherWeb** (`RakionServer.LauncherWeb`) — autenticação por ticket, update assinado,
    página de compra de Cash e endpoint público de status/jogadores online.
  - **Admin** (`RakionServer.Admin`) — painel web (Blazor) pra gerenciar **contas, gold/cash, itens no inventário** (visual estilo jogo, com nomes), a **config do Power User** (preço/bônus/multiplicadores/promoção) e **publicar updates** do launcher.
- 🟢 **Launcher próprio para Windows** — login, atualização, resolução/modo de tela, opções do jogo,
  status do servidor e quantidade de jogadores conectados.
- 🟢 **Compatibilidade v258 por DLL** — `version.dll` faz somente o bootstrap e carrega
  `RakionClientPatch.dll`, que concentra redirecionamento de IP, retirada do GameGuard, correções de
  UI/lifecycle, Add Bot, ponte de combate dos bots e acesso à página de Cash.
- 🟢 **Pacote reproduzível do cliente** — um comando gera uma sobreposição self-contained, com
  `Bin`, `Data`, launcher, configurações, manifesto SHA-256 e verificador de integridade.
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
| **Stack de servidor roda nativo em Windows/Linux** (.NET 9, sem Wine) | ✅ |
| **Launcher**: login, update, opções, resolução, status e jogadores online | ✅ |
| **DLL de compatibilidade v258**: no-GG, IP, UI e patches golden em memória | ✅ |
| Criação/troca de personagem e atualização do Messenger no char-select | ✅ validado no cliente |
| Stage PvE: criação, entrada, saída durante a partida e retorno à lista | ✅ validado no cliente |
| Botão **Buy Cash** e página web de recarga | ✅ implementado com debounce; reabertura aguarda confirmação visual final |
| Modos PvP (Golem/Deathmatch/TeamDeath/Boss) | ✅ motor de round server-side + **validado headless com 2 clientes no fio** (criar/entrar sala, ready/start, movimento e combate UDP, win/lose persistido no DB) |
| **Bots PvP** (`Add Bot` somente em Battle) | ✅ fluxo, movimento, dano e morte cobertos headless; smoke visual continua sendo gate de lançamento |
| **Buddy/Mensageiro (F9)**: amigos, grupos, presença, SMS/PM, tunnel P2P | ✅ serviço canônico do stack; persistência atômica de amigos/grupos e presença bidirecional |
| **GameGuard** original | ❌ serviço externo offline; fluxo neutralizado pela DLL de compatibilidade |
| Navegação inventário/loja ↔ lista de salas (botão **Previous**) | ✅ |

> O GameGuard original não é recuperável porque depende de um serviço externo desativado. O pacote
> atual usa a DLL de compatibilidade para neutralizar esse fluxo. Isso não deve ser confundido com
> validação gráfica completa de todos os modos, que ainda precisa ser registrada por build.

### Engenharia reversa e validação

- **RE estático completo** do v258: os 29 domínios do jogo, todas as 10 famílias de NPC + 3 classes especiais, e um **censo de 116 classes de entidade** com veredito por classe. Ver [`docs/audits/entity-class-census.md`](docs/audits/entity-class-census.md) e [`docs/audits/re-status-summary.md`](docs/audits/re-status-summary.md).
- **Validação dinâmica via backend** com **dois clientes headless** dirigindo o `WorldServer` real (TCP + AES + UDP + banco): login, sala, partida, movimento/combate UDP, settlement persistido, matriz de modos e chat. Ver [`docs/audits/dynamic-validation.md`](docs/audits/dynamic-validation.md).
- suítes do servidor e **21 testes do launcher** verdes, além do build nativo `/W4 /WX` e smoke
  das 17 exportações do proxy `version.dll`.

Documentação técnica, mapas de RE e lacunas de validação: [`docs/README.md`](docs/README.md).

---

## Estrutura do repositório

```
openrakion/
├── client/
│   ├── RakionLauncher/       Launcher WinForms
│   ├── RakionClientCompat/   Proxy version.dll e RakionClientPatch.dll
│   └── build_client_package.ps1  Gera a sobreposição distribuível
├── server/
│   ├── RakionServer/     Servidor .NET — código-fonte
│   │   ├── src/          .Broker, .World, .Buddy, .Common, .LauncherWeb (auth :80), .Admin (painel :8080)
│   │   ├── tools/        OracleDiff (diff de blobs de login)
│   │   ├── deploy/        worldserver.ini (template)
│   │   ├── start-stack.ps1  Sobe os serviços .NET
│   │   ├── Dockerfile     Container (Linux + .NET)
│   │   └── TUTORIAL.md    Setup passo a passo
│   ├── config/           Templates (Settings.ini, GameServers.ini, worldserver.ini)
│   └── README.md
├── tools/                XFS2, probes, publicação e análise reproduzível
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

# 4) rodar — tudo de uma vez (Windows PowerShell):
.\start-stack.ps1
#    ou cada serviço em seu processo:
#    - launcher web: RakionServer.LauncherWeb (RakionLauncherWeb)  porta 80
#    - broker:       RakionServer.Broker      (BrokenServer)       porta 40706
#    - world:        RakionServer.World       (RakionWorldServer)  TCP 40708 / UDP 40708-40709
#    - admin:        RakionServer.Admin       (RakionAdmin)        porta 8080
#    - buddy:        RakionServer.Buddy       (BuddyServer)         portas 8500/8504
```

> Os **web apps** (LauncherWeb/Admin) usam o runtime ASP.NET. Se o .NET não estiver no diretório
> padrão do sistema, exporte `DOTNET_ROOT` — o `start-stack.ps1` já cuida disso.

Com o stack ativo, `GET /api/v1/server-status` responde o estado do World, jogadores autenticados e
capacidade. O launcher consulta esse endpoint ao abrir e a cada dez segundos; snapshots vencidos
são tratados como offline. Após o login, os inputs são substituídos pelos amigos online e pelas
ações de trocar conta, iniciar o jogo e abrir opções; os nomes são atualizados a cada 30 segundos
por um endpoint autenticado.

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

## Preparar o cliente para distribuição

O OpenRakion não distribui os arquivos proprietários do jogo. O administrador fornece uma cópia
legítima do baseline v258 golden e executa, na raiz deste repositório:

```powershell
.\client\build_client_package.ps1 `
  -GoldenRoot '<caminho-do-cliente-v258-golden>' `
  -ServerHost '<ipv4-do-world>' `
  -LauncherBaseUrl 'https://launcher.exemplo.com/' `
  -CashStoreUrl 'https://launcher.exemplo.com/cash'
```

A saída é `artifacts/client-v258-overlay`, com esta estrutura:

```text
client-v258-overlay/
├── RakionLauncher.exe
├── launcher.settings.json
├── server.host
├── cash-shop.url
├── display.mode
├── client-package.json
├── verify-package.ps1
├── Bin/
│   ├── rakion.exe
│   ├── engine.dll
│   ├── version.dll
│   └── RakionClientPatch.dll
└── Data/
    └── SeriousSam.gms
```

`DataSetup.xfs` também fica na raiz da pasta. O launcher é self-contained; o jogador não precisa
instalar o .NET. Antes de distribuir ou depois de copiar sobre o cliente original, valide:

```powershell
.\verify-package.ps1
```

Copie **todo o conteúdo** da sobreposição para a raiz do cliente, preservando `Bin` e `Data`, e
inicie `RakionLauncher.exe`. Não copie somente as DLLs para um cliente de versão desconhecida e não
renomeie `rakion.bin` para `rakion.exe`. O procedimento completo, hashes e rollback estão em
[docs/guides/client-compatibility-dll.md](docs/guides/client-compatibility-dll.md).

Para habilitar atualização assinada, acrescente
`-EnableUpdates -PublicKeyPath '<chave-publica.pem>'`. A URL remota do LauncherWeb exige HTTPS.

## GameGuard (veredito honesto)

O GameGuard (nProtect, 2007) não inicializa mais porque seu serviço de update/auth está offline. Não
é um problema resolvido por VM ou versão do Windows. O fluxo suportado usa o executável pristine
v258 com `version.dll` + `RakionClientPatch.dll`; os patches são aplicados em memória antes do entry
point, sem distribuir um executável modificado. Detalhes em
[docs/guides/gameguard.md](docs/guides/gameguard.md) e
[docs/guides/client-compatibility-dll.md](docs/guides/client-compatibility-dll.md).

## Tools

- **`tools/xfs_repack.py`** / **`xfs_read.py`** — leitor/repacker do formato **XFS2** da SoftNyx (Python puro). Edita arquivos dentro de `DataSetup.xfs` (ex.: renomear o servidor no `locale.ini`) sem o iXFS. Round-trip validado.
- **`tools/worldprobe.py`** / **`listprobe.py`** — sondas headless do protocolo do world (login, inventário, loja) — conectam, decifram o AES e validam os pacotes sem abrir o jogo.
- **`tools/extract_item_names.py`** / **`finalize_item_names.py`** — extraem o nome de cada item dos labels do `items.dat` (o `iteminfo` do DB não tem nome) e geram um fallback "categoria+nível" para os ausentes; saída usada pelo painel admin (`item_names.tsv`).

## Créditos

Apoiado em mais de uma década de trabalho da comunidade — em especial **CarlosX** ([RakionLauncher](https://github.com/CarlosX/RakionLauncher), GPL-3.0: BrokenServer, GConfig, auth web PHP), **SirMaster**, **jdastridge** (iXFS) e a comunidade do **RaGEZONE**. O servidor .NET deste repo foi **reescrito do zero** a partir da engenharia reversa do protocolo, mas se apoia nesse conhecimento. Veja **[CREDITS.md](CREDITS.md)**.

## Licença

Material original deste repositório (servidor .NET, tools, docs) sob **GPL-3.0**; componentes de terceiros mantêm suas licenças GPL originais. Veja [LICENSE](LICENSE) e [NOTICE.md](NOTICE.md).
