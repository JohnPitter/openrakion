#!/bin/bash
# Rakion .NET stack — sobe MariaDB + auth web (PHP) + broker + world + buddy.
set -u
log() { echo "[rakion-net] $*"; }

MYSQL_ROOT_PASSWORD="${MYSQL_ROOT_PASSWORD:-123456}"

#############################################
# 1. MariaDB (datadir persistente em /var/lib/mysql)
#############################################
log "iniciando MariaDB..."
mkdir -p /run/mysqld && chown -R mysql:mysql /run/mysqld
FIRST=0
if [ ! -d /var/lib/mysql/mysql ]; then
    FIRST=1
    mariadb-install-db --user=mysql --datadir=/var/lib/mysql >/dev/null 2>&1
fi
chown -R mysql:mysql /var/lib/mysql
mariadbd --user=mysql --bind-address=0.0.0.0 --lower-case-table-names=1 >/var/log/mariadb.log 2>&1 &
for i in $(seq 1 60); do mysqladmin ping --silent 2>/dev/null && break; sleep 1; done
mysqladmin ping 2>/dev/null && log "MariaDB up" || { log "MariaDB FALHOU"; cat /var/log/mariadb.log; }

mysql 2>/dev/null <<SQL || true
SET PASSWORD FOR 'root'@'localhost' = PASSWORD('${MYSQL_ROOT_PASSWORD}');
CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '${MYSQL_ROOT_PASSWORD}';
CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY '${MYSQL_ROOT_PASSWORD}';
GRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION;
GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION;
FLUSH PRIVILEGES;
SQL
MQ() { mysql -uroot -p"$MYSQL_ROOT_PASSWORD" "$@"; }

HASDB=$(MQ -N -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='rakion';" 2>/dev/null || echo 0)
if [ "$FIRST" = "1" ] || [ "${HASDB:-0}" = "0" ]; then
    if [ -f /deploy/db/rakion_data.sql ]; then
        log "carregando /deploy/db/rakion_data.sql..."
        MQ < /deploy/db/rakion_data.sql 2>&1 | sed 's/^/[db] /' | tail -2
    elif [ -f /deploy/db/rakion_all.sql ]; then
        MQ -e "CREATE DATABASE IF NOT EXISTS rakion CHARACTER SET utf8;"
        MQ rakion < /deploy/db/rakion_all.sql 2>&1 | sed 's/^/[db] /' | tail -2
    else
        log "(sem dump em /deploy/db — criando schema vazio)"
        MQ -e "CREATE DATABASE IF NOT EXISTS rakion CHARACTER SET utf8;"
    fi
    MQ rakion <<'SQL' 2>&1 | sed 's/^/[acct] /' || true
INSERT IGNORE INTO user (id,password) VALUES ('test','test');
INSERT IGNORE INTO usergameinfo (id,name) VALUES (1,'test');
SQL
fi

#############################################
# 2. Auth web PHP (:80) — se presente
#############################################
if [ -d /deploy/web ]; then
    log "subindo auth web em :80..."
    php -S 0.0.0.0:80 -t /deploy/web >/var/log/php.log 2>&1 &
fi

#############################################
# 3. Servidores .NET: broker, world, buddy
#############################################
export DOTNET_CLI_TELEMETRY_OPTOUT=1
INI="${RAKION_WORLD_INI:-/deploy/worldserver.ini}"

log "=== broker (BrokenServer :40706) ==="
( cd /app/broker && dotnet BrokenServer.dll ) >/var/log/broker.log 2>&1 &
sleep 2

log "=== world (RakionWorldServer :40708) ==="
( cd /app/world && dotnet RakionWorldServer.dll "$INI" ) >/var/log/world.log 2>&1 &
sleep 1

log "=== buddy (BuddyServer :8500/:8504) ==="
( cd /app/buddy && dotnet BuddyServer.dll ) >/var/log/buddy.log 2>&1 &
sleep 1

#############################################
# 4. Status + mantem vivo
#############################################
echo; log "######## PORTAS ########"
ss -ltnp 2>/dev/null | grep -E '4070[0-9]|:80 |850[04]|3306' || true
echo; log "servidor de pe — tail dos logs"
tail -f /var/log/broker.log /var/log/world.log /var/log/buddy.log
