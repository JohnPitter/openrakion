# Rakion v258 — stack de servidor .NET (broker + world + buddy) + MariaDB + auth web.
# Imagem self-contained, NATIVA Linux (sem Wine), ao contrario dos exes originais.
#
#   stage build:  compila a solucao .NET (sdk 9)
#   stage final:  ubuntu + dotnet-runtime 9 + mariadb + php (auth web) + os 3 servidores
#
# O schema/dump do MySQL e a auth web (PHP) vao em ./deploy/db e ./deploy/web
# (NAO versionados; ver README). worldserver.ini default embutido.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY RakionServer.sln ./
COPY src/ ./src/
RUN dotnet restore RakionServer.sln && \
    dotnet publish src/RakionServer.World/RakionServer.World.csproj   -c Release -o /app/world && \
    dotnet publish src/RakionServer.Broker/RakionServer.Broker.csproj -c Release -o /app/broker && \
    dotnet publish src/RakionServer.Buddy/RakionServer.Buddy.csproj   -c Release -o /app/buddy

# ---------- runtime ----------
FROM ubuntu:22.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        wget ca-certificates gnupg2 procps iproute2 \
        mariadb-server mariadb-client php-cli php-mysql \
        libicu70 && \
    wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/ms.deb && \
    dpkg -i /tmp/ms.deb && rm /tmp/ms.deb && \
    apt-get update && apt-get install -y --no-install-recommends dotnet-runtime-9.0 && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app /app
COPY deploy/ /deploy/
COPY src/RakionServer.Broker/Settings/ /app/broker/Settings/
COPY docker-entrypoint.sh /entrypoint.sh
RUN tr -d '\r' < /entrypoint.sh > /entrypoint.unix && mv /entrypoint.unix /entrypoint.sh && chmod +x /entrypoint.sh

# 80=auth web, 40706=broker, 40708/tcp+udp=world, 40709/udp, 8500/8504=buddy
EXPOSE 80 40706 40708 40708/udp 40709/udp 8500 8504
ENTRYPOINT ["/entrypoint.sh"]
