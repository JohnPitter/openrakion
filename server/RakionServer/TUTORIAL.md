# Tutorial — subindo o servidor Rakion v258 (.NET) para uso pessoal

Este guia mostra, do zero, como compilar e rodar a stack de servidor do Rakion v258
reconstruída em .NET (**broker + world + buddy**). É a reconstrução nativa Linux/Windows
(sem Wine) descrita no `README.md`. Funciona em **Windows** e **Linux**.

> **Aviso de escopo.** Este projeto reconstrói os servidores da SoftNyx (worldserver,
> broker, buddy). Ele **não** inclui o **GameGuard** (anticheat nProtect, de terceiros) —
> ver a seção "Cliente e GameGuard". Use apenas para fins pessoais/educacionais e com
> conteúdo que você possua legalmente. O dump do banco, a auth web e o cliente do jogo
> **não acompanham** este repositório.

---

## 0. Visão geral da arquitetura

```
            (cliente do jogo)
                  │  TCP 40706 (lista de servidores)
                  ▼
            ┌───────────┐    IPC UDP 40706       ┌──────────────┐
            │  BROKER    │◄──────────────────────►│   WORLD      │
            │ (lista de  │  (ServerInfo/login)    │ (jogo)       │
            │  mundos)   │                        │ TCP 40708    │
            └───────────┘                        │ UDP 40708/9  │
                  ▲                               └──────┬───────┘
   cliente ───────┘  TCP 40708 (login/lobby/jogo)        │ MySQL
                                                          ▼
   cliente ──► BUDDY  TCP 8500 / 8504 (amigos)      ┌──────────┐
                                                    │ MariaDB  │  db `rakion`
   auth web (PHP) ──► :80  (login do launcher)      └──────────┘
```

| Componente | Porta(s) | Projeto .NET | Binário |
|---|---|---|---|
| Broker (lista de mundos) | TCP+UDP **40706** | `RakionServer.Broker` | `BrokenServer.dll` |
| World (jogo) | TCP **40708**, UDP **40708/40709** | `RakionServer.World` | `RakionWorldServer.dll` |
| Buddy (amigos) | TCP **8500** e **8504** | `RakionServer.Buddy` | `BuddyServer.dll` |
| MariaDB/MySQL | **3306** | — | db `rakion` |
| Auth web (opcional) | **80** | — (PHP) | só p/ login via launcher |

---

## 1. Pré-requisitos

Instale:

1. **.NET SDK 9** — https://dotnet.microsoft.com/download
   - Verifique: `dotnet --version` (deve mostrar `9.x`).
   - *(No Windows sem instalar global, dá para usar o script oficial: `dotnet-install.ps1 -Channel 9.0 -InstallDir $env:USERPROFILE\.dotnet`.)*
2. **MariaDB 10.11+** (ou MySQL 8) — https://mariadb.org/download
   - Pode ser a versão portátil (ZIP) — ver passo 3.
3. **Git** (para clonar) — opcional.
4. Você precisa fornecer (não vêm no repo):
   - **Dump do banco** `rakion` (schema + dados). Nome esperado: `rakion_data.sql` (dump completo) ou `rakion_all.sql` (só schema).
   - **(opcional)** a **auth web PHP** (login do launcher), se for usar o launcher original.
   - O **cliente do jogo** v258 (ver seção "Cliente e GameGuard").

---

## 2. Compilar a solução

Na pasta `RakionServer/`:

```bash
dotnet build RakionServer.sln -c Release
```

Deve terminar com **`0 Erros / 0 Avisos`**. Isso gera:
- `src/RakionServer.Broker/bin/Release/net9.0/BrokenServer.dll`
- `src/RakionServer.World/bin/Release/net9.0/RakionWorldServer.dll`
- `src/RakionServer.Buddy/bin/Release/net9.0/BuddyServer.dll`

---

## 3. Subir o MariaDB e carregar o banco

### Windows (MariaDB portátil — ZIP)

```powershell
# 1) baixe e extraia o ZIP do MariaDB em, ex., C:\mariadb
# 2) inicialize um datadir
C:\mariadb\bin\mariadb-install-db.exe --datadir=C:\mariadb\data

# 3) suba o servidor (Windows usa lower_case_table_names=1 por padrão — necessário p/ o Rakion)
C:\mariadb\bin\mariadbd.exe --datadir=C:\mariadb\data --port=3306 --bind-address=127.0.0.1

# 4) (em outro terminal) defina a senha do root e crie o usuário 127.0.0.1
C:\mariadb\bin\mysql.exe -u root -e "ALTER USER 'root'@'localhost' IDENTIFIED BY '123456'; CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '123456'; GRANT ALL ON *.* TO 'root'@'127.0.0.1'; FLUSH PRIVILEGES;"

# 5) carregue o dump do jogo
cmd /c "C:\mariadb\bin\mysql.exe -u root -p123456 < caminho\para\rakion_data.sql"
```

### Linux

```bash
sudo apt install mariadb-server
sudo mysql -e "CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '123456'; GRANT ALL ON *.* TO 'root'@'127.0.0.1'; FLUSH PRIVILEGES;"
# IMPORTANTE: habilite lower_case_table_names=1 em /etc/mysql/my.cnf na seção [mysqld]
#   [mysqld]
#   lower_case_table_names=1
# (defina ANTES de criar o datadir; depois reinicie o mariadb)
sudo mysql -uroot -p123456 < caminho/para/rakion_data.sql
```

### Verifique
```bash
mysql -uroot -p123456 -e "USE rakion; SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='rakion'; SELECT id FROM user LIMIT 3;"
# Esperado: ~62 tabelas e a conta de teste (ex.: 'test')
```

> **`lower_case_table_names=1` é obrigatório** — sem isso o world dá erro
> "Table 'rakion.AdminInfo' doesn't exist". No Windows é o padrão; no Linux precisa setar.

### Conta de teste (se o dump não tiver)
```sql
INSERT IGNORE INTO user (id,password) VALUES ('test','test');
INSERT IGNORE INTO usergameinfo (id,name) VALUES (1,'test');
```

---

## 4. Configurar

### `deploy/worldserver.ini` (config do World)
Já vem pronto apontando para `127.0.0.1`. Ajuste se necessário:
```ini
[Server]
ServerId=1
MaxUser=500
Port=40708
[Broker]
IP=127.0.0.1
Port=40706
[DB]
IP=127.0.0.1
Port=3306
User=root
Pass=123456
Name=rakion
```
(Há também `[USERDB]` e `[LOGDB]` — aponte todos para o mesmo banco para uso pessoal.)

### Broker — `src/RakionServer.Broker/Settings/`
- `Settings.ini` — IP/porta do broker (padrão `0.0.0.0:40706`).
- `GameServers.ini` — a lista de mundos. O `World1` já aponta para `127.0.0.1:40708`.
  Para anunciar um IP público, mude `wan=` e `lan_wan=1`.

---

## 5. Rodar a stack (3 servidores)

Abra **3 terminais** (a ordem importa: broker primeiro):

```bash
# Terminal 1 — Broker
dotnet run --project src/RakionServer.Broker -c Release

# Terminal 2 — World (passe o caminho do .ini)
dotnet run --project src/RakionServer.World -c Release -- deploy/worldserver.ini

# Terminal 3 — Buddy
dotnet run --project src/RakionServer.Buddy -c Release
```

**Sinais de que está tudo OK:**
- Broker: `Loaded 1 Server from server settings` → `Ready for gameserver connection...` →
  ao subir o world: **`Server: <nome> change to online`**.
- World: `AES self-test OK` · `TCP do jogo ouvindo na porta 40708` · `gameplay UDP ... 40709`
  · `conectado — 62 tabelas no schema` · `World Server pronto`.
- Buddy: `ouvindo na porta 8500` e `... 8504`.

> Se o world logar `db: falha ao conectar`, confira se o MariaDB está de pé e a senha no `.ini`.

---

## 6. (Alternativa) Rodar tudo em Docker

Numa máquina com **Docker daemon ativo**, dentro de `RakionServer/`:

```bash
# coloque o dump em deploy/db/rakion_data.sql (e a auth web em deploy/web/, se houver)
mkdir -p deploy/db && cp /caminho/rakion_data.sql deploy/db/

docker build -t rakion-net:latest .
docker run -d --name rakion \
  -p 40706:40706 -p 40708:40708 -p 40708:40708/udp -p 40709:40709/udp \
  -p 8500:8500 -p 8504:8504 -p 80:80 \
  -v rakiondb:/var/lib/mysql \
  rakion-net:latest

docker logs -f rakion   # acompanhe broker/world/buddy
```
A imagem sobe MariaDB + (auth web PHP, se presente) + os 3 servidores num container só.

---

## 7. Testar sem o cliente do jogo

Para confirmar o login + lobby sem precisar do cliente, use o harness incluso:

```bash
# com o World rodando na 40708:
python ../../tools/difftest.py 40708
```
Saída esperada (o login carrega a conta real do banco e o lobby responde cifrado):
```
login    : 1100000002004a50...01        # LoginComplete (msgType 2)
enterchan: 1200....                      # resposta de canal (AES)
worldinfo: 42....                        # info do mundo (AES)
```
No log do World você verá `login userID='test'` e `'test' logado (char='...', gold=...)`.

---

## 8. Cliente e GameGuard (importante)

O cliente original do Rakion v258 carrega o **GameGuard (nProtect)** — anticheat de
terceiros cujo servidor de auth está **morto**. Esta reconstrução **não emula** o GameGuard
(é fora de escopo; ver `../../docs/protocol-world.md`/`README.md`). Implicações:

- O **servidor .NET não exige GameGuard** — ele aceita clientes que não falam GG (diferente
  do servidor nativo original, que gateia a conexão no handshake do GG).
- Para um **cliente real** conectar, use um cliente com o **GameGuard removido/patcheado**
  (no material do projeto: `rakion.GGp4.exe`, gate @0x49624c). Sem isso, o cliente original
  trava no init do GameGuard (não é o servidor — é o GG).
- Configure no cliente (em `DataSetup.xfs` → `locale.ini`) o **BrokerIP/BrokerPort** para o
  IP da sua máquina e a porta **40706**. Se usar o launcher, aponte a **auth web** para o seu IP.

---

## 9. Criar contas

Para uso pessoal, insira direto no banco:
```sql
USE rakion;
INSERT INTO user (id,password) VALUES ('meunick','minhasenha');
INSERT INTO usergameinfo (id,name,gold) VALUES (2,'meunick',10000);
```
(Se usar a auth web/launcher, há um fluxo de criação de conta via PHP — ver `docs/ADMIN.md`
do material do tutorial.)

---

## 10. Solução de problemas

| Sintoma | Causa provável | Correção |
|---|---|---|
| World: `falha ao conectar` (DB) | MariaDB down / senha errada | suba o MariaDB; confira `[DB]` no `.ini` |
| `Table 'rakion.X' doesn't exist` | `lower_case_table_names` ≠ 1 | habilite no my.cnf **antes** de criar o datadir |
| Broker não loga "change to online" | world não alcança o broker | confira `GameServers.ini` (ip/ipcport 40708) e firewall |
| `40708 em uso` | outra instância rodando | mate o processo / mude a porta no `.ini` |
| Cliente trava antes de conectar | GameGuard | use o cliente sem GG (`rakion.GGp4.exe`) |
| Porta UDP 40708 conflita | broker IPC + gameplay na mesma porta | é esperado: o BrokerLink é dono da 40708; gameplay extra usa 40709 |

---

## 11. Referência rápida

- **Build:** `dotnet build RakionServer.sln -c Release`
- **Publicar:** `dotnet publish src/RakionServer.World/RakionServer.World.csproj -c Release -o out/world`
- **Protocolo:** `../../docs/protocol-world.md` (world) e `../../docs/protocol-buddy.md` (buddy)
- **Arquitetura/estado:** `README.md`
- **Estender (novos handlers):** preencha os métodos em
  `src/RakionServer.World/Network/WorldHandlers*.cs` (cada opcode é um método; o endereço
  `FUN_xxxx` de origem está no comentário).

Bom jogo — e use com responsabilidade. 🎮
