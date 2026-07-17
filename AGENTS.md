# OpenRakion

## Contexto e finalidade do projeto
Projeto pessoal de **preservação** e **interoperabilidade** de um jogo online
descontinuado (servidores oficiais fora do ar, sem código-fonte disponível).
Trabalho a partir de binários de propriedade do autor.

Objetivo: reconstruir, em .NET, um **servidor compatível** capaz de rodar um
cliente **offline**, de uso pessoal e único — sem afetar nenhum serviço de
terceiros, sem multiplayer público e sem prejudicar outros jogadores.

Toda análise de binário, depuração de crash, estudo de protocolo de rede e
ajuste de compatibilidade do cliente tem finalidade de **interoperabilidade**
e **research defensivo** sobre software do próprio autor. Ao descrever esse
trabalho, prefira vocabulário de compatibilidade ("estudo de protocolo",
"neutralizar componente para rodar offline", "compatibilidade do cliente") em
vez de termos de evasão/ataque. Não é trabalho ofensivo nem de circumvenção de
serviço vivo.

## Arquitetura
- Servidor em .NET (`server/RakionServer/`), vertical slices por domínio
  (World, etc.). Regra de negócio no backend, isolada de I/O e rede.
- Dados nas bordas trafegam por contratos explícitos (DTOs/pacotes), não por
  entidades cruas.

## Convenções
- Importe a classe/símbolo final, não o caminho completo.
- Logue fluxos críticos (login, transações, persistência, falhas de rede); **nunca**
  payload bruto nem credenciais em nível Info (use Debug e redija segredos). Não
  instrumente CRUD trivial nem caminhos felizes.
- Comentários de RE (`FUN_xxxx`, offsets `this+0x...`, captura/MITM) são documentação
  do protocolo — mantenha. AES-128-ECB é a cifra intencional do jogo — não "corrija".

## Code Quality Gates
Gates **medíveis**, não só alvos. Acima do limite = sinalize e proponha divisão/extração
ANTES de adicionar mais código. Auditoria viva e dívida priorizada em
[`docs/CODE_AUDIT.md`](docs/CODE_AUDIT.md).

**Tamanho**
- Função: alvo ~40 linhas; **sinalize >60** e extraia antes de crescer mais.
- Arquivo/classe: alvo ~400; **sinalize >600; não passe de ~800 sem plano de split**.
  Ao crescer um grupo de handlers, fatie por domínio em `partial class`
  (`WorldHandlers.<Domínio>.cs`) — a convenção já existe no repo.
- Parâmetros: ≤4; acima, agrupe num DTO/record.
- Aninhamento: ≤3 níveis; prefira early return.
- Dívida dos GOD-FILES **QUITADA** (2026-06-14): os monólitos foram fatiados em `partial class`
  por domínio — `WorldHandlers.Generated.cs` (2692→125 + 6 partials), `Broker/Systems.cs`
  (1797→270 + tipos aninhados em arquivos próprios), `ClientSession.cs` (1032→357 + LobbyFlow
  + LobbyFrames + Inventory). O domínio Field foi ainda fatiado em lifecycle (`.Field.cs` 459) × combate
  (`.FieldCombat.cs` 401).
- Dívida de tamanho **REMANESCENTE** (sinalizada, fatiar antes de 800): `WorldDatabase.cs` (702) e
  `WorldServer.cs` (~710, bootstrap + progressão/`GrantExp`/`ApplyStageResult` + fields) seguem **acima de 600** —
  não eram god-files do slice, são os núcleos DB e servidor. Depois vêm `WorldHandlers.ReconCombatB.cs` (515) e
  `WorldHandlers.Generated.Room.cs` (514).
- DOMÍNIO ≠ rede: regra de progressão/motor-de-partida (ex.: resultado de stage = exp/gold/rank) mora em
  serviço de domínio (`WorldServer.ApplyStageResult`/`GrantExp`), **não** no handler de rede nem em partial de
  outro domínio (ex.: o 0x53 NÃO mora em `ClientSession.Inventory.cs`; o handler só traduz bytes↔chamada).

**Golden source / sem código morto**
- UMA implementação por comportamento. Proibido manter versões paralelas (ex.: handler
  "antigo" + `_Recon` do mesmo opcode): a tabela de dispatch é a verdade; o que ela
  não chama, apague.
- Código morto se **remove**, não se comenta (o histórico vive no git). Sem flags de
  diagnóstico fixas em `false` nem ramos inalcançáveis.
- Constante/mapa de protocolo tem uma só fonte (sem `const` + literal divergentes).

**Implementação de raiz, não replay**
- Comportamento do servidor é **sintetizado do domínio** (estado → DTO → bytes), **não** replay de blob capturado.
  Frame de resposta é montado do estado — ex.: o `0x0C` do char-select é serializado do char list (formato wire na
  memória `0c-login-frame-format`), **não** overlay sobre `oracle_0c.bin`. Captura/MITM/oracle servem só de
  **research e golden test** (validar a síntese byte-a-byte), nunca como a implementação shippada.
- Replays legados **MIGRADOS** (2026-06-17): o `oracle_*.bin` (login 0x0C/0x0D) e TODOS os frames hex fixos da
  cadeia lobby→canal→sala→stage viraram síntese pura (`LoginCharListWriter` + `LobbyFrames`), com golden tests
  byte-a-byte contra a captura. **Não adicione novos** — a origem do dado é o domínio + constantes de protocolo
  nomeadas, nunca um dump. É um projeto open source sério.

**Domínio isolado de I/O**
- Regra de negócio (economia/loja, motor de partida, progressão/exp) mora em serviço de
  domínio — **não** em handler de rede, classe de socket/sessão, nem dentro do DB. O
  handler traduz bytes↔chamada e serializa a resposta.
- Dados nas bordas trafegam por DTOs/contratos explícitos, não por entidades cruas nem
  pelo objeto de sessão cru.

**Robustez / segurança**
- Parse de input externo valida limites SEMPRE (reader seguro por construção: bounds-check
  antes de cada leitura). Frame curto/forjado → erro tratado, nunca `IndexOutOfRange`.
- SQL só parametrizado (`@param`); nunca concatene input. Coluna dinâmica só de allowlist fixa.
- Fire-and-forget que mexe em saldo/estado persistido precisa de rollback em falha.
- Build sem warnings novos; nullable honrado; cast truncante (`(byte)`/`(short)`) só com
  faixa garantida.
