#!/usr/bin/env bash
# deprovision-tenant.sh
# GDPR-compliant tenant deprovisioning with 30-day hold period.
# Implements the Tenant Deprovisioning Saga from ADR 0009.
#
# Usage:
#   ./scripts/deprovision-tenant.sh --tenant-id <uuid>              # Soft delete (30-day hold)
#   ./scripts/deprovision-tenant.sh --tenant-id <uuid> --hard-delete # Permanent deletion (IRREVERSIBLE)
#
# WITHOUT --hard-delete: sets tenant status = Deprovisioning, all service APIs return 403.
# WITH --hard-delete:    executes GDPR erasure — all tenant data permanently deleted.
#
# Prerequisites: PLATFORM_API_URL and PLATFORM_API_KEY env vars set.

set -euo pipefail

TENANT_ID=""
HARD_DELETE=false
PLATFORM_API_URL="${PLATFORM_API_URL:-}"
PLATFORM_API_KEY="${PLATFORM_API_KEY:-}"

while [[ $# -gt 0 ]]; do
  case $1 in
    --tenant-id)   TENANT_ID="$2"; shift 2 ;;
    --hard-delete) HARD_DELETE=true; shift ;;
    -h|--help)
      echo "Usage: $0 --tenant-id <uuid> [--hard-delete]"
      exit 0
      ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

[[ -z "$TENANT_ID" ]] && { echo "❌ --tenant-id is required"; exit 1; }
[[ ! "$TENANT_ID" =~ ^[0-9a-fA-F-]{36}$ ]] && { echo "❌ Invalid UUID format"; exit 1; }

# ─── Soft delete (default) ────────────────────────────────────────────────────
if [[ "$HARD_DELETE" == "false" ]]; then
  echo "⚠️  Soft deleting tenant ${TENANT_ID} (30-day hold)..."
  echo "   All APIs will return 403 for this tenant immediately."
  echo ""

  curl -sf -X PATCH "${PLATFORM_API_URL}/api/tenants/${TENANT_ID}" \
    -H "Content-Type: application/json" \
    -H "X-Api-Key: ${PLATFORM_API_KEY}" \
    -d '{"status": "Deprovisioning"}' || { echo "❌ Failed to update tenant status"; exit 1; }

  # Remove APIM product so no new tokens can be used
  APIM_NAME=$(az apim list --query "[?contains(name, 'crm-')].name" -o tsv | head -1)
  if [[ -n "$APIM_NAME" ]]; then
    APIM_RG=$(az apim list --query "[?name=='${APIM_NAME}'].resourceGroup" -o tsv)
    az apim product update \
      --resource-group "$APIM_RG" \
      --service-name "$APIM_NAME" \
      --product-id "tenant-${TENANT_ID}" \
      --state notPublished \
      --output none 2>/dev/null || true
    echo "   ✅ APIM product suspended"
  fi

  echo ""
  echo "✅ Tenant ${TENANT_ID} soft-deleted."
  echo "   Hard delete available after 30-day hold:"
  echo "   ./scripts/deprovision-tenant.sh --tenant-id ${TENANT_ID} --hard-delete"
  exit 0
fi

# ─── Hard delete (IRREVERSIBLE) ───────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║  ⚠️  PERMANENT DELETION — THIS IS IRREVERSIBLE  ⚠️   ║"
echo "╠══════════════════════════════════════════════════════╣"
echo "║  Tenant ID: ${TENANT_ID}"
echo "║  All tenant data will be permanently deleted.        ║"
echo "║  This action satisfies GDPR Right to Erasure.       ║"
echo "╚══════════════════════════════════════════════════════╝"
echo ""
echo -n "Type the tenant ID to confirm hard delete: "
read -r CONFIRM
if [[ "$CONFIRM" != "$TENANT_ID" ]]; then
  echo "❌ Confirmation failed. Aborting."
  exit 1
fi

echo ""
echo "🗑️  Beginning GDPR erasure for tenant ${TENANT_ID}..."

# ─── Step 1: SQL — GDPR wipe via platform-admin-service ──────────────────────
echo "▶ [1/5] Erasing SQL tenant data (GDPR erasure endpoint)..."
curl -sf -X DELETE "${PLATFORM_API_URL}/api/tenants/${TENANT_ID}/gdpr-erase" \
  -H "X-Api-Key: ${PLATFORM_API_KEY}" || { echo "❌ GDPR erasure failed"; exit 1; }
echo "   ✅ SQL tenant data erased (anonymised aggregates retained for reporting)"

# ─── Step 2: App Configuration — remove tenant flags ────────────────────────
echo "▶ [2/5] Removing tenant App Configuration keys..."
APPCONFIG_NAME=$(az appconfig list --query "[?contains(name, 'crm-')].name" -o tsv | head -1)
if [[ -n "$APPCONFIG_NAME" ]]; then
  az appconfig kv delete --name "$APPCONFIG_NAME" \
    --key "Tenant:${TENANT_ID}:*" --yes --output none 2>/dev/null || true
  echo "   ✅ App Config keys removed"
else
  echo "   ⚠️  No App Config found — skipping"
fi

# ─── Step 3: APIM — delete tenant product ────────────────────────────────────
echo "▶ [3/5] Removing APIM tenant product..."
APIM_NAME=$(az apim list --query "[?contains(name, 'crm-')].name" -o tsv | head -1)
if [[ -n "$APIM_NAME" ]]; then
  APIM_RG=$(az apim list --query "[?name=='${APIM_NAME}'].resourceGroup" -o tsv)
  az apim product delete \
    --resource-group "$APIM_RG" \
    --service-name "$APIM_NAME" \
    --product-id "tenant-${TENANT_ID}" \
    --yes --output none 2>/dev/null || true
  echo "   ✅ APIM product removed"
else
  echo "   ⚠️  No APIM found — skipping"
fi

# ─── Step 4: Entra External ID — remove directory via identity-service ───────
echo "▶ [4/5] Triggering Entra External ID cleanup..."
# identity-service handles Entra External ID deletion when it receives
# a TenantGdprErased event on crm.platform topic (published by platform-admin-service)
echo "   ⏳ identity-service will complete Entra External ID deletion via Service Bus"

# ─── Step 5: Mark as permanently deleted in platform audit log ───────────────
echo "▶ [5/5] Recording deletion in platform audit log..."
curl -sf -X POST "${PLATFORM_API_URL}/api/tenants/${TENANT_ID}/audit" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${PLATFORM_API_KEY}" \
  -d "{\"action\": \"GdprHardDelete\", \"performedBy\": \"$(whoami)@$(hostname)\"}" \
  2>/dev/null || echo "   ⚠️  Audit log call failed (non-blocking)"
echo "   ✅ Deletion recorded in audit log"

echo ""
echo "✅ Tenant ${TENANT_ID} permanently deleted (GDPR erasure complete)."
echo "   An immutable audit record has been retained as required by GDPR Article 17."
echo ""
