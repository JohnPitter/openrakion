# Validação visual do cliente v258

## Objetivo

Este é o registro canônico dos testes que dependem do cliente gráfico real. Ele complementa a
[`dynamic-validation.md`](dynamic-validation.md): testes headless provam protocolo, persistência e
regras; somente observação da janela do jogo prova render, animação, HUD, colisão e interação.

Um processo vivo, ausência de disconnect ou linha de log não vale como aprovação visual. Cada item
deve registrar a build, o resultado observado e uma captura ou vídeo quando a aparência for parte
do contrato.

## Baseline instalada

| Item | Evidência atual |
|---|---|
| Cliente de validação | `C:\Users\joaop\Downloads\Rakion-Original\Rakion` |
| Executável pristine v258 | `Bin/rakion.exe`, SHA-256 `88E177F243FA4C43769CD323FB4D73E106AE833070F9BCE7B2DC05B8DDFD6AF8` |
| Engine golden | SHA-256 `83B20D6C32CD66B95C8F8E41AD6DE13A58E8F5F948CD21CBD118D42EF8CF88F2` |
| Proxy determinístico | `Bin/version.dll`, SHA-256 `13C1D0CC022D0000FA2E7ED03ABD0107AD41D894E0AF302D74CF3D42B0F33263` |
| Forwarder oficial | carregado de `%SystemRoot%\SysWOW64\version.dll`; não é distribuído |
| Patches golden | `Bin/RakionClientPatch.dll`, SHA-256 `6455F14820FD74AE1CAC65E490B6F8B3C186CE9D0149F8064B75FD07D439857D` |
| Destino | `server.host=127.0.0.1`, `display.mode=windowed` |
| Verificação estática | `validation-install.json` atualizado e 14/14 arquivos aprovados em 18/07/2026 |

O `validation-install.json` é a fonte reproduzível desses hashes. O `rakion-final` continua sendo o
golden source; este diretório é somente o ambiente de execução.

## Estado da rodada de 18/07/2026

| Gate | Estado | Evidência |
|---|---|---|
| MariaDB | aprovado | container `rakion-db` disponível; 28 E2E obrigatórios passaram |
| Broker | aprovado | TCP/UDP `40706` ouvindo |
| World | aprovado | TCP `40708` e UDP `40708/40709` ouvindo |
| Buddy | aprovado | TCP/UDP `8500/8504` ouvindo |
| Build do cliente/DLL | aprovado | solução 0 warnings/erros; launcher 14/14; DLLs x86 `/W4 /WX`, 317 patches, 17 exports e duas builds com hashes idênticos |
| Instalação do cliente | aprovado | refresh transacional concluído; 14/14 arquivos íntegros, `verorig.dll` removida e `originalBackupRoot` preservado |
| Auto-update de conteúdo | aprovado | smoke HTTP assinado `258→259` atualizou `version.dll` e `RakionClientPatch.dll`, sem resíduos |
| Abertura sem elevação | aprovado | launcher `asInvoker`; processo legado recebeu `RunAsInvoker`; nenhum processo `consent.exe` foi aberto |
| Login e render inicial | aprovado | cliente pristine exibiu servidor e `GoHeroi` lvl 40; login `test`, TCP World `40708`, Buddy `8500`, `SuccessUDP`, canal e lista de salas confirmados |
| Messenger F9 | parcial | backend completo passou em E2E headless; no cliente gráfico o painel abriu, callback deixou `modelCount=1`, mas a lista permaneceu visualmente vazia |
| Add Bot | aprovado | botão enviou `0x47` `GoHeroi : /addbot`; `Rok` apareceu no time azul da sala e dentro do stage Mammoth |
| Ataque humano→bot | aprovado no transporte e lifecycle | DLL registrou o primeiro ataque; World reduziu `500→0`; cliente aplicou `alive seq=1` e `dead seq=2` |
| HUD/animação final do bot | pendente | captura ainda mostrou `0 Kills`; reação de dano, queda visível próxima, respawn e placar exigem novo smoke |

Na captura de 18/07/2026, o primeiro uso da porta UDP exibiu o diálogo do Windows Firewall. Ele
não é UAC nem falha do launcher; permitir acesso é necessário para P2P fora de localhost. A janela
ao fundo confirmou o char-select. O World registrou também a sequência real `0x0E`, entrada no
canal, inventário e `0x36 FieldList` sem disconnect.

## Smoke 1 — launcher, login e lobby

Use `test` / `test` no launcher atual. O teste só passa quando todos os itens forem observados:

- launcher abre sem Nyx/GameGuard;
- `START GAME` inicia `Bin/rakion.exe` pristine e a DLL aceita a build;
- janela aparece em modo windowed, sem trocar a resolução do desktop;
- Alt+Tab, minimizar e restaurar não corrompem o backbuffer;
- login chega ao char-select sem mensagem de versão, senha ou servidor inválido;
- personagem `GoHeroi` aparece com classe, nível, equipamento e ranks coerentes;
- seleção entra no canal e lista salas sem loop ou disconnect;
- saída fecha o cliente sem deixar processo órfão.

Registrar ao final: horário, hash do manifesto, resultado de cada linha, screenshot do char-select e
do canal, e trechos correspondentes dos dois logs temporários.

## Smoke 2 — dois clientes e social

Parcial executado em 18/07/2026. Duas instâncias pristine chegaram juntas ao lobby; F9 abriu nos
dois lados e os traces confirmaram login, relação, presença, registro interno de `0x116` bytes e
`modelCount=1` depois do callback do `rakion.exe`. Separadamente, o E2E headless aprovou presença e
SMS completo com ACK. A lista não foi renderizada, então o gate gráfico continua parcial até o
painel exibir a relação e uma mensagem ser observada pela interface.

- abrir `test` e `test2` simultaneamente pelo mesmo launcher;
- confirmar presença, cor de clã, Buddy, whisper e chat de canal nos dois lados;
- criar sala, entrar com o segundo cliente e validar roster, seats, teams, ready e host;
- testar convite, vote kick, saída e retorno ao canal;
- confirmar que a segunda instância não é bloqueada e que não há janela sobreposta incorretamente.

## Smoke 3 — PvP, P2P e bot

Parcial executado em 18/07/2026 com um cliente pristine: Add Bot, roster, entidade no stage,
ataque humano→bot, HP e lifecycle de morte aprovados. Permanecem abertos os itens abaixo, sobretudo
a observação próxima da animação/queda e o HUD de placar.

- executar Golem, Deathmatch, Team Death e Boss com dois clientes;
- validar movimento, arma, ataque, hit, HP/AP, morte, respawn, placar e encerramento;
- repetir em UDP direto, UDP bloqueado/túnel, LAN e NAT diferente;
- adicionar bot, validar posição no chão, perseguição e reação;
- confirmar visualmente que o humano reduz o HP do bot, mata-o e vê o ciclo dead/alive;
- confirmar que ambos os humanos veem o mesmo estado do bot.

## Smoke 4 — PvE e NPC

- executar os 48 stages, respeitando `minplayers/maxplayers` do catálogo;
- registrar spawn, waves, objetivo, clear, morte, give up, rank e recompensa;
- validar as dez famílias base e as três classes especiais com seus efeitos e projéteis;
- comparar hitbox, trajetória, altitude, colisão, morte e late join entre dois clientes.

## Smoke 5 — economia e progressão

- inventário, storage, equip/unequip, stack e expiração;
- compra/venda Gold e Cash, cupom, bundle e random present;
- enchant preview/commit/relog;
- Gift Box peek/accept/dispose e item após relog;
- Power User, slots, saldos, validade e bônus de EXP;
- ranking no char-select e nas telas que o exibem.

## Resultado e encerramento

O RE estático e a compatibilidade headless estão fechados, mas a validação visual permanece aberta
até os cinco smokes acima possuírem evidência. Comportamentos ausentes no v258 — liquidação de
loteria, checkout, evento Valentine, PC Bang independente, SMTP e replay autoritativo — continuam
classificados como extensão autoral, não como falha do smoke.
