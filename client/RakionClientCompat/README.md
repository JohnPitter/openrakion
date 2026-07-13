# RakionClientCompat

Proxy x86 completo de `version.dll` que carrega pelo fluxo normal do cliente e instala a correção visual
de HIT/SHOT depois que `entitiesmp.dll` estiver pronta. As 17 exportações são encaminhadas para uma cópia
da DLL oficial de 32 bits do Windows, produzida localmente como `verorig.dll`.

O servidor continua sendo a autoridade sobre dano, HP e morte. Esta DLL altera somente o contador visual
local que o cliente original não atualiza para o bot sintético.
