#!/usr/bin/env bash
# scripts/onboard-developer.sh
# Grants a new developer the necessary Azure RBAC roles to work on the CRM platform locally.
# ADR 0012: All dev onboarding performed via this script — no manual portal clicks.
#
# Usage:
#   ./scripts/onboard-developer.sh --email "dev@company.com" [--env dev]
#
# Grants:
#   - Key Vault Secrets User         (read dev secrets)
#   - Service Bus Data Owner         (send + receive on all topics)
#   - Storage Blob Data Contributor  (read/write blobs)
#   - App Configuration Data Reader  (read feature flags)
#   - Cognitive Services User        (AI Foundry — if applicable)
#
# Prerequisites:
#   - az CLI logged in with Owner or User Access Administrator on the dev subscription
#   - AZURE_SUBSCRIPTION_ID env var set (or default subscription configured)

set -euo pipefail

DEV_EMAIL=""
ENVIRONMENT="dev"

# ─── Parse args ───────────────────────────────────────────────────────────────
usage() {
  echo "Usage: $0 --email <azure-ad-email> [--env <dev|test>]"
  exit 1
}

while [[ $# -gt 0 ]]; do
  case $1 in
    --email) DEV_EMAIL="$2"; shift 2 ;;
    --env)   ENVIRONMENT="$2"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "Unknown argument: $1"; usage ;;
  esac
done

[[ -z "$DEV_EMAIL" ]] && { echo "❌ --email is required"; usage; }
[[ "$ENVIRONMENT" != "dev" && "$ENVIRONMENT" != "test" ]] && {
  echo "❌ --env must be 'dev' or 'test'. Prod/staging onboarding requires elevated process."
  exit 1
}

RESOURCE_GROUP="crm-${ENVIRONMENT}-rg"

echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║  CRM Platform — Developer Onboarding                 ║"
echo "╠══════════════════════════════════════════════════════╣"
echo "║  Developer: ${DEV_EMAIL}"
echo "║  Environment: ${ENVIRONMENT}"
echo "║  Resource Group: ${RESOURCE_GROUP}"
echo "╚══════════════════════════════════════════════════════╝"
echo ""

# ─── Resolve developer's Object ID ───────────────────────────────────────────
echo "🔍 Resolving Azure AD Object ID for ${DEV_EMAIL}..."
OBJECT_ID=$(az ad user show --id "$DEV_EMAIL" --query id -o tsv 2>/dev/null || \
            az ad user list --filter "mail eq '${DEV_EMAIL}'" --query "[0].id" -o tsv)

if [[ -z "$OBJECT_ID" ]]; then
  echo "❌ Could not find Azure AD user for email: ${DEV_EMAIL}"
  echo "   Ensure the user exists in the Entra ID tenant and you have 'az login' with correct tenant."
  exit 1
fi
echo "   ✅ Object ID: ${OBJECT_ID}"

# ─── Resolve resource IDs ─────────────────────────────────────────────────────
echo "🔍 Resolving resource IDs in ${RESOURCE_GROUP}..."

KV_ID=$(az keyvault list --resource-group "$RESOURCE_GROUP" --query "[0].id" -o tsv 2>/dev/null || echo "")
SB_ID=$(az servicebus namespace list --resource-group "$RESOURCE_GROUP" --query "[0].id" -o tsv 2>/dev/null || echo "")
SA_ID=$(az storage account list --resource-group "$RESOURCE_GROUP" --query "[0].id" -o tsv 2>/dev/null || echo "")
AC_ID=$(az appconfig list --resource-group "$RESOURCE_GROUP" --query "[0].id" -o tsv 2>/dev/null || echo "")

# ─── Grant roles ─────────────────────────────────────────────────────────────

grant_role() {
  local scope="$1"
  local role="$2"
  local resource_name="$3"

  if [[ -z "$scope" ]]; then
    echo "   ⚠️  ${resource_name} not found in ${RESOURCE_GROUP} — skipping"
    return
  fi

  az role assignment create \
    --assignee-object-id "$OBJECT_ID" \
    --assignee-principal-type User \
    --role "$role" \
    --scope "$scope" \
    --output none 2>/dev/null && echo "   ✅ ${role} → ${resource_name}" || \
    echo "   ⚠️  Role may already exist: ${role} on ${resource_name}"
}

echo ""
echo "▶ Granting Azure RBAC roles..."

grant_role "$KV_ID" "Key Vault Secrets User"          "Key Vault"
grant_role "$SB_ID" "Azure Service Bus Data Owner"     "Service Bus"
grant_role "$SA_ID" "Storage Blob Data Contributor"    "Storage Account"
grant_role "$AC_ID" "App Configuration Data Reader"    "App Configuration"

# ─── SQL — grant SQL login (Entra auth) ──────────────────────────────────────
echo ""
echo "▶ SQL Server: Entra authentication access..."
SQL_SERVER=$(az sql server list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv 2>/dev/null || echo "")
if [[ -n "$SQL_SERVER" ]]; then
  echo "   SQL Server: ${SQL_SERVER}"
  echo "   ℹ️  Run the following SQL to grant CrmPlatform DB access:"
  echo ""
  echo "   CREATE USER [${DEV_EMAIL}] FROM EXTERNAL PROVIDER;"
  echo "   ALTER ROLE db_datareader ADD MEMBER [${DEV_EMAIL}];"
  echo "   ALTER ROLE db_datawriter ADD MEMBER [${DEV_EMAIL}];"
  echo ""
else
  echo "   ⚠️  No SQL server found in ${RESOURCE_GROUP} — skipping"
fi

echo ""
echo "✅ Onboarding complete for ${DEV_EMAIL}."
echo ""
echo "Next steps:"
echo "  1. Clone the repo: git clone https://github.com/dr-pabs/crm-platform"
echo "  2. Start local infrastructure: make dev-infra"
echo "  3. Get a dev token: ./scripts/local/get-dev-token.sh --tenant TenantA --role SalesRep"
echo "  4. Read the developer guide: docs/local-development.md"
echo ""
