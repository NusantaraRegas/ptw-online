-- Creates or rotates the least-privilege SQL logins the production stack uses. Runs after EF Core
-- migrations (so the database exists) and is idempotent: re-running with the same .env is a no-op,
-- re-running with a changed password rotates it. `sa` stays reserved for the migrate step.
--
-- Scripting variables come from the container environment (sqlcmd reads them as $(NAME)):
--   PTW_SQL_APP_PASSWORD     required; api and worker (read/write data only, no DDL)
--   PTW_SQL_BACKUP_PASSWORD  optional; the internal backup system (backup operator + read)
-- Passwords must satisfy the SQL Server complexity policy and must not contain a single quote.
SET NOCOUNT ON;

IF N'$(PTW_SQL_APP_PASSWORD)' = N''
BEGIN
    RAISERROR (N'PTW_SQL_APP_PASSWORD is empty.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'ptw_app')
    CREATE LOGIN ptw_app WITH PASSWORD = N'$(PTW_SQL_APP_PASSWORD)', CHECK_POLICY = ON, DEFAULT_DATABASE = PtwOnline;
ELSE
    ALTER LOGIN ptw_app WITH PASSWORD = N'$(PTW_SQL_APP_PASSWORD)';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ptw_app')
    CREATE USER ptw_app FOR LOGIN ptw_app;
IF IS_ROLEMEMBER(N'db_datareader', N'ptw_app') <> 1
    ALTER ROLE db_datareader ADD MEMBER ptw_app;
IF IS_ROLEMEMBER(N'db_datawriter', N'ptw_app') <> 1
    ALTER ROLE db_datawriter ADD MEMBER ptw_app;

IF N'$(PTW_SQL_BACKUP_PASSWORD)' <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'ptw_backup')
        CREATE LOGIN ptw_backup WITH PASSWORD = N'$(PTW_SQL_BACKUP_PASSWORD)', CHECK_POLICY = ON, DEFAULT_DATABASE = PtwOnline;
    ELSE
        ALTER LOGIN ptw_backup WITH PASSWORD = N'$(PTW_SQL_BACKUP_PASSWORD)';

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ptw_backup')
        CREATE USER ptw_backup FOR LOGIN ptw_backup;
    IF IS_ROLEMEMBER(N'db_backupoperator', N'ptw_backup') <> 1
        ALTER ROLE db_backupoperator ADD MEMBER ptw_backup;
    IF IS_ROLEMEMBER(N'db_datareader', N'ptw_backup') <> 1
        ALTER ROLE db_datareader ADD MEMBER ptw_backup;
END;

PRINT N'PTW SQL logins are in place.';
