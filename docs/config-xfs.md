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

## Patch de mensagem (language.txt) — ex.: compra do Power User

As mensagens de UI ficam em `datasetup\language.txt` dentro do `DataSetup.xfs` (formato `id\ttexto`, CRLF).
A compra do Power User (`0x34`) NÃO tem como mostrar o popup de sucesso *limpo*: o frame de sucesso real
(`0x17`, canal **field**) dependia do cash-server online (offline) e é irreplicável. O servidor .NET
CONCEDE o PU na compra (debita cash + soma `powerlevelpoint`, ver `HandleBuyPowerUser`), mas o cliente
sempre exibe a mensagem do status. Solução: **trocar a string de erro por uma de sucesso**. A msg `641`
("You can not purchase Power User for 6 months in advance", exibida no **status 2**) foi patchada p/
"Power User purchased! Relog to see your bonus points." — e o handler manda status 2.

Repack (round-trip valida 87/88; só `language.txt` muda):
```python
import zlib, struct; import xfs_repack as X
OLD='You can not purchase Power User for 6 months in advance'
NEW='Power User purchased! Relog to see your bonus points.'
d,ver,cnt,val,tail,files=X.parse('DataSetup.xfs')
for f in files:
    if f[0].split(b'\x00')[0].lower().endswith(b'language.txt'):
        txt=zlib.decompress(d[f[1]+8:f[1]+f[4]]).decode('latin-1').replace(OLD,NEW,1)
        np=txt.encode('latin-1'); zc=zlib.compress(np,6)
        block=struct.pack('<H',len(np)&0xffff)+b'\x80'+len(zc).to_bytes(3,'little')+b'\x00\x00'+zc
        f[3]=len(np); f[4]=len(block); f.append(block); break
open('DataSetup.xfs','wb').write(X.build(d,ver,val,tail,files))
```
Copie por cima do `client/DataSetup.xfs` (backup antes) e reinicie o cliente p/ recarregar o XFS.
