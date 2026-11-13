\set ON_ERROR_STOP on

SELECT format('CREATE ROLE tc_app LOGIN PASSWORD %L', :'app_db_password')
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_roles
    WHERE rolname = 'tc_app'
)
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

-- The initial schema grants only the columns required by application operations.
\i /db/migrations/001-initial-schema.sql
\i /db/migrations/002-registration-worlds.sql
\i /db/migrations/003-admin-roles.sql
