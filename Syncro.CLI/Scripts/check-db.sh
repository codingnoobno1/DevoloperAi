#!/bin/bash
# check-db.sh — Syncro database connectivity checker

echo "========================================="
echo "  Syncro Database Connectivity Check"
echo "========================================="

check_port() {
  local host=$1
  local port=$2
  local name=$3
  if nc -z -w 2 "$host" "$port" 2>/dev/null; then
    echo "✅  $name reachable at $host:$port"
  else
    echo "❌  $name NOT reachable at $host:$port"
  fi
}

# MongoDB
MONGO_HOST="${MONGO_HOST:-localhost}"
MONGO_PORT="${MONGO_PORT:-27017}"
check_port "$MONGO_HOST" "$MONGO_PORT" "MongoDB"

# PostgreSQL
PG_HOST="${PG_HOST:-localhost}"
PG_PORT="${PG_PORT:-5432}"
check_port "$PG_HOST" "$PG_PORT" "PostgreSQL"

# MySQL / MariaDB
MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_PORT="${MYSQL_PORT:-3306}"
check_port "$MYSQL_HOST" "$MYSQL_PORT" "MySQL/MariaDB"

# Redis
REDIS_HOST="${REDIS_HOST:-localhost}"
REDIS_PORT="${REDIS_PORT:-6379}"
check_port "$REDIS_HOST" "$REDIS_PORT" "Redis"

# Check if mongo CLI is available
if command -v mongosh &>/dev/null; then
  echo ""
  echo "MongoDB shell version: $(mongosh --version 2>&1 | head -1)"
fi

# Check if psql is available
if command -v psql &>/dev/null; then
  echo "PostgreSQL client version: $(psql --version)"
fi

echo ""
echo "Set MONGO_HOST, PG_HOST, REDIS_HOST etc. to override defaults."
