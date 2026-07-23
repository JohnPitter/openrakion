# BotHost headless multippeer

## Objetivo e fallback

O BotHost executa uma instância nativa sem renderização por field ativo e controla os bots daquele
field como jogadores locais do engine. O `BotManager` sintético continua disponível como fallback
quando não houver BotHost saudável. O World permanece responsável por roster, autorização, HP,
pontuação, morte e respawn; o engine headless fornece física, colisão, animações e eventos de arma.

O alvo inicial é uma instância por field, não uma instância por bot. Um único world/mapa é global
dentro do engine, portanto compartilhar a mesma instância entre fields diferentes ainda não está
comprovado e não faz parte do primeiro rollout.

## Evidência nativa da build v258

`engine.dll` exporta estes contratos:

| Símbolo | RVA | Evidência |
|---|---:|---|
| `_bDedicatedServer` | `0x002A0680` | flag global consultada na inicialização gráfica, sonora e de display |
| `CNetworkLibrary::AddPlayer_t` | `0x000F3EB0` | cria uma nova fonte local em um slot livre |
| `CNetworkLibrary::GetLocalPlayerCount` | `0x000EFE00` | expõe a quantidade de fontes locais |
| `CNetworkLibrary::GetLocalPlayer` | `0x000EFE30` | resolve a fonte local primária |
| `CNetworkLibrary::IsPlayerLocal` | `0x000F41C0` | verifica se uma entidade pertence a alguma fonte local |

`AddPlayer_t` percorre o vetor de fontes em `CNetworkLibrary+0x28/+0x2C`, procura um slot livre e
inicializa um `CPlayerSource` de `0x370` bytes. O limite é a capacidade do vetor, não uma constante
de um único jogador nesse método. Isso comprova suporte estrutural a múltiplas fontes locais, mas
não comprova ainda que o protocolo Rakion aceite todas como seats distintos sem adaptação.

## Ativação segura

Quando `OPENRAKION_HEADLESS=1`, `RakionClientPatch.dll` resolve o export
`?_bDedicatedServer@@3HA` e grava `1` antes do entry point do executável. O caminho normal não é
alterado. Se o engine ou o símbolo não corresponderem à build esperada, a DLL falha fechado e o
processo headless não continua em modo gráfico por engano.

Essa ativação é apenas o primeiro gate. Ainda faltam o supervisor BotHost, registro no World,
criação das fontes adicionais, associação fonte-seat, entrada automática no field e controle das
ações. Um processo iniciar com a flag não significa que o combate multippeer esteja validado.

O smoke local confirmou a mensagem `headless ativado antes do entry point
(_bDedicatedServer=1)` e o processo permaneceu vivo. A aplicação ainda criou uma janela principal,
portanto o primeiro gate está parcial: o modo dedicated do engine foi ativado, mas o supervisor
ainda precisa ocultar a janela do shell e comprovar que nenhum dispositivo gráfico foi aberto.

## Gates de lançamento

1. Processo headless inicia sem dispositivo gráfico, áudio ou janela interativa.
2. Uma instância registra um field e cria pelo menos dois `CPlayerSource`.
3. Cada fonte publica `0x030A/0x030F/0x0311` com seat correto e sequência independente.
4. Humanos acertam bots e bots acertam humanos usando o pipeline nativo.
5. Queda, morte, respawn e placar convergem no World.
6. Queda ou timeout do BotHost troca o field para o bot sintético sem desconectar humanos.
7. Teste de carga mede memória e CPU por field e por bot adicional.
