-- CRM Platform — Local SQL Initialisation Script 03
-- Seeds two development tenants: TenantA and TenantB.
-- These Guids are FIXED — referenced across all local dev scripts and tests.
-- See ADR 0012 for local development strategy.

USE CrmPlatform;
GO

-- Tenants table is owned by identity-service / platform-admin-service.
-- This seed runs before EF migrations, so we create a minimal bootstrap table.
-- EF Core migrations will add columns — this provides the minimal rows needed.

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tenants' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Tenants (
        Id             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Name           NVARCHAR(256)    NOT NULL,
        Slug           NVARCHAR(64)     NOT NULL UNIQUE,
        Status         NVARCHAR(32)     NOT NULL DEFAULT 'Active',
        CreatedAt      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        ModifiedAt     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        IsDeleted      BIT              NOT NULL DEFAULT 0,
        DeletedAt      DATETIME2        NULL
    );
END
GO

-- Fixed Guids for TenantA and TenantB — used across all local dev and tests
DECLARE @TenantAId UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @TenantBId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantAId)
BEGIN
    INSERT INTO dbo.Tenants (Id, Name, Slug, Status)
    VALUES (@TenantAId, 'Acme Corp (Dev TenantA)', 'acme-dev', 'Active');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantBId)
BEGIN
    INSERT INTO dbo.Tenants (Id, Name, Slug, Status)
    VALUES (@TenantBId, 'Beta Ltd (Dev TenantB)', 'beta-dev', 'Active');
END

PRINT 'Dev tenants seeded: TenantA=11111111-... TenantB=22222222-...';
GO
