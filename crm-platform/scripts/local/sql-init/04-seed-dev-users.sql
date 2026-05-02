-- CRM Platform — Local SQL Initialisation Script 04
-- Seeds development users for each tenant and role combination.
-- These users correspond to the Entra ID dev users in ADR 0012.
-- Fixed Guids for deterministic testing.

USE CrmPlatform;
GO

-- Bootstrap TenantUsers and UserRoles tables for local dev.
-- EF Core migrations from identity-service will add full schema later.

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantUsers' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.TenantUsers (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        TenantId        UNIQUEIDENTIFIER NOT NULL,
        EntraObjectId   NVARCHAR(128)    NOT NULL,
        Email           NVARCHAR(256)    NOT NULL,
        DisplayName     NVARCHAR(256)    NOT NULL,
        Status          NVARCHAR(32)     NOT NULL DEFAULT 'Active',
        CreatedAt       DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        ModifiedAt      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        IsDeleted       BIT              NOT NULL DEFAULT 0,
        DeletedAt       DATETIME2        NULL
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserRoles' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.UserRoles (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        TenantUserId    UNIQUEIDENTIFIER NOT NULL,
        TenantId        UNIQUEIDENTIFIER NOT NULL,
        Role            NVARCHAR(64)     NOT NULL,
        GrantedAt       DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        GrantedBy       UNIQUEIDENTIFIER NULL,
        CreatedAt       DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        ModifiedAt      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        IsDeleted       BIT              NOT NULL DEFAULT 0,
        DeletedAt       DATETIME2        NULL
    );
END
GO

DECLARE @TenantAId UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @TenantBId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

-- ===================== TENANT A USERS =====================
DECLARE @UserARep   UNIQUEIDENTIFIER = 'A1000001-0000-0000-0000-000000000001';
DECLARE @UserAMgr   UNIQUEIDENTIFIER = 'A1000002-0000-0000-0000-000000000002';
DECLARE @UserAAdmin UNIQUEIDENTIFIER = 'A1000003-0000-0000-0000-000000000003';
DECLARE @UserASupport UNIQUEIDENTIFIER = 'A1000004-0000-0000-0000-000000000004';
DECLARE @UserAMarketing UNIQUEIDENTIFIER = 'A1000005-0000-0000-0000-000000000005';

-- TenantA SalesRep
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE Id = @UserARep)
BEGIN
    INSERT INTO dbo.TenantUsers (Id, TenantId, EntraObjectId, Email, DisplayName)
    VALUES (@UserARep, @TenantAId, 'entra-a-salesrep', 'dev-salesrep-a@devtenant.onmicrosoft.com', 'Dev SalesRep A');
    INSERT INTO dbo.UserRoles (Id, TenantUserId, TenantId, Role)
    VALUES (NEWID(), @UserARep, @TenantAId, 'SalesRep');
END

-- TenantA SalesManager
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE Id = @UserAMgr)
BEGIN
    INSERT INTO dbo.TenantUsers (Id, TenantId, EntraObjectId, Email, DisplayName)
    VALUES (@UserAMgr, @TenantAId, 'entra-a-manager', 'dev-manager-a@devtenant.onmicrosoft.com', 'Dev Manager A');
    INSERT INTO dbo.UserRoles (Id, TenantUserId, TenantId, Role)
    VALUES (NEWID(), @UserAMgr, @TenantAId, 'SalesManager');
END

-- TenantA TenantAdmin
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE Id = @UserAAdmin)
BEGIN
    INSERT INTO dbo.TenantUsers (Id, TenantId, EntraObjectId, Email, DisplayName)
    VALUES (@UserAAdmin, @TenantAId, 'entra-a-admin', 'dev-admin-a@devtenant.onmicrosoft.com', 'Dev Admin A');
    INSERT INTO dbo.UserRoles (Id, TenantUserId, TenantId, Role)
    VALUES (NEWID(), @UserAAdmin, @TenantAId, 'TenantAdmin');
END

-- TenantA SupportAgent
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE Id = @UserASupport)
BEGIN
    INSERT INTO dbo.TenantUsers (Id, TenantId, EntraObjectId, Email, DisplayName)
    VALUES (@UserASupport, @TenantAId, 'entra-a-support', 'dev-support-a@devtenant.onmicrosoft.com', 'Dev Support A');
    INSERT INTO dbo.UserRoles (Id, TenantUserId, TenantId, Role)
    VALUES (NEWID(), @UserASupport, @TenantAId, 'SupportAgent');
END

-- TenantA MarketingUser
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE Id = @UserAMarketing)
BEGIN
    INSERT INTO dbo.TenantUsers (Id, TenantId, EntraObjectId, Email, DisplayName)
    VALUES (@UserAMarketing, @TenantAId, 'entra-a-marketing', 'dev-marketing-a@devtenant.onmicrosoft.com', 'Dev Marketing A');
    INSERT INTO dbo.UserRoles (Id, TenantUserId, TenantId, Role)
    VALUES (NEWID(), @UserAMarketing, @TenantAId, 'MarketingUser');
END

-- ===================== TENANT B USERS =====================
DECLARE @UserBRep UNIQUEIDENTIFIER = 'B2000001-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE Id = @UserBRep)
BEGIN
    INSERT INTO dbo.TenantUsers (Id, TenantId, EntraObjectId, Email, DisplayName)
    VALUES (@UserBRep, @TenantBId, 'entra-b-salesrep', 'dev-salesrep-b@devtenant.onmicrosoft.com', 'Dev SalesRep B');
    INSERT INTO dbo.UserRoles (Id, TenantUserId, TenantId, Role)
    VALUES (NEWID(), @UserBRep, @TenantBId, 'SalesRep');
END

PRINT 'Dev users seeded for TenantA and TenantB.';
GO
