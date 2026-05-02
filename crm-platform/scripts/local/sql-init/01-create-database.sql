-- CRM Platform — Local SQL Initialisation Script 01
-- Creates the CrmPlatform database.
-- Run automatically by SQL Server container on first start.
-- See ADR 0012 for local development strategy.

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'CrmPlatform')
BEGIN
    CREATE DATABASE CrmPlatform
        COLLATE Latin1_General_CI_AS;
END
GO

USE CrmPlatform;
GO

-- Ensure the crm application login exists (used by services locally)
IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = N'crm_app')
BEGIN
    CREATE LOGIN crm_app WITH PASSWORD = 'App_Password_123!';
END
GO

IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = N'crm_app')
BEGIN
    CREATE USER crm_app FOR LOGIN crm_app;
    ALTER ROLE db_datareader ADD MEMBER crm_app;
    ALTER ROLE db_datawriter ADD MEMBER crm_app;
    ALTER ROLE db_ddladmin   ADD MEMBER crm_app;
END
GO
