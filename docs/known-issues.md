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

---

# Sala/PvP 2 humanos — wire do 0x37 (2026-07-03, FECHADO 2026-07-04)

Comparação-ouro contra `orig_capture2` — agora com golden test do **frame COMPLETO de 216B**
(`RoomJoinWireGoldenTests`, máscara só uid/endereço-NAT) — cravou o wire inteiro; pendente validação
in-game da travada do joiner:

- **Header:** MASTER slot em **+2** (validado in-game: consertou o joiner-vira-master/START).
- **Header +6/+7 = MAP e MODE (bytes), NÃO fieldId u16.** Eu escrevia o f.Id ali → o joiner lia
  map=idLow/mode=idHigh ("map 0 mode 0" numa sala Gravity/PvP) → TRAVAVA segundos após entrar.
  Cravado cruzando o C>S 0x3b com o S>C 0x36/0x37 (a sala da captura ERA Gravity 210).
- **Record do roster = a forma do 0x38 SEM a cauda de equip de 11B** (variável nome\0+74B; "JP"=77B vs
  88B no 0x38). A cauda alongava cada slot e desalinhava o parse (o velho "78B" era isto — mas FIXO por
  buffer crashava; a forma é VARIÁVEL). slotFlag pós-uid = 00 sempre (não o team).
- **Slots trancados + observers:** índice no bloco do time >= MaxPlayers/2 → `05`; fim do frame =
  `05 05 05` (3 observers) + u32 0. Capacidade default 12 (captura Gravity; o 0x3b não a carrega).
- **0x3b:** cauda b3/b4/b5 = frag/minLevel/maxLevel — agora parseada e aplicada ao Field.
  Ver [[room-state-0x37-master-offset]].

## Gap ADICIONAL — PATCHADO server-side (`cab3dfd`, pendente validação)
O handler de **sair da sala** (`ClientSession.LobbyFlow` `case 0x3A`) resetava só a própria sessão: **não
liberava o seat** do membro no field nem avisava os demais → o master mantinha o card de quem saiu (fantasma
pós-saída). **Corrigido:** SÓ na sala pré-partida com outros membros (guard `Settled && Count>1`, que blinda
o fluxo pós-partida solo validado), libera o rec, reatribui o master se o host saiu, e faz broadcast
`[3a 00][seat]` aos restantes (remoção inline, sem acionar o W.O.). NOTA: se o client 2 (achando-se master
pelo bug do header, já corrigido) não mandava o `0x3A`, este fix só surte efeito após o fix do header fazer
ele mandar o opcode certo de leave. Vazamento pré-existente NÃO tratado: último membro saindo de sala
`Settled` (Count==1) — o tick não liquida sala Settled; fora de escopo aqui.
