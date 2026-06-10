# Rakion World Reconstruction Progress

## Objetivo

- Independencia total do world original.
- O original deve ser usado apenas como golden source/oraculo de captura e comparacao, nao como runtime/proxy permanente.

## Estado operacional atual

- Docker/MariaDB usado pelo stack local: schema `rakion`, 62 tabelas.
- World .NET publicado em `C:\temp\rakpub\world`.
- Logs principais:
  - World: `C:\temp\rakpub\w.log`
  - World stderr: `C:\temp\rakpub\w.err.log`
  - Broker: `C:\temp\rakpub\broker_console.log`
- World atual apos hotfix: processo `RakionWorldServer`, porta TCP `40708`, UDP gameplay `40709`.

## Ferramentas adicionadas

- `tools/RakionServer.OracleDiff`
  - Comandos: `--self-test`, `summarize`, `compare`.
  - Proposito: comparar capturas/oraculos do original com comportamento do .NET sem loop de chute em runtime.

## Correcoes confirmadas

- Cleanup de sessao/field:
  - `WorldServer.RemoveSessionAsync` chama `LeaveField(s)` antes de remover a sessao.
  - Evita field/slot preso e loop em socket disposed.
- Timer/HUD do mapa:
  - `Field.DefaultRoundDurationSec = 432`.
  - Com `+3s` de countdown, o primeiro `0x48` fica alinhado com captura funcional do original (`0x01B3`).
  - Usuario confirmou: o tempo passou a vir zerado/aceito pelo cliente.
- Estado de arma separado:
  - `PlayerRec.WeaponState` criado para separar arma atual de `PlayerRec.State`.
  - `State` continua podendo ficar `4` durante gameplay, sem quebrar a troca de arma TCP `0x3D`.

## Descobertas descartadas

- Nao tratar UDP `0040` com `pkt[4] == 0x07` como troca de arma.
  - Isso foi testado e causou regressao: ao iniciar o jogo apareceu cerca de `250 hits`.
  - Conclusao: `0x07` tambem participa de estado/acao/hit e nao identifica weapon swap sozinho.
- Nao enviar `1583 ... state=03` genericamente como resposta a `0040`.
  - Esse estado altera diretamente a maquina de acao do cliente.
  - O hotfix removeu esse comportamento e voltou `0040` para `DefaultGameplayState`.

## Estado atual do bug

- Bug novo dos `250 hits` foi revertido por hotfix conservador.
- Troca de armas ainda nao esta resolvida.
- A solucao correta deve vir de decode offline/golden source, diferenciando a troca real de arma da trilha generica `0040`.

## Evidencias importantes dos logs

- Durante a falha de troca/acoes, o cliente manda rajadas UDP `0040` de 11 bytes.
- Formato observado:
  - `00 40 [counter] [grupo] [marker] 00 00 [echoSeq] 00 00 00`
  - Exemplo com `marker=07`: `004087760700000B000000`
  - Exemplo da regressao com `marker=03`: `0040C1620300007F000000`
- O mapeamento `marker -> state` nao pode ser direto sem contexto temporal/acao.

## Golden sources relevantes

- `C:\Users\joaop\Desenvolvimento\rakion-work\capture_field_entry\mitm_move_133859.log`
- `C:\Users\joaop\Desenvolvimento\rakion-work\capture_gameplay_completo\protocolo_tcp_decifrado.log`
- `C:\Users\joaop\Desenvolvimento\rakion-work\capture_gameplay_completo\gameplay.txt`

## Comandos de validacao obrigatorios apos alteracao

```powershell
& 'C:\Users\joaop\.dotnet\dotnet.exe' clean RakionServer.sln
& 'C:\Users\joaop\.dotnet\dotnet.exe' build RakionServer.sln -warnaserror
```

## Proximo caminho recomendado

- Parar de inferir weapon swap via `0040` isolado.
- Fazer decode offline comparando uma captura do original contendo:
  - entrada no mapa,
  - movimento normal,
  - troca de arma Q ida/volta,
  - ataque sem alvo,
  - hit real,
  - saida/fechamento.
- Implementar a maquina de estados de gameplay no backend com base nessa diferenca, usando o original como oraculo.
