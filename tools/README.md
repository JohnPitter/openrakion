# Ferramentas

## `xfs_repack.py`

Leitor e **repacker** do formato XFS2 (Xenesis2) da SoftNyx, em Python puro. Permite editar arquivos dentro de um `.xfs` (ex.: `DataSetup.xfs`) e reempacotar de forma confiável — sem o `iXFS` (que tem bugs no reempacote). Round-trip validado: reconstrói o arquivo com todos os blocos idênticos.

Detalhes do formato em [../docs/config-xfs.md](../docs/config-xfs.md).

### Uso

```bash
# Teste de round-trip (reconstrucao identica) - ajuste o caminho do .xfs no script
python xfs_repack.py roundtrip

# Renomear o servidor (1o WorldServerName no locale.ini)
python xfs_repack.py rename "Meu Servidor" 0 saida.xfs
#   "Meu Servidor" = novo nome | 0 = checksum (o jogo nao valida) | saida.xfs = output
```

> Edite o caminho do `DataSetup.xfs` de origem dentro do script (`src=...`). Faça backup antes de substituir o arquivo do cliente.

## `xfs_read.py`

Lista os arquivos contidos em um `.xfs` (nome, offset, tamanhos).

```bash
python xfs_read.py DataSetup.xfs            # lista
python xfs_read.py DataSetup.xfs locale.ini # tenta extrair (single-block)
```

## Outras (de terceiros, não inclusas)

- **GConfig** (gerador de `config.xfs`) e **Md5Check** — do [RakionLauncher do CarlosX](https://github.com/CarlosX/RakionLauncher) (`compiled/`).
- **iXFS** (editor GUI de XFS) — por *jdastridge*. Útil para inspeção.
