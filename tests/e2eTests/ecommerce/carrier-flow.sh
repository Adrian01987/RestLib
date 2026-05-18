#!/usr/bin/env bash
# =============================================================================
# carrier-flow.sh - E2E tests for ecommerce carrier fulfillment
# =============================================================================
# Tests: carrier login, carrier-scoped shipment list, cross-carrier isolation,
# batch patch, append-only shipment events, and customer-visible status updates.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

AUTH_URL="${BASE_URL}/auth"
ADMIN_CARRIERS_URL="${BASE_URL}/api/admin/carriers"
STOREFRONT_PRODUCTS_URL="${BASE_URL}/api/v1/storefront/products"
CARTS_URL="${BASE_URL}/api/storefront/carts"
CHECKOUT_URL="${BASE_URL}/api/storefront/checkout"
STOREFRONT_ORDERS_URL="${BASE_URL}/api/storefront/orders"
CARRIER_SHIPMENTS_URL="${BASE_URL}/api/carrier/shipments"

BOOTSTRAP_KEY="${E2E_ADMIN_BOOTSTRAP_KEY:-dev-bootstrap-key}"
ADMIN_USER_NAME="${E2E_ADMIN_USER_NAME:-admin}"
ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-admin@example.com}"
ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-admin-password}"

CARRIER_RUN_ID="${E2E_CARRIER_FLOW_RUN_ID:-$(date +%s)-$$}"
FLOW_CARRIER_USER_NAME="${E2E_CARRIER_FLOW_USER_NAME:-carrier-flow-${CARRIER_RUN_ID}}"
FLOW_CARRIER_EMAIL="${E2E_CARRIER_FLOW_EMAIL:-carrier-flow-${CARRIER_RUN_ID}@example.com}"
FLOW_CARRIER_PASSWORD="${E2E_CARRIER_FLOW_PASSWORD:-carrier-password}"
FLOW_CARRIER_DISPLAY_NAME="${E2E_CARRIER_FLOW_DISPLAY_NAME:-! Carrier Flow ${CARRIER_RUN_ID}}"

SEED_CARRIER_USER_NAME="${E2E_SEED_CARRIER_USER_NAME:-carrier}"
SEED_CARRIER_PASSWORD="${E2E_SEED_CARRIER_PASSWORD:-carrier-password}"

ADMIN_TOKEN=""
CARRIER_TOKEN=""
OTHER_CARRIER_TOKEN=""
CUSTOMER_TOKEN=""
CUSTOMER_ID=""
CART_ID=""
PRODUCT_ID=""
ORDER_ID=""
SHIPMENT_ID=""
FLOW_CARRIER_ID=""

header "Ecommerce Carrier Flow - E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# HTTP helpers with bearer tokens
# =============================================================================
http_request_with_headers() {
  local method="$1"
  local url="$2"
  local body="$3"
  shift 3
  local extra_headers=("$@")
  local tmpbody tmpheaders

  tmpbody=$(mktemp)
  tmpheaders=$(mktemp)

  local curl_args=(-s -g -D "$tmpheaders" -o "$tmpbody" -w "%{http_code}" -X "$method")
  if [ -n "$body" ]; then
    curl_args+=(-H "Content-Type: application/json" -d "$body")
  fi
  for header in "${extra_headers[@]}"; do
    curl_args+=(-H "$header")
  done
  curl_args+=("$url")

  HTTP_STATUS=$(curl "${curl_args[@]}")
  HTTP_BODY=$(cat "$tmpbody")
  HTTP_HEADERS=$(cat "$tmpheaders")
  rm -f "$tmpbody" "$tmpheaders"
}

http_get_admin() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${ADMIN_TOKEN}"
}

http_post_admin() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${ADMIN_TOKEN}"
}

http_get_carrier() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CARRIER_TOKEN}"
}

http_post_carrier() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CARRIER_TOKEN}"
}

http_get_other_carrier() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${OTHER_CARRIER_TOKEN}"
}

http_get_customer() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer_empty() {
  http_request_with_headers POST "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

resolve_url() {
  local href="$1"
  case "$href" in
    http://*|https://*) printf "%s" "$href" ;;
    /*) printf "%s%s" "$BASE_URL" "$href" ;;
    *) printf "%s/%s" "$BASE_URL" "$href" ;;
  esac
}

login_actor() {
  local user_name_or_email="$1"
  local password="$2"
  local expected_role="$3"
  local token_variable="$4"
  local body token

  body=$(jq -n \
    --arg user_name_or_email "$user_name_or_email" \
    --arg password "$password" \
    '{user_name_or_email:$user_name_or_email,password:$password}')

  http_post "${AUTH_URL}/login" "$body"

  assert_http_status "200"                               || return 1
  assert_json_field ".user.role" "$expected_role"        || return 1

  token=$(jq_val ".access_token")
  assert_ne "${expected_role} token" "$token" "null"     || return 1
  assert_ne "${expected_role} token" "$token" ""         || return 1

  printf -v "$token_variable" "%s" "$token"
}

poll_customer_order_status() {
  local expected="$1"
  local max_attempts="${2:-10}"
  local attempt status

  for attempt in $(seq 1 "$max_attempts"); do
    http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
    if [ "$HTTP_STATUS" = "200" ]; then
      status=$(jq_val ".status")
      if [ "$status" = "$expected" ]; then
        pass "customer order status reached '${expected}'"
        return 0
      fi
    fi

    sleep 1
  done

  fail "order ${ORDER_ID} did not reach status '${expected}'"
  echo "$HTTP_BODY" | jq . 2>/dev/null || echo "$HTTP_BODY"
  return 1
}

create_customer_order() {
  local suffix="$1"
  local user_name email body shipment_id

  user_name="carrier-flow-customer-${suffix}-${CARRIER_RUN_ID}"
  email="${user_name}@example.com"

  body=$(jq -n \
    --arg user_name "$user_name" \
    --arg email "$email" \
    --arg password "customer-password" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${AUTH_URL}/register-customer" "$body"
  assert_http_status "201"                               || return 1
  assert_json_field ".user.role" "Customer"              || return 1

  CUSTOMER_ID=$(jq_val ".user.id")
  CUSTOMER_TOKEN=$(jq_val ".access_token")
  assert_ne "customer id" "$CUSTOMER_ID" ""              || return 1
  assert_ne "customer token" "$CUSTOMER_TOKEN" ""        || return 1

  http_get "${STOREFRONT_PRODUCTS_URL}?is_active=true&fields=id,name,price,stock_on_hand&limit=20"
  assert_http_status "200"                               || return 1

  PRODUCT_ID=$(echo "$HTTP_BODY" | jq -r 'first(.items[] | select(.stock_on_hand > 0) | .id) // empty')
  assert_ne "in-stock product id" "$PRODUCT_ID" ""       || return 1

  http_post_customer "$CARTS_URL" '{}'
  assert_http_status "201"                               || return 1
  assert_json_field ".customer_id" "$CUSTOMER_ID"        || return 1
  assert_json_field ".status" "ACTIVE"                   || return 1

  CART_ID=$(jq_val ".id")
  assert_ne "cart id" "$CART_ID" ""                      || return 1

  body=$(jq -n --arg product_id "$PRODUCT_ID" '{product_id:$product_id,quantity:1}')
  http_post_customer "${CARTS_URL}/${CART_ID}/items" "$body"
  assert_http_status "201"                               || return 1
  assert_json_field ".cart_id" "$CART_ID"                || return 1
  assert_json_field ".product_id" "$PRODUCT_ID"          || return 1

  http_post_customer "$CHECKOUT_URL" '{"payment_method":"card"}'
  assert_http_status "201"                               || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1

  ORDER_ID=$(jq_val ".order_id")
  shipment_id=$(jq_val ".shipment_id")
  assert_ne "order id" "$ORDER_ID" ""                    || return 1
  assert_ne "shipment id" "$shipment_id" ""              || return 1

  SHIPMENT_ID="$shipment_id"
}

append_shipment_event() {
  local status="$1"
  local expected_order_status="$2"
  local body

  body=$(jq -n \
    --arg status "$status" \
    --arg notes "Carrier flow event ${status}" \
    '{status:$status,notes:$notes}')

  http_post_carrier "${CARRIER_SHIPMENTS_URL}/${SHIPMENT_ID}/events" "$body"
  assert_http_status "201"                               || return 1
  assert_json_field ".shipment_id" "$SHIPMENT_ID"        || return 1
  assert_json_field ".status" "$status"                  || return 1

  poll_customer_order_status "$expected_order_status"    || return 1
}

# =============================================================================
# TEST 1: Bootstrap or login admin
# =============================================================================
test_admin_login() {
  local bootstrap_body login_body
  bootstrap_body=$(jq -n \
    --arg user_name "$ADMIN_USER_NAME" \
    --arg email "$ADMIN_EMAIL" \
    --arg password "$ADMIN_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_request_with_headers POST "${AUTH_URL}/admin-bootstrap" "$bootstrap_body" \
    "X-Bootstrap-Key: ${BOOTSTRAP_KEY}"

  if [ "$HTTP_STATUS" = "201" ]; then
    ADMIN_TOKEN=$(jq_val ".access_token")
    assert_ne "admin token" "$ADMIN_TOKEN" "null"        || return 1
    assert_ne "admin token" "$ADMIN_TOKEN" ""            || return 1
    pass "Bootstrapped admin user"
    return 0
  fi

  if [ "$HTTP_STATUS" != "409" ]; then
    fail "Admin bootstrap returned ${HTTP_STATUS}; expected 201 or 409"
    echo "$HTTP_BODY" | jq . 2>/dev/null || echo "$HTTP_BODY"
    return 1
  fi

  login_body=$(jq -n \
    --arg user_name_or_email "$ADMIN_USER_NAME" \
    --arg password "$ADMIN_PASSWORD" \
    '{user_name_or_email:$user_name_or_email,password:$password}')

  http_post "${AUTH_URL}/login" "$login_body"

  assert_http_status "200"                               || return 1
  ADMIN_TOKEN=$(jq_val ".access_token")
  assert_ne "admin token" "$ADMIN_TOKEN" "null"          || return 1
  assert_ne "admin token" "$ADMIN_TOKEN" ""              || return 1
}

# =============================================================================
# TEST 2: Admin provisions a dedicated carrier
# =============================================================================
test_admin_creates_carrier() {
  local body user_id
  body=$(jq -n \
    --arg user_name "$FLOW_CARRIER_USER_NAME" \
    --arg email "$FLOW_CARRIER_EMAIL" \
    --arg password "$FLOW_CARRIER_PASSWORD" \
    --arg display_name "$FLOW_CARRIER_DISPLAY_NAME" \
    '{user_name:$user_name,email:$email,password:$password,display_name:$display_name,service_area:"Carrier flow service area"}')

  http_post_admin "$ADMIN_CARRIERS_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".display_name" "$FLOW_CARRIER_DISPLAY_NAME" || return 1
  assert_json_field ".service_area" "Carrier flow service area" || return 1

  FLOW_CARRIER_ID=$(jq_val ".id")
  user_id=$(jq_val ".user_id")
  assert_ne "carrier id" "$FLOW_CARRIER_ID" ""           || return 1
  assert_eq "carrier id matches user id" "$FLOW_CARRIER_ID" "$user_id" || return 1
}

# =============================================================================
# TEST 3: Carrier login succeeds
# =============================================================================
test_carrier_login() {
  login_actor "$FLOW_CARRIER_USER_NAME" "$FLOW_CARRIER_PASSWORD" "Carrier" CARRIER_TOKEN
  login_actor "$SEED_CARRIER_USER_NAME" "$SEED_CARRIER_PASSWORD" "Carrier" OTHER_CARRIER_TOKEN
}

# =============================================================================
# TEST 4: Create a shipment assigned to the dedicated carrier
# =============================================================================
test_create_assigned_shipment_for_carrier() {
  local carrier_count attempts attempt found

  http_get_admin "${ADMIN_CARRIERS_URL}?limit=100"
  assert_http_status "200"                               || return 1
  carrier_count=$(jq_len ".items")
  attempts=$((carrier_count + 2))
  if [ "$attempts" -lt 4 ]; then
    attempts=4
  fi

  for attempt in $(seq 1 "$attempts"); do
    create_customer_order "$attempt"                     || return 1

    http_get_carrier "${CARRIER_SHIPMENTS_URL}?limit=100&fields=id,order_id,status,carrier_id"
    assert_http_status "200"                             || return 1

    found=$(echo "$HTTP_BODY" | jq -r --arg id "$SHIPMENT_ID" 'first(.items[] | select(.id == $id) | .id) // empty')
    if [ "$found" = "$SHIPMENT_ID" ]; then
      pass "shipment ${SHIPMENT_ID} assigned to dedicated carrier"
      return 0
    fi
  done

  fail "No checkout shipment was assigned to carrier ${FLOW_CARRIER_ID} after ${attempts} attempts"
  return 1
}

# =============================================================================
# TEST 5: Carrier shipment list is scoped
# =============================================================================
test_carrier_list_scoped_shipments() {
  local listed_id other_listed_id

  http_get_carrier "${CARRIER_SHIPMENTS_URL}?limit=100&fields=id,order_id,status,carrier_id"
  assert_http_status "200"                               || return 1

  listed_id=$(echo "$HTTP_BODY" | jq -r --arg id "$SHIPMENT_ID" 'first(.items[] | select(.id == $id) | .id) // empty')
  assert_eq "carrier list includes assigned shipment" "$listed_id" "$SHIPMENT_ID" || return 1

  http_get_other_carrier "${CARRIER_SHIPMENTS_URL}?limit=100&fields=id"
  assert_http_status "200"                               || return 1

  other_listed_id=$(echo "$HTTP_BODY" | jq -r --arg id "$SHIPMENT_ID" 'first(.items[] | select(.id == $id) | .id) // empty')
  assert_eq "other carrier list excludes assigned shipment" "$other_listed_id" "" || return 1
}

# =============================================================================
# TEST 6: Another carrier cannot read the shipment by id
# =============================================================================
test_other_carrier_cannot_read_shipment_by_id() {
  http_get_other_carrier "${CARRIER_SHIPMENTS_URL}/${SHIPMENT_ID}"

  assert_http_status "404"                               || return 1
}

# =============================================================================
# TEST 7: Carrier batch patches the shipment
# =============================================================================
test_batch_patch_shipment() {
  local body

  body=$(jq -n \
    --arg id "$SHIPMENT_ID" \
    '{action:"patch",items:[{id:$id,body:{status:"CARRIER ACKNOWLEDGED"}}]}')

  http_post_carrier "${CARRIER_SHIPMENTS_URL}/batch" "$body"

  assert_http_status "200"                               || return 1
  assert_items_count "1"                                 || return 1
  assert_item_status 0 "200"                             || return 1
  assert_json_field ".items[0].entity.id" "$SHIPMENT_ID" || return 1
  assert_json_field ".items[0].entity.status" "CARRIER ACKNOWLEDGED" || return 1
}

# =============================================================================
# TEST 8: Carrier appends ASSIGNED event and customer still sees ASSIGNED
# =============================================================================
test_append_assigned_event() {
  append_shipment_event "ASSIGNED" "ASSIGNED"
}

# =============================================================================
# TEST 9: Customer pays order before fulfillment progress
# =============================================================================
test_customer_pays_order() {
  local pay_href pay_url

  http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1
  assert_json_field_exists "._links.pay.href"            || return 1

  pay_href=$(jq_val "._links.pay.href")
  pay_url=$(resolve_url "$pay_href")
  http_post_customer_empty "$pay_url"

  assert_http_status "200"                               || return 1
  assert_json_field ".order_id" "$ORDER_ID"              || return 1
  assert_json_field ".status" "PAID"                     || return 1

  poll_customer_order_status "PAID"                      || return 1
}

# =============================================================================
# TEST 10: Carrier events propagate to customer-visible order status
# =============================================================================
test_append_fulfillment_events() {
  append_shipment_event "ON THE WAY" "ON THE WAY"        || return 1
  append_shipment_event "NOT DELIVERED" "NOT DELIVERED"  || return 1
  append_shipment_event "ON THE WAY" "ON THE WAY"        || return 1
  append_shipment_event "DELIVERED" "DELIVERED"          || return 1

  http_get_carrier "${CARRIER_SHIPMENTS_URL}/${SHIPMENT_ID}"
  assert_http_status "200"                               || return 1
  assert_json_field ".status" "DELIVERED"                || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Bootstrap or Login Admin"                      test_admin_login
run_test "Admin Creates Carrier"                         test_admin_creates_carrier
run_test "Carrier Login"                                 test_carrier_login
run_test "Create Assigned Shipment for Carrier"          test_create_assigned_shipment_for_carrier
run_test "Carrier Shipment List Is Scoped"               test_carrier_list_scoped_shipments
run_test "Other Carrier Cannot Read Shipment by ID"      test_other_carrier_cannot_read_shipment_by_id
run_test "Batch Patch Shipment"                          test_batch_patch_shipment
run_test "Append ASSIGNED Event"                         test_append_assigned_event
run_test "Customer Pays Order"                           test_customer_pays_order
run_test "Append Fulfillment Events"                     test_append_fulfillment_events

print_summary
exit $?
