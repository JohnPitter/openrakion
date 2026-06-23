# Assets do RakionLauncher

Assets de UI do launcher (domínio público — o jogo foi descontinuado). Ficam **versionados** aqui:

| Arquivo | Origem |
|---|---|
| `app.ico` | Ícone do launcher. Extraído do `rakion.exe` em 4 tamanhos (256/48/32/16) via `PrivateExtractIcons`, montados num `.ico` multi-resolução. |
| `rakion_banner.png` | Banner — arte do dragão recortada do `front_img.bmp` do cliente. |
| `rakion_card.bmp` | Cópia do `selectgame_rakionimg.bmp` do cliente. |

O `RakionLauncher.csproj` embute por glob (`Assets\*.ico;*.png;*.bmp`). O build **tolera a ausência** (a UI
degrada sem ícone/banner), mas normalmente eles estão aqui.
