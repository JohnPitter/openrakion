# Dispatcher CNet de gameplay — Rakion v258

Golden sources: `engine.dll` SHA-256 `83b20d6c32cd66b95c8f8e41ad6de13a58e8f5f948cd21cbd118d42ef8cf88f2`; `rakion.bin` SHA-256
`435f50e3ff9f3f140d4c335336b4ba4a758df823c146210cc8da90460960ffff`.

O pump real é `rakion.bin:0x004124A0`: ele drena `CNet::RecvData` e entrega cada mensagem
a `rakion.bin:0x00411760`. Esse dispatcher trata diretamente transporte, diagnóstico e
algumas ações; quando o estado da partida é válido, o `default` encaminha gameplay para
`CSessionState::HandleMessage @ engine.dll:0x3610D7C0`.

Os cases tratados diretamente pelo executável são 0x0201, 0x0203, 0x0304, 0x0305, 0x030E, 0x0311, 0x0313, 0x0314, 0x0315, 0x0318, 0x0401, 0x0402, 0x0403, 0x0501 e 0x0502. Os nove cases
de gameplay delegados e seus layouts estáticos são:

| Tipo lógico | Semântica | Corpo consumido |
|---:|---|---|
| `0x0307` | Create general NPC | `u8 owner, u8 index, u16 entity, 6*f32 placement, init blob` |
| `0x0308` | Create Master Golem | `u8 host, u8 team, u16 entity, 6*f32 placement, init blob` |
| `0x0309` | Create map NPC | `u8 host, u8 index, u16 entity, 6*f32 placement, init blob` |
| `0x030A` | Player action/movement | `CNetMessage de ação serializada` |
| `0x030B` | Entity placement/state | `u16 state, u8 kind/group/index, 4*s16 placement` |
| `0x030C` | Entity event | `u8 source/class/indexA/indexB, u32 event, u32 length, payload` |
| `0x030F` | Player sync snapshot | `u8 source echo, 6*u8 sync fields` |
| `0x0310` | Map NPC state/action | `u8 state, u8 kind, u8 map index` |
| `0x0312` | Map item snapshot | `u8 count, count * (u8 index, u8 state)` |

No UDP reliable, o transporte acrescenta `0x8000` ao tipo lógico; por exemplo,
`0x030C` aparece no fio como `0x830C`. ACK `0x4000`, sequência e slot de origem são
metadados de transporte e não pertencem ao payload acima.
O case direto `0x0311` lê `u8 sourceEcho` e delega o restante a `CPlayer::DoAnimPacket`:
`kind=0/1` consome `u8 animationId`; `kind=2` consome três argumentos `u8` de dano.
A implementação original tolera dois bytes finais não consumidos em pacotes de kind `0/1`.

`worldserv!FUN_0041B940` não produz mensagens CNet. Ela grava a fila de requests DB no formato
`[u16 requestSequence][u16 commandType][data]`; `FUN_0041B3F0/FUN_0041AE50` a consomem.
Na saída, o comando `0x0C [characterId][remainingExp]` executa `UPDATE CharacterInfo.exp`
em `FUN_004138B0`; somente seu ACK interno não possui consumer. O retorno cliente-visível é
WorldNet `0x58 [i32 remainingExp]`, sem relação com o evento CNet `0x030C`.

Total: **9 cases de gameplay delegados** e **15 cases diretos** no dispatcher externo.
