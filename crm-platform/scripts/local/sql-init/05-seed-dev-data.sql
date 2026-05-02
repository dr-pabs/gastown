-- CRM Platform — Local SQL Initialisation Script 05
-- Seeds representative CRM data for local development and manual testing.
-- TenantA: Acme Corp — has leads, cases, campaigns
-- TenantB: Beta Ltd  — minimal data (used to verify tenant isolation)
-- See ADR 0012 for local development strategy.

USE CrmPlatform;
GO

-- Note: This script seeds bootstrap data only.
-- Full schema (with all columns) is created by EF Core migrations per service.
-- This script is re-run via: make seed

PRINT 'Dev seed data will be populated by EF Core migrations per service.';
PRINT 'Run "make migrate" first, then "make seed" to load representative data.';
PRINT '';
PRINT 'TenantA ID: 11111111-1111-1111-1111-111111111111 (Acme Corp)';
PRINT 'TenantB ID: 22222222-2222-2222-2222-222222222222 (Beta Ltd)';
PRINT '';
PRINT 'Get dev tokens:';
PRINT '  ./scripts/local/get-dev-token.sh --tenant TenantA --role SalesRep';
PRINT '  ./scripts/local/get-dev-token.sh --tenant TenantA --role SalesManager';
PRINT '  ./scripts/local/get-dev-token.sh --tenant TenantA --role TenantAdmin';
PRINT '  ./scripts/local/get-dev-token.sh --tenant TenantB --role SalesRep';
GO
