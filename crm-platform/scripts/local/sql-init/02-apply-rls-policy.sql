-- CRM Platform — Local SQL Initialisation Script 02
-- Applies the Row-Level Security (RLS) policy using SESSION_CONTEXT.
-- This is the SAME policy used in production — no local bypass.
-- See ADR 0001 for multi-tenant database strategy.

USE CrmPlatform;
GO

-- ============================================================
-- Tenant filter function
-- Reads TenantId from SESSION_CONTEXT set by EF Core interceptor.
-- db_owner bypass allows EF Core migrations to run without RLS.
-- ============================================================
IF OBJECT_ID('dbo.fn_TenantFilter', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_TenantFilter;
GO

CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
RETURNS TABLE WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS fn_result
    WHERE
        @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER)
        OR IS_MEMBER('db_owner') = 1;
GO

-- ============================================================
-- Stored procedure to set SESSION_CONTEXT for a request.
-- Called by the EF Core DbCommandInterceptor in the _template.
-- ============================================================
IF OBJECT_ID('dbo.sp_SetTenantContext', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SetTenantContext;
GO

CREATE PROCEDURE dbo.sp_SetTenantContext
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    EXEC sp_set_session_context N'TenantId', @TenantId;
END
GO

-- ============================================================
-- NOTE: Security policies are applied per-table by EF Core
-- migrations after each service creates its schema.
-- The policy pattern for each business table is:
--
-- CREATE SECURITY POLICY dbo.TenantIsolationPolicy_{TableName}
--     ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.{TableName},
--     ADD BLOCK  PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.{TableName} AFTER INSERT
--     WITH (STATE = ON);
--
-- This script only installs the shared function and procedure.
-- ============================================================
PRINT 'RLS policy function and procedure created successfully.';
GO
