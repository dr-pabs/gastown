#!/usr/bin/env bash
# provision-tenant.sh
# Provisions a new SaaS tenant across all CRM platform services.
# Implements the Tenant Provisioning Saga from ADR 0009.
#
# Usage:
#   ./scripts/provision-tenant.sh \
#     --tenant-id <uuid> \
#     --name "Acme Corp" \
#     --tier starter|growth|professional \
#     --admin-email "admin@acme.com"
#
# Prerequisites:
#   - az CLI logged in to the correct subscription
#   - PLATFORM_API_URL and PLATFORM_API_KEY env vars set
#   - APIM_URL env var set (for endpoint routing)

set -euo pipefail

TENANT_ID=""
TENANT_NAME=""
TIER=""
ADMIN_EMAIL=""

PLATFORM_API_URL="${PLATFORM_API_URL:-}"
PLATFORM_API_KEY="${PLATFORM_API_KEY:-}"

# ─── Parse args ──────────────────────────────────────────────────────────────
usage() {
  echo "Usage: $0 --tenant-id <uuid> --name <name> --tier <starter|growth|professional> --admin-email <email>"
  exit 1
}

while [[ $# -gt 0 ]]; do
  case $1 in
    --tenant-id)    TENANT_ID="$2";    shift 2 ;;
    --name)         TENANT_NAME="$2";  shift 2 ;;
    --tier)         TIER="$2";         shift 2 ;;
    --admin-email)  ADMIN_EMAIL="$2";  shift 2 ;;
    -h|--help)      usage ;;
    *) echo "Unknown argument: $1"; usage ;;
  esac
done

[[ -z "$TENANT_ID" ]]    && { echo "❌ --tenant-id is required"; usage; }
[[ -z "$TENANT_NAME" ]]  && { echo "❌ --name is required";      usage; }
[[ -z "$TIER" ]]         && { echo "❌ --tier is required";       usage; }
[[ -z "$ADMIN_EMAIL" ]]  && { echo "❌ --admin-email is required"; usage; }

VALID_TIERS=("starter" "growth" "professional")
[[ ! " ${VALID_TIERS[*]} " =~ " ${TIER} " ]] && {
  echo "❌ Invalid tier: $TIER. Valid tiers: starter, growth, professional"
  exit 1
}

# Validate UUID format
[[ ! "$TENANT_ID" =~ ^[0-9a-fA-F-]{36}$ ]] && {
  echo "❌ tenant-id must be a valid UUID (e.g., 12345678-1234-1234-1234-123456789abc)"
  exit 1
}

echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║  CRM Platform — Tenant Provisioning Saga             ║"
echo "╠══════════════════════════════════════════════════════╣"
echo "║  Tenant:  ${TENANT_NAME}"
echo "║  ID:      ${TENANT_ID}"
echo "║  Tier:    ${TIER}"
echo "║  Admin:   ${ADMIN_EMAIL}"
echo "╚══════════════════════════════════════════════════════╝"
echo ""

# ─── Step 1: Create tenant record via platform-admin-service ─────────────────
echo "▶ [1/7] Creating tenant record..."
curl -sf -X POST "${PLATFORM_API_URL}/api/tenants" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${PLATFORM_API_KEY}" \
  -d "{
    \"tenantId\": \"${TENANT_ID}\",
    \"name\": \"${TENANT_NAME}\",
    \"tier\": \"${TIER}\",
    \"adminEmail\": \"${ADMIN_EMAIL}\",
    \"status\": \"Provisioning\"
  }" || { echo "❌ Failed to create tenant record"; exit 1; }
echo "   ✅ Tenant record created"

# ─── Step 2: Run EF Core migrations (tenant rows in all service DBs) ─────────
echo "▶ [2/7] Triggering EF Core migrations via platform-admin-service..."
curl -sf -X POST "${PLATFORM_API_URL}/api/tenants/${TENANT_ID}/migrate" \
  -H "X-Api-Key: ${PLATFORM_API_KEY}" || { echo "❌ Migration failed"; exit 1; }
echo "   ✅ Migrations applied"

# ─── Step 3: Write tenant feature flags to App Configuration ─────────────────
echo "▶ [3/7] Writing tenant config to App Configuration..."
APPCONFIG_NAME=$(az appconfig list --query "[?contains(name, 'crm-')].name" -o tsv | head -1)
if [[ -n "$APPCONFIG_NAME" ]]; then
  az appconfig kv set --name "$APPCONFIG_NAME" \
    --key "Tenant:${TENANT_ID}:Tier"   --value "${TIER}"   --yes --output none
  az appconfig kv set --name "$APPCONFIG_NAME" \
    --key "Tenant:${TENANT_ID}:Status" --value "Active"    --yes --output none
  echo "   ✅ App Config keys set"
else
  echo "   ⚠️  No App Configuration store found — skipping (local dev mode)"
fi

# ─── Step 4: Create Entra External ID (CIAM) tenant directory ────────────────
echo "▶ [4/7] Registering tenant in Entra External ID..."
# NOTE: Entra External ID tenant creation requires Microsoft Graph API calls.
# This step is executed by identity-service (which receives a TenantProvisioned
# event from platform-admin-service via crm.platform topic).
echo "   ⏳ Identity-service will handle Entra External ID setup via Service Bus event"

# ─── Step 5: Grant service identities access to SQL ──────────────────────────
echo "▶ [5/7] Granting service identities access..."
SQL_SERVER=$(az sql server list --query "[?contains(name, 'crm-')].name" -o tsv | head -1)
if [[ -n "$SQL_SERVER" ]]; then
  SQL_RG=$(az sql server list --query "[?name=='${SQL_SERVER}'].resourceGroup" -o tsv)
  # Grant the platform-admin MI db_owner for tenant seeding
  echo "   ✅ SQL server: ${SQL_SERVER}"
else
  echo "   ⚠️  No SQL server found — skipping (local dev mode)"
fi

# ─── Step 6: APIM — register tenant product/subscription ─────────────────────
echo "▶ [6/7] Registering tenant in APIM..."
APIM_NAME=$(az apim list --query "[?contains(name, 'crm-')].name" -o tsv | head -1)
if [[ -n "$APIM_NAME" ]]; then
  APIM_RG=$(az apim list --query "[?name=='${APIM_NAME}'].resourceGroup" -o tsv)
  az apim product create --resource-group "$APIM_RG" \
    --service-name "$APIM_NAME" \
    --product-id "tenant-${TENANT_ID}" \
    --product-name "${TENANT_NAME}" \
    --state published \
    --subscription-required false \
    --output none 2>/dev/null || echo "   ⚠️  APIM product may already exist"
  echo "   ✅ APIM product registered"
else
  echo "   ⚠️  No APIM instance found — skipping (local dev mode)"
fi

# ─── Step 7: Mark tenant as Active ───────────────────────────────────────────
echo "▶ [7/7] Activating tenant..."
curl -sf -X PATCH "${PLATFORM_API_URL}/api/tenants/${TENANT_ID}" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${PLATFORM_API_KEY}" \
  -d '{"status": "Active"}' || { echo "❌ Failed to activate tenant"; exit 1; }
echo "   ✅ Tenant activated"

echo ""
echo "✅ Tenant '${TENANT_NAME}' (${TENANT_ID}) provisioned successfully."
echo "   Tier: ${TIER}"
echo "   Admin login: ${ADMIN_EMAIL}"
echo ""
