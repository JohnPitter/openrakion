# Guia de Setup — Servidor Rakion v258

Passo a passo para subir o servidor e logar. Pressupõe que você **já tem os arquivos originais do Rakion** (cliente + binários de servidor). Veja [NOTICE.md](../NOTICE.md).

## Arquitetura

```
Cliente (Windows)                         Servidor (Docker/Linux)
─────────────────                         ───────────────────────
NyxLauncher  ── HTTP ──►  web auth (PHP, php -S :80)
   │  login web → token de sessão           ├─ launcherlogin.php  (valida conta, retorna sha1)
   │                                         ├─ fetch/fetch.php    (versão/patch)
   ▼                                         └─ file.php           (md5 dos arquivos)
rakion.bin ── TCP 40706 ──► BrokerServer  (lista de world via GameServers.ini)
   │                                         │
   └──────── TCP/UDP 40708/40709 ──────────► RakionWorldServ ──► MariaDB
```

## 1. Banco de dados (MariaDB)

- Crie o banco `rakion` e importe o schema (veja `database/`).
- Use `lower_case_table_names=1` (senão dá `Table 'rakion.AdminInfo' doesn't exist`).
- Conta de teste: insira um usuário em `user` (id/senha) + `usergameinfo` (id, gold, etc.) + `cash`.

## 2. Servidor (Docker, sob Wine para os binários Windows)

- Os binários de servidor (`BrokerServer.exe`/`BrokenServer.exe`, `RakionWorldServ.exe`) rodam sob **Wine** num container Linux.
- `RakionWorldServ` via SCM do Wine (`-install` + `sc start "Rakion World [1]"`).
- Patch de 1 byte no `RakionWorldServ.exe` para tornar o envio de e-mail/CDOSYS não-fatal (senão crasha sem servidor SMTP).
- Veja `server/` para o Dockerfile e scripts de entrypoint (templates).

## 3. Configuração de servidor (templates em `server/config/`)

- **`Settings.ini`** (BrokerServer): `ip=0.0.0.0`, `port=40706`.
- **`GameServers.ini`** (BrokerServer): define o **IP que o broker anuncia** para o cliente. Campos `ip`/`wan`/`lan_wan` — este é o ponto do *advertised IP*. Para tudo numa máquina, `ip=127.0.0.1`, `lan_wan=0`.
- **`worldserver.ini`** (RakionWorldServ): `Port=40708`, `[Broker] IP=127.0.0.1:40706`, `[ServerList] IP0=127.0.0.1`, credenciais de DB.

## 4. Backend web (`web/`)

PHP simples (base CarlosX, GPL). Sirva com `php -S 0.0.0.0:80 -t web`.

- **`launcherlogin.php`** — recebe `?user=X&pass=HEX`, valida em `user`, retorna `sha1(user + "freeclient" + hexpass)` = token de sessão.
- **`fetch/fetch.php`** — checagem de versão por `AppId` (tabela `fetchapp`; ex.: app `400`=launcher, `11001`=Rakion v258).
- **`file.php`** — retorna o sha1 esperado de arquivos do cliente (checagem de integridade; o jogo tolera divergência neste build).
- Ajuste o DB em `config.php`.

## 5. Rede (host)

O cliente conecta nos IPs que estão no `locale.ini` (dentro do `DataSetup.xfs`) e na config do launcher:
- **Broker** (ex.: `61.74.68.178:40706`) e **auth web** (ex.: `192.168.1.5`).
- Faça esses IPs apontarem para o seu servidor. Numa só máquina, use **aliases de IP** na interface de rede (ou edite o `locale.ini`/`config.xfs` para os seus IPs).

## 6. Cliente

1. Coloque os binários `Bin258` (engine/rakion/etc.) e o `NyxLauncher258`.
2. Adicione **`PMReaderLib.dll`** e o **`config.xfs` gerado** (veja [config-xfs.md](config-xfs.md)) em `client/Bin/`.
3. Adicione o **`load.bin`** em `client/Bin/`.
4. Crie a chave de registro `HKCU\Software\Softnyx\Rakion` com `RootDir` = pasta do cliente.
5. Edite o `DataSetup.xfs` (`locale.ini`) com os seus IPs, se necessário (use `tools/xfs_repack.py`).

## 7. Logar

1. Suba o servidor (Docker) e o web.
2. Abra o `NyxLauncher.exe`, logue `test/test`, START GAME.
3. O launcher faz `launcherlogin.php` → token → lança `load.bin`→`rakion.bin` → broker → world.

**Sinais de sucesso** (logs do servidor):
- web: `GET /launcherlogin.php?user=test&pass=...` → 200
- broker: `SV_AUTH_LOGIN_*` → `Opcode: 257` → `Recv Serv-Con USER: 1`, `CUR=1`
- MySQL: `INSERT INTO LogUserConnect ...`, `SELECT ... FROM UserItemInfo`, etc.

## Dicas

- **Modo janela**: no `Scripts/PersistentSymbols.ini`, `m_bActiveFullScreen=(INDEX)0`. A janela abre sem barra de título no canto — use um utilitário para adicionar `WS_CAPTION` (veja `tools/`).
- **Renomear o servidor** no lobby: edite o 1º `WorldServerName` no `locale.ini` dentro do `DataSetup.xfs` com `tools/xfs_repack.py`.
