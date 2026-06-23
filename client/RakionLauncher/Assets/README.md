# Assets do RakionLauncher

Estes assets de UI são **derivados do cliente Rakion** (conteúdo proprietário da SoftNyx) e por isso
**não são versionados** (ver o `.gitignore` da raiz do repo). Sem eles o launcher **ainda compila e
roda** — só fica sem ícone/banner. Para a UI completa, gere-os a partir da sua cópia do cliente:

| Arquivo | Origem |
|---|---|
| `app.ico` | Ícone do launcher. Extraído do `Bin/rakion.exe` em 4 tamanhos (256/48/32/16) via `PrivateExtractIcons`, montados num `.ico` multi-resolução. |
| `rakion_banner.png` | Banner. Recorte da arte do dragão do `front_img.bmp` do cliente. |
| `rakion_card.bmp` | Cópia do `selectgame_rakionimg.bmp` do cliente. |

O `RakionLauncher.csproj` embute por glob (`Assets\*.ico;*.png;*.bmp`) — basta colocar os arquivos aqui.
