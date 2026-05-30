# Banco de dados

O servidor usa **MariaDB/MySQL**, banco `rakion`, com `lower_case_table_names=1`.

## Schema

O schema (tabelas `user`, `usergameinfo`, `UserItemInfo`, `cash`, `fetchapp`, `fetchfile`, `LogUserConnect`, etc.) vem do projeto **RakionLauncher** do CarlosX (GPL-3.0):

➡️ https://github.com/CarlosX/RakionLauncher → pasta `database/` (`rakion_all.sql`)

Não redistribuímos o dump aqui para não incluir dados de conteúdo do jogo. Baixe de lá e importe:

```bash
mysql -uroot -p rakion < rakion_all.sql
```

## Conta de teste

Depois de importar, crie uma conta para logar (exemplo conceitual):

- `user`: `id='test'`, `password='test'`
- `usergameinfo`: `id=1`, `name='test'`, `gold=10000`
- `cash`: registro para `test`

A senha é enviada pelo cliente em hex; o `launcherlogin.php` faz `hexToStr` e compara com a coluna `password`.

## Versões (tabela `fetchapp`)

- `AppId=400` (launcher) → `VerLimit=1`
- `AppId=11001` (Rakion) → `VerLimit=258`

O `fetch.php` retorna vazio quando a versão do cliente == `VerLimit` (sem update).
