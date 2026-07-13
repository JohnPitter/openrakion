# Diagnóstico do muro do HIT×N — diff de entidade (task #21)

Leitura externa pura (`client/RakionDiag/diag.ps1`, `ReadProcessMemory`) das entidades de cada slot no
cliente do HOST (seat 0 local; seat 10 = humano-peer remoto **que recebe HIT×N**; seat 11 = bot **que não**).
24 snapshots; diff byte-a-byte (`diff_entities.py`) + três-vias (`three_way.py`, local+humano concordam vs bot).

## Resultado — o que a comparação PROVA

1. **O bot É uma CPlayerEntity real, mesmo tipo do humano.** Header inicial (vtable + campos 0x00–0x18)
   IDÊNTICO entre humano e bot. Renderiza como personagem. Não é NPC nem tipo errado.

2. **team/alive do bot estão CORRETOS.** Esses campos são inteiros pequenos (0/1). Se o bot estivesse no
   time errado ou "morto", apareceriam como diff humano-vs-bot. **Não aparecem** → o bot casa com o humano
   em team/alive. O gate clássico "team/alive/HP" **passa** para o bot.

3. **NÃO existe um "campo de gate" único que o bot zere e os players tenham.** O três-vias (local+humano
   concordam ≠ bot) encontrou ~1 candidato, e ele é falso positivo (região desalinhada). As milhares de
   diferenças do two-way são **ponteiros** (endereços de heap, diferem por alocação) e **floats**
   (posição/orientação), não flags.

4. **A partir de ~0x2f0 os layouts DESALINHAM.** A entidade do humano tem um bloco de aparência/equipamento
   (pares `(id, active=1)` de 0x13f4 a 0x2278) que o bot tem **zerado** — o `0x4b` do bot manda um blob
   mínimo (67B), sem o CPlayerCharacter completo. Isso empurra todos os offsets seguintes → o byte-diff
   além do header vira ruído (não é comparável campo-a-campo).

5. **O buffer de posição/orientação em rede (0x2f0–0x318) do bot é ANÔMALO.** No cliente do host, o humano
   tem floats reais ali (posição interpolada); o bot tem uma sequência crescente estranha
   (0x49645F, +0xFFFF por dword) — não é posição válida. É o campo que a stream de gametick CONFIRMADO
   preenche num peer real; o bot, cujo movimento é só `0x30a` unreliable (fora do gametick confirmado),
   nunca o recebe direito.

## Conclusão (redireciona o problema)

O diagnóstico **descarta** a hipótese "falta um campo simples na entidade": team/alive estão certos, o tipo
está certo. O que sobra bate com o **§22.10** (chão arquitetural): o bot **não é peer de gamestream
confiável**, então:
- a posição/estado CONFIRMADO dele (0x2f0+) nunca é preenchido como o de um peer real;
- o cliente não conta HIT×N (que lê o gametick confirmado) contra ele — e, com ele registrado no gametick
  reliable, a agregação daquele tick não fecha → **o HIT×N congela para TODOS** (o sintoma que o usuário vê:
  dano funciona, HIT some quando o bot entra).

**A diferença REAL não é um flag da entidade — é o bot não participar do gametick confirmado.** Cravar isso
custaria hookar a agregação do tick (bloqueado: injeção crasha o anti-tamper). Server-side, o caminho é o bot
alimentar o mesmo canal confirmado que um peer real (a incerteza da fronteira de RE).

## Ferramentas (repo)

`client/RakionDiag/diag.ps1` (leitura externa), `diff_entities.py` (two-way), `three_way.py` (local+humano
vs bot). Dumps em `C:\temp\entdiff\pid<PID>\`.

---

# Fase 2 — encarar o gametick confirmado (task #24)

Como o diff de entidade descartou "campo faltante", o alvo é o **contador de gametick confirmado**: a
sequência que o cliente só avança quando agrega a ação de TODOS os players registrados; o HIT×N é creditado
nesse tick. Com o bot (que não ticka como peer real) registrado, a agregação não fecha → o tick confirmado
trava → HIT congela p/ todos.

**RE estática (engine.dll, base 0x36000000):**
- Cadeia de estado: `pNet=[0x362ba778]`; `A(CSessionState)=[pNet+0x18]` (A+0xc = nº slots, A+0x10 = tabela de
  players stride 0x100); `G(tick object)=[pNet+0x2c]`.
- `SessionStateLoop@0x3610cde8` roda o tick; `0x36103040` (ecx=G) é o EMISSOR da ação local por-tick — monta
  e envia o `0x30f` a cada 100ms (push 0x30f @0x36103106, send via `pNet+0x119c`), gated por is-server
  (`[0x3636f260]->vt[+8]`) + timers (`[0x36215608]`). Estrutura de tick em `G+0x264`.

**Medição ao vivo (o atalho — RE estática do agregador é semanas):** `watch_session.ps1` amostra A e G por
leitura externa e reporta os dwords MONOTÔNICOS (contadores de tick). Rodar 2×:
1. `.\watch_session.ps1 -Tag sem-bot` — 2 humanos batendo (HIT ok) → contadores AVANÇAM.
2. `.\watch_session.ps1 -Tag com-bot` — 2 humanos + bot (HIT congela) → o tick CONFIRMADO TRAVA.
O offset que avança em (1) e trava em (2) = o gametick confirmado, o alvo do fix server-side.
