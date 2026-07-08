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
