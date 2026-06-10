# Créditos

Este projeto **não existiria** sem o trabalho da comunidade Rakion ao longo de mais de uma década. Obrigado a todos. 🙏

## Principais

- **CarlosX** — autor do [RakionLauncher](https://github.com/CarlosX/RakionLauncher) (GPL-3.0): o **BrokenServer** (emulador do broker), o **backend web de autenticação** em PHP, o **GConfig** (gerador de `config.xfs`), o **Md5Check**, o `load.bin` e a receita base de servidor v258. É a fundação técnica deste projeto.
- **SirMaster** — creditado por CarlosX no RakionLauncher; contribuições fundamentais à engenharia reversa do Rakion.
- **rjbelisario** e **so0rk** — contribuidores do RakionLauncher.
- **jdastridge** — autor do **iXFS**, o editor de arquivos XFS (Xenesis2) da SoftNyx, que permitiu entender e extrair o conteúdo de `DataSetup.xfs`/`Classes.xfs`.

## Comunidade

- **RaGEZONE** — fórum onde grande parte do conhecimento de private servers de Rakion foi compartilhado (packs de servidor/cliente, `DataSetup.xfs`/`Classes.xfs` desempacotados, discussões de `config.xfs` e `locale.ini`).
- **thepatan55** (RaGEZONE) — guias de montagem de servidor/cliente.
- **LegacyGamers** — documentação do método de *launch direto* do cliente.
- Todos os autores anônimos dos posts, packs e ferramentas que circularam pela comunidade.

## Tecnologias de base

- **SoftNyx Co., Ltd.** — desenvolvedora original do **Rakion**. Todos os direitos do jogo, seus binários e marca pertencem à SoftNyx. Este projeto é apenas educacional/de preservação e **não** redistribui material proprietário.
- **Croteam** — a **Serious Engine**, sobre a qual o Rakion foi construído.
- **nProtect (INCA Internet)** — GameGuard (anticheat original).

## Este repositório (OpenRakion)

- **Servidor reescrito do zero em .NET** (broker + world + buddy + common) a partir da engenharia reversa do protocolo: login completo, lobby/salas, seleção de personagem, **inventário + armazém (box) persistente**, **loja com débito de ouro em tempo real**, handshake UDP e motor de partida.
- Auth web reimplementado em **Python** (`launcher_web.py`), diagnóstico do "stuck at login", análise do GameGuard, e o **repacker XFS2 em Python** (`xfs_repack.py`).

---

Se você contribuiu e não está listado, abra uma issue/PR — será um prazer creditar.
