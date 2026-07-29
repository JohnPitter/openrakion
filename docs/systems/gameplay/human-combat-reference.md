# Referência de combate humano e catálogo de ataques

## Objetivo

Esta referência combina uma captura integral de um round humano contra humano da build v258 com
os comandos documentados do Rakion. O tráfego capturado é o golden source para protocolo, IDs e
cadência; páginas externas servem para nomear famílias de golpe e nunca para inventar bytes.

Use esta separação ao implementar bots:

- **entrada:** teclas, mouse, arma e estado mantidos pelo jogador;
- **apresentação:** animações `0x0311` emitidas em fases;
- **combate:** alcance, orientação, janela ativa, dano e reação;
- **ciclo de vida:** queda, morte, respawn e placar.

Um pacote de animação isolado não prova contato nem autoriza dano.

## Captura de 29/07/2026

Os dois clientes usaram personagens `class=1`, identificados como Archer pelo modelo exibido e
pelos logs de seleção. O round útil foi delimitado automaticamente pelo primeiro request `0x004B`
e pelo primeiro request `0x0050` posterior:

| medida | valor |
|---|---:|
| início relativo | `14.615.640 ms` |
| fim relativo | `14.918.703 ms` |
| duração | `303.063 ms` |
| reportes de morte `0x004F` | `8` |
| clientes | `2` |

A sessão de captura ficou ativa por mais tempo que o round. Estatísticas globais da sessão não
devem ser usadas para calibrar o bot sem aplicar essa janela.

Para reproduzir a análise:

```powershell
python tools/analyze_human_combat_round.py <diretorio-da-captura>
```

O comando gera `round-analysis.json` e `round-analysis.md`. A ferramenta lê `timeline.jsonl` em
streaming e não carrega a captura inteira em memória.

### IDs de ataque observados

Contagem no stream local `0x0311 kind=Attack`:

| ID | cliente A | cliente B |
|---:|---:|---:|
| `27` | 16 | 17 |
| `10` | 24 | 17 |
| `25` | 15 | 9 |
| `26` | 15 | 9 |
| `18` | 15 | 9 |
| `24` | 7 | 6 |
| `12` | 7 | 6 |
| `0` | 3 | 5 |
| `1` | 1 | 4 |
| `8` | 8 | 2 |
| `15` | 3 | 4 |
| `29` | 2 | 1 |
| `28` | 1 | 1 |
| `19` | 1 | 1 |
| `13` | 0 | 1 |

Os IDs não são ataques independentes. Sequências repetidas com intervalo máximo de `900 ms`:

| sequência | cliente A | cliente B | interpretação segura |
|---|---:|---:|---|
| `25 → 24 → 12` | 7 | 6 | ataque multifase completo |
| `27 → 26 → 18` | 2+ | 4+ | combo multifase, repetível |
| `0 → 1` | 1 | 4 | ataque de duas fases |
| `10` | 24 | 17 | ataque de uma fase ou fase autossuficiente |
| `8` | 8 | 2 | ataque de uma fase ou fase autossuficiente |
| `28 → 19` | 1 | 1 | transição rara, ainda sem nome de domínio |

O `+` existe porque cliques consecutivos concatenaram múltiplas ocorrências da sequência dentro da
janela de agrupamento. A ordem interna permaneceu estável.

### Reações de dano observadas

Argumentos de `0x0311 kind=Damage` no mesmo round:

| argumentos | ocorrências | observação |
|---|---:|---|
| `(0,10,1)` | 28 | reação predominante |
| `(0,10,0)` | 3 | mesma família, terminador alternativo |
| `(15,7,1)` | 14 | queda/morte frequente |
| `(15,7,0)` | 2 | variante observada |
| `(8,4,0/1)` | 5 | outra reação |
| `(13,3,1)` | 2 | reação rara |
| `(15,4,0)` | 1 | reação rara |

Isso amplia a captura anterior: o terceiro argumento não é invariavelmente `1`. O sintetizador
deve preservar a variante medida junto ao contexto, em vez de fixá-la globalmente.

## Comandos comuns documentados

A [página oficial de controles da Softnyx](https://rakion.softnyx.com/GameInfo/BeginnersGuide/Control.aspx)
documenta:

- `W/A/S/D`: deslocamento;
- `Shift`: defesa;
- `Space`: salto;
- `W`, `W` e botão esquerdo: corrida com ataque;
- botão direito mantido entre um e três segundos: ataque especial;
- botões esquerdo e direito juntos, perto do alvo: grip; se falhar, ataque invencível;
- troca de arma e Chaos como ações próprias.

O [guia oficial da PlayPark](https://rakionsea.playpark.com/game-guide/game-feature/) confirma as
famílias bash, combo, especial, guard e catch/grip, incluindo levantamento do alvo para continuação.

Portanto, o estado mínimo de um combatente não cabe em `moving/attacking`. Ele precisa distinguir:

1. idle, locomoção, corrida e salto;
2. guard e guard impactado;
3. ataque básico e fase do combo;
4. ataque de corrida e ataque aéreo;
5. carga e liberação do especial;
6. tentativa, acerto e falha do grip;
7. ataque invencível;
8. arma de alcance e projétil;
9. stagger, knockdown, rising e morte;
10. arma atual, Chaos e classe avançada.

## Catálogo por classe

As listas comunitárias abaixo ajudam a nomear o vocabulário. Elas não substituem a captura da build
v258 e não fornecem IDs `0x0311`.

### Swordsman

Segundo o [catálogo do Swordsman](https://rakion.fandom.com/wiki/Swordsman):

- espada: slash simples, duplo e triplo, duas variantes de combo, ataque aéreo, stab e slash de
  avanço, especial carregado de três golpes e ataque invencível;
- defesa: guard, rising, grip frontal e traseiro;
- distância: arremesso de dagger;
- Chaos: golpes de lança, combos, dash e stab;
- classe avançada: lança.

### Blacksmith

Segundo o [catálogo do Blacksmith](https://rakion.fandom.com/wiki/Blacksmith):

- martelo: swing simples, dois combos duplos, dois avanços, ataque aéreo, especial de área carregado
  e giro invencível;
- defesa: guard, rising, grip frontal e traseiro;
- distância: arremesso de machado;
- Chaos: swings, combo triplo, golpe carregado e giro;
- classe avançada: arma de punho.

### Archer

Segundo o [catálogo da Archer](https://rakion.fandom.com/wiki/Archer):

- espada curta: slash simples/duplo, ataque aéreo, stab de avanço, especial carregado de três golpes
  e giro invencível;
- defesa: guard, rising, grip frontal e traseiro;
- distância: disparo de arco;
- Chaos: tiro rápido, cinco flechas, wake e guard;
- classe avançada: besta com três ataques consecutivos.

### Mage

Segundo o [catálogo do Mage](https://rakion.fandom.com/wiki/Mage):

- cajado: hit simples/duplo, ataque aéreo, projection, blessing e blast invencível;
- defesa: guard, rising, curse frontal e power projection traseiro;
- distância: magic bomb carregada e fireball guiada;
- Chaos: projection tripla, fireball dupla, cura e escudo;
- classe avançada: foice.

### Ninja

Segundo o [catálogo da Ninja](https://rakion.fandom.com/wiki/Ninja):

- dagger: stab simples, duplo, dois triplos, dois quádruplos, ataque aéreo, air kick, dash, especial
  carregado e power explosion invencível;
- defesa: guard, rising/explosion, grip frontal e traseiro;
- distância: dart simples e rajada carregada de nove darts;
- Chaos: dashes, explosion, rajadas e phoenix;
- classe avançada: chakram.

A [página oficial de classe avançada](https://rakion.softnyx.com/GameInfo/ETC/ClassAdvancement.aspx)
confirma as armas lança, besta, punhos, foice e chakram, além de movimentos próprios. Esses perfis
precisam de capturas separadas; não é seguro reutilizar IDs da arma base.

## Contrato para o bot

### Estado e início da partida

- Antes de `field.State=2` e `phase=Playing`, publicar apenas idle; não mover nem atacar.
- Ao entrar em `Playing`, começar de `Stand` e só depois aceitar intenção do Bot Engine Host.
- Durante hit reaction, knockdown ou morte, zerar movimento e ataque.
- Respawn deve restabelecer `Stand`, HP, alvo e sequenciador, sem herdar fases anteriores.

### Locomoção

- `0x030A` transporta pose; na captura, `actionCode` permanece `0`.
- `0x0311 Normal` seleciona a animação visível.
- `0x030F` mantém o estado companheiro.
- Pose nova sem animação coerente produz deslizamento.
- Salto precisa de início, trajetória física, aterrissagem e retorno à locomoção; enviar apenas o ID
  de jump não cria esse ciclo.

### Ataque

- Cada intenção abre uma instância com identificador próprio.
- Uma instância escolhe um perfil compatível com classe e arma.
- O perfil agenda todas as fases observadas, preservando ordem e tempo.
- Só a janela ativa pode consultar hitbox e aplicar dano.
- Soltar o botão ou perder o alvo não pode converter fases antigas em um novo ataque.
- O sequenciador atual que alterna `25`, `24` e `12` entre três ataques está incorreto: a captura
  mostra `25 → 24 → 12` dentro do mesmo ataque.

### Dano e apresentação

- Ataque visual, contato, redução de HP, reação e HIT são eventos correlacionados, não sinônimos.
- A vítima publica a reação sobre si.
- Knockdown bloqueia pathfinding, perseguição e ataque até rising/respawn.
- Morte encerra a instância de ataque e qualquer movimento pendente.
- O contador HIT deve iniciar no snapshot atual; uma sequência persistida antes do handshake não
  pode ser reproduzida como golpes novos.

## Próximas capturas necessárias

Para fechar todas as classes sem heurística:

1. um round por classe usando a arma básica;
2. uma execução isolada de cada comando comum;
3. uma execução por arma de distância;
4. grips frontal, traseiro e falho;
5. knockdown, rising, morte e respawn;
6. Chaos por classe;
7. classe avançada e segunda arma.

Cada sessão deve registrar classe, arma, input humano e janela exata do round. Até essas capturas
existirem, o bot deve usar perfis comprovados e um fallback conservador, sem atribuir nomes a IDs
apenas por frequência.
