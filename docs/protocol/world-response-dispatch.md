# Catálogo do dispatcher IScavengerWorldNet S→C — Rakion v258

Golden sources: `engine.dll` SHA-256 `83b20d6c32cd66b95c8f8e41ad6de13a58e8f5f948cd21cbd118d42ef8cf88f2`; `rakion.bin` SHA-256
`435f50e3ff9f3f140d4c335336b4ba4a758df823c146210cc8da90460960ffff`; dispatcher `0x36197320`; vtable final `0x004DDC08`.

Este catálogo enumera todos os cases aceitos pela fila de respostas `IScavengerWorldNet`.
`ProcessWorldRecvBuffer @ 0x36197A40` é seu único caller na `engine.dll`; o executável chama
esse export uma vez por iteração principal em `rakion.bin:0x004126BD`. Os cases FIELD abaixo
são respostas de controle que também passam por WorldNet. `worldserv!FUN_0041B940` não é
outro stream cliente: ela alimenta a fila de requisições do worker DB do World.
A família indica contexto funcional esperado e não substitui a causalidade/canal por estado.

| Opcode | Handler engine | Destino | Implementação rakion.bin | Família funcional |
|---:|---:|---:|---:|---|
| `0x00` | `virtual+0x15C` | `callback+0x15C` | `0x00472B50` | sessão/login |
| `0x04` | `0x36192D60` | `callback+0x160` | `0x00473740` | sessão/login |
| `0x05` | `0x36192D80` | `callback+0x164` | `0x00473750` | sessão/login |
| `0x0C` | `0x36195E10` | `callback+0x168` | `0x0047E140` | sessão/login |
| `0x0D` | `0x36196690` | `callback+0x16C` | `0x0047E6F0` | sessão/login |
| `0x0E` | `0x36192DA0` | `callback+0x170` | `0x00477200` | sessão/login |
| `0x10` | `0x36192E10` | `callback+0x174` | `0x004763F0` | sessão/login |
| `0x12` | `0x36192E30` | `callback+0x178` | `0x0047C4D0` | personagem |
| `0x13` | `0x36192E70` | `callback+0x17C` | `0x0047C7A0` | personagem |
| `0x14` | `0x36192F90` | `callback+0x180` | `0x0047CB40` | personagem |
| `0x15` | `0x36192FB0` | `callback+0x184` | `0x004785B0` | personagem |
| `0x16` | `0x36193000` | `callback+0x188` | `0x00475A30` | personagem |
| `0x17` | `0x361930B0` | `callback+0x18C` | `0x00475D80` | personagem |
| `0x18` | `0x361930F0` | `callback+0x19C` | `0x00475F10` | personagem |
| `0x19` | `0x36193170` | `callback+0x190` | `0x00476450` | personagem |
| `0x1B` | `0x36195400` | `callback+0x194` | `0x0047DFF0` | personagem |
| `0x1C` | `0x36195500` | `callback+0x198` | `0x004787B0` | personagem |
| `0x1D` | `0x361931E0` | `callback+0x1A0` | `0x00474260` | canal |
| `0x1E` | `0x361932A0` | `callback+0x1A4` | `0x00472BF0` | canal |
| `0x1F` | `0x361933F0` | `callback+0x1A8` | `0x00473DA0` | canal |
| `0x20` | `0x36193490` | `callback+0x1AC` | `0x00472C10` | canal |
| `0x21` | `0x361934B0` | `callback+0x1B0` | `0x00475620` | canal |
| `0x22` | `0x361934E0` | `callback+0x1B4` | `0x00475630` | canal |
| `0x25` | `0x36193550` | `callback+0x1B8` | `0x004756C0` | canal |
| `0x26` | `0x36193590` | `callback+0x1BC` | `0x004756D0` | canal |
| `0x27` | `0x361935D0` | `callback+0x1C0` | `0x004756E0` | canal |
| `0x28` | `0x361935F0` | `callback+0x1C4` | `0x004756F0` | canal |
| `0x29` | `0x36193610` | `callback+0x1C8` | `0x00473760` | canal |
| `0x2A` | `0x36193630` | `callback+0x1CC` | `0x00473770` | canal |
| `0x2C` | `0x36193650` | `callback+0x1D0` | `0x00474C70` | inventário/progressão |
| `0x2D` | `0x36193680` | `callback+0x1D4` | `0x00474DE0` | inventário/progressão |
| `0x2E` | `0x36195640` | `callback+0x1D8` | `0x004774E0` | inventário/progressão |
| `0x2F` | `0x361936A0` | `callback+0x1DC` | `0x00478A70` | inventário/progressão |
| `0x31` | `0x36193810` | `callback+0x1E4` | `0x0047D1D0` | inventário/progressão |
| `0x32` | `0x361957C0` | `callback+0x1E8` | `0x00475220` | inventário/progressão |
| `0x33` | `0x361938A0` | `callback+0x1EC` | `0x0047DBB0` | inventário/progressão |
| `0x34` | `0x361958C0` | `callback+0x1F0` | `0x00474F50` | inventário/progressão |
| `0x35` | `0x361959C0` | `callback+0x1F4` | `0x004753F0` | inventário/progressão |
| `0x36` | `0x36193900` | `callback+0x208` | `0x00474140` | lista/sala |
| `0x37` | `0x36196CE0` | `callback+0x20C` | `0x0047A370` | lista/sala |
| `0x38` | `0x361970E0` | `callback+0x210` | `0x0047A140` | lista/sala |
| `0x39` | `0x36193A60` | `callback+0x214` | `0x004759C0` | lista/sala |
| `0x3A` | `0x36193A70` | `callback+0x218` | `0x00474410` | lista/sala |
| `0x3B` | `0x36193A90` | `callback+0x21C` | `0x0047CD20` | lista/sala |
| `0x3C` | `0x36193AC0` | `callback+0x220` | `0x004728F0` | lista/sala |
| `0x3D` | `0x36193AE0` | `callback+0x224` | `0x004746E0` | lista/sala |
| `0x3E` | `0x36193B10` | `callback+0x228` | `0x0047A5A0` | lista/sala |
| `0x41` | `0x36193B50` | `callback+0x22C` | `0x00476100` | lista/sala |
| `0x42` | `0x36193C50` | `callback+0x230` | `0x00478A00` | lista/sala |
| `0x43` | `0x36193C80` | `callback+0x234` | `0x00474790` | lista/sala |
| `0x44` | `0x36193CA0` | `callback+0x244` | `0x00474A20` | field/partida |
| `0x45` | `0x36193CC0` | `callback+0x238` | `0x004744F0` | field/partida |
| `0x46` | `0x36193CF0` | `callback+0x23C` | `0x00479760` | field/partida |
| `0x47` | `0x36193D10` | `callback+0x240` | `0x00475670` | field/partida |
| `0x48` | `0x36193DE0` | `callback+0x248` | `0x00472C80` | field/partida |
| `0x49` | `0x36193E40` | `callback+0x24C` | `0x00475890` | field/partida |
| `0x4A` | `0x36193E80` | `callback+0x250` | `0x004799E0` | field/partida |
| `0x4B` | `0x36193D70` | `callback+0x254` | `0x00472C70` | field/partida |
| `0x4E` | `0x36193ED0` | `callback+0x258` | `0x00472DB0` | field/partida |
| `0x4F` | `0x36193EE0` | `callback+0x25C` | `0x00479300` | field/partida |
| `0x51` | `0x36194100` | `callback+0x268` | `0x00475590` | field/partida |
| `0x52` | `0x36194130` | `callback+0x26C` | `0x00478CC0` | field/partida |
| `0x53` | `0x36194190` | `callback+0x270` | `0x00472750` | field/partida |
| `0x54` | `0x36194220` | `callback+0x274` | `0x00472F00` | field/partida |
| `0x55` | `0x36194230` | `callback+0x278` | `0x00472F10` | field/partida |
| `0x57` | `0x36194240` | `callback+0x27C` | `0x00472F20` | field/partida |
| `0x58` | `0x361942A0` | `callback+0x280` | `0x00473270` | field/partida |
| `0x59` | `0x361942C0` | `callback+0x294` | `0x00473780` | field/partida |
| `0x5A` | `0x361942E0` | `callback+0x298` | `0x00473790` | field/partida |
| `0x5C` | `0x36194310` | `callback+0x284` | `0x004737A0` | field/partida |
| `0x5D` | `0x36194360` | `callback+0x28C` | `0x00476860` | field/partida |
| `0x5F` | `0x361943D0` | `callback+0x290` | `0x00476A80` | field/partida |
| `0x61` | `0x361945B0` | `envia-0x61` | `envia-0x61` | field/partida |
| `0x62` | `0x36194510` | `callback+0x2A4` | `0x00473980` | field/partida |
| `0x63` | `0x36194460` | `callback+0x29C` | `0x00475730` | field/partida |
| `0x67` | `0x361940A0` | `callback+0x2D4` | `0x004734F0` | eventos/presentes |
| `0x68` | `0x361940B0` | `callback+0x2D8` | `0x00473500` | eventos/presentes |
| `0x69` | `0x361940C0` | `callback+0x2DC` | `0x00473510` | eventos/presentes |
| `0x6A` | `0x361940E0` | `callback+0x2E0` | `0x00473520` | eventos/presentes |
| `0x6B` | `0x36194530` | `callback+0x2BC` | `0x0047B7B0` | eventos/presentes |
| `0x6C` | `0x36194560` | `callback+0x2C0` | `0x00478090` | eventos/presentes |
| `0x6D` | `0x36194590` | `callback+0x2C4` | `0x00473540` | eventos/presentes |
| `0x6F` | `0x36195AC0` | `callback+0x1F8` | `0x00477840` | inventário/progressão |
| `0x70` | `0x36195BD0` | `callback+0x1FC` | `0x004724B0` | inventário/progressão |
| `0x71` | `0x36195CC0` | `callback+0x200` | `0x00472600` | inventário/progressão |
| `0x72` | `0x36193F40` | `callback+0x260` | `0x004761D0` | lista/sala |
| `0x73` | `0x36194080` | `callback+0x264` | `0x004782E0` | inventário/progressão |
| `0x74` | `0x36194600` | `callback+0x288` | `0x00478D80` | inventário/progressão |

Total: **88 cases**, sem handler ausente ou opcode duplicado.

Observações estáticas fechadas:

- a tabela termina em `0x74`; `0x75/0x76` da loteria existem no World original, mas não possuem
  consumer S→C neste `engine.dll`, nem builder C→S localizado nesta build do cliente;
- `0x61` não chama a UI: remonta e devolve `[u16 0x61][i32 value]` ao World;
- `0x04`, `0x05`, `0x29`, `0x2A`, `0x59`, `0x5A`, `0x5C` e `0x67..0x6A` apontam para funções vazias no `rakion.bin`;
- em especial, `0x6A` não gera UI visual nesta build.

Contratos de progressão fechados pelo consumidor e pelo produtor original:

| Opcode | Payload lógico S→C | Consumidor | Efeito |
|---:|---|---:|---|
| `0x51` | `[u8 newLevel][u16 levelPoints]` | `0x36194100` | atualiza nível e pontos locais |
| `0x52` | `[u8 seat][u8 playerLevel][u8 cellLevel0][u8 cellLevel1][u8 cellLevel2]` | `0x36194130` | aplica level-up ao player remoto e atualiza os três slots de cell do jogador local |

O `u16` intermediário usado pelo builder original de `0x52` representa os dois primeiros
níveis de cell em bytes little-endian; o consumidor encaminha os cinco bytes separadamente.
`ProgressionResponseBodies` é a golden source de emissão no World .NET.

Respostas simples ou dormentes auditadas:

| Opcode | Consumo estrutural no engine | Callback Rakion | Produtor World |
|---:|---|---|---|
| `0x5C` | `cstr text` | copia a string; callback final vazio | sem produtor literal na build |
| `0x63` | `cstr text` | copia e encaminha a string | `FUN_0041F290` |
| `0x67` | corpo não lido | callback sem argumentos e vazio | sem produtor literal na build |
| `0x68` | corpo não lido | callback sem argumentos e vazio | sem produtor literal na build |
| `0x69` | ponteiro bruto | callback recebe o endereço e retorna | sem produtor literal na build |
| `0x6A` | ponteiro bruto | callback recebe o endereço e retorna | `FUN_0041C330/0041D650` |

`corpo não lido` não equivale a payload obrigatoriamente vazio: o handler ignora qualquer
byte posterior. `ponteiro bruto` também não define um `u32`; é o endereço do início do corpo.
Somente o produtor pode fechar a gramática desses casos. Para `0x6A`, o produtor de presentes
fecha `[count:u8][itemId:u32 * count][accountName:cstr]`, embora a UI final seja vazia.
A busca de `0x5C/0x67..0x69` percorreu até quatro chamadas até os senders World. Em `0x5C`,
todas as ocorrências eram offsets. Os únicos literais `0x67/0x69` e um dos `0x68` eram razões
de disconnect em `FUN_00423CC0`; o outro `0x68` era stride de `IMUL` no sender. Essas APIs
existem no cliente, mas não possuem produtor estático no `worldserv.exe` v258 analisado.
