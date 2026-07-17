# Resumo do RE completo — estado e objetivos restantes

Atualizado em 17 de julho de 2026. Este é o resumo executivo; a matriz detalhada e as evidências
continuam em [`re-coverage.md`](re-coverage.md).

## Objetivo geral

Mapear a superfície do Rakion v258 encontrada no cliente, servidores originais, banco e assets;
comparar cada contrato com o servidor .NET; implementar o que for necessário para compatibilidade;
e separar três níveis de conclusão:

1. **RE estático**: contrato, regra, layout ou ausência comprovados nos artefatos;
2. **headless**: implementação validada por build, testes e probes sem interface gráfica;
3. **runtime visual**: comportamento observado no cliente real, inclusive com dois clientes e redes
   diferentes quando aplicável.

O projeto já concluiu a catalogação estática dos **29 domínios** da auditoria. Isso não significa que
o jogo inteiro esteja validado visualmente: a principal fronteira restante é dinâmica.

## Objetivos já concluídos

| Objetivo | Estado atual |
|---|---|
| Organizar a documentação | Concluído: documentos canônicos estão separados em `protocol/`, `systems/`, `guides/`, `audits/` e `archive/` |
| Auditar a superfície completa do jogo | Concluído estaticamente: 29/29 domínios possuem passe dedicado e lacunas explícitas |
| Clã | RE e ciclo básico headless concluídos; login, árvore, persistência, Admin, Buddy e presença de canal documentados |
| Amigos/Buddy | RE e implementação headless concluídos; lista, add/remove, apelido, grupos, presença, SMS e túnel documentados |
| Natal e presentes | Gift Box implementada e validada headless; conteúdo natalino ausente/dormente nesta build foi comprovado |
| Protocolo e fundação World | Dispatches, opcodes, estados, lobby, sala, field, P2P/túnel e ciclo de personagem mapeados e testados headless |
| Economia e progressão | Loja, inventário, storage, coupons, Power User, enchant, ranking e vendas locais fechados no limite headless documentado |
| Gameplay comum | PvP, stage, combate, potions, Chaos, objetivos, votes e fluxo de Cells/NPC possuem contratos canônicos |
| Curvas e infraestrutura de NPC | 47 tipos, 99 níveis, 43 classes carregáveis, atributos, CP, targeting, ownership, dano, late join e catálogo de eventos fechados estaticamente |
| Famílias-base de NPC até Dragon | Nak, Panzer, CrossBow, Blazer, Golem, SoulCannon, LongBow, Taurus e Dragon possuem documentos e extratores reproduzíveis |
| Ferramentas de análise | SDK .NET e fluxo headless do Ghidra estão disponíveis para reproduzir extrações, testes e builds |

## Objetivo em andamento

### 1. Fechar a família IceWind

IceWind é a última família-base listada em `creaturelist.txt` sem documento dedicado. O marco deve
entregar:

- descritores das quatro variantes e da base;
- tabela completa de eventos e handler default;
- construtor, defaults, seletores, ataques, movimento e comportamento aéreo;
- assets, animações, áudio e pontos de attachment;
- extrator Ghidra reproduzível e documento canônico;
- atualização da auditoria, testes e build sem warnings;
- commit e push próprios.

## Objetivos que ainda faltam

### 2. Auditar classes residuais e especiais de NPC

Depois de IceWind, fazer uma varredura de fechamento para provar que nenhuma classe concreta ficou
fora da documentação. O passe inclui aliases e variantes Gold, Master, Black e Special, além de
confirmar formalmente os quatro `NpcBlackDragon*` ausentes nesta build. O resultado deve distinguir:

- classe real com comportamento próprio;
- alias de uma família já documentada;
- conteúdo configurado sem classe carregável;
- nome/asset sem produtor ou consumidor ativo.

### 3. Fechar a auditoria estática final

Recontar descritores, classes, eventos e documentos após o passe residual; eliminar referências como
“famílias após Dragon”; verificar links e fontes canônicas; e publicar uma matriz final de
**coberto**, **ausente comprovado** ou **extensão fora do original**. Esse marco encerra o RE estático,
caso a varredura não revele um novo subsistema.

### 4. Executar validação dinâmica com o cliente real

Esta é a maior pendência para chamar o jogo de completo em runtime:

- duas contas: login, personagem, canal, sala, field, clã e Buddy;
- matriz P2P direta, TunnelOne/TunnelAll, mesma máquina, LAN, NAT e UDP bloqueado;
- PvP: movimento, dano, morte, respawn, placar, round e resultado;
- PvE: os 48 stages, waves, objetivos, clear/derrota, settlement e late join;
- NPCs: spawn, animação, targeting, hitbox, projéteis, efeitos, áudio e morte;
- economia/UI: inventário, loja, storage, presentes, enchant, coupons, Power User e ranking;
- launcher/update, ticket, integridade e rollback no fluxo real.

Cada validação precisa registrar build/hash, configuração, captura ou log, resultado esperado e
resultado observado. Build verde não substitui a prova visual.

### 5. Decidir extensões que não pertencem ao v258 original

Somente depois do fechamento compatível devem ser priorizadas features autorais: servidor de combate
autoritativo, bots/puppets, checkout/recarga, liquidação de loteria, política moderna de PC Bang,
SMTP e eventual evento de Natal novo. Elas não devem ser contabilizadas como lacunas do RE original.

## Ordem recomendada

1. IceWind;
2. classes residuais/especiais;
3. auditoria estática final;
4. smoke visual da jornada básica com dois clientes;
5. matriz PvP/P2P;
6. matriz PvE/NPC;
7. economia, launcher e integrações externas;
8. extensões autorais escolhidas para o lançamento.

## Critério de encerramento

O **RE estático completo** exige IceWind, passe residual e auditoria final sem classe ou domínio sem
veredito. O **jogo completo para lançamento** exige, além disso, os goldens runtime dos fluxos
críticos. Enquanto esses testes visuais não forem executados, o estado correto do projeto é:
**superfície estática catalogada e ampla compatibilidade headless, com validação dinâmica pendente**.
