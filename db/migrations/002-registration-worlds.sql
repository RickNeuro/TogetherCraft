BEGIN;

ALTER TABLE public.tc_users
    ADD COLUMN IF NOT EXISTS email VARCHAR(254),
    ADD COLUMN IF NOT EXISTS minecraft_uuid UUID,
    ADD COLUMN IF NOT EXISTS wants_fabric BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS wants_create BOOLEAN NOT NULL DEFAULT TRUE;

-- Existing accounts keep their old Minecraft nickname, but new accounts
-- receive the verified nickname only after Microsoft/Minecraft linking.
ALTER TABLE public.tc_users
    ALTER COLUMN minecraft_username DROP NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tc_users_email
    ON public.tc_users (LOWER(email))
    WHERE email IS NOT NULL AND email <> '';

CREATE UNIQUE INDEX IF NOT EXISTS ux_tc_users_minecraft_uuid
    ON public.tc_users (minecraft_uuid)
    WHERE minecraft_uuid IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_tc_users_at_least_one_server'
          AND conrelid = 'public.tc_users'::regclass
    ) THEN
        ALTER TABLE public.tc_users
            ADD CONSTRAINT ck_tc_users_at_least_one_server
            CHECK (wants_fabric OR wants_create) NOT VALID;
    END IF;
END $$;

ALTER TABLE public.tc_users
    VALIDATE CONSTRAINT ck_tc_users_at_least_one_server;

-- Extend the existing least-privilege application grants.
GRANT INSERT (email, wants_fabric, wants_create)
    ON public.tc_users TO tc_app;

GRANT UPDATE (minecraft_username, minecraft_uuid)
    ON public.tc_users TO tc_app;

COMMIT;
