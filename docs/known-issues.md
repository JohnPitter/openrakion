# Known issues — pós-merge (messenger F9) + 2 clientes — 2026-06-25

Reportados pelo usuário rodando **2 clientes na mesma máquina** contra o stack .NET (pós-merge do
messenger F9 + launcher register/login). ANOTADOS p/ investigar depois — o foco atual é a captura do combate.

## 1. Team change não troca o time
Clicar "Team change" na sala não muda o jogador de time. (Nota: durante o DIAG2 do botão Add Bot o
Team change CONGELAVA, mas era o cave de diagnóstico — já revertido. Agora é "não troca", sem travar.)
A confirmar: o `0x137` chega no servidor? É server-side (o handler de team-change) ou client?

## 2. Salas não aparecem na game list (2º cliente)
Cliente A cria uma sala ("Game room 0"); o cliente B vê a **game list VAZIA**. Provável raiz: o servidor
resolve ambos os clientes pelo **MESMO IP** e assume 1 cliente/IP (ver [[2-clientes-mesma-maquina]]) → o
broadcast da sala/lista não chega ao 2º. RE: o handler de game-list no `ClientSession.LobbyFlow` + a
resolução por IP (`GetSessionByIp`).

## 3. World chat não funciona
Digitar no chat do **LOBBY/game-list** não mostra nada. (Distinto do chat da SALA `0x47`, que FUNCIONA —
o /addbot e o botão provam.) Server-side: o handler/broadcast do world-chat. RE: achar o opcode do world
chat + por que não ecoa.

## 4. Adicionar amigo (Arrocha) no messenger não faz nada
F9 → adicionar "Arrocha" (o 2º cliente) → nada acontece. Provável: identidade multi-cliente no Buddy
(2 no mesmo IP) — ver [[messenger-buddy-protocol]] (já trata 2-no-mesmo-IP, mas pode ter regredido no
merge ou o add 0x19 recíproco não fechou). RE: `BuddyServer` (porta 8500) + `messenger_session` por IP.

## Hipótese comum (2, 3, 4)
Os três cheiram à MESMA raiz: o servidor .NET resolve o jogador por **IP** e assume **1 cliente/IP**
(ver [[2-clientes-mesma-maquina]]). Com 2 clientes no mesmo IP, o 2º não é distinguido → não recebe
broadcasts (salas, world chat) nem é endereçável (messenger). **Resolver por IP:porta** (em vez de só IP)
provavelmente destrava os 3 de uma vez. O #1 (team change) pode ser separado (acontece dentro de 1 cliente).
