# RE de PC Bang, país, região e status de serviço — Rakion v258

## Escopo e veredito

O cliente e o World v258 analisados **não possuem um contrato ativo e independente de PC Bang**.
As tabelas `pcbangiplist` e `logpcbang` existem no dump legado, mas não têm referência no World
original nem no servidor .NET. Os assets `Img_PCBang` são reutilizados pela UI para o estado de
**Power User** e, portanto, não comprovam classificação por IP nem recompensa PC Bang.

O único bônus fechado nos binários é o de Power User: EXP `×1,5`, com divisão inteira e Gold
inalterado. País é metadado persistido; região e `servicestatus` não participam de uma regra dessa
build. Assim, não há uma feature PC Bang fiel a ativar. Resolver estabelecimento por IP, criar badge
próprio ou conceder recompensa seria uma funcionalidade nova.

## Evidência do dump e dos binários

| Fonte | Evidência | Conclusão nesta build |
|---|---|---|
| `pcbangiplist` | `ip int(15)` | schema residual; nenhuma string SQL/xref no World |
| `logpcbang` | `userid`, `ip`, `connecttime` | schema residual; nenhuma escrita no World |
| `country_reference` | número, nome, região e código | catálogo residual; nenhuma consulta no World |
| `user`/`usergameinfo.country` | país persistido e carregado | metadado de conta/auditoria, sem política regional |
| `servicestatus` | serial, hora, IP, porta e status | sem consumidor; `SetServiceStatus` importado é a API do Windows SCM |
| `Img_PCBang.tex` | carregado e desenhado no resultado | badge reutilizado quando Power User está ativo |
| `Img_PCBang_small.tex` | carregado e liberado | nenhum consumidor de render ativo localizado |

O World original contém SQL relacionado a `country`, mas não contém as strings `pcbangiplist`,
`logpcbang`, `country_reference` ou a tabela `servicestatus`. A amostra decimal de
`pcbangiplist.ip` continua ambígua, porém sua conversão não faz parte do contrato executado pelo
World v258 disponível.

## O que o cliente chama de PC Bang

Em `entitiesmp_dump.bin`, a tela de resultado lê o campo criptografado em
`AccountInfo_s+0x2D80/+0x2D84`. O endereço resolve para o objeto retornado por
`IScavengerWorldNet::GetAccountInfo`; no `engine.dll`, `AccountInfo_s::SetPowerUser` escreve
exatamente esse campo. O login inicializa o valor com zero e o ativa a partir de `powertime`; a
compra `0x34` também chama `SetPowerUser(1)`.

Quando o campo está ativo, `FUN_351EBE50` executa:

```text
expFinal = expBase + expBase / 2
goldFinal = goldBase
```

A divisão é inteira com truncamento. O mesmo estado controla o desenho de `Img_PCBang` no resultado.
Logo, o nome histórico do asset não representa uma segunda flag de conta.

## Implementação no OpenRakion

O comportamento fiel fica no domínio de Power User:

- `PuConfig.ExpMult = 1.5`;
- `PuConfig.GoldMult = 1.0`;
- snapshot do benefício por partida;
- Gold configurável permanece disponível apenas como extensão operacional, neutra por padrão;
- migração `config_version=3` neutraliza somente os antigos valores-semente `1.50/2.00`, preservando
  valores customizados pelo operador.

Authority administrativa não deriva de canal, país, PC Bang ou do status histórico `5`. O status
especial é somente estado de navegação do protocolo; autorização usa a authority autenticada e a
política descrita em [`gm-admin-commands.md`](gm-admin-commands.md).

## Se uma feature moderna de PC Bang for desejada

Ela deve ser tratada explicitamente como extensão:

1. modelar estabelecimentos e faixas CIDR versionadas, sem reaproveitar o inteiro ambíguo;
2. aceitar IP encaminhado somente de proxies em allowlist;
3. separar `AccessContext.PcBang` de `AccountAuthority`;
4. calcular recompensa no settle autoritativo e registrar motivo/configuração no ledger;
5. usar retenção curta e acesso restrito para IPs;
6. ativar primeiro em modo observação, depois badge e, por último, recompensa.

Essa extensão exige contrato de produto (elegibilidade, percentual, limites e UI); tais valores não
podem ser atribuídos ao original v258.

## Ativação e rollback

Para fidelidade v258, basta iniciar o World após a migração de schema; PU concede EXP `×1,5` e não
altera Gold. Não existe flag PC Bang a habilitar.

Se o operador ativar a extensão de Gold pelo painel, o rollback consiste em retornar
`gold_mult` e `promo_gold_mult` para `1.00`. Uma futura extensão de PC Bang deve ter feature flag
própria e nunca conceder authority.

## Evidência executada em 2026-07-15

- `DecompileClientPcBangResult.py` fechou a fórmula e o uso do badge na tela de resultado;
- `TraceClientPcBangFlag.py` rastreou `+0x2D80/+0x2D84` em entities, cliente e engine até
  `AccountInfo_s::SetPowerUser`;
- busca de strings/xrefs no World original não encontrou consumidores das tabelas PC Bang/service;
- o caminho original `0x50` no World confirma EXP `×1,5` e Gold bruto;
- defaults e migração do servidor foram alinhados sem remover a extensão administrativa de Gold.

## Classificação final

- **Confirmado:** asset reutilizado por Power User, EXP `×1,5`, Gold inalterado, país como metadado.
- **Confirmado por ausência:** nenhuma flag, consulta, log ou recompensa PC Bang no World v258.
- **Residual/externo:** `pcbangiplist`, `logpcbang`, `country_reference` e `servicestatus`.
- **Extensão futura:** resolver IP/CIDR, política regional, badge PC Bang próprio ou recompensa.
