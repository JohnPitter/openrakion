# Investigação: stuttering da câmera (Rakion / Serious Engine 1)

Status: **não resolvido por configuração**. Causa isolada ao **timer/loop interno da engine** (SE1 em
hardware moderno). Fix real = patch binário na `engine.dll` — deferido como tarefa de RE dedicada.

## Sintoma

Ao **girar a câmera** (mouse esquerda/direita) há micro-travadas (judder), mesmo com o FPS alto. O
movimento em si e a animação do bot estão bons; o problema é a fluidez do giro da view.

## Ambiente

- GPU **NVIDIA RTX 4070**; monitor **2560×1440 @ 240 Hz**, **FreeSync** (não G-Sync).
- Rakion roda sobre a **Serious Engine 1** (client-authoritative P2P; o **jogador local** é simulado no
  cliente, não vem da rede).
- **Tick de simulação = 20 Hz** (`CTimer::TickQuantum = 1/20`, constante da engine em `Timer.cpp`); o
  render interpola entre ticks por um *lerp factor*.

## O que foi testado (e NÃO resolveu)

Todos os ajustes de vídeo ficam em `rakion-final/Scripts/PersistentSymbols.ini` (+ o arquivo
`rakion-final/display.mode` que o launcher usa pra lembrar windowed/borderless/fullscreen).

| Tentativa | CVAR / ação | Resultado |
|---|---|---|
| Subir o FPS | `cli_iFPSLimit` 30 → 60 → 240 → 0 | FPS subiu; **treme igual** |
| Ligar VSync | `gap_iSwapInterval` 0 → 1 | sem efeito no judder |
| Borderless nativo | `display.mode=borderless`, 2560×1440 | sem efeito |
| Fullscreen exclusivo | `m_bActiveFullScreen=1`, `display.mode=fullscreen` | sem efeito |
| Refresh do fullscreen | `gap_iRefreshRate` 0 → 240 (0 caía p/ 60Hz no exclusivo) | corrigiu o refresh; **treme igual** |
| Resolução nativa | `m_pixScreenWidth/Height` = 2560×1440 (via launcher) | pegou; **treme igual** |
| Soltar o cap | `cli_iFPSLimit=0` (tira o `Sleep()` do limitador) + VSync pace | **treme igual** |

**Conclusão dos testes:** o judder **sobrevive a toda mudança de display** (janela, borderless,
fullscreen exclusivo, refresh 240, resolução nativa, VSync on/off, cap on/off). Logo **não é**
display / compositor (DWM) / VSync / refresh / resolução.

## Descobertas (por que não é o que parecia)

- **Não é DWM/modo janela:** o fullscreen exclusivo contorna o compositor e mesmo assim treme.
- **Não é rede / interpolação de peer:** o jogador local é **client-authoritative** → a rotação da view
  é **local**, não interpolada da rede. Logo `cli_bLerpActions` (default `FALSE`; é lerp de ação de rede)
  **não afeta** o giro da câmera local.
- **Timer da SE1 é RDTSC:** `ReadTSC()` (instrução `rdtsc`) + calibração única de MHz no boot (`Timer.cpp`).
  Em CPU moderna o **TSC é invariante** (roda na frequência nominal, imune a turbo/throttle) → a
  miscalibração clássica da SE1 é **amplamente mitigada** hoje; não deve ser o gargalo, mas não foi
  descartado por medição.
- **Limitador de FPS por `Sleep()`:** a SE1 capa por `Sleep`, cuja granularidade grossa (~1–15 ms) injeta
  jitter de frame-time. Soltar o cap (`=0`) + VSync **não** resolveu → o jitter não vinha (só) daí.

## Gotcha da persistência do `.ini`

A engine **reescreve** o `PersistentSymbols.ini` na saída (dump "automatically saved persistent symbols"):
- CVARs `persistent extern user ...` (ex.: `cli_iFPSLimit`, `gap_iSwapInterval`, `gap_iRefreshRate`)
  **são salvos** → edições persistem.
- CVARs `persistent extern INDEX ...` **sem `user`** (ex.: `m_pixScreenWidth/Height`) são **revalidados/
  resetados** pela engine no setup de modo → editar solto pode reverter; setar **pelo launcher** (GameSettings)
  ou junto do modo pega.
- O launcher trava o `.ini` **read-only** de propósito (senão a engine reescreve o modo de display na saída) —
  ver [[fps-cap-cli-ifpslimit]] e `client/RakionLauncher/GameSettings.cs`.

## Config de vídeo atual (deixada estável)

`cli_iFPSLimit=0` (sem cap), `gap_iSwapInterval=1` (VSync), `gap_iRefreshRate=240`,
`m_bActiveFullScreen=1` + `display.mode=fullscreen`, `m_pixScreenWidth/Height=2560×1440`.
(FPS a 240 já é ótimo pro usuário; o que resta é o judder do giro.)

## Causa provável e o fix real (deferido)

Sobrando display, o judder é **interno da engine**: ou o **pacing do loop de render** (frame-time
irregular) ou a **interpolação da view local** não render-rate. O caminho de fix é **patch na `engine.dll`**:

1. **Trocar o timer para `QueryPerformanceCounter`** no lugar de `RDTSC`+MHz-calibrado (é o fix consagrado
   dos forks da comunidade SE1 — *SeriousSamClassic revolution* etc. — pra exatamente este stutter).
2. **Medir os frame-times reais** (PresentMon) pra separar *constante* (timer/pacing) de *periódico*
   (GC/rede/streaming) — pendente: caracterizar se o tremor é contínuo ou soluço a cada ~1s.
3. Testar **afinidade de CPU** (1 core) — mitigação clássica da SE1, hoje menos relevante (TSC invariante).
4. Isolar **rede**: reproduzir SEM bot e no MENU (sem gameplay) — se sumir sem gameplay, é hitch de
   rede/tick do **nosso servidor**, não render.

## Próximo passo

Deferido. Retomar como capítulo próprio de RE quando o bot (objetivo principal) estiver fechado.
Ver [[fps-cap-cli-ifpslimit]], [[re-serious-engine-rakion]], [[launcher-dotnet-modo-janela]].
