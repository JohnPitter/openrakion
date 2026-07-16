# Engenharia reversa de GM, Admin e comandos operacionais — Rakion v258

## Escopo e veredito

Cobre authority, status GM, `0x03..0x0B`, `0x64`, variáveis, notice, ban, open/close, painel Admin
e auditoria.

**Veredito:** a elevação por canal foi corrigida. O World autentica `user.Authority` no login direto,
guarda-a na sessão e cada opcode GM consulta uma permissão explícita; `[GM] Enabled=0` é o padrão.
Canal `Special` só muda a apresentação para uma identidade já autorizada e nunca cria authority.
MD5 inválido é recusado e os hashes deixaram de aparecer nos logs. O painel Admin ainda usa senha e
connection string externas obrigatórias, bind local, antiforgery e rate limit. Viewer/Operator/Owner
são aplicados no backend, HTTP externo sem TLS falha fechado e toda mutação possui ledger
transacional encadeado. Exposição externa ainda exige certificado/proxy e credenciais próprias.

## Superfície World

- `0x03`: open/close; close desconecta todos que não estão `LobbyGm`;
- `0x04`: exportado como AdminBan, mas o World apenas ecoa `[flag][texto curto]`; não grava ban;
- `0x05`: notice por escopo `0/2/3`, field e nome opcional; não é whisper global;
- `0x08/0x0A`: set/get de até 512 GM vars;
- `0x09`: consulta entry, serialização ainda stub;
- `0x0B`: altera dois MD5 do client em memória/config;
- `0x64`: `SendGMOperation` sem payload; exige substatus ASCII `0x34` e IP permitido;
- ban/notice/kick por comando GM não formam um catálogo implementado.

`GmVars` é array em memória; não há definição tipada, range por variável, persistência nem rollback.
SetClientMd5 valida 32 dígitos hexadecimais e não registra os hashes. Open/close continua sendo uma
mutação global destrutiva, agora protegida por permission explícita.

## Contrato exato do `0x64`

O export `IScavengerWorldNet::SendGMOperation @ engine.dll:0x36194E00` escreve somente o opcode
`u16 0x64`; não existe payload. No World original, `FUN_004283A0`:

1. exige `user+0x146C == 0x34` (ASCII `'4'`) ou desconecta com `0xB9`;
2. obtém o IPv4 da sessão por `FUN_0040ABE0`;
3. compara esse IPv4 com quatro entradas inicializadas por `inet_addr`;
4. desconecta com `0xBA` se nenhuma entrada casar; quando casa, retorna sem resposta.

Na build analisada, as quatro strings da allowlist original são `192.168.1.6`. A reconstrução usa
`GmOperationPolicy` e `[GM] AllowedIPs`, preserva os motivos `B9/BA` e também termina sem resposta
quando o IP é aceito. O antigo `VerifyTutorialStage` e `AllowedTutorialStages` foram removidos.

## Autorização corrigida no World

O comportamento vulnerável antigo era:

```text
channel.Special ? LobbyGm : Lobby
```

Agora `GmAuthorization` exige simultaneamente papel suficiente, feature flag ligada e permissão
tipada. A política da reconstrução é `0=Player`, `1=Moderator`, `2=GameMaster`,
`3=Administrator`: Moderator lê variáveis/entries, GameMaster também escreve variáveis e
Administrator também fecha o servidor e troca hashes. `Op_EnterChannel` calcula o status visual com
essa política; `0x03/0x08/0x09/0x0A/0x0B` revalidam a permissão na execução. Com a flag desligada,
nenhum papel executa comandos.

Essa numeração é uma política explícita do OpenRakion. O World original autoriza por estado interno
`LobbyGm`; não foi encontrada uma comparação que prove o significado histórico dos valores da coluna
`user.Authority`. Portanto ela não deve ser apresentada como enumeração original. PC Bang permanece
separado de authority e é tratado no domínio próprio.

## Painel Admin

O painel altera senha, ban, gold, cash, itens, PU, configs e manifesto do launcher. Senha e connection string foram
removidas do `appsettings.json`: `Admin__Password` (mínimo 16 caracteres) e
`ConnectionStrings__Rakion` são obrigatórias, com falha fechada na ausência. O bind padrão é
`127.0.0.1:8080`, `AllowedHosts` restringe localhost, login/logout usam antiforgery, a senha é
comparada em tempo constante, cookies são HttpOnly/SameSite Strict e o login limita cinco tentativas
por minuto.

`Admin__Role` aceita `Viewer`, `Operator` ou `Owner`. A mesma fonte cria a claim do cookie e autoriza
o serviço de banco; esconder botão não é a barreira de segurança. Operator pode alterar segurança de
conta, economia e inventário. Owner também cria contas, altera configuração e publica updates.
Viewer é somente leitura. Bind não-loopback em HTTP é recusado; HTTPS externo depende do certificado
Kestrel ou de um reverse proxy TLS configurado pelo operador.

As tabelas `admin_audit` e `admin_audit_head` formam um ledger transacional. Cada mutação registra
operador, papel, ação, alvo, motivo, metadados sem segredo, hashes do estado antes/depois, hash anterior
e hash da entrada. A cabeça é bloqueada com `FOR UPDATE`, evitando bifurcação em ações concorrentes.
Senha não entra em details nem em snapshots. Gold/cash mantém também o ledger específico com delta e
saldo. A conta do banco usada pelo painel deve receber `INSERT/SELECT` no ledger e não deve receber
`UPDATE/DELETE` em `admin_audit` em produção.

Antes de qualquer bind externo:

- mover segredos para environment/secret store e rotacionar os atuais;
- usar autenticação com hash forte, sessão segura, CSRF e rate limit;
- restringir rede/host/TLS;
- configurar `Admin__User` e o menor `Admin__Role` necessário;
- exigir reason/ticket para ban/economia;
- evitar exibir hashes/senhas e dados sensíveis em log.

## Modelo recomendado

```text
Principal(AccountId, Roles, Permissions)
AdminCommand(Id, Actor, Type, Target, Parameters, Reason, RequestedAt)
AdminAudit(CommandId, BeforeHash, AfterHash, Result, SourceIp)
```

Handlers/adapters chamam o mesmo serviço de comandos usado pelo painel; regra e autorização ficam no
backend. Operações críticas podem exigir confirmação/dual control.

## Implementação e ativação

1. manter promoção por canal removida e `Authority` autenticada na sessão;
2. manter GM separado de PC Bang;
3. usar a matriz tipada sem autorizar pelo status de lobby;
4. manter `0x03/0x08..0x0B` sob RBAC e `0x64` sob allowlist vazia por padrão;
5. tipar/versionar GM vars;
6. manter segredos externos, RBAC no backend e ledger encadeado; para bind externo, configurar TLS;
7. implementar ban/kick persistentes por command service próprio; manter `0x04` como eco legado e
   `0x05` como notice compatível, sem atribuir persistência que o original não possui;
8. testar revogação, sessão ativa e ações concorrentes.

```ini
[GM]
Enabled=0
AllowedIPs=
[Admin]
User=admin
Role=Owner
Url=http://127.0.0.1:8080
```

```powershell
$env:Admin__Password = '<segredo-com-16-ou-mais-caracteres>'
$env:Admin__User = 'operador-local'
$env:Admin__Role = 'Owner'
$env:ConnectionStrings__Rakion = '<connection-string>'
```

## Testes mínimos

- usuário normal em canal normal/special nunca vira GM;
- cada permission permitida/negada e revogação durante sessão;
- close concorrente, retry e recuperação do servidor;
- limites/tipos de GM vars e MD5 inválido;
- `0x64` recusa substatus incorreto com `B9`, IP negado com `BA` e não responde ao IP permitido;
- login Admin, matriz Viewer/Operator/Owner, CSRF, rate, cookie/TLS e secret ausente;
- ban persistente/economia com actor/reason/before/after;
- `0x04` ecoa sem mutação; `0x05` respeita escopo, estado, nome exato, broadcast e ack;
- auditoria não alterável e sem senha/token.

## Critério de conclusão

Os contratos `0x04`, `0x05` e `0x64` estão fechados para esta build. GM/Admin está funcionalmente
fechado no headless: privilégios vêm da identidade, cada comando tem permission tipada, segredos não
estão versionados, bind externo sem TLS falha e mutações são auditadas. MFA/SSO e provisionamento de
certificado são integrações de operação, não contratos do cliente Rakion. A semântica histórica da
coluna `Authority` continua classificada corretamente como não provada.

## Evidência executada em 2026-07-15

- `GmAuthorizationTests` cobre canal normal/special, authority ausente/presente e flag ligada/desligada;
- 358/358 testes .NET e build sem warnings;
- configuração de deploy mantém `[GM] Enabled=0` e `AllowedIPs=` vazio;
- handlers de close, vars, query e MD5 não consultam mais `LobbyGm` como autorização.
- Admin falhou fechado sem secrets e iniciou com variáveis externas; login com antiforgery foi
  validado em `127.0.0.1:8080`.
- smoke isolado iniciou o Admin, criou `admin_audit`, `admin_audit_head` e
  `admin_currency_adjustment`; um ban transacional validou ator/papel/ação/motivo, diferença dos
  hashes antes/depois, raiz e avanço da cabeça. A base temporária foi removida.
- testes cobrem matriz World 1/2/3, matriz Admin Viewer/Operator/Owner, autorização antes de I/O,
  motivo obrigatório e recusa de HTTP externo.
- probes vivos confirmaram `DISC B9` para substatus incorreto, `DISC BA` para IP negado e ausência de
  resposta/desconexão para `127.0.0.1` temporariamente permitido; a configuração foi restaurada.
