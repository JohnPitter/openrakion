# DLL de compatibilidade do cliente v258

## Objetivo

`RakionClientCompat` é a única camada nativa de compatibilidade do cliente. O bootstrap x86
`version.dll` é carregado pelo próprio import do `rakion.exe`, antes do entry point, encaminha as
17 exportações oficiais para a `version.dll` do diretório do sistema e carrega
`RakionClientPatch.dll`. A segunda DLL concentra todos os patches, sem alterar o executável em
disco; `version.dll` não contém regra de compatibilidade do jogo.

O `rakion-final` é o **golden source**. O pacote
`C:\Users\joaop\Downloads\Rakion-Original\Rakion` serve somente como exemplo de instalação e
ambiente de smoke depois de receber a build v258; seus binários antigos não geram endereços nem
bytes para a DLL.

## Baselines aceitas

| Artefato | SHA-256 | Papel |
|---|---|---|
| `rakion-final/Bin/rakion.exe.orig` | `88E177F243FA4C43769CD323FB4D73E106AE833070F9BCE7B2DC05B8DDFD6AF8` | executável v258 pristine |
| `rakion-final/Bin/rakion.exe` | `435F50E3FF9F3F140D4C335336B4BA4A758DF823C146210CC8DA90460960FFFF` | resultado golden com todos os patches históricos |
| `Rakion-Original/Bin/rakion.bin` | `3D2E2A837C827865B12C6687D59BA3F31F6875AA2BC5EA9F37A3FC488DFEDB2D` | exemplo antigo, incompatível com os RVAs v258 |

O diff golden contém 317 bytes. A DLL valida **todos** os bytes antes de aplicar o conjunto; build
desconhecida é rejeitada sem patch parcial. O `rakion-tutorial` documenta somente a retirada do
GameGuard em outra build e não é fonte de bytes para o cliente final.

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
- manter HIT/SHOT, lifecycle e ground-snap visual dos bots;
- ler o IPv4 de `server.host` e redirecionar somente `40706`, `40708` e `40709`, em TCP/UDP;
- espelhar movimento e ataque P2P humano ao World pelo envelope `0xB07A`, sem reenviar esse pacote
  aos peers. Movimento e ataque são capturados na entrada de `CNet::SendToOtherClient`, antes do
  loop que ignora seats sem endpoint; portanto humano + bot funciona mesmo sem um segundo peer
  real. O World pode então calcular proximidade, HP e morte do bot.

Resolução, mouse, som e gamma continuam em `Scripts/PersistentSymbols.ini`; `display.mode` continua
selecionando `windowed`, `borderless` ou `fullscreen`. São preferências do jogador, não patches.

## Build

Pré-requisito: Visual Studio Build Tools com C++ x86 e o SDK .NET 9 para o launcher.

```powershell
cd client\RakionClientCompat
.\build.ps1 `
  -PatchedExe "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin\rakion.exe" `
  -OriginalExe "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin\rakion.exe.orig"
```

Os parâmetros regeneram `baked_patches.h`. Sem eles, o build usa o manifesto versionado. O script
valida também o hash e o prólogo de `CNet::SendToOtherClient` na `engine.dll`, compila x86 com
`/W4 /WX` e executa `proxy_smoke.exe` para conferir exports e forwarding.

O linker usa `/Brepro`: duas compilações consecutivas com o mesmo toolchain e as mesmas entradas
devem produzir o mesmo SHA-256. A build estável validada em 19/07/2026 gerou
`13C1D0CC022D0000FA2E7ED03ABD0107AD41D894E0AF302D74CF3D42B0F33263` para `version.dll` e
`7711BE128CF4E6134BA53BAE11C0AD8152F70FF526F85E531B67745C5437CFEA` para
`RakionClientPatch.dll` nas duas execuções. O build de
`client/RakionLauncher` chama esse script, embute `version.dll` e `RakionClientPatch.dll` e instala
as duas em `Bin` antes de iniciar o jogo.

## Instalação e ativação

1. Atualize/copie para o diretório de jogo a build v258 golden (`rakion.exe`, `engine.dll` e dados
   correspondentes). O pacote original de 2007 sozinho não é compatível.
2. Coloque o launcher publicado na raiz do cliente.
3. Crie `server.host` na raiz com um IPv4, por exemplo `203.0.113.10`.
4. Escolha o modo no launcher; ele grava `display.mode` e `PersistentSymbols.ini`.
5. Clique em START GAME. O launcher instala o proxy em `Bin`; a carga é automática pelo Windows.

O nosso launcher é o método oficial porque instala os artefatos e grava as configurações. Depois
de `version.dll`, `RakionClientPatch.dll`, `server.host` e a build v258 estarem no lugar, iniciar
`Bin/rakion.exe` diretamente também carrega a DLL. O launcher antigo não injeta a DLL e só é
compatível se não restaurar arquivos, iniciar o GameGuard ou sobrescrever a configuração.

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
  -TargetRoot "C:\Users\joaop\Downloads\Rakion-Original\Rakion" `
  -GoldenRoot "C:\Users\joaop\Desenvolvimento\Rakion\rakion-final" `
  -ServerHost "127.0.0.1" `
  -DisplayMode windowed

python .\verify_validation_install.py `
  "C:\Users\joaop\Downloads\Rakion-Original\Rakion"
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
