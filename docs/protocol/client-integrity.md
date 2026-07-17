# RE de integridade do cliente e anticheat

## Escopo e decisão

Este sistema é separado do GameGuard. O nProtect original está morto e não deve ser emulado; o
cliente de desenvolvimento permanece no-GG. A reconstrução contém challenge constante, aceite do
report GameGuard e uma verificação MD5 parcial. Isso fornece compatibilidade, não anticheat efetivo.

## Sinais do cliente e protocolo

| Sinal | Evidência | Situação no World |
|---|---|---|
| `SendGameGuard` | opcode `0x10` | challenge fixo; report aceito |
| `SendGMOperation` | export `0x64` sem payload | IP gate original reconstruído; não é integridade nem tutorial |
| `SendChCode(char*)` | export `0x65` | revalidação do MD5 selecionado no login, reconstruída |
| `SendPacketSpeedTest` | export com corpo vazio nessa build | sem rota/medição útil |
| hashes GM | request `0x0B` | altera MD5 em runtime/config |

O challenge `0x10` é um frame dourado constante. `Op_GameGuardAuth` apenas registra e aceita qualquer
payload. Isso é coerente com o cliente no-GG, mas deve ser nomeado como modo de compatibilidade.

## Login e verificação `0x65`

O primeiro byte do request de login `0x0C` não é um tipo de conexão. `FUN_0041F6C0` grava esse byte
em `user+0x237C` como modo e lê em seguida uma string de até 32 caracteres. O modo `1` seleciona
`World+0x14D` (`MD5_2`); os demais selecionam `World+0x12C` (`MD5_1`); modo `4` pula a comparação
no login. Divergência retorna erro de login `{0x0C, 8}`. Depois vêm account, password e `u16 tail`.

`IScavengerWorldNet::SendChCode @ engine.dll:0x36192A90` monta exatamente
`[u16 0x65][cstr md5]`. `FUN_00428430` copia 32 bytes, ignora modos `4/5`, exige field (`DISC BB`),
seleciona os mesmos hashes globais e compara com case-sensitive; divergência gera `DISC BC`, e
sucesso não responde. O export ocupa o slot `0x150` da vtable. Não foi localizada uma chamada direta
ativa: `rakion.bin` importa o símbolo, mas só possui IAT/thunk sem consumidor; os 29 consumidores de
`_pRakionWorldNet` no `engine.dll` e o consumidor do `gamemp.dll` não usam o slot. Portanto esta build
não possui cadência de ChCode a reproduzir. O handler `0x65` permanece como adapter compatível.

`SendPacketSpeedTest @ engine.dll:0x361929C0` retorna imediatamente, ocupa o slot `0x144` e também
não tem consumidor nos módulos disponíveis. Não existe payload, resposta ou medição original nessa
build; criar um protocolo com esse nome seria comportamento novo.

Como o handler copia exatamente os primeiros 32 bytes, o terminador NUL não participa da comparação;
payload truncado e diferença apenas de caixa falham com `BC`. A troca GM publica o par de hashes em
um único snapshot imutável, evitando que logins concorrentes observem `MD5_1` novo com `MD5_2` antigo.

Na reconstrução, `ClientHashPolicy` é a fonte única para login e field. `[Client] EnforceMD5=0` é o
padrão explícito de compatibilidade; quando ligado, `MD5_1` e `MD5_2` são obrigatórios, têm 32 hex e
o servidor falha no startup se a configuração for inválida. Os hashes são globais como no original,
não pertencem à sessão, e nunca são escritos nos logs.

Limitações que permanecem: MD5 não prova que o código medido está em execução, não há nonce e uma
resposta pode ser reutilizada. O servidor também não mede cadência, módulos carregados ou movimento
impossível por esse mecanismo.

## Integridade recomendada para lançamento

Não prometer anticheat forte no cliente legado. O desenho mínimo seguro é defesa em camadas:

- manifestos do launcher assinados e arquivos validados com SHA-256;
- app/build explícitos no ticket de login, com gate configurável no World;
- regras autoritativas no servidor para movimento, dano, cooldown, inventário e economia;
- limites por opcode e detecção de sequências impossíveis;
- eventos auditáveis, sem coletar dados pessoais ou varrer a máquina;
- fila de revisão/ban com evidência e ação administrativa separada.

O adapter legado pode continuar respondendo `0x10`, enquanto `IntegrityPolicy` decide builds aceitas.
Hashes esperados vêm de um catálogo imutável publicado com o build, nunca de mutação GM ad hoc.

O caminho moderno já grava `app_id/build_version` no ticket. `[Client] RequiredAppId` e
`RequiredBuildVersion` fazem o consumo atômico aceitar somente o par configurado; uma divergência
não queima o ticket. Isso é controle de rollout, não atestado criptográfico do processo em execução.

## Ativação e rollback

1. declarar `CompatibilityNoGameGuard=true` e retirar a falsa impressão de validação;
2. preencher os dois hashes do build distribuído e testar `EnforceMD5=1` em observação controlada;
3. introduzir build id/manifesto assinado em modo observação;
4. preencher `RequiredAppId/RequiredBuildVersion` somente depois da atualização distribuída;
5. ativar regras autoritativas individualmente com métricas de falso positivo.

Rollback do MD5 é `EnforceMD5=0`; os hashes podem permanecer configurados. Não deve reativar
GameGuard morto nem esconder nos logs operacionais que a política está desligada.

```ini
[Client]
EnforceMD5=0
MD5_1=
MD5_2=
RequiredAppId=0
RequiredBuildVersion=0
```

## Validações restantes fora do RE estático

- reconexão e build antigo em cliente real;
- teste visual do cliente no-GG do login ao field.

Spam, movimento impossível e economia manipulada não são pendências deste contrato de integridade:
pertencem, respectivamente, aos limites do dispatcher e à autoridade de combate/economia. O MD5
legado não consegue provar esses comportamentos e não deve ser apresentado como anticheat.

## Classificação

- **Confirmado:** challenge estático, builder `0x65`, MD5 de 32 bytes, modos, hashes globais,
  `BB/BC`, sucesso sem resposta e contrato do login.
- **Implementado:** política única, config validada, enforcement default-off, dispatcher, probes,
  update assinado e gate app/build no ticket.
- **Ausência confirmada nesta build:** call site/cadência de ChCode e contrato de packet-speed.

## Evidência executada em 2026-07-15

- `DecompileWorldChCode.py`, `DecompileClientChCode.py`, `TraceClientChCode.py` e
  `TraceClientWorldNetIntegrity.py` fecharam handler, builder, vtable, imports sem consumidor e os
  writers do modo no login;
- `world_ch_code_probe.py` confirmou ao vivo `DISC BB`, hash correto sem resposta e `DISC BC`;
- o login antigo com hash sintético continuou funcionando com `EnforceMD5=0`;
- 761/761 testes do World, 11/11 do launcher e build Release sem warnings; o smoke MariaDB
  também confirmou migração do schema antigo e rejeição de build divergente sem consumir ticket.
