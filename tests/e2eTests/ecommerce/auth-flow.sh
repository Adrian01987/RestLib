#!/usr/bin/env bash
# =============================================================================
# auth-flow.sh - E2E tests for the ecommerce auth and identity flow
# =============================================================================
# Tests: bootstrap admin, register customer, admin creates carrier, all three
# logins succeed, customer /me works, password hashes stay out of responses.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

AUTH_URL="${BASE_URL}/auth"
ADMIN_USERS_URL="${BASE_URL}/api/admin/users"
ADMIN_CARRIERS_URL="${BASE_URL}/api/admin/carriers"
STOREFRONT_ME_URL="${BASE_URL}/api/storefront/me"

BOOTSTRAP_KEY="${E2E_ADMIN_BOOTSTRAP_KEY:-dev-bootstrap-key}"
ADMIN_USER_NAME="${E2E_ADMIN_USER_NAME:-admin}"
ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-admin@example.com}"
ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-admin-password}"

AUTH_RUN_ID="${E2E_AUTH_RUN_ID:-$(date +%s)-$$}"
CUSTOMER_USER_NAME="${E2E_CUSTOMER_USER_NAME:-customer-e2e-${AUTH_RUN_ID}}"
CUSTOMER_EMAIL="${E2E_CUSTOMER_EMAIL:-customer-e2e-${AUTH_RUN_ID}@example.com}"
CUSTOMER_PASSWORD="${E2E_CUSTOMER_PASSWORD:-customer-password}"
CARRIER_USER_NAME="${E2E_CARRIER_USER_NAME:-carrier-e2e-${AUTH_RUN_ID}}"
CARRIER_EMAIL="${E2E_CARRIER_EMAIL:-carrier-e2e-${AUTH_RUN_ID}@example.com}"
CARRIER_PASSWORD="${E2E_CARRIER_PASSWORD:-carrier-password}"
CARRIER_DISPLAY_NAME="${E2E_CARRIER_DISPLAY_NAME:-E2E Carrier ${AUTH_RUN_ID}}"

ADMIN_TOKEN=""
CUSTOMER_TOKEN=""
CUSTOMER_ID=""
CARRIER_TOKEN=""
CARRIER_ID=""

header "Ecommerce Auth Flow - E2E Tests"
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

http_get_customer() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

assert_no_password_hash() {
  local label="$1"

  assert_not_contains "$label" "$HTTP_BODY" "password_hash" || return 1
  assert_not_contains "$label" "$HTTP_BODY" "passwordHash"  || return 1
  assert_not_contains "$label" "$HTTP_BODY" "PasswordHash"  || return 1
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
  assert_no_password_hash "${expected_role} login response" || return 1
  assert_json_field ".user.role" "$expected_role"        || return 1

  token=$(jq_val ".access_token")
  assert_ne "${expected_role} token" "$token" "null"     || return 1
  assert_ne "${expected_role} token" "$token" ""         || return 1

  printf -v "$token_variable" "%s" "$token"
}

# =============================================================================
# TEST 1: Bootstrap admin if needed
# =============================================================================
test_bootstrap_admin_if_needed() {
  local body
  body=$(jq -n \
    --arg user_name "$ADMIN_USER_NAME" \
    --arg email "$ADMIN_EMAIL" \
    --arg password "$ADMIN_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_request_with_headers POST "${AUTH_URL}/admin-bootstrap" "$body" \
    "X-Bootstrap-Key: ${BOOTSTRAP_KEY}"

  if [ "$HTTP_STATUS" = "201" ]; then
    assert_no_password_hash "admin bootstrap response"   || return 1
    assert_json_field ".user.role" "Admin"               || return 1
    pass "Bootstrapped admin user"
    return 0
  fi

  if [ "$HTTP_STATUS" = "409" ]; then
    assert_no_password_hash "admin bootstrap conflict response" || return 1
    pass "Admin user already exists"
    return 0
  fi

  fail "Admin bootstrap returned ${HTTP_STATUS}; expected 201 or 409"
  echo "$HTTP_BODY" | jq . 2>/dev/null || echo "$HTTP_BODY"
  return 1
}

# =============================================================================
# TEST 2: Admin login succeeds
# =============================================================================
test_admin_login() {
  login_actor "$ADMIN_USER_NAME" "$ADMIN_PASSWORD" "Admin" ADMIN_TOKEN
}

# =============================================================================
# TEST 3: Customer self-registration succeeds
# =============================================================================
test_register_customer() {
  local body token
  body=$(jq -n \
    --arg user_name "$CUSTOMER_USER_NAME" \
    --arg email "$CUSTOMER_EMAIL" \
    --arg password "$CUSTOMER_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${AUTH_URL}/register-customer" "$body"

  assert_http_status "201"                               || return 1
  assert_no_password_hash "customer registration response" || return 1
  assert_json_field ".user.role" "Customer"              || return 1
  assert_json_field ".user.user_name" "$CUSTOMER_USER_NAME" || return 1
  assert_json_field ".user.email" "$CUSTOMER_EMAIL"      || return 1

  token=$(jq_val ".access_token")
  CUSTOMER_ID=$(jq_val ".user.id")
  assert_ne "customer registration token" "$token" "null" || return 1
  assert_ne "customer registration token" "$token" ""    || return 1
  assert_ne "customer id" "$CUSTOMER_ID" "null"          || return 1
  assert_ne "customer id" "$CUSTOMER_ID" ""              || return 1
  CUSTOMER_TOKEN="$token"
}

# =============================================================================
# TEST 4: Customer login succeeds
# =============================================================================
test_customer_login() {
  login_actor "$CUSTOMER_USER_NAME" "$CUSTOMER_PASSWORD" "Customer" CUSTOMER_TOKEN
}

# =============================================================================
# TEST 5: Customer /me returns safe profile
# =============================================================================
test_customer_me() {
  http_get_customer "$STOREFRONT_ME_URL"

  assert_http_status "200"                               || return 1
  assert_no_password_hash "customer /me response"         || return 1
  assert_json_field ".id" "$CUSTOMER_ID"                 || return 1
  assert_json_field ".user_name" "$CUSTOMER_USER_NAME"   || return 1
  assert_json_field ".email" "$CUSTOMER_EMAIL"           || return 1
  assert_json_field ".role" "Customer"                   || return 1
}

# =============================================================================
# TEST 6: Admin creates carrier account and reference row
# =============================================================================
test_admin_creates_carrier() {
  local body user_id
  body=$(jq -n \
    --arg user_name "$CARRIER_USER_NAME" \
    --arg email "$CARRIER_EMAIL" \
    --arg password "$CARRIER_PASSWORD" \
    --arg display_name "$CARRIER_DISPLAY_NAME" \
    '{user_name:$user_name,email:$email,password:$password,display_name:$display_name,service_area:"E2E service area"}')

  http_post_admin "$ADMIN_CARRIERS_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_no_password_hash "carrier provisioning response" || return 1
  assert_json_field ".display_name" "$CARRIER_DISPLAY_NAME" || return 1
  assert_json_field ".service_area" "E2E service area"   || return 1

  CARRIER_ID=$(jq_val ".id")
  user_id=$(jq_val ".user_id")
  assert_ne "carrier id" "$CARRIER_ID" "null"            || return 1
  assert_ne "carrier id" "$CARRIER_ID" ""                || return 1
  assert_eq "carrier id matches user id" "$CARRIER_ID" "$user_id" || return 1
}

# =============================================================================
# TEST 7: Carrier login succeeds
# =============================================================================
test_carrier_login() {
  login_actor "$CARRIER_USER_NAME" "$CARRIER_PASSWORD" "Carrier" CARRIER_TOKEN
}

# =============================================================================
# TEST 8: Admin user directory hides password hashes
# =============================================================================
test_admin_user_directory_hides_password_hashes() {
  http_get_admin "${ADMIN_USERS_URL}?email=${CARRIER_EMAIL}"

  assert_http_status "200"                               || return 1
  assert_no_password_hash "admin user directory response" || return 1
  assert_items_count "1"                                 || return 1
  assert_json_field ".items[0].email" "$CARRIER_EMAIL"   || return 1
  assert_json_field ".items[0].role" "Carrier"           || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Bootstrap Admin If Needed"                     test_bootstrap_admin_if_needed
run_test "Admin Login"                                   test_admin_login
run_test "Register Customer"                             test_register_customer
run_test "Customer Login"                                test_customer_login
run_test "Customer /me"                                  test_customer_me
run_test "Admin Creates Carrier"                         test_admin_creates_carrier
run_test "Carrier Login"                                 test_carrier_login
run_test "Admin User Directory Hides Password Hashes"    test_admin_user_directory_hides_password_hashes

print_summary
exit $?
