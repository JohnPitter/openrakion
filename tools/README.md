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

## Sondas de protocolo (servidor)

Clients headless que falam o protocolo do World direto (conectam, cifram/decifram o AES, validam pacotes) — úteis para testar o servidor sem abrir o jogo.

- **`worldprobe.py`** — login + lobby + inventário + loja: valida que o World responde corretamente (conta carrega do banco, box e ouro batem).
- **`listprobe.py`** — sonda a lista de canais/salas.
- **`difftest.py`** — **teste diferencial**: dirige um World (nativo OU .NET) pela mesma sequência e compara as respostas. `python difftest.py <porta>` (ex.: `40708`).

## Captura do servidor ORIGINAL (debug de compatibilidade)

Quando o cliente offline diverge do nosso .NET, o jeito mais rápido de descobrir o comportamento correto é **rodar o servidor original** (binários do autor) com captura total e observar o que ele faz. Três scripts PowerShell automatizam o ciclo:

- **`orig_capture.ps1`** — para o stack .NET, sobe o original (`rakion-cap`, imagem `openrakion-server:latest`) e liga **toda a captura**: `general_log` do MariaDB (cada query SQL — de qual tabela/userid/condição o server lê), MITM `41708→40708` (frames W↔C **decifrados** em `C:\temp\mitm.log`) e `docker logs`. `-NoMitm` pula o proxy.
- **`orig_diag.ps1`** — lê o estado do DB do original (char/itens/gold/ranks, com as semânticas de slot anotadas), as **queries capturadas** (general_log) e o stdout do server.
- **`orig_restore.ps1`** — tira o original + MITM e religa o stack .NET.

Apoio: `mitm_cap.py` (proxy AES-128-ECB) e `GameServers_cap.ini` (config do original apontando o World pro MITM).

```powershell
.\orig_capture.ps1     # sobe o original com captura; depois logue o cliente (test/test)
.\orig_diag.ps1        # estado do DB + queries que o server rodou
.\orig_restore.ps1     # volta pro stack .NET
```

> **Lição de ouro:** o `general_log` (`SET GLOBAL general_log=1`) destravou os bugs de inventário/quickslot — mostra de qual tabela e com qual filtro o server lê cada coisa (ex.: o quickslot vem de `useriteminfo` slots 13/14/15 no login → `0x0c@149`). Histórico na memória `cliente-crash-inventario-e-gameguard`.

## Outras (de terceiros, não inclusas)

- **GConfig** (gerador de `config.xfs`) e **Md5Check** — do [RakionLauncher do CarlosX](https://github.com/CarlosX/RakionLauncher) (`compiled/`).
- **iXFS** (editor GUI de XFS) — por *jdastridge*. Útil para inspeção.
