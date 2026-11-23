#!/usr/bin/env bash
# End-to-end test against a running stack (docker compose up). Drives the whole
# checkout saga through the gateway: a successful order, a declined card (stock
# must be released) and an out-of-stock product.
set -euo pipefail
API="${API:-http://localhost:5100}"
pass() { echo "  ✓ $1"; }
fail() { echo "  ✗ $1"; exit 1; }

wait_for() { for _ in $(seq 1 60); do curl -fs "$1" >/dev/null 2>&1 && return 0; sleep 2; done; fail "timed out waiting for $1"; }

echo "Waiting for every service to be ready (database and message bus connected)..."
for svc in catalog basket ordering payment identity notification; do wait_for "$API/health/$svc"; done
wait_for "$API/api/catalog/categories"

EMAIL="e2e-$RANDOM@shopflow.dev"
TOKEN=$(curl -fs -X POST "$API/api/identity/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"Str0ng!Pass\",\"fullName\":\"E2E Buyer\"}" | jq -r .token)
[ -n "$TOKEN" ] && [ "$TOKEN" != null ] && pass "registered $EMAIL"
AUTH=(-H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json')

product() { curl -fs "$API/api/catalog/products?search=$1" | jq -r '.items[0] | "\(.id) \(.availableStock)"'; }
read -r KEYBOARD STOCK_BEFORE < <(product keyboard)
read -r HUB _ < <(product hub)

checkout() { # $1 product, $2 card -> prints order id
  curl -fs -X POST "$API/api/basket/items" "${AUTH[@]}" -d "{\"productId\":\"$1\",\"quantity\":1}" >/dev/null || true
  curl -fs -X POST "$API/api/basket/checkout" "${AUTH[@]}" \
    -d "{\"fullName\":\"E2E\",\"street\":\"1 Main\",\"city\":\"Islamabad\",\"country\":\"PK\",\"postalCode\":\"44000\",\"cardNumber\":\"$2\"}" | jq -r .orderId
}
status() { curl -fs "$API/api/orders/$1" "${AUTH[@]}" | jq -r .status 2>/dev/null || echo pending; }
await_status() { # $1 order, $2 expected
  for _ in $(seq 1 60); do s=$(status "$1"); [ "$s" = "$2" ] && return 0; [ "$s" = Confirmed ] || [ "$s" = Cancelled ] && break; sleep 1; done
  fail "order $1 ended as '$s', expected $2"
}

ORDER=$(checkout "$KEYBOARD" 4242424242424242)
await_status "$ORDER" Confirmed && pass "order confirmed through reserve-stock → payment → confirm"
read -r _ STOCK_AFTER < <(product keyboard)
[ "$STOCK_AFTER" -eq $((STOCK_BEFORE - 1)) ] && pass "stock decreased by 1 ($STOCK_BEFORE → $STOCK_AFTER)"

ORDER=$(checkout "$KEYBOARD" 4000000000000002)
await_status "$ORDER" Cancelled && pass "declined card cancels the order"
sleep 2
read -r _ STOCK_RELEASED < <(product keyboard)
[ "$STOCK_RELEASED" -eq "$STOCK_AFTER" ] && pass "reserved stock released after the failed payment"

# The hub has no stock, so it can't go in the basket; check that the API refuses it.
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$API/api/basket/items" "${AUTH[@]}" -d "{\"productId\":\"$HUB\",\"quantity\":1}")
[ "$CODE" = 409 ] && pass "out-of-stock product refused (409)"

for _ in $(seq 1 20); do N=$(curl -fs "$API/api/notifications" "${AUTH[@]}" | jq length); [ "$N" -ge 2 ] && break; sleep 1; done
[ "$N" -ge 2 ] && pass "buyer notified of both orders ($N notifications)"
echo "End-to-end checkout flow passed."
