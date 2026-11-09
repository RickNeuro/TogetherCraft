\set ON_ERROR_STOP on

SELECT format('CREATE ROLE tc_app LOGIN PASSWORD %L', :'app_db_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tc_app')
\gexec

ALTER ROLE tc_app
    WITH LOGIN
    PASSWORD :'app_db_password'
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOREPLICATION;

GRANT CONNECT ON DATABASE togethercraft TO tc_app;
GRANT USAGE ON SCHEMA public TO tc_app;

CREATE TABLE IF NOT EXISTS public.tc_users (
    id BIGSERIAL PRIMARY KEY,
    username VARCHAR(32) NOT NULL,
    username_normalized VARCHAR(32) NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    minecraft_username VARCHAR(16) NOT NULL,
    account_role VARCHAR(16) NOT NULL DEFAULT 'User',
    account_status VARCHAR(16) NOT NULL DEFAULT 'Pending',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    approved_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_tc_users_status_created
    ON public.tc_users (account_status, created_at_utc);

\i /db/migrations/002-registration-worlds.sql

GRANT SELECT, INSERT, UPDATE ON TABLE public.tc_users TO tc_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO tc_app;

ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE ON TABLES TO tc_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO tc_app;
