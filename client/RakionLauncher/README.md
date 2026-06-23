# RakionLauncher

Launcher .NET (WinForms, `net9.0-windows`, x64) do cliente Rakion offline — substitui o
**NyxLauncher/load.bin** original. Faz login, game options (tela/mouse/som) e PLAY lançando o
`rakion.exe` direto. Visual no estilo do Softnyx Game Launcher, só com o Rakion.

## Modo janela (a peça-chave)

A Serious Engine só guarda um bit de fullscreen (`m_bActiveFullScreen`); "modo janela" de verdade são
três coisas (ver [`WindowMode.cs`](WindowMode.cs)):

1. **Patch de 1 byte** no `rakion.exe` (sem ASLR, ImageBase `0x400000`): em `0x40D46D` há
   `85C0`(TEST EAX,EAX) `7452`(JZ) `FF15…`(CALL = setup de fullscreen + troca de resolução). Trocar o
   `0x74` (JZ) por `0xEB` (JMP) força o salto e **pula** o CALL → o engine roda windowed sem trocar a
   resolução do desktop. Aplicado com o jogo lançado **suspenso** (`GameLauncher.LaunchSuspended` →
   `WindowMode.PatchWindowedMode` → `Resume`), antes de o engine inicializar o display. É o mesmo patch
   que o "Window Mode" do NyxLauncher fazia (descoberto por RE do `load.bin`/`RakionLauncher.Loader`).
2. **Reformatar a janela** "Rakion" via Win32 (título + centralizar/preencher), re-achando a janela a
   cada recriação do engine (troca de cena login → char select → sala) e **tirando o título enquanto
   minimizada** — senão a engine encolhe o backbuffer pela borda a cada restore (faixa preta crescente).
3. **Destravar o Alt+Tab** com um patch em memória no `keyhook.dll` (`WindowMode.PatchKeyHook`).

## Build / publish

```sh
dotnet build -c Release
# publish self-contained single-file (o jogador não precisa instalar o .NET):
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Roda elevado (o `rakion.exe` exige admin; `CreateProcess` sem elevar = erro 740) — ver `app.manifest`.

## Assets

Os assets de UI (`Assets/*.ico|png|bmp`) são **derivados do cliente** (proprietários) e **não são
versionados** — ver [`Assets/README.md`](Assets/README.md). O launcher **compila e roda sem eles** (a UI
degrada sem ícone/banner); o `.csproj` os embute por glob quando presentes.
