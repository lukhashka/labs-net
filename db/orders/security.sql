-- Least privilege: a separate application login with access to OrdersDb only.
-- Run with sqlcmd in the context of OrdersDb and pass the password from outside:
--   sqlcmd -S localhost,1433 -U sa -P <sa> -d OrdersDb -v AppPassword="<strong password>" -i security.sql
USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'orders_app')
    CREATE LOGIN orders_app WITH PASSWORD = '$(AppPassword)', CHECK_POLICY = ON;
GO
USE OrdersDb;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'orders_app')
    CREATE USER orders_app FOR LOGIN orders_app;
GO
ALTER ROLE db_datareader ADD MEMBER orders_app;
ALTER ROLE db_datawriter ADD MEMBER orders_app;
GRANT EXECUTE ON SCHEMA::dbo TO orders_app;
GO
