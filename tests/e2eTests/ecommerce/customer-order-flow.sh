#!/usr/bin/env bash
# =============================================================================
# customer-order-flow.sh - E2E tests for ecommerce customer order flow
# =============================================================================
# Tests: register, login, browse, add to cart, checkout, observe order links by
# status, and confirm delivery through the advertised HATEOAS command link.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

AUTH_URL="${BASE_URL}/auth"
STOREFRONT_PRODUCTS_URL="${BASE_URL}/api/v1/storefront/products"
CARTS_URL="${BASE_URL}/api/storefront/carts"
CHECKOUT_URL="${BASE_URL}/api/storefront/checkout"
STOREFRONT_ORDERS_URL="${BASE_URL}/api/storefront/orders"
ADMIN_ORDERS_URL="${BASE_URL}/api/admin/orders"

BOOTSTRAP_KEY="${E2E_ADMIN_BOOTSTRAP_KEY:-dev-bootstrap-key}"
ADMIN_USER_NAME="${E2E_ADMIN_USER_NAME:-admin}"
ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-admin@example.com}"
ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-admin-password}"

ORDER_RUN_ID="${E2E_CUSTOMER_ORDER_RUN_ID:-$(date +%s)-$$}"
CUSTOMER_USER_NAME="${E2E_CUSTOMER_ORDER_USER_NAME:-order-${ORDER_RUN_ID}}"
CUSTOMER_EMAIL="${E2E_CUSTOMER_ORDER_EMAIL:-order-${ORDER_RUN_ID}@example.com}"
CUSTOMER_PASSWORD="${E2E_CUSTOMER_ORDER_PASSWORD:-customer-password}"

ADMIN_TOKEN=""
CUSTOMER_TOKEN=""
CUSTOMER_ID=""
CART_ID=""
PRODUCT_ID=""
PRODUCT_NAME=""
ORDER_ID=""

header "Ecommerce Customer Order Flow - E2E Tests"
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

http_get_customer() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer_empty() {
  http_request_with_headers POST "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_get_admin() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${ADMIN_TOKEN}"
}

http_patch_admin_if_match() {
  local url="$1"
  local body="$2"
  local etag="$3"
  http_request_with_headers PATCH "$url" "$body" \
    "Authorization: Bearer ${ADMIN_TOKEN}" \
    "If-Match: ${etag}"
}

resolve_url() {
  local href="$1"
  case "$href" in
    http://*|https://*) printf "%s" "$href" ;;
    /*) printf "%s%s" "$BASE_URL" "$href" ;;
    *) printf "%s/%s" "$BASE_URL" "$href" ;;
  esac
}

assert_order_link() {
  local rel="$1"
  local method="$2"
  local path="._links.${rel}"
  if [ "$rel" = "confirm-delivery" ]; then
    path='._links["confirm-delivery"]'
  fi

  assert_json_field_exists "${path}.href"                || return 1
  assert_json_field "${path}.method" "$method"           || return 1
}

assert_order_link_absent() {
  local rel="$1"
  local path="._links.${rel}"
  if [ "$rel" = "confirm-delivery" ]; then
    path='._links["confirm-delivery"]'
  fi

  assert_json_field_null "$path"
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
        pass "order status reached '${expected}'"
        return 0
      fi
    fi

    sleep 1
  done

  fail "order ${ORDER_ID} did not reach status '${expected}'"
  echo "$HTTP_BODY" | jq . 2>/dev/null || echo "$HTTP_BODY"
  return 1
}

transition_order_status() {
  local target_status="$1"
  local etag body

  http_get_admin "${ADMIN_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  etag=$(get_header "ETag")
  assert_ne "order ETag" "$etag" ""                      || return 1

  body=$(jq -n --arg status "$target_status" '{status:$status}')
  http_patch_admin_if_match "${ADMIN_ORDERS_URL}/${ORDER_ID}" "$body" "$etag"

  assert_http_status "200"                               || return 1
  assert_json_field ".status" "$target_status"           || return 1
}

# =============================================================================
# TEST 1: Customer self-registration succeeds
# =============================================================================
test_register_customer() {
  local body
  body=$(jq -n \
    --arg user_name "$CUSTOMER_USER_NAME" \
    --arg email "$CUSTOMER_EMAIL" \
    --arg password "$CUSTOMER_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${AUTH_URL}/register-customer" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".user.role" "Customer"              || return 1
  assert_json_field ".user.user_name" "$CUSTOMER_USER_NAME" || return 1

  CUSTOMER_ID=$(jq_val ".user.id")
  CUSTOMER_TOKEN=$(jq_val ".access_token")
  assert_ne "customer id" "$CUSTOMER_ID" "null"          || return 1
  assert_ne "customer token" "$CUSTOMER_TOKEN" "null"    || return 1
  assert_ne "customer token" "$CUSTOMER_TOKEN" ""        || return 1
}

# =============================================================================
# TEST 2: Customer login succeeds
# =============================================================================
test_customer_login() {
  local body
  body=$(jq -n \
    --arg user_name_or_email "$CUSTOMER_USER_NAME" \
    --arg password "$CUSTOMER_PASSWORD" \
    '{user_name_or_email:$user_name_or_email,password:$password}')

  http_post "${AUTH_URL}/login" "$body"

  assert_http_status "200"                               || return 1
  assert_json_field ".user.id" "$CUSTOMER_ID"            || return 1
  assert_json_field ".user.role" "Customer"              || return 1

  CUSTOMER_TOKEN=$(jq_val ".access_token")
  assert_ne "customer login token" "$CUSTOMER_TOKEN" "null" || return 1
  assert_ne "customer login token" "$CUSTOMER_TOKEN" ""  || return 1
}

# =============================================================================
# TEST 3: Customer browses products and selects an in-stock item
# =============================================================================
test_browse_and_select_product() {
  http_get "${STOREFRONT_PRODUCTS_URL}?is_active=true&fields=id,name,price,stock_on_hand&limit=20"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1

  PRODUCT_ID=$(echo "$HTTP_BODY" | jq -r 'first(.items[] | select(.stock_on_hand > 0) | .id) // empty')
  assert_ne "in-stock product id" "$PRODUCT_ID" ""       || return 1

  PRODUCT_NAME=$(echo "$HTTP_BODY" | jq -r --arg id "$PRODUCT_ID" '.items[] | select(.id == $id) | .name')
  assert_ne "in-stock product name" "$PRODUCT_NAME" ""   || return 1
}

# =============================================================================
# TEST 4: Customer creates an active cart
# =============================================================================
test_create_active_cart() {
  http_post_customer "$CARTS_URL" '{}'

  assert_http_status "201"                               || return 1
  assert_json_field ".customer_id" "$CUSTOMER_ID"        || return 1
  assert_json_field ".status" "ACTIVE"                   || return 1

  CART_ID=$(jq_val ".id")
  assert_ne "cart id" "$CART_ID" "null"                  || return 1
  assert_ne "cart id" "$CART_ID" ""                      || return 1
}

# =============================================================================
# TEST 5: Customer adds the selected product to the cart
# =============================================================================
test_add_product_to_cart() {
  local body
  body=$(jq -n --arg product_id "$PRODUCT_ID" '{product_id:$product_id,quantity:1}')

  http_post_customer "${CARTS_URL}/${CART_ID}/items" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".cart_id" "$CART_ID"                || return 1
  assert_json_field ".product_id" "$PRODUCT_ID"          || return 1
  assert_json_field ".quantity" "1"                      || return 1
}

# =============================================================================
# TEST 6: Customer checks out the active cart
# =============================================================================
test_checkout_cart() {
  http_post_customer "$CHECKOUT_URL" '{"payment_method":"card"}'

  assert_http_status "201"                               || return 1
  assert_json_field ".payment_method" "card"             || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1

  ORDER_ID=$(jq_val ".order_id")
  assert_ne "order id" "$ORDER_ID" "null"                || return 1
  assert_ne "order id" "$ORDER_ID" ""                    || return 1
}

# =============================================================================
# TEST 7: ASSIGNED order exposes pay link only
# =============================================================================
test_assigned_order_links() {
  poll_customer_order_status "ASSIGNED"                  || return 1

  assert_order_link "pay" "POST"                         || return 1
  assert_order_link_absent "cancel"                      || return 1
  assert_order_link_absent "confirm-delivery"            || return 1
}

# =============================================================================
# TEST 8: Bootstrap or login admin for status progression
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
# TEST 9: Admin progresses order to delivery and customer links track status
# =============================================================================
test_delivery_status_links() {
  transition_order_status "PAID"                         || return 1
  poll_customer_order_status "PAID"                      || return 1
  assert_order_link_absent "cancel"                      || return 1
  assert_order_link_absent "pay"                         || return 1
  assert_order_link_absent "confirm-delivery"            || return 1

  transition_order_status "ON THE WAY"                   || return 1
  poll_customer_order_status "ON THE WAY"                || return 1
  assert_order_link_absent "cancel"                      || return 1
  assert_order_link_absent "pay"                         || return 1
  assert_order_link_absent "confirm-delivery"            || return 1

  transition_order_status "DELIVERED"                    || return 1
  poll_customer_order_status "DELIVERED"                 || return 1
  assert_order_link_absent "cancel"                      || return 1
  assert_order_link_absent "pay"                         || return 1
  assert_order_link "confirm-delivery" "POST"            || return 1
}

# =============================================================================
# TEST 10: Customer confirms delivery through the advertised link
# =============================================================================
test_confirm_delivery() {
  local confirm_href confirm_url

  http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  confirm_href=$(jq_val '._links["confirm-delivery"].href')
  assert_ne "confirm-delivery href" "$confirm_href" "null" || return 1
  assert_ne "confirm-delivery href" "$confirm_href" ""   || return 1

  confirm_url=$(resolve_url "$confirm_href")
  http_post_customer_empty "$confirm_url"

  assert_http_status "200"                               || return 1
  assert_json_field ".status" "DELIVERY CONFIRMED"       || return 1

  poll_customer_order_status "DELIVERY CONFIRMED"        || return 1
  assert_order_link_absent "confirm-delivery"            || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Register Customer"                             test_register_customer
run_test "Customer Login"                                test_customer_login
run_test "Browse and Select Product"                     test_browse_and_select_product
run_test "Create Active Cart"                            test_create_active_cart
run_test "Add Product to Cart"                           test_add_product_to_cart
run_test "Checkout Cart"                                 test_checkout_cart
run_test "ASSIGNED Order Links"                          test_assigned_order_links
run_test "Bootstrap or Login Admin"                      test_admin_login
run_test "Delivery Status Links"                         test_delivery_status_links
run_test "Confirm Delivery"                              test_confirm_delivery

print_summary
exit $?
