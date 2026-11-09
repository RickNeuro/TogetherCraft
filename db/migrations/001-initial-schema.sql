-- Initial TogetherCraft account schema.
-- Safe to re-run for clean databases and existing installations.

BEGIN;

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

GRANT SELECT ON TABLE public.tc_users TO tc_app;
GRANT INSERT (
    username,
    username_normalized,
    password_hash,
    minecraft_username,
    account_role,
    account_status,
    approved_at_utc
) ON TABLE public.tc_users TO tc_app;
GRANT UPDATE (account_status, approved_at_utc)
    ON TABLE public.tc_users TO tc_app;
GRANT USAGE, SELECT ON SEQUENCE public.tc_users_id_seq TO tc_app;

COMMIT;
