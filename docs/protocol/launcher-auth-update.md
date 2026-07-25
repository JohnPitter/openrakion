# RE do launcher, autenticação e atualização

## Estado

O RE estático do Nyx v258 está fechado e o caminho moderno foi implementado com validação
headless. O fluxo moderno usa ticket aleatório de uso único e atualização assinada com staging e
rollback. Ainda faltam a captura dinâmica descartável dos comandos do Nyx original e a validação
visual de login/update no cliente v258.

| Caminho | Uso | Segurança |
|---|---|---|
| Nyx + `fetch.php` | compatibilidade histórica/local | sem assinatura; não expor à Internet |
| `RakionLauncher` + API v1 | distribuição atual | HTTPS externo, ticket único, ECDSA e SHA-256 |

## RE confirmado do Nyx

O original analisado é
`rakion-work/carlosx/Launcher258/NyxLauncher.exe`. `NyxLauncherEnc.xfs` é um XFS2 de 230 bytes;
o zlib em `0x0C` extrai `__NyxLauncher.INI` com:

```ini
[Rakion]
Url_Fetch=http://192.168.1.5/fetch/fetch.php
Ip=192.168.1.5
Port=40706
```

O request é exatamente `Url_Fetch + "?%d&%d"`, ou seja,
`fetch.php?AppId&Version`. A resposta legada é:

```text
+{NoticeUrl}
={FileUrl}
~{quantidade};{somaFileSize};{VerLimit}
{Command};{FileDir};{FileIns};{FileVer};{FileSize}
```

Cada registro exige cinco tokens separados por `;`, CR ou LF. `FileIns` forma a URL
`FileUrl + FileIns`; seu basename é o temporário local. `FileVer` e `FileSize` alimentam a
verificação de versão/tamanho do XFS, sem hash criptográfico. `FileDir` é obrigatório no parser,
mas não participa do caminho principal observado.

| Comando | Semântica decompilada |
|---|---|
| `M` | mescla os entries do XFS baixado no XFS destino e otimiza |
| `D` | lê paths internos do download e os exclui do XFS destino |
| `R` | exporta todo o XFS baixado para a raiz de destino |
| `E` | exporta e executa o resultado com `ShellExecuteA("open")` |
| outro | apaga o destino e substitui pelo basename baixado via `MoveFileA` |

As duas builds analisadas não contêm `file.php` nem `launcherlogin`. Portanto, o consumo do login
web e um endpoint `file.php` não são fatos desta build. A decompilação reproduzível usa
`tools/ghidra/DecompileNyxAutoFetch.py`; a extração do INI usa
`tools/extract_nyx_config.py`.

## Login moderno implementado

O LauncherWeb e o launcher distribuído habilitam autenticação por ticket por padrão. Essa
configuração deve permanecer alinhada: desabilitar `Auth:Enabled` no servidor enquanto
`ticketAuthEnabled` estiver ativo no launcher faz o endpoint de login responder `404` e o cliente
exibir uma recusa genérica. `Auth__Enabled=false` continua disponível somente para rollout legado
coordenado com um launcher que também desabilite tickets.

`POST /api/v1/auth/ticket` recebe JSON com `user`, `password`, `appId` e `buildVersion`. Em sucesso,
retorna `{ "ticket": "...", "expiresAt": "..." }`.

- o ticket possui 15 bytes CSPRNG codificados em Base64URL, exatamente 20 caracteres para caber no
  campo de credencial do protocolo v258;
- o banco armazena somente SHA-256 do ticket, conta, app/build, criação, expiração e consumo;
- validade configurável entre 15 e 300 segundos, padrão 60;
- limite de 5 tentativas por minuto por IP;
- ticket vinculado à conta, uso único e consumo atômico;
- o World pode exigir o par app/build gravado no ticket e recusa versão divergente sem consumi-lo;
- o cliente envia o ticket diretamente em `IScavengerWorldNet::SendLogin`; o World o consome
  atomicamente antes de promover a sessão;
- credencial inválida não promove mais a sessão do World;
- uma segunda sessão da mesma conta é recusada pelo World mesmo que o launcher seja contornado;
- o World publica somente hashes SHA-256 normalizados das contas ativas em um snapshot local;
- depois de validar a senha, o LauncherWeb responde `409 account_in_use` quando a conta já está
  conectada; requisição inválida usa `400 invalid_request` e credenciais incorretas usam
  `401 invalid_credentials`;
- quando ticket está habilitado no launcher, falha da API interrompe o launch, sem downgrade
  silencioso para senha reutilizável;
- senha e ticket nunca são escritos nos logs.

O banco original ainda mantém senha em texto. Migrar a tabela legada para hash forte exige uma
migração separada e coordenada com todos os consumidores; o ticket remove a senha da linha de
comando e do protocolo World, mas não corrige sozinho o armazenamento histórico.

Schema provisionado automaticamente por LauncherWeb/World:

```sql
CREATE TABLE launcher_ticket (
  token_hash BINARY(32) PRIMARY KEY,
  account_id VARCHAR(16) NOT NULL,
  app_id INT NULL,
  build_version INT NULL,
  expires_at DATETIME(6) NOT NULL,
  used_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL,
  INDEX ix_launcher_ticket_account(account_id, expires_at)
) ENGINE=InnoDB;
```

## Atualização moderna implementada

Endpoints:

- `GET /api/v1/server-status`: estado público do World e jogadores conectados;
- `GET /api/v1/updates/{appId}?version=N`: envelope JSON assinado ou `204`;
- `GET /api/v1/update-files/{appId}/{version}/{path}`: conteúdo da release.

O manifesto canônico contém `schema`, `appId`, `version`, `publishedAt` e entries com `path`,
`operation` (`Replace`/`Delete`), `size`, `sha256` e `url`. O servidor assina bytes JSON em ordem
fixa com ECDSA P-256/SHA-256; a chave privada fica fora do repositório e somente a pública é
distribuída com o launcher.

O servidor publica apenas diretórios `ContentRoot/AppId/Version` que contêm `_ready`. O launcher:

1. valida assinatura, app, versão, limites e todos os paths;
2. aceita downloads relativos e de mesma origem;
3. baixa para `.update/staging`, conferindo tamanho e SHA-256 durante o stream;
4. move destinos antigos para backup e aplica Replace/Delete sob lock;
5. grava `.update/version` dentro da transação reversível;
6. faz rollback em ordem inversa se qualquer alvo ou commit falhar.

## Status do servidor no launcher

O World publica a cada dois segundos um snapshot atômico com estado, conexões autenticadas,
capacidade e horário UTC. O LauncherWeb lê esse contrato compartilhado e responde
`GET /api/v1/server-status`; snapshots com mais de seis segundos são tratados como offline, de
modo que encerramentos inesperados não deixam o servidor falsamente online. O launcher consulta o
endpoint ao abrir e a cada dez segundos, exibindo `Online`, `Offline` e `Jogadores: atual/máximo`.

Após autenticar, a resposta do ticket inclui somente os amigos online daquela conta. O launcher
substitui os inputs pela lista e apresenta três ações: trocar de conta, iniciar o jogo e abrir as
opções. Autenticar não inicia o cliente. O ticket permanece apenas em memória até **Iniciar game**
e é renovado se expirar antes desse clique. A relação é atualizada a cada 30 segundos por
`POST /api/v1/friends/online`. Esse endpoint exige usuário e senha válidos, é limitado por IP e
cruza `buddy_relation` com o snapshot de contas online; ele não publica a lista global de jogadores.
Em acesso remoto, a configuração do LauncherWeb exige HTTPS para proteger as credenciais.

World e LauncherWeb devem receber o mesmo caminho absoluto em
`RAKION_SERVER_STATUS_PATH` quando executados por contas diferentes. Se ambos forem iniciados pelo
`start-stack.ps1` sob o mesmo usuário, o caminho temporário padrão já é compartilhado. Para
instalações em máquinas distintas, esse snapshot local precisa ser substituído por um transporte
compartilhado; não exponha arquivos de rede graváveis publicamente.

Paths absolutos, `..`, backslash, `:`, NUL, reparse points e a raiz reservada `.update` são
recusados. O updater também recusa substituir o próprio launcher em execução; self-update requer
um bootstrapper externo e não faz parte deste contrato.

No fluxo de PLAY, o launcher instala primeiro o baseline embutido de `version.dll` e
`RakionClientPatch.dll`; depois, a release assinada pode substituir essas duas DLLs. Antes de abrir
o jogo, o launcher confirma que ambas continuam presentes. Assim o baseline garante a primeira
instalação sem impedir atualizações posteriores dos patches.

## Como ativar

### 1. Gerar as chaves

```powershell
.\tools\new_update_key.ps1 `
  -PrivateKeyPath "$env:RAKION_SECRETS\update-private.pem" `
  -PublicKeyPath "$env:RAKION_CLIENT_PACKAGE\update-public.pem"
```

Nunca copie a chave privada para o cliente ou para o Git.

### 2. Configurar o LauncherWeb

Para máquina local, HTTP loopback é permitido. Bind externo falha no boot sem HTTPS.

```powershell
$env:LauncherWeb__Url='https://launcher.exemplo.com:443'
$env:ConnectionStrings__Rakion='Server=127.0.0.1;Database=rakion;Uid=launcher_app;Pwd=troque;'
$env:Auth__Enabled='true'
$env:Auth__EnsureSchema='false'
$env:Auth__TicketLifetimeSeconds='900'
$env:Updates__Enabled='true'
$env:Updates__ContentRoot=$env:RAKION_UPDATE_ROOT
$env:Updates__SigningPrivateKeyPath="$env:RAKION_SECRETS\update-private.pem"
$env:Legacy__Enabled='false'
```

No primeiro boot/migração, use `Auth__EnsureSchema=true` com uma conta capaz de criar a tabela.
Depois use `false` e uma conta runtime dedicada com `SELECT` em `user(id,password)` e
`SELECT/INSERT/UPDATE` em `launcher_ticket`; não use `root` em produção.

O ticket é descartável e continua sendo consumido uma única vez pelo World. O valor recomendado de
900 segundos cobre o tempo em que o cliente legado pode permanecer na seleção de servidor antes de
abrir a conexão World. Valores configurados são limitados ao intervalo de 60 a 1800 segundos.

### 3. Publicar uma release

```powershell
.\tools\publish_update.ps1 `
  -SourceDir $env:RAKION_RELEASE_SOURCE `
  -ContentRoot $env:RAKION_UPDATE_ROOT `
  -AppId 11001 -Version 259 `
  -DeleteListPath $env:RAKION_DELETE_LIST
```

O script copia para diretório temporário, recusa reparse/path inseguro, move a release e cria
`_ready` por último.

### 4. Configurar e distribuir o launcher

`launcher.settings.json`:

```json
{
  "updatesEnabled": true,
  "ticketAuthEnabled": true,
  "updateBaseUrl": "https://launcher.exemplo.com/",
  "appId": 11001,
  "baseVersion": 258,
  "publicKeyPemPath": "update-public.pem"
}
```

Distribua `update-public.pem` ao lado do launcher. Para rollout, ative primeiro a API, publique o
launcher compatível e somente então defina no World (o exemplo exige a release `259`):

```ini
[Authentication]
Type=0
AllowPasswordLogin=0

[Client]
RequiredAppId=11001
RequiredBuildVersion=259
```

Com `AllowPasswordLogin=1`, tickets e senha direta funcionam. Com `0`, somente ticket válido é
aceito. `RequiredAppId=0` e `RequiredBuildVersion=0` desativam apenas o gate de versão. O build no
ticket impede lançamento acidental de versões antigas, mas não é atestado inviolável: um cliente
adulterado ainda pode declarar outro número, por isso regras de jogo continuam autoritativas no
servidor. Variáveis equivalentes do launcher:
`RAKION_UPDATE_ENABLED`, `RAKION_TICKET_AUTH_ENABLED`, `RAKION_UPDATE_URL`,
`RAKION_UPDATE_PUBLIC_KEY`, `RAKION_UPDATE_APP_ID` e `RAKION_UPDATE_BASE_VERSION`.

Smoke operacional sem abrir a UI:

```powershell
dotnet RakionLauncher.dll --update-only $env:RAKION_DIR
```

## Rollback

- update de conteúdo: corrija a release e publique uma versão maior; downgrade não é aceito;
- falha durante aplicação: o launcher restaura os backups automaticamente;
- indisponibilidade temporária do auth: durante rollout, volte `AllowPasswordLogin=1` e
  `ticketAuthEnabled=false`; não habilite downgrade silencioso no código;
- chave comprometida: gere novo par, distribua a pública em um launcher confiável e só então troque
  a privada do serviço.

## Evidência de validação

- suíte .NET do servidor, incluindo status, formato, migração e vínculo app/build do ticket;
- 23 testes do launcher, incluindo amigos online, contas múltiplas, assinatura, hash, traversal,
  rollback, DLLs e auth sem downgrade;
- smoke MariaDB real: conta/build vinculados, uso único, replay recusado e expiração recusada;
- smoke HTTP real: endpoint em loopback emitiu ticket de 20 caracteres e persistiu app `11001`,
  build `259` e somente o hash de 32 bytes;
- smoke de rede do updater: manifesto/arquivo via LauncherWeb, replace/delete e versão `259` sem
  resíduos de staging/backup;
- smoke repetido em 18/07/2026 após a separação das DLLs: update HTTP assinado `258 -> 259`
  substituiu `Bin/version.dll` e `Bin/RakionClientPatch.dll`, conferiu os dois SHA-256 e terminou
  sem staging/backup da release.

## Pendências delimitadas

- captura dinâmica descartável do Nyx executando `M/D/R/E`;
- validação visual do launcher + cliente v258 em uma sessão completa;
- bootstrapper externo para self-update do launcher;
- migração das senhas legadas para hash forte.
