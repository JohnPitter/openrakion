# DLL de compatibilidade do cliente v258

## Objetivo

`RakionClientCompat` é a única camada nativa de compatibilidade do cliente. O bootstrap x86
`version.dll` é carregado pelo próprio import do `rakion.exe`, antes do entry point, encaminha as
17 exportações oficiais para a `version.dll` do diretório do sistema e carrega
`RakionClientPatch.dll`. A segunda DLL concentra todos os patches, sem alterar o executável em
disco; `version.dll` não contém regra de compatibilidade do jogo.

O diretório informado como `<cliente-v258-golden>` é o **golden source**. Um cliente original serve
somente como destino da sobreposição e ambiente de smoke depois de receber a build v258; seus
binários antigos não geram endereços nem bytes para a DLL.

## Baselines aceitas

| Artefato | SHA-256 | Papel |
|---|---|---|
| `rakion-tutorial/client/Bin/rakion.bin` | `0682B6464DA64C2A79B4B1BF648594273DD7A3BF2AB8EA005A59920B10899D2F` | executável v258 pristine confirmado por diff |
| `rakion-tutorial/client/Bin/rakion.exe` | `0A2A481E3D0F63AD73EFD33F6C1ADD05ED2E92A865A1DB1F4404CCD0DD4C2883` | pristine com somente 18 bytes de retirada do GameGuard |
| `rakion-final/Bin/rakion.exe.orig` | `88E177F243FA4C43769CD323FB4D73E106AE833070F9BCE7B2DC05B8DDFD6AF8` | baseline histórico de instalação; não é pristine |
| `rakion-final/Bin/rakion.exe` | `435F50E3FF9F3F140D4C335336B4BA4A758DF823C146210CC8DA90460960FFFF` | resultado golden com todos os patches históricos |
| `Rakion-Original/Bin/rakion.bin` | `3D2E2A837C827865B12C6687D59BA3F31F6875AA2BC5EA9F37A3FC488DFEDB2D` | exemplo antigo, incompatível com os RVAs v258 |

O diff golden contra o baseline histórico contém 317 bytes. A DLL valida **todos** os bytes antes de
aplicar o conjunto; build desconhecida é rejeitada sem patch parcial. Uma auditoria byte a byte
mostrou que o baseline `88E177...` já difere em 110 bytes do pristine `0682B6...`: 18 pertencem à
retirada do GameGuard e 92 são alterações adicionais, incluindo a remoção do lifecycle de destruição
da tela de personagens. O pristine do tutorial é usado somente como evidência para restaurar esse
fluxo; o `rakion-final` permanece o golden source distribuído.

O binário legado `client/RakionClientPatch/build/RakionClientPatch.dll`, recuperado do trabalho
guardado, não substitui o golden. Ele não contém os hooks de rede/bot, mas sua tabela compilada
confirma as mesmas **317 tuplas `{RVA, byte novo, byte original}`** do manifesto atual. O artefato
final com o mesmo nome é sempre recompilado dos fontes de `RakionClientCompat`. A comparação do
legado é reproduzível sem carregar a DLL:

```powershell
python client\RakionClientCompat\verify_legacy_client_patch.py `
  client\RakionClientPatch\build\RakionClientPatch.dll `
  client\RakionClientCompat\baked_patches.h
```

## Responsabilidades

- reproduzir em memória os 317 bytes do `rakion-final`, incluindo retirada do GameGuard e code
  caves já presentes no golden;
- neutralizar em memória a URL residual do GameGuard `http://218.145.66.176:10200`, evitando a
  espera de SYN mesmo após o fluxo legado já ter sido neutralizado;
- aplicar multi-instância sempre e, conforme `display.mode`, janela e bloqueio do reset de display;
- neutralizar o bloqueio de Alt+Tab em `keyhook.dll`;
- restaurar as cinco chamadas originais de fechamento da tela de personagens e o flag de preview;
  o destrutor de `csComponent` recebe antes um unlink seguro no `uitoolkit.dll`, evitando ponteiros
  de siblings pendurados e o acesso inválido observado em `engine.dll`;
- abrir a página de créditos configurada em `cash-shop.url` quando a compra de Power User retorna
  status `3` (Cash insuficiente), sem alterar saldo ou regra econômica no cliente;
- adicionar o botão nativo `Buy Cash` ao lado de `Potion slot` (command `0x18B`); o hook registra
  todas as instâncias criadas pela tela, consome somente o clique desses controles e abre a mesma
  `cash-shop.url`;
- manter HIT/SHOT, lifecycle e ground-snap visual dos bots;
- ler o IPv4 de `server.host` e redirecionar somente `40706`, `40708` e `40709`, em TCP/UDP;
- espelhar movimento e ataque P2P humano ao World pelo envelope `0xB07A`, sem reenviar esse pacote
  aos peers. Movimento e ataque são capturados na entrada de `CNet::SendToOtherClient`, antes do
  loop que ignora seats sem endpoint; portanto humano + bot funciona mesmo sem um segundo peer
  real. O World pode então calcular proximidade, HP e morte do bot.

Resolução, mouse, som e gamma continuam em `Scripts/PersistentSymbols.ini`; `display.mode` continua
selecionando `windowed`, `borderless` ou `fullscreen`. São preferências do jogador, não patches.
O refresh do Messenger também não pertence à DLL. O World fixa o primeiro personagem por slot como
identidade inicial no char-select, e o Buddy server envia `RET_SET_NICK` antes de cada `RET_LOGIN` e
repete esse par quando a identidade persistida muda.

## Build

Pré-requisitos: Visual Studio Build Tools com C++ x86, SDK .NET 9 e Python 3 disponíveis no
ambiente de build.

```powershell
cd client\RakionClientCompat
.\build.ps1 `
  -PatchedExe "$env:RAKION_GOLDEN_ROOT\Bin\rakion.exe" `
  -OriginalExe "$env:RAKION_GOLDEN_ROOT\Bin\rakion.exe.orig"
```

Os parâmetros regeneram `baked_patches.h`. Sem eles, o build usa o manifesto versionado. O script
valida também o hash e o prólogo de `CNet::SendToOtherClient` na `engine.dll`, compila x86 com
`/W4 /WX` e executa `proxy_smoke.exe` para conferir exports e forwarding.

O linker usa `/Brepro`: duas compilações consecutivas com o mesmo toolchain e as mesmas entradas
devem produzir o mesmo SHA-256. A build estável validada em 20/07/2026 gerou
`13C1D0CC022D0000FA2E7ED03ABD0107AD41D894E0AF302D74CF3D42B0F33263` para `version.dll` e
`751CA95AE0DA5C9C54006BC2A99AE2C83DF85B4B379D46E68ECAA66D68EC9689` para
`RakionClientPatch.dll` nas duas execuções. O build de
`client/RakionLauncher` chama esse script, embute `version.dll` e `RakionClientPatch.dll` e instala
as duas em `Bin` antes de iniciar o jogo.

## Instalação e ativação

1. Atualize/copie para o diretório de jogo a build v258 golden (`rakion.exe`, `engine.dll`,
   `DataSetup.xfs` e `Data/SeriousSam.gms`). O pacote original de 2007 sozinho não é compatível.
2. Coloque **todos** os arquivos da publicação do launcher na raiz do cliente; não copie somente o
   executável, salvo quando ele tiver sido publicado explicitamente como single-file self-contained.
3. Crie `server.host` na raiz com um IPv4, por exemplo `203.0.113.10`.
4. Crie `cash-shop.url` na raiz com a URL HTTP(S) da loja, por exemplo
   `https://jogo.exemplo/cash`. O deploy de validação gera `http://<ServerHost>/cash` por padrão.
5. Escolha o modo no launcher; ele grava `display.mode` e `PersistentSymbols.ini`.
6. Clique em START GAME. O launcher instala o proxy em `Bin`; a carga é automática pelo Windows.

Na tela Shop/Box, `Buy Cash` deve aparecer imediatamente à direita de `Potion slot`. O clique abre a
URL configurada no navegador padrão. Essa navegação não concede saldo: crédito continua sendo uma
operação exclusiva do backend. Cliques concorrentes são consolidados e o botão é liberado novamente
após dois segundos, permitindo reabrir a página sem criar vários workers simultâneos no cliente.

O nosso launcher é o método oficial porque instala os artefatos e grava as configurações. Depois
de `version.dll`, `RakionClientPatch.dll`, `server.host`, `cash-shop.url` e a build v258 estarem no lugar, iniciar
`Bin/rakion.exe` diretamente também carrega a DLL. O launcher antigo não injeta a DLL e só é
compatível se não restaurar arquivos, iniciar o GameGuard ou sobrescrever a configuração.

Portanto, para um cliente original arbitrário, compilar apenas o launcher e as duas DLLs e
sobrescrevê-los **não é suficiente**. O pacote administrativo deve partir do baseline v258 acima e
incluir os quatro artefatos golden, a publicação completa do launcher e os arquivos
`server.host`, `cash-shop.url`, `display.mode` e `launcher.settings.json`. O script abaixo é a fonte
reproduzível desse pacote e impede combinações de versões incompatíveis.

### Gerar o pacote para distribuição

Execute a partir da raiz do repositório. A saída padrão é
`artifacts/client-v258-overlay`, já com `Bin`, `Data`, launcher e configurações nas posições
corretas:

```powershell
$env:RAKION_GOLDEN_ROOT = '<caminho-do-cliente-v258-golden>'

.\client\build_client_package.ps1 `
  -GoldenRoot $env:RAKION_GOLDEN_ROOT `
  -ServerHost '203.0.113.10' `
  -LauncherBaseUrl 'https://launcher.exemplo.com/' `
  -CashStoreUrl 'https://launcher.exemplo.com/cash'
```

O launcher é publicado como single-file self-contained, portanto o jogador não precisa instalar o
.NET. O administrador distribui a pasta gerada e o usuário copia **todo o conteúdo** dela para a
raiz do cliente original compatível, preservando as subpastas. O arquivo `client-package.json`
registra os hashes de todos os artefatos; `verify-package.ps1` confere a pasta antes ou depois da
cópia. Para ativar update assinado, acrescente
`-EnableUpdates -PublicKeyPath '<chave-publica.pem>'`; para autenticação por ticket, acrescente
`-EnableTicketAuth`.

Não renomeie `rakion.bin` antigo para `rakion.exe`: além do tamanho diferente, sua `engine.dll`
também é incompatível. A atualização para v258 deve acontecer antes da ativação da DLL.

### Instalação reproduzível no cliente usado como exemplo

O script instala somente a sobreposição necessária, cria backup dos destinos substituídos e não
inicia o jogo. Antes da primeira escrita, verifica todas as origens e tenta abrir com exclusividade
cada destino que realmente mudará; arquivo idêntico é apenas registrado. Se uma falha ocorrer após
o preflight, os arquivos já tocados são restaurados ou removidos conforme existiam antes. Ele
preserva o `Bin/rakion.bin` antigo e copia `rakion.exe.orig` como
`Bin/rakion.exe`; assim o smoke realmente parte do executável pristine e os 317 bytes só podem vir
da DLL em memória.

```powershell
cd client\RakionClientCompat
.\deploy_validation.ps1 `
  -TargetRoot "$env:RAKION_TEST_CLIENT" `
  -GoldenRoot "$env:RAKION_GOLDEN_ROOT" `
  -ServerHost "127.0.0.1" `
  -LauncherBaseUrl "http://127.0.0.1/" `
  -DisplayMode windowed

python .\verify_validation_install.py `
  "$env:RAKION_TEST_CLIENT"
```

O verificador confere todos os hashes de `validation-install.json`, baseline pristine, golden da
engine, import de `version.dll` pelo executável e as 17 exportações do proxy. Em 18/07/2026, essa
instalação estática passou com 14 arquivos íntegros; nenhum processo do jogo foi iniciado.
Uma instalação existente só é atualizada com `-Refresh`; o manifesto mantém
`originalBackupRoot`, impedindo que um refresh esconda o backup do cliente histórico.

## Validação

- `build.ps1`: proxy x86 e smoke das 17 exportações;
- testes World: codec `0xB07A`, autenticação pelo endpoint UDP e E2E de ataque humano, reação e
  morte do bot;
- testes do launcher: instalação/configuração sem gravar patches remotamente no processo;
- smoke visual manual no pacote de exemplo já atualizado: login pelo IP de `server.host`, janela,
  Alt+Tab, Add Bot, primeiro hit reduzindo HP, reação e morte.

O smoke gráfico é o único gate que não deve ser inferido do teste headless.

## Rollback

Para voltar ao cliente anteriormente patcheado, feche o jogo, desative a instalação automática no
launcher usado para rollback, remova `Bin/version.dll` e `Bin/RakionClientPatch.dll` e restaure o
`rakion-final/Bin/rakion.exe` golden. Não combine o executável já patcheado com uma DLL de outra
versão; embora a aplicação seja idempotente na build correta, misturar builds invalida o gate.

Na instalação de exemplo, restaure os arquivos da pasta indicada por `originalBackupRoot` em
`validation-install.json`. Arquivos que não existiam antes (`Bin/rakion.exe`, proxy, launcher e
manifesto) devem ser removidos somente após conferir o backup. O script nunca apaga nem altera o
`Bin/rakion.bin` histórico.
