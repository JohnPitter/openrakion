# Servidor

Os binários de servidor da SoftNyx (`BrokerServer`/`BrokenServer`, `RakionWorldServ`) rodam sob **Wine** em um container Linux. **Eles não estão neste repositório** (proprietários — veja [NOTICE.md](../NOTICE.md)); coloque os seus.

## Esboço de container (Docker)

Imagem base sugerida: Debian/Ubuntu + Wine (win32) + Mono + MariaDB.

Passos do entrypoint (alto nível):
1. Iniciar MariaDB (`lower_case_table_names=1`), importar schema, criar conta de teste.
2. Iniciar o **web** de autenticação: `php -S 0.0.0.0:80 -t /webroot`.
3. Iniciar o **BrokerServer/BrokenServer** (Wine) — lê `Settings/Settings.ini` e `Settings/GameServers.ini`.
4. Instalar e iniciar o **RakionWorldServ** via SCM do Wine (`-install` + `sc start "Rakion World [1]"`).

## Configs

Templates em `config/`:
- `Settings.ini`, `GameServers.ini` → vão em `BrokerServer/Settings/`
- `worldserver.ini` → vai em `RakionWorldServ/`

Ajuste IPs e credenciais de DB. O ponto mais sensível é o **advertised IP** no `GameServers.ini` (veja comentários no arquivo).

## Patch conhecido

`RakionWorldServ.exe` tenta enviar e-mail via CDOSYS e crasha sem servidor SMTP. Um patch de 1 byte (transformar o salto condicional do envio em incondicional) torna isso não-fatal. Faça por sua conta com os seus binários.
