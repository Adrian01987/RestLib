#!/usr/bin/env bash
# =============================================================================
# admin-catalog.sh - E2E tests for the ecommerce admin catalog
# =============================================================================
# Tests: protected access, action-aware batch authorization, admin login,
# batch create/patch, ETag patch, stale ETag.
# Resource used: admin v2 products.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

ADMIN_PRODUCTS_URL="${BASE_URL}/api/v2/admin/products"
BOOTSTRAP_KEY="${E2E_ADMIN_BOOTSTRAP_KEY:-dev-bootstrap-key}"
ADMIN_USER_NAME="${E2E_ADMIN_USER_NAME:-admin}"
ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-admin@example.com}"
ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-admin-password}"
ADMIN_TOKEN=""
ADMIN_CATALOG_RUN_ID="${E2E_ADMIN_CATALOG_RUN_ID:-$(date +%s)-$$}"
CUSTOMER_USER_NAME="admin-catalog-customer-${ADMIN_CATALOG_RUN_ID}"
CUSTOMER_EMAIL="${CUSTOMER_USER_NAME}@example.com"
CUSTOMER_PASSWORD="customer-password"
CUSTOMER_TOKEN=""
ADMIN_PRODUCT_ID="30000000-0000-0000-0000-000000000001"
ANONYMOUS_PRODUCT_ID="30000000-0000-0000-0000-000000000002"
NON_ADMIN_PRODUCT_ID="30000000-0000-0000-0000-000000000003"
ADMIN_PRODUCT_SKU="E2E-ADMIN-CATALOG"
ECOMMERCE_ELECTRONICS_ID="10000000-0000-0000-0000-000000000001"

header "Ecommerce Admin Catalog - E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# HTTP helpers with arbitrary headers
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

http_patch_admin() {
  http_request_with_headers PATCH "$1" "$2" "Authorization: Bearer ${ADMIN_TOKEN}"
}

http_patch_admin_if_match() {
  local url="$1"
  local body="$2"
  local etag="$3"
  http_request_with_headers PATCH "$url" "$body" \
    "Authorization: Bearer ${ADMIN_TOKEN}" \
    "If-Match: ${etag}"
}

http_delete_admin() {
  http_request_with_headers DELETE "$1" "" "Authorization: Bearer ${ADMIN_TOKEN}"
}

batch_create_product_payload() {
  local id="$1"
  local sku="$2"
  local name="$3"

  jq -n \
    --arg id "$id" \
    --arg category_id "$ECOMMERCE_ELECTRONICS_ID" \
    --arg sku "$sku" \
    --arg name "$name" \
    --arg created_at "2026-01-01T00:00:00Z" \
    '{
      action: "create",
      items: [
        {
          id: $id,
          category_id: $category_id,
          sku: $sku,
          name: $name,
          description: "Created by admin catalog E2E",
          price: 42.50,
          stock_on_hand: 6,
          is_active: true,
          created_at: $created_at
        }
      ]
    }'
}

batch_patch_product_payload() {
  local id="$1"

  jq -n \
    --arg id "$id" \
    '{action: "patch", items: [{id: $id, body: {description: "Batch patched by admin"}}]}'
}

cleanup_admin_products() {
  if [ -n "${ADMIN_TOKEN}" ]; then
    local product_id
    for product_id in "$ADMIN_PRODUCT_ID" "$ANONYMOUS_PRODUCT_ID" "$NON_ADMIN_PRODUCT_ID"; do
      http_delete_admin "${ADMIN_PRODUCTS_URL}/${product_id}" >/dev/null 2>&1 || true
    done
  fi
}
trap cleanup_admin_products EXIT

# =============================================================================
# TEST 1: Anonymous admin catalog access is rejected
# =============================================================================
test_anonymous_admin_access_rejected() {
  http_get "${ADMIN_PRODUCTS_URL}"

  assert_http_status "401"                               || return 1
}

# =============================================================================
# TEST 2: Anonymous batch create is rejected by authorization middleware
# =============================================================================
test_anonymous_batch_create_rejected() {
  local payload
  payload=$(batch_create_product_payload \
    "$ANONYMOUS_PRODUCT_ID" \
    "E2E-ANONYMOUS-BATCH-CREATE" \
    "Anonymous Batch Create")

  http_post "${ADMIN_PRODUCTS_URL}/batch" "$payload"

  assert_http_status "401"                               || return 1
}

# =============================================================================
# TEST 3: Anonymous batch patch is rejected by authorization middleware
# =============================================================================
test_anonymous_batch_patch_rejected() {
  local payload
  payload=$(batch_patch_product_payload "$ANONYMOUS_PRODUCT_ID")

  http_post "${ADMIN_PRODUCTS_URL}/batch" "$payload"

  assert_http_status "401"                               || return 1
}

# =============================================================================
# TEST 4: Bootstrap or login admin
# =============================================================================
test_admin_login() {
  local bootstrap_body login_body
  bootstrap_body=$(jq -n \
    --arg user_name "$ADMIN_USER_NAME" \
    --arg email "$ADMIN_EMAIL" \
    --arg password "$ADMIN_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_request_with_headers POST "${BASE_URL}/auth/admin-bootstrap" "$bootstrap_body" \
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

  http_post "${BASE_URL}/auth/login" "$login_body"

  assert_http_status "200"                               || return 1
  ADMIN_TOKEN=$(jq_val ".access_token")
  assert_ne "admin token" "$ADMIN_TOKEN" "null"          || return 1
  assert_ne "admin token" "$ADMIN_TOKEN" ""              || return 1
}

# =============================================================================
# TEST 5: An authenticated customer cannot use admin batch patch
# =============================================================================
test_customer_batch_patch_forbidden() {
  local registration_payload patch_payload
  registration_payload=$(jq -n \
    --arg user_name "$CUSTOMER_USER_NAME" \
    --arg email "$CUSTOMER_EMAIL" \
    --arg password "$CUSTOMER_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${BASE_URL}/auth/register-customer" "$registration_payload"
  assert_http_status "201"                               || return 1

  CUSTOMER_TOKEN=$(jq_val ".access_token")
  assert_ne "customer token" "$CUSTOMER_TOKEN" "null"   || return 1
  assert_ne "customer token" "$CUSTOMER_TOKEN" ""       || return 1

  patch_payload=$(batch_patch_product_payload "$NON_ADMIN_PRODUCT_ID")
  http_request_with_headers POST "${ADMIN_PRODUCTS_URL}/batch" "$patch_payload" \
    "Authorization: Bearer ${CUSTOMER_TOKEN}"

  assert_http_status "403"                               || return 1
}

# =============================================================================
# TEST 6: Batch create product through admin catalog
# =============================================================================
test_batch_create_product() {
  cleanup_admin_products

  local payload
  payload=$(batch_create_product_payload \
    "$ADMIN_PRODUCT_ID" \
    "$ADMIN_PRODUCT_SKU" \
    "E2E Admin Catalog Product")

  http_post_admin "${ADMIN_PRODUCTS_URL}/batch" "$payload"

  assert_http_status "200"                               || return 1
  assert_items_count "1"                                 || return 1
  assert_item_status 0 "201"                             || return 1
  assert_json_field ".items[0].entity.id" "$ADMIN_PRODUCT_ID" || return 1
  assert_json_field ".items[0].entity.sku" "$ADMIN_PRODUCT_SKU" || return 1
}

# =============================================================================
# TEST 7: Batch patch product through admin catalog
# =============================================================================
test_batch_patch_product() {
  local payload
  payload=$(batch_patch_product_payload "$ADMIN_PRODUCT_ID")

  http_post_admin "${ADMIN_PRODUCTS_URL}/batch" "$payload"

  assert_http_status "200"                               || return 1
  assert_items_count "1"                                 || return 1
  assert_item_status 0 "200"                             || return 1
  assert_json_field ".items[0].entity.id" "$ADMIN_PRODUCT_ID" || return 1
  assert_json_field ".items[0].entity.description" "Batch patched by admin" || return 1
}

# =============================================================================
# TEST 8: Dense admin field selection preserves the configured nested category shape
# =============================================================================
test_admin_nested_field_selection() {
  http_get_admin "${ADMIN_PRODUCTS_URL}?fields=id,sku,stock_on_hand,category.name,category.slug&limit=1"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items[0].id"                || return 1
  assert_json_field_exists ".items[0].sku"               || return 1
  assert_json_field_exists ".items[0].stock_on_hand"     || return 1
  assert_json_field_exists ".items[0].category.name"     || return 1
  assert_json_field_exists ".items[0].category.slug"     || return 1
  assert_json_field_null '.items[0]["category.name"]'    || return 1
  assert_json_field_null '.items[0]["category.slug"]'    || return 1
}

# =============================================================================
# TEST 9: PATCH with If-Match succeeds
# =============================================================================
test_patch_with_if_match() {
  http_get_admin "${ADMIN_PRODUCTS_URL}/${ADMIN_PRODUCT_ID}"
  assert_http_status "200"                               || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                            || return 1

  http_patch_admin_if_match "${ADMIN_PRODUCTS_URL}/${ADMIN_PRODUCT_ID}" \
    '{"stock_on_hand": 11}' \
    "$etag"

  assert_http_status "200"                               || return 1
  assert_json_field ".stock_on_hand" "11"                || return 1
}

# =============================================================================
# TEST 10: Stale If-Match returns 412
# =============================================================================
test_stale_if_match_returns_412() {
  http_get_admin "${ADMIN_PRODUCTS_URL}/${ADMIN_PRODUCT_ID}"
  assert_http_status "200"                               || return 1

  local stale_etag
  stale_etag=$(get_header "ETag")
  assert_ne "stale ETag" "$stale_etag" ""                || return 1

  http_patch_admin "${ADMIN_PRODUCTS_URL}/${ADMIN_PRODUCT_ID}" '{"price": 43.00}'
  assert_http_status "200"                               || return 1

  http_patch_admin_if_match "${ADMIN_PRODUCTS_URL}/${ADMIN_PRODUCT_ID}" \
    '{"price": 44.00}' \
    "$stale_etag"

  assert_http_status "412"                               || return 1
  assert_problem_type "/problems/precondition-failed"    || return 1
}

# =============================================================================
# TEST 11: Cleanup test product
# =============================================================================
test_cleanup_product() {
  http_delete_admin "${ADMIN_PRODUCTS_URL}/${ADMIN_PRODUCT_ID}"

  if [ "$HTTP_STATUS" = "204" ] || [ "$HTTP_STATUS" = "404" ]; then
    pass "Cleanup returned ${HTTP_STATUS}"
    return 0
  fi

  fail "Cleanup returned ${HTTP_STATUS}; expected 204 or 404"
  echo "$HTTP_BODY" | jq . 2>/dev/null || echo "$HTTP_BODY"
  return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Anonymous Admin Access Rejected"                test_anonymous_admin_access_rejected
run_test "Anonymous Batch Create Rejected"                test_anonymous_batch_create_rejected
run_test "Anonymous Batch Patch Rejected"                 test_anonymous_batch_patch_rejected
run_test "Bootstrap or Login Admin"                       test_admin_login
run_test "Customer Batch Patch Forbidden"                 test_customer_batch_patch_forbidden
run_test "Batch Create Product"                           test_batch_create_product
run_test "Batch Patch Product"                            test_batch_patch_product
run_test "Nested Admin Field Selection"                   test_admin_nested_field_selection
run_test "PATCH with If-Match"                            test_patch_with_if_match
run_test "Stale If-Match Returns 412"                     test_stale_if_match_returns_412
run_test "Cleanup Product"                                test_cleanup_product

print_summary
exit $?
