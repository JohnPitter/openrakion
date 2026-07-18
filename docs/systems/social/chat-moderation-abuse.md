# Engenharia reversa de chat, moderação e abuso — Rakion v258

## Estado atual

As rotas textuais comprovadas do World usam agora um único pipeline de moderação, sem alterar seus
frames:

- room/channel `0x22`;
- field `0x47`;
- whisper por nome `0x16`.

O pipeline valida 128 caracteres ASCII, rejeita controles, aplica a lista original de abuso,
limita burst e repetição por sessão/escopo, persiste auto-mute, respeita block list no whisper e
audita decisões sem gravar o texto em claro.

Tunneling `0x56/0x57`, mensagens codificadas `0x5D/0x5E` e gameplay UDP continuam binários e nunca
passam por filtro textual.

O SMS central do Buddy agora usa o mesmo pipeline. O servidor autentica o login cifrado legado,
decifra `0x2030`, aplica mute/block/rate/repetição/filtro, persiste a mensagem e entrega online ou
no próximo login por `NTF_SAVE_PACKET 0x2010`. A rota P2P direta `0xC015` continua sendo uma
fronteira não autoritativa; ela permanece indisponível enquanto presença/UDP do sistema de amigos
não for ativada corretamente.

## Contratos do World

| Request | Escopo | Contrato comprovado |
|---|---|---|
| `0x16` | whisper | `[target\0][text\0]`, target menor que 13, texto menor que 129 |
| `0x22` | room | string C; broadcast preserva o subtipo e o layout original |
| `0x47` | field | `[text\0]`; resposta `[u16 0x47][senderSeat][text\0]` |

O golden de `0x47` continua sendo `seat + ASCII + NUL`. O filtro apenas substitui o texto antes de
entregá-lo ao mesmo builder.

O whisper original não é limitado a field: `FUN_00420200` exige `usergameinfo.id` e personagem
selecionado, percorre todas as sessões e compara `CharName` por `lstrcmpA` (case-sensitive). O alvo
precisa estar ocupado, autenticado, com personagem selecionado e `SubStatus != 1`. Em sucesso, o
World envia para alvo e remetente `[0x16:u16][0][sender\0][text\0]`; em falha envia apenas
`[0x16:u16][1]`. Os callbacks do cliente exibem o resultado no canal, sala e field.

`0x56/0x57` transportam buffers opacos de até 1000 bytes. Interpretá-los como chat permitiria
corromper ações P2P e criar falsos positivos sobre dados arbitrários.

## `abusestring.txt`

A distribuição contém `ragezone/DataSetup/abusestring.txt`, com linhas tab-separated
`padrão → LOVE`. A versão canônica usada pelo servidor está em
`server/RakionServer/deploy/abusestring.txt`, deduplicada por case porque o matcher é
case-insensitive.

O filtro original roda no cliente e, sozinho, não é autoridade. O backend replica a substituição nas
rotas textuais do World. Matching permanece substring, inclusive os falsos positivos inerentes à
lista v258; alterar para palavras inteiras seria mudar a semântica do conteúdo original.

## Pipeline central

```text
decode ASCII/NUL
  -> tamanho e controles
  -> mute persistente
  -> block do destinatário no whisper
  -> token window por escopo
  -> repetição normalizada
  -> substituições de abusestring
  -> frame original
```

Defaults de deployment:

```ini
[Chat]
Enabled=1
AbuseFile=abusestring.txt
Burst=5
WindowSeconds=5
RepeatLimit=3
RepeatWindowSeconds=10
AutoMuteSeconds=30
```

O rate limit é separado entre room, field e whisper. Exceder burst ou repetir a mesma mensagem três
vezes no intervalo aplica mute de 30 segundos. Configurações são limitadas no parser para impedir
valores negativos ou absurdos.

## Persistência

O boot do World cria:

- `chat_mute`: conta, validade UTC, motivo, operador e atualização;
- `chat_block`: owner e conta bloqueada, com chave composta;
- `chat_moderation_log`: sender/target, scope, ação, regra, hash SHA-256, comprimentos e timestamp.

O login carrega mute e blocks antes do `0x0C`. Auto-mute atualiza memória imediatamente e faz upsert
persistente. Um whisper bloqueado é tratado como não entregue, sem revelar ao remetente que está na
lista do destinatário.

Mensagens permitidas não são persistidas. Decisões de filtro/rejeição guardam apenas hash e
metadados; isso reduz exposição de conversa sem perder correlação de abuso repetido.

## Buddy e SMS central

O RE adicional de `Buddy2.dll` está em `C:\temp\buddy_sms_flows.txt` e
`C:\temp\buddy_sms_key.txt`, gerados por:

```powershell
py tools/ghidra/FindBuddySmsFlows.py
py tools/ghidra/FindBuddySmsKey.py
```

`FUN_10001F30` monta o plaintext `[recipient char[20]][u16 length][message]`, completa até múltiplo
de 12, prefixa cada bloco com o marcador `0x2DBABE65` e cifra AES-128-ECB para enviar
`SVC_SMS_SEND 0x2030`. `RET_SMS_SEND 0x2031` contém um `u16` de resultado.

O login `0x1010` possui 32 bytes de credencial AES com a chave fixa original
`2C45926CF3396642B670D006A1FA8182`, seguidos por 176 bytes da sessão.
A credencial revela `accountId[20]` e o seed do `RET_PRECREDENTIAL`; a chave da sessão usa SHA-0
sobre `accountId || seedLE32` e os quatro primeiros words em little-endian. Marcador e header lógico
`0x1B` são validados antes
de autenticar a sessão.

A entrega central usa o registro exato consumido por `NTF_SAVE_PACKET`:

```text
u16 count
repetir:
  u32 messageId
  char senderAccount[20]
  wchar senderDisplay[20]
  u32 unixTime
  u16 innerOpcode = 0xC015
  u16 messageLength
  byte message[messageLength]
```

O lote é cifrado com a chave da sessão do destinatário. O cliente processa `0xC015` no callback e
confirma `[u16 count][u32 id...]` em `0x2011`; só então `acked_at` é preenchido. `buddy_sms` mantém a
fila offline. Texto em claro existe nessa tabela porque é necessário para entrega; a auditoria de
moderação continua guardando somente hash e metadados.

## Implementação

- `RakionServer.Common/ChatModeration.cs`: regra única compartilhada por World e Buddy;
- `WorldServer.Chat.cs`: coordenação e auditoria;
- `Database/WorldDatabase.Chat.cs`: mute/block/log;
- `WorldHandlers.Field.cs` e `WorldHandlers.FieldChat.cs`: adapters dos opcodes;
- `deploy/abusestring.txt`: regra canônica server-side;
- `tools/ghidra/FindBuddySmsFlows.py`: RE reproduzível do SMS.
- `tools/ghidra/FindBuddySmsKey.py`: chave, framing AES e aliases do contexto.
- `BuddyCrypto.cs`, `BuddySmsCodec.cs` e `BuddyDatabase.cs`: autenticação, wire e fila offline.

## Validação

- testes cobrem filtro sem case, controles, rate por escopo, repetição, mute e block;
- goldens cobrem os frames originais de field chat, whisper e localização;
- smoke MariaDB isolado confirmou mute ativo, block `bob→alice` e auditoria de whisper bloqueado
  com hash de 64 caracteres, sem texto claro;
- smoke Buddy com duas conexões confirmou login cifrado, `alice→bob`, entrega `0x2010`, inner
  `0xC015`, ACK `0x2011`, `delivered_at` e `acked_at`;
- a busca de personagem cobre escopo global, nome case-sensitive e exclusão do subestado especial;
- 668 testes do World e build Release passaram sem warnings; schemas temporários removidos.

## Lacunas restantes

- validar visualmente SMS central com dois clientes reais;
- completar listas/presença/UDP/tunnel do Buddy antes de habilitar P2P direto;
- decidir se `0xC015` direto será desativado ou receberá enforcement no cliente;
- disponibilizar mutação de block list por um contrato comprovado; não existe opcode World para isso;
- validação visual com dois clientes em room, field e whisper.

World textual e SMS central estão implementados headless. O domínio de moderação fecha no servidor;
P2P direto continua explicitamente fora da autoridade até o sistema de presença do Buddy ser
reconstruído e ativado.
