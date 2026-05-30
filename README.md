# OpenRakion

Um guia e conjunto de ferramentas **open source** para colocar de pé um servidor privado de **Rakion** (SoftNyx, versão XfsVer258) e conseguir **logar e jogar** de verdade — incluindo a peça que a comunidade ficava travada há anos: o **login completo** (o famoso *"stuck at login"*).

> ⚠️ **Importante / Legal:** Este repositório **NÃO contém** os arquivos proprietários do jogo Rakion (cliente, engine, `DataSetup.xfs`, GameGuard, executáveis de servidor). Esses arquivos são **copyright da SoftNyx** e você deve obtê-los a partir de uma cópia legítima do jogo. Aqui estão apenas: a documentação/receita, scripts de servidor (Docker), o backend web de autenticação (open source), ferramentas e configs. Veja [NOTICE.md](NOTICE.md).

---

## O que este projeto resolve

Servidores privados de Rakion existem há mais de uma década, mas quase todos paravam no mesmo ponto: o cliente **conectava no broker** mas **não completava o login** ("stuck at login"). Este projeto documenta, com evidências, a cadeia completa que **fecha o login**:

1. **Servidor** (broker + world + auth web + MariaDB) rodando em Docker.
2. **`config.xfs` gerado** pelo `GConfig` apontando para a SUA URL web — sem isso o cliente trava em *"Config.xfs File not found or changed"*.
3. **Fluxo real do launcher** (`NyxLauncher`): login web → token de sessão → lançamento do jogo → broker → world.
4. **Advertised IP** correto (o broker anuncia o IP do world via `GameServers.ini`).

Resultado comprovado: conta loga, world aceita a conexão, banco carrega personagem/inventário. 🎉

## Sobre o GameGuard (veredito honesto)

O GameGuard original (nProtect, 2007) **não inicializa mais** — o GameMon faz uma requisição para o servidor de update/auth do nProtect (`61.78.35.29:6060`), que está **morto** há anos, e falha com *"Game guard error : 0"*. Isso **não é** problema de Windows 11 nem de driver; uma VM Windows 7/10 **não resolve**. As únicas saídas são:

- Jogar pelo fluxo do launcher (em alguns builds o `rakion.bin` conecta mesmo com o GG falhando de forma não-fatal), ou
- Aplicar um patch *no-GG* no client.

Detalhes em [docs/gameguard.md](docs/gameguard.md).

---

## Estrutura do repositório

```
openrakion/
├── docs/            Guia completo: setup, GameGuard, config.xfs, rede, formato XFS
├── server/          Setup Docker do servidor + templates de config (sem binários)
├── web/             Backend de autenticação em PHP (base CarlosX, GPL)
├── tools/           Ferramentas (repacker XFS em Python, etc.)
├── database/        Schema do banco
├── CREDITS.md       Créditos a todos que contribuíram
└── NOTICE.md        Aviso legal sobre arquivos proprietários
```

## Começando

1. Leia o [guia de setup](docs/setup-guide.md).
2. Obtenha os arquivos originais do Rakion (cliente + binários de servidor) de uma cópia legítima.
3. Suba o servidor (Docker), configure o web e o banco.
4. Gere o `config.xfs` para a sua URL (veja [docs/config-xfs.md](docs/config-xfs.md)).
5. Monte o cliente e logue pelo launcher.

## Ferramentas inclusas

- **`tools/xfs_repack.py`** — leitor/repacker do formato **XFS2** da SoftNyx (em Python puro). Permite editar arquivos dentro de `DataSetup.xfs` (ex.: renomear o servidor no `locale.ini`) de forma confiável, sem o iXFS. Round-trip validado.
- **`tools/xfs_read.py`** — parser/listagem de arquivos XFS.

## Créditos

Este projeto se apoia no trabalho de muita gente da comunidade. Veja **[CREDITS.md](CREDITS.md)** — em especial **CarlosX** (RakionLauncher / BrokenServer / web auth), **SirMaster**, **jdastridge** (iXFS) e a comunidade do **RaGEZONE**.

## Licença

Componentes de terceiros mantêm suas licenças originais (o backend web e o emulador de servidor derivam de projetos **GPL-3.0**). O material original deste repositório (documentação e ferramentas próprias) é liberado sob **GPL-3.0**. Veja [LICENSE](LICENSE) e [NOTICE.md](NOTICE.md).
