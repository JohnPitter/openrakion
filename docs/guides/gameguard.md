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

No cliente v258 pristine, a inicialização ainda referencia `http://218.145.66.176:10200`. Os
patches golden tornam a falha não fatal, mas uma tentativa TCP residual pode aguardar timeout e
manter a tela preta. `RakionClientPatch.dll` substitui essa URL em memória por um endpoint loopback
fechado antes do entry point, fazendo a falha ocorrer imediatamente; o restante das conexões
continua seguindo `server.host`.

## Alternativas para jogar

1. **Fluxo do launcher** — em alguns builds o `rakion.bin` lançado pelo `NyxLauncher` conecta ao broker/world **mesmo com o GameGuard falhando de forma não-fatal**. Foi o caminho que funcionou nos nossos testes.
2. **DLL de compatibilidade v258 (método atual)** — o proxy `version.dll` reproduz em memória o
   diff no-GG do `rakion-final`; não é necessário alterar manualmente o executável instalado. Veja
   [`client-compatibility-dll.md`](client-compatibility-dll.md). O patch manual abaixo permanece
   apenas como evidência histórica do RE.

O gate histórico fica na função de inicialização do GG, na chamada que lança o GameMon: substituir
a `call` por `xor eax,eax; add esp,4` produz resultado de sucesso com a pilha balanceada. Esses
bytes não devem ser aplicados manualmente no fluxo atual.

> Nota: patches no client mudam o MD5/sha1; se o servidor (`file.php`) impuser a checagem, atualize os hashes esperados. No build v258 testado, a checagem não era imposta.
