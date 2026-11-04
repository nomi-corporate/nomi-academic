#!/bin/sh
set -eu

# Executado pela imagem oficial somente em um volume novo e vazio.
psql -X --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 \
  --set=migrator_password="$NOMI_ACADEMIC_MIGRATOR_PASSWORD" \
  --set=app_password="$NOMI_ACADEMIC_APP_PASSWORD" <<'SQL'
CREATE ROLE nomi_academic_migrator LOGIN PASSWORD :'migrator_password'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE nomi_academic_app LOGIN PASSWORD :'app_password'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE DATABASE nomi_academic OWNER nomi_academic_migrator;
REVOKE ALL ON DATABASE nomi_academic FROM PUBLIC;
GRANT CONNECT ON DATABASE nomi_academic TO nomi_academic_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;
SQL

psql -X --username "$POSTGRES_USER" --dbname nomi_academic --set=ON_ERROR_STOP=1 <<'SQL'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA academic AUTHORIZATION nomi_academic_migrator;
CREATE SCHEMA migrations AUTHORIZATION nomi_academic_migrator;
REVOKE ALL ON SCHEMA academic, migrations FROM PUBLIC;
GRANT USAGE ON SCHEMA academic TO nomi_academic_app;
ALTER DEFAULT PRIVILEGES FOR ROLE nomi_academic_migrator IN SCHEMA academic
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO nomi_academic_app;
ALTER DEFAULT PRIVILEGES FOR ROLE nomi_academic_migrator IN SCHEMA academic
  GRANT USAGE, SELECT ON SEQUENCES TO nomi_academic_app;
SQL
