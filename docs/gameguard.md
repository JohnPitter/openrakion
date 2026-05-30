# GameGuard — análise e veredito

## TL;DR

O GameGuard original do Rakion (**nProtect / INCA, build de 2007**) **não inicializa mais**. Ele falha com **"Game guard error : 0"** porque o `GameMon` tenta contatar o servidor de update/auth do nProtect, que está **fora do ar** há anos. **Não é** problema de Windows 11, de driver de kernel, nem de assinatura — e por isso **uma VM Windows 7/10 não resolve**.

## Evidência

Capturando a rede do processo durante a inicialização do GameMon:

```
TCP  10.0.2.15:49708  ->  61.78.35.29:6060   SYN_SENT   (GameMon.des)
```

- `61.78.35.29:6060` é o servidor de atualização/autenticação do nProtect (faixa coreana da SoftNyx/INCA). Fica em **`SYN_SENT`** (sem resposta) → o GG aborta com erro 0.
- O log de evento de **Code Integrity** do Windows **não** mostra bloqueio de driver. O serviço `npggNT` **nem chega a ser instalado** — a falha é **antes** da fase de driver.
- O comportamento é **idêntico** em Windows 11 e numa VM Windows 10 com `testsigning on` → confirma que o sistema operacional não é o fator.

## Conclusão

Para "fazer o GameGuard funcionar" seria necessário **emular o protocolo do servidor nProtect** (proprietário, não documentado) — inviável na prática.

## Alternativas para jogar

1. **Fluxo do launcher** — em alguns builds o `rakion.bin` lançado pelo `NyxLauncher` conecta ao broker/world **mesmo com o GameGuard falhando de forma não-fatal**. Foi o caminho que funcionou nos nossos testes.
2. **Patch no-GG no client** — neutralizar o lançamento do GameMon no `rakion.bin` (engenharia reversa). O "gate" fica na função de init do GG, na chamada que lança o GameMon: substituir a `call` por `xor eax,eax; add esp,4` (resultado = 0/sucesso, pilha balanceada) faz o jogo rodar sem GameGuard. Lado servidor, pode ser necessário neutralizar a checagem de GG (auth) do world.

> Nota: patches no client mudam o MD5/sha1; se o servidor (`file.php`) impuser a checagem, atualize os hashes esperados. No build v258 testado, a checagem não era imposta.
