# config.xfs e o formato XFS2

## Por que o `config.xfs` importa

Sem um `config.xfs` válido, o cliente trava em **"Config.xfs File not found or changed"**. Esse arquivo informa ao cliente a **URL do backend web de autenticação**. Ele é lido pela `PMReaderLib.dll` — então **ambos** precisam estar em `client/Bin/`.

## Como gerar

Use o **GConfig** (do projeto RakionLauncher do CarlosX):

1. Rode `GConfig.exe`.
2. No campo **URL**, coloque o host do seu web server **sem `http://`** (ex.: `192.168.1.5`).
3. Clique **Generar** → gera o `config.xfs`.
4. Copie o `config.xfs` para `client/Bin/` (e na raiz do cliente, por garantia).

> Um `config.xfs` de exemplo de outra pessoa **não serve** — o jogo o rejeita como *"changed"*. Gere o seu.

## O formato XFS2 (para ferramentas próprias)

Os arquivos `.xfs` da SoftNyx usam o container **XFS2 ("Xenesis2 file system")**. **Não é criptografado** — cada arquivo é só zlib com um cabeçalho. Layout:

```
[i32 start_offset]                     # offset da seção de cabeçalho
[ blocos de dados ... ]                # um por arquivo
[1 byte zsize][zlib(head)]             # head = "XFS2" + i32 version + i32 count
                                       #        + i32 validation + i32 offset2(=start_offset)
                                       #        + "The Xenesis2 file system version 2.0.0\0\0"
[3 bytes info_size][zlib(filetable)]   # filetable: por arquivo →
                                       #   name[112] + i32 foff + i32 comp + i32 uc + i32 cs
```

Cada **bloco de arquivo** (single-chunk, < 64KB) é:

```
[u16 UCSize][0x80][u24 zlen][u16 cksum] + zlib(conteúdo)
```

- `UCSize` = tamanho descomprimido; `zlen` = tamanho do stream zlib; `cs` (na filetable) = 8 + zlen.
- `cksum` é um checksum de 2 bytes que **o jogo não valida** (pode ser `0`).
- Arquivos grandes (ex.: `creatures.dat`, `items.dat`) são **multi-chunk** — não mexa neles; copie byte-a-byte.

## Editar com `tools/xfs_repack.py`

O `iXFS` (editor GUI da comunidade) é útil para inspecionar, mas tem bugs ao reempacotar (o prefixo `datasetup\` quebra o export). O `xfs_repack.py` faz o repack de forma confiável em Python puro (round-trip validado em 88/88 arquivos).

Exemplo — renomear o servidor no lobby (1º `WorldServerName` no `locale.ini`):

```bash
python tools/xfs_repack.py rename "Meu Servidor" 0 DataSetup_novo.xfs
# 1º arg após 'rename' = novo nome; '0' = checksum; último = arquivo de saída
```

Depois copie `DataSetup_novo.xfs` por cima do `client/DataSetup.xfs` (faça backup antes).
