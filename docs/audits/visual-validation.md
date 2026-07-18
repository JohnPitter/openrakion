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
| Proxy golden determinístico | `Bin/version.dll`, SHA-256 `2DA526C8DA13A8F499DDCD6E6BA7568D6D41D71E6466550A2DBA825D9857D7FE` |
| Forwarder oficial | `Bin/verorig.dll`, SHA-256 `4F66B88731E84BE8E1545EEFC589A79E701CCA426CE916E4AC9322E60EA9680B` |
| Destino | `server.host=127.0.0.1`, `display.mode=windowed` |
| Verificação estática | baseline anterior aprovada; refresh do manifesto para o proxy determinístico pendente |

O `validation-install.json` é a fonte reproduzível desses hashes. O `rakion-final` continua sendo o
golden source; este diretório é somente o ambiente de execução.

## Estado da rodada de 18/07/2026

| Gate | Estado | Evidência |
|---|---|---|
| MariaDB | aprovado | container `rakion-db` disponível; 28 E2E obrigatórios passaram |
| Broker | aprovado | TCP/UDP `40706` ouvindo |
| World | aprovado | TCP `40708` e UDP `40708/40709` ouvindo |
| Buddy | aprovado | TCP/UDP `8500/8504` ouvindo |
| Build do cliente/DLL | aprovado | solução 0 warnings/erros; launcher 11/11; DLL x86 `/W4 /WX`, 317 patches, 17 exports e duas builds com hash idêntico |
| Instalação do cliente | pendente | o UAC bloqueou `RakionLauncher.exe`; preflight atômico aprovado, mas o refresh final precisa ser repetido após fechar o prompt |
| Elevação e abertura do launcher | pendente | prompt UAC aberto; exige confirmação interativa do usuário |
| Login e render inicial | pendente | nenhuma observação visual registrada nesta rodada |

Logs antigos em `%TEMP%\rakion_launcher.log` e `%TEMP%\rakion_client_compat.log` comprovam que a
DLL já carregou, aplicou patches e recebeu lifecycle em execuções anteriores. Eles não registram o
conteúdo desenhado e, portanto, não fecham nenhum gate visual.

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

- abrir `test` e `test2` simultaneamente pelo mesmo launcher;
- confirmar presença, cor de clã, Buddy, whisper e chat de canal nos dois lados;
- criar sala, entrar com o segundo cliente e validar roster, seats, teams, ready e host;
- testar convite, vote kick, saída e retorno ao canal;
- confirmar que a segunda instância não é bloqueada e que não há janela sobreposta incorretamente.

## Smoke 3 — PvP, P2P e bot

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
