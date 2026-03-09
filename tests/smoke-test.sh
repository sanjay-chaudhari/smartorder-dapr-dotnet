#!/usr/bin/env bash
# SmartOrder Smoke Test Suite
# Requires: running Docker Compose stack (docker compose up -d)
# Usage: bash tests/smoke-test.sh

set -uo pipefail

ORDER_SVC="http://localhost:5001"
INVENTORY_SVC="http://localhost:5002"
PAYMENT_SVC="http://localhost:5003"
NOTIFICATION_SVC="http://localhost:5004"
WORKFLOW_SVC="http://localhost:5005"

PASS=0
FAIL=0

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "${GREEN}  PASS${NC} $1"; PASS=$((PASS+1)); }
fail() { echo -e "${RED}  FAIL${NC} $1 — $2"; FAIL=$((FAIL+1)); }
section() { echo -e "\n${YELLOW}=== $1 ===${NC}"; }

assert_status() {
  local label="$1" expected="$2" actual="$3"
  if [[ "$actual" == "$expected" ]]; then pass "$label (HTTP $actual)"
  else fail "$label" "expected HTTP $expected, got HTTP $actual"; fi
}

assert_contains() {
  local label="$1" needle="$2" haystack="$3"
  if echo "$haystack" | grep -q "$needle"; then pass "$label"
  else fail "$label" "expected '$needle' in response: $haystack"; fi
}

# ─── Seed inventory ───────────────────────────────────────────────────────────
section "Setup: Seed Inventory"
docker compose exec -T inventory-service curl -s -X POST http://localhost:3502/v1.0/state/statestore \
  -H "Content-Type: application/json" \
  -d '[{"key":"inventory-prod-001","value":{"productId":"prod-001","availableQuantity":100,"reservedQuantity":0}}]' \
  > /dev/null && pass "Inventory seeded (prod-001, qty=100)"

# ─── Health checks ────────────────────────────────────────────────────────────
section "Health Checks"
for entry in \
  "order-service:$ORDER_SVC" \
  "inventory-service:$INVENTORY_SVC" \
  "payment-service:$PAYMENT_SVC" \
  "notification-service:$NOTIFICATION_SVC" \
  "workflow-orchestrator:$WORKFLOW_SVC"; do
  name="${entry%%:*}"
  url="${entry#*:}"
  status=$(curl -s -o /dev/null -w "%{http_code}" "$url/health")
  assert_status "$name /health" "200" "$status"
done

# ─── Order Service ────────────────────────────────────────────────────────────
section "Order Service — POST /orders"

# Valid order
resp=$(curl -s -w "\n%{http_code}" -X POST "$ORDER_SVC/orders" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":2,"price":19.99}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /orders valid" "202" "$status"
assert_contains "POST /orders returns orderId" '"orderId"' "$body"
ORDER_ID=$(echo "$body" | python3 -c "import sys,json; print(json.load(sys.stdin)['orderId'])" 2>/dev/null || echo "")

# Zero quantity → 400
status=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$ORDER_SVC/orders" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":0,"price":9.99}')
assert_status "POST /orders zero quantity" "400" "$status"

# Negative price → 400
status=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$ORDER_SVC/orders" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":1,"price":-5}')
assert_status "POST /orders negative price" "400" "$status"

# Quantity > 100 → 422
status=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$ORDER_SVC/orders" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":101,"price":9.99}')
assert_status "POST /orders quantity > 100" "422" "$status"

# Missing productId → 400
status=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$ORDER_SVC/orders" \
  -H "Content-Type: application/json" \
  -d '{"quantity":1,"price":9.99}')
assert_status "POST /orders missing productId" "400" "$status"

section "Order Service — GET /orders/{id}"

# Existing order
if [[ -n "$ORDER_ID" ]]; then
  resp=$(curl -s -w "\n%{http_code}" "$ORDER_SVC/orders/$ORDER_ID")
  body=$(echo "$resp" | head -1)
  status=$(echo "$resp" | tail -1)
  assert_status "GET /orders/{id} existing" "200" "$status"
  assert_contains "GET /orders/{id} returns orderId" '"orderId"' "$body"
  assert_contains "GET /orders/{id} returns productId" '"productId"' "$body"
else
  fail "GET /orders/{id} existing" "no orderId captured from POST"
fi

# Non-existent order
status=$(curl -s -o /dev/null -w "%{http_code}" "$ORDER_SVC/orders/nonexistent-order-id")
assert_status "GET /orders/{id} nonexistent" "404" "$status"

# ─── Inventory Service ────────────────────────────────────────────────────────
section "Inventory Service — POST /inventory/reserve"

resp=$(curl -s -w "\n%{http_code}" -X POST "$INVENTORY_SVC/inventory/reserve" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":1,"orderId":"smoke-test-order-1"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /inventory/reserve valid" "200" "$status"
assert_contains "POST /inventory/reserve returns success" '"success"' "$body"

# Reserve more than available → 422 UnprocessableEntity with success:false
resp=$(curl -s -w "\n%{http_code}" -X POST "$INVENTORY_SVC/inventory/reserve" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":9999,"orderId":"smoke-test-order-2"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /inventory/reserve insufficient stock" "422" "$status"
assert_contains "POST /inventory/reserve insufficient stock returns success:false" '"success":false' "$body"

# ─── Inventory Actor Endpoints ────────────────────────────────────────────────
section "Inventory Service — Virtual Actors"

# Seed actor state via the actor seed endpoint (PUT /inventory/actor/{productId}/stock)
status=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "$INVENTORY_SVC/inventory/actor/prod-actor-001/stock" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-actor-001","availableQuantity":50,"reservedQuantity":0}')
assert_status "Actor inventory seeded (prod-actor-001, qty=50)" "200" "$status"

# Reserve via actor
resp=$(curl -s -w "\n%{http_code}" -X POST "$INVENTORY_SVC/inventory/actor/reserve" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-actor-001","quantity":5,"orderId":"smoke-actor-order-1"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /inventory/actor/reserve valid" "200" "$status"
assert_contains "POST /inventory/actor/reserve returns success" '"success"' "$body"

# Get stock via actor
resp=$(curl -s -w "\n%{http_code}" "$INVENTORY_SVC/inventory/actor/prod-actor-001/stock")
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "GET /inventory/actor/{productId}/stock" "200" "$status"
assert_contains "GET /inventory/actor stock returns productId" '"productId"' "$body"

# Release via actor
resp=$(curl -s -w "\n%{http_code}" -X POST "$INVENTORY_SVC/inventory/actor/release" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-actor-001","quantity":5,"orderId":"smoke-actor-order-1"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /inventory/actor/release valid" "200" "$status"

# ─── Payment Service ──────────────────────────────────────────────────────────
section "Payment Service — POST /payments/process"

resp=$(curl -s -w "\n%{http_code}" -X POST "$PAYMENT_SVC/payments/process" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"smoke-test-order-1","amount":19.99,"customerId":"cust-001"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /payments/process valid" "200" "$status"
assert_contains "POST /payments/process returns success" '"success"' "$body"

# ─── Notification Service ─────────────────────────────────────────────────────
section "Notification Service — POST /notifications/send"

resp=$(curl -s -w "\n%{http_code}" -X POST "$NOTIFICATION_SVC/notifications/send" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"smoke-test-order-1","customerId":"cust-001","message":"Your order is confirmed"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /notifications/send valid" "200" "$status"

# ─── Workflow Orchestrator — Full Saga ────────────────────────────────────────
section "Workflow Orchestrator — Full Order Saga"

resp=$(curl -s -w "\n%{http_code}" -X POST "$WORKFLOW_SVC/workflow/orders" \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":2,"price":29.99,"customerId":"cust-saga-1"}')
body=$(echo "$resp" | head -1)
status=$(echo "$resp" | tail -1)
assert_status "POST /workflow/orders start saga" "202" "$status"
assert_contains "POST /workflow/orders returns instanceId" '"instanceId"' "$body"
INSTANCE_ID=$(echo "$body" | python3 -c "import sys,json; print(json.load(sys.stdin)['instanceId'])" 2>/dev/null || echo "")

# Poll for completion (up to 15s)
if [[ -n "$INSTANCE_ID" ]]; then
  echo "  Waiting for saga $INSTANCE_ID to complete..."
  SAGA_STATUS=""
  for i in $(seq 1 15); do
    sleep 1
    SAGA_RESP=$(curl -s "$WORKFLOW_SVC/workflow/orders/$INSTANCE_ID")
    SAGA_STATUS=$(echo "$SAGA_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" 2>/dev/null || echo "")
    if [[ "$SAGA_STATUS" == "1" ]]; then break; fi
  done
  if [[ "$SAGA_STATUS" == "1" ]]; then
    pass "Saga completed (status=Completed)"
  else
    fail "Saga completion" "expected status=1 (Completed), got status=$SAGA_STATUS after 15s"
  fi

  # GET workflow status
  resp=$(curl -s -w "\n%{http_code}" "$WORKFLOW_SVC/workflow/orders/$INSTANCE_ID")
  body=$(echo "$resp" | head -1)
  status=$(echo "$resp" | tail -1)
  assert_status "GET /workflow/orders/{instanceId}" "200" "$status"
  assert_contains "GET /workflow/orders/{instanceId} returns instanceId" '"instanceId"' "$body"
else
  fail "Saga start" "no instanceId captured"
fi

# Non-existent workflow instance
status=$(curl -s -o /dev/null -w "%{http_code}" "$WORKFLOW_SVC/workflow/orders/nonexistent-instance")
# Dapr returns 500 for unknown instance — acceptable
if [[ "$status" == "404" || "$status" == "500" || "$status" == "200" ]]; then
  pass "GET /workflow/orders/{nonexistent} handled ($status)"
else
  fail "GET /workflow/orders/{nonexistent}" "unexpected status $status"
fi

# ─── Output Binding ───────────────────────────────────────────────────────────
section "Order Service — Output Binding (Webhook)"

if [[ -n "$ORDER_ID" ]]; then
  resp=$(curl -s -w "\n%{http_code}" -X POST "$ORDER_SVC/orders/$ORDER_ID/notify-webhook")
  body=$(echo "$resp" | head -1)
  status=$(echo "$resp" | tail -1)
  assert_status "POST /orders/{id}/notify-webhook" "200" "$status"
  assert_contains "Webhook response contains webhookSent" '"webhookSent"' "$body"
else
  fail "POST /orders/{id}/notify-webhook" "no orderId captured"
fi

# ─── Summary ──────────────────────────────────────────────────────────────────
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
TOTAL=$((PASS + FAIL))
echo -e "Results: ${GREEN}$PASS passed${NC} / ${RED}$FAIL failed${NC} / $TOTAL total"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [[ $FAIL -gt 0 ]]; then exit 1; fi
