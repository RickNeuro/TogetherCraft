-- Allow the application to update the account role.
-- Authorization remains enforced by AdminOnly and AdminController.SetRole.
-- Do not grant broader table UPDATE privileges to tc_app.
BEGIN;
GRANT UPDATE (account_role) ON TABLE public.tc_users TO tc_app;
COMMIT;
