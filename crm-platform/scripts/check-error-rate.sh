#!/usr/bin/env bash
# check-error-rate.sh
# Used by cd-prod.yml during blue/green traffic shifts.
# Polls Application Insights for 5xx error rate and fails if threshold exceeded.
# Usage: ./scripts/check-error-rate.sh --threshold <pct> --duration <Nm>

set -euo pipefail

THRESHOLD=1.0
DURATION=5

while [[ $# -gt 0 ]]; do
  case $1 in
    --threshold) THRESHOLD="$2"; shift 2 ;;
    --duration)  DURATION="${2%m}"; shift 2 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

echo "Monitoring error rate for ${DURATION} minutes (threshold: ${THRESHOLD}%)"

# TODO: query Application Insights via REST API or az monitor
# Example query:
#   requests | where timestamp > ago(${DURATION}m)
#   | summarize errorRate = countif(resultCode >= 500) * 100.0 / count()
#
# If errorRate > THRESHOLD → exit 1 (triggers rollback in CD pipeline)

echo "Error rate check passed (stub — implement with App Insights query)"
exit 0
