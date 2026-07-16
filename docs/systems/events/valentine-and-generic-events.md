# RE de Valentine e eventos genéricos

## Veredito

O cliente v258 contém assets genéricos de evento e três arquivos com nomes Valentine, mas a análise
do conteúdo fecha Valentine como **feature ausente/residual nesta build**, não como sistema parcial.
Cada arquivo possui somente 7 bytes (`// 종료`, “fim”), nenhum aparece no `levellist` e nenhum
módulo contém string, calendário, ID, texto ou regra Valentine.
Os exports `SendEvent1` (`0x66`) e `SendEvent4` (`0x69`) não pertencem a um protocolo de Valentine
comprovado: são builders vazios, não têm consumidor ativo e são rejeitados pelo World desta build.

O servidor .NET deve continuar respondendo `DISC C9` a ambos. Não há sistema Valentine para ativar
fielmente a partir destes artefatos; criar handlers ou ligar os assets seria regra nova.

## Evidência confirmada no cliente

O índice dos XFS contém:

- `DataSetup\LevelData\ValentineEvenStage1st.txt`;
- `ValentineEvenStage2nd.txt` e `ValentineEvenStage3rd.txt`;
- `TexturesSV\UI\Event\eventlogo.tex`, `eventtime.tex`, `name_heart.tex` e números de pontos;
- `TexturesSV\MapItem\eventitem.tex`;
- janelas `img_event_box`, `img_event_result` e `img_event_window`;
- resultados `eventround`, `eventround_1`, `_2` e `_3`.

No `DataSetup.xfs` distribuído, os três entries têm `UCSize=7`, conteúdo idêntico e somente o
comentário coreano `// 종료`. A lista real de níveis contém tutorial, batalhas e stages `001..055`,
sem Valentine ou Christmas. A busca em todos os `.exe/.dll/.bin`, configs, textos, SQL e fontes
disponíveis não encontrou sequer a palavra `Valentine` fora dos três nomes do XFS.

`TraceClientEventAssets.py` encontrou um único xref em `rakion.bin`:
`img_event_window.tex` é carregado por `FUN_00401130` em um componente de textura que também carrega
`img_kill.tex`; não há regra, request ou calendário. No `entitiesmp-unpacked.dll`, o único xref de
recurso é `MapItem/EventItem.tex` no inicializador visual do `EventItem`. As classes
`EChristmasSetting/ESpawnEventItem/EGetEventItem/EDestroyEventItem` vinculam esse pipeline ao Natal,
tratado no documento próprio, e não a Valentine.

## Prova negativa de `0x66/0x69`

| Opcode | Export | Builder | Referências no executável | World original |
|---:|---|---|---|---|
| `0x66` | `SendEvent1 @ 0x36192C40` | somente `u16 0x66` | IAT/thunk; nenhuma chamada ativa | default, `DISC C9` |
| `0x69` | `SendEvent4 @ 0x36192C80` | somente `u16 0x69` | IAT/thunk; nenhuma chamada ativa | default, `DISC C9` |

No sentido S→C, `0x67..0x6A` existem no dispatcher da `engine.dll`, porém os quatro slots finais
da vtable apontam para `rakion.bin:0x004734F0/00473500/00473510/00473520`, funções que retornam sem
efeito. Eles não formam um subsistema de evento utilizável nesta build.

Os slots da vtable são `+0x2CC` e `+0x2D0`. A jump table de `worldserv.exe` não contém esses cases,
e o probe ao vivo confirmou a desconexão C9 para cada opcode. Nomes sequenciais não provam que
existam `Event2/3`, nem vinculam os exports ao conteúdo Valentine.

## Fronteira do RE

O RE desta build está encerrado por ausência: não há consumidor dos markers, entrada de mapa,
protocolo, persistência ou regra de negócio Valentine. Recuperar um evento histórico diferente exige
outro cliente e outro servidor que realmente o contenham; isso seria novo escopo de evidência, não
uma lacuna escondida no v258 atual.

## Contrato para um evento novo

Se a intenção for criar um evento novo, ele deve usar contrato explícito e versionado, sem reutilizar
`0x66/0x69` apenas pelo nome. A regra fica no backend: janela UTC, objetivo, progresso, limites e
grant idempotente dentro da transação de inventário/economia. UI e assets apenas apresentam estado.
Natal e presentes permanecem no documento próprio e não devem ser reutilizados automaticamente.

## Validação e rollback de uma futura implementação

- testar fronteiras de tempo, reinício, duplicidade, concorrência, inventário cheio e falha parcial;
- validar visualmente logo, relógio, coração, rodada, resultado e item concedido;
- ativar primeiro sem recompensa ou para grupo de teste;
- desabilitar a definição no rollback, preservando progresso e grants legítimos;
- corrigir recompensa apenas por operação auditada, nunca removendo item automaticamente.

## Estado da evidência

- **Confirmado:** três markers vazios fora do `levellist`, assets genéricos, xrefs visuais,
  builders vazios, ausência de call sites e rejeição `C9`.
- **Ausência confirmada nesta build:** feature, calendário, objetivo, score, recompensa,
  persistência e protocolo Valentine.
- **Não relacionado:** `0x66/0x69`; EventItem e os eventos de entidade são natalinos/genéricos.

## Evidência reproduzível — 2026-07-15

- `DataSetup.xfs` analisado: SHA-256
  `093F1D39121FFA2C670433F8CACFE07FBDAFC93ECE0C125F76BC0588D3B3CC19`;
- `xfs_read.py DataSetup.xfs valentineevenstage1st` extrai 7 bytes e o mesmo hash/conteúdo ocorre
  nos três markers;
- a extração de `levellist.txt` retorna zero referências a `valentine/christmas`;
- `TraceClientEventAssets.py` produziu `client_event_assets_rakion.txt` e
  `client_event_assets_entitiesmp-unpacked.txt` com os xrefs descritos;
- `DecompileClientWorldEvents.py` e `world_dormant_event_probe.py` preservam a prova de `C9`.
