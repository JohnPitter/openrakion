# RakionLauncher

Launcher .NET (WinForms, `net9.0-windows`, x64) do cliente Rakion offline — substitui o
**NyxLauncher/load.bin** original. Faz login, game options (tela/mouse/som) e PLAY lançando o
`rakion.exe` direto. Visual no estilo do Softnyx Game Launcher, só com o Rakion.

## Modo janela (a peça-chave)

A Serious Engine só guarda um bit de fullscreen (`m_bActiveFullScreen`); "modo janela" de verdade são
três coisas (ver [`WindowMode.cs`](WindowMode.cs)):

1. **Patch de 1 byte pela `RakionClientPatch.dll`** no `rakion.exe` (sem ASLR, ImageBase `0x400000`): em `0x40D46D` há
   `85C0`(TEST EAX,EAX) `7452`(JZ) `FF15…`(CALL = setup de fullscreen + troca de resolução). Trocar o
   `0x74` (JZ) por `0xEB` (JMP) força o salto e **pula** o CALL → o engine roda windowed sem trocar a
   resolução do desktop. Aplicado pelo proxy antes do entry point e antes de o engine inicializar o display. É o mesmo patch
   que o "Window Mode" do NyxLauncher fazia (descoberto por RE do `load.bin`/`RakionLauncher.Loader`).
2. **Reformatar a janela** "Rakion" via Win32 (título + centralizar/preencher), re-achando a janela a
   cada recriação do engine (troca de cena login → char select → sala) e **tirando o título enquanto
   minimizada** — senão a engine encolhe o backbuffer pela borda a cada restore (faixa preta crescente).
3. **Destravar o Alt+Tab** com um patch da DLL em `keyhook.dll`.

O launcher grava opções e enquadra a janela, mas não escreve mais bytes no processo do jogo. Consulte
[`client-compatibility-dll.md`](../../docs/guides/client-compatibility-dll.md).

## Build / publish

```sh
dotnet build -c Release
# publish self-contained single-file (o jogador não precisa instalar o .NET):
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

O projeto ativa `EnableCompressionInSingleFile`, reduzindo o executável self-contained sem exigir
que o jogador instale o .NET Desktop Runtime. Não habilite trimming no WinForms: além dos warnings
da plataforma, ele pode remover caminhos acessados em runtime.

O launcher roda como usuário normal. Para o `rakion.exe` legado, ele cria um bloco de ambiente
dedicado com `__COMPAT_LAYER=RunAsInvoker`; assim preserva a linha de comando especial
`argv[0]=user` e evita o prompt UAC sem alterar o executável pristine.

## Bandeja do sistema

O botão `X` oculta o launcher e o mantém na bandeja do sistema. Um clique no ícone ou a opção
**Abrir launcher** restaura a janela. O processo só é encerrado pela opção **Fechar** do menu da
bandeja; isso mantém o status dos clientes e as verificações do servidor ativos.

## Login por ticket e update assinado

O launcher pode trocar a senha por um ticket aleatório de 20 caracteres antes de iniciar o jogo e
aplicar releases assinadas com ECDSA P-256/SHA-256. Em servidor remoto, a URL precisa ser HTTPS.

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

Copie a chave pública para junto do launcher. Se a emissão do ticket falhar, o jogo não é aberto e
não há fallback silencioso para a senha. A UI distingue login inválido, credenciais inválidas e
conta já conectada antes de abrir o cliente. O launcher inclui `appId` e a versão persistida em
`.update/version` no ticket; o World pode exigir esse par com `RequiredAppId` e
`RequiredBuildVersion`. O fluxo visual separa autenticação e execução: inicialmente aparecem os
inputs e **Login**; após autenticar, eles são substituídos pela lista de amigos online e pelos botões
**Outra conta**, **Iniciar game** e **Game options**. A presença é atualizada a cada 30 segundos sem
expor a lista global de usuários. Ao autenticar duas ou mais contas na mesma execução, aparece um
seletor de conta acima da lista de amigos; a troca altera a conta usada no próximo **Iniciar game**.
As credenciais permanecem somente na memória e são descartadas ao fechar o launcher. O cabeçalho
identifica explicitamente a conta autenticada para
evitar confusão entre launchers simultâneos. O ticket fica somente em memória e é renovado se expirar antes do
clique em **Iniciar game**. Para testar somente o updater:

```powershell
dotnet RakionLauncher.dll --update-only C:\Rakion
```

O passo a passo de servidor, publicação, rollout e rollback está em
[`docs/protocol/launcher-auth-update.md`](../../docs/protocol/launcher-auth-update.md).

O arquivo versionado `launcher.settings.json` mantém updates desabilitados por segurança e a
autenticação por ticket habilitada. O recurso de update
só fica ativo depois de configurar a URL, distribuir `update-public.pem` e definir
`updatesEnabled: true`. Ele atualiza o conteúdo do cliente, inclusive `version.dll` e
`RakionClientPatch.dll`; atualizar o próprio launcher em execução exige um bootstrapper externo.

## Cliente controlado para bot

O modo experimental `--puppet` inicia uma conta como cliente real, usando a mesma autenticação,
compatibilidade nativa e configuração de tela do fluxo visual. A senha não entra na linha de
comando nem é herdada pelo `rakion.exe`:

```powershell
$env:RAKION_PUPPET_PASSWORD = 'senha-da-conta-de-bot'
.\RakionLauncher.exe --puppet bot_01
Remove-Item Env:\RAKION_PUPPET_PASSWORD
```

O argumento opcional depois do usuário substitui o `serverId`. Este modo prepara o processo real
que será dirigido pela IA; seleção de personagem, entrada automática na sala e controle de ações
ainda dependem do driver e não devem ser considerados prontos apenas porque o cliente abriu.

## Assets

Os assets de UI (`Assets/*.ico|png|bmp`) são de domínio público e ficam **versionados** — ver
[`Assets/README.md`](Assets/README.md). O launcher **compila e roda sem eles** (a UI degrada sem
ícone/banner); o `.csproj` os embute por glob quando presentes.
