#!/usr/bin/env bash
# scripts/local/get-dev-token.sh
#
# Convenience wrapper to get a signed JWT from the local auth-stub service.
# Requires auth-stub to be running (make dev-infra + auth-stub, or make dev-sfa etc.)
#
# Usage:
#   ./scripts/local/get-dev-token.sh --tenant TenantA --role SalesRep
#   ./scripts/local/get-dev-token.sh --tenant TenantB --role TenantAdmin
#   ./scripts/local/get-dev-token.sh --tenant TenantA --role SalesRep --user A1000001-0000-0000-0000-000000000001
#
# Supported roles:
#   SalesRep | SalesManager | TenantAdmin | SupportAgent | MarketingUser | AnalystUser | PlatformAdmin
#
# Supported tenants (dev fixed GUIDs):
#   TenantA → 11111111-1111-1111-1111-111111111111
#   TenantB → 22222222-2222-2222-2222-222222222222

set -euo pipefail

AUTH_STUB_URL="http://localhost:5100"
TENANT=""
ROLE=""
USER_ID=""

# ─── Tenant ID map ───────────────────────────────────────────────────────────
declare -A TENANT_IDS
TENANT_IDS["TenantA"]="11111111-1111-1111-1111-111111111111"
TENANT_IDS["TenantB"]="22222222-2222-2222-2222-222222222222"

# ─── Default user ID map (tenant + role → fixed dev GUID) ────────────────────
declare -A DEFAULT_USERS
DEFAULT_USERS["TenantA:SalesRep"]="a1000001-0000-0000-0000-000000000001"
DEFAULT_USERS["TenantA:SalesManager"]="a1000002-0000-0000-0000-000000000002"
DEFAULT_USERS["TenantA:TenantAdmin"]="a1000003-0000-0000-0000-000000000003"
DEFAULT_USERS["TenantA:SupportAgent"]="a1000004-0000-0000-0000-000000000004"
DEFAULT_USERS["TenantA:MarketingUser"]="a1000005-0000-0000-0000-000000000005"
DEFAULT_USERS["TenantB:SalesRep"]="b2000001-0000-0000-0000-000000000001"

# ─── Parse args ──────────────────────────────────────────────────────────────
usage() {
  echo "Usage: $0 --tenant <TenantA|TenantB> --role <SalesRep|SalesManager|TenantAdmin|SupportAgent|MarketingUser|AnalystUser|PlatformAdmin> [--user <guid>]"
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tenant) TENANT="$2"; shift 2 ;;
    --role)   ROLE="$2";   shift 2 ;;
    --user)   USER_ID="$2"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "Unknown argument: $1"; usage ;;
  esac
done

[[ -z "$TENANT" ]] && { echo "❌ --tenant is required"; usage; }
[[ -z "$ROLE" ]]   && { echo "❌ --role is required";   usage; }

# ─── Resolve tenant GUID ─────────────────────────────────────────────────────
TENANT_ID="${TENANT_IDS[$TENANT]:-}"
if [[ -z "$TENANT_ID" ]]; then
  # Allow passing a raw GUID directly as --tenant
  if [[ "$TENANT" =~ ^[0-9a-fA-F-]{36}$ ]]; then
    TENANT_ID="$TENANT"
  else
    echo "❌ Unknown tenant: $TENANT. Use TenantA, TenantB, or a raw GUID."
    exit 1
  fi
fi

# ─── Resolve user ID ─────────────────────────────────────────────────────────
if [[ -z "$USER_ID" ]]; then
  USER_ID="${DEFAULT_USERS["${TENANT}:${ROLE}"]:-}"
  if [[ -z "$USER_ID" ]]; then
    # Generate a deterministic placeholder for unknown combos
    USER_ID="00000000-0000-0000-0000-$(echo "${TENANT}${ROLE}" | md5 | head -c 12)"
    echo "⚠️  No default user for ${TENANT}:${ROLE}, using generated ID: $USER_ID"
  fi
fi

# ─── Check auth-stub is running ───────────────────────────────────────────────
if ! curl -sf "${AUTH_STUB_URL}/health" > /dev/null 2>&1; then
  echo "❌ auth-stub is not running at ${AUTH_STUB_URL}"
  echo "   Start it with: make dev-sfa (or make dev)"
  exit 1
fi

# ─── Request token ───────────────────────────────────────────────────────────
echo "🔑 Requesting token for ${TENANT} (${TENANT_ID}) / ${ROLE} / user ${USER_ID}..."

RESPONSE=$(curl -sf -X POST "${AUTH_STUB_URL}/token" \
  -H "Content-Type: application/json" \
  -d "{\"tenantId\": \"${TENANT_ID}\", \"userId\": \"${USER_ID}\", \"role\": \"${ROLE}\"}")

if [[ -z "$RESPONSE" ]]; then
  echo "❌ Empty response from auth-stub. Is it healthy?"
  exit 1
fi

TOKEN=$(echo "$RESPONSE" | grep -o '"token":"[^"]*"' | sed 's/"token":"//;s/"//')

if [[ -z "$TOKEN" ]]; then
  echo "❌ Could not parse token from response:"
  echo "$RESPONSE"
  exit 1
fi

echo ""
echo "✅ Token issued:"
echo ""
echo "$TOKEN"
echo ""
echo "# Use with curl:"
echo "curl -H \"Authorization: Bearer $TOKEN\" -H \"X-Tenant-Id: $TENANT_ID\" http://localhost:5010/api/leads"
echo ""
echo "# Export for shell session:"
echo "export CRM_TOKEN=\"$TOKEN\""
echo "export CRM_TENANT_ID=\"$TENANT_ID\""
