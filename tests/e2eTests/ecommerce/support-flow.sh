#!/usr/bin/env bash
# =============================================================================
# support-flow.sh - E2E tests for ecommerce support tickets
# =============================================================================
# Tests: customer/carrier shared ticket creation, support ticket owner/status
# stamping, and admin-only support ticket reads.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

AUTH_URL="${BASE_URL}/auth"
SUPPORT_URL="${BASE_URL}/api/support/tickets"
ADMIN_SUPPORT_URL="${BASE_URL}/api/admin/support/tickets"

BOOTSTRAP_KEY="${E2E_ADMIN_BOOTSTRAP_KEY:-dev-bootstrap-key}"
ADMIN_USER_NAME="${E2E_ADMIN_USER_NAME:-admin}"
ADMIN_EMAIL="${E2E_ADMIN_EMAIL:-admin@example.com}"
ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-admin-password}"

SUPPORT_RUN_ID="${E2E_SUPPORT_RUN_ID:-$(date +%s)-$$}"
CUSTOMER_USER_NAME="${E2E_SUPPORT_CUSTOMER_USER_NAME:-support-customer-${SUPPORT_RUN_ID}}"
CUSTOMER_EMAIL="${E2E_SUPPORT_CUSTOMER_EMAIL:-support-customer-${SUPPORT_RUN_ID}@example.com}"
CUSTOMER_PASSWORD="${E2E_SUPPORT_CUSTOMER_PASSWORD:-customer-password}"

SEED_CARRIER_USER_NAME="${E2E_SEED_CARRIER_USER_NAME:-carrier}"
SEED_CARRIER_PASSWORD="${E2E_SEED_CARRIER_PASSWORD:-carrier-password}"

ADMIN_TOKEN=""
CUSTOMER_TOKEN=""
CUSTOMER_ID=""
CARRIER_TOKEN=""
CARRIER_ID=""
CUSTOMER_TICKET_ID=""
CARRIER_TICKET_ID=""

header "Ecommerce Support Flow - E2E Tests"
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

http_get_customer() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_get_carrier() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CARRIER_TOKEN}"
}

http_post_carrier() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CARRIER_TOKEN}"
}

login_actor() {
  local user_name_or_email="$1"
  local password="$2"
  local expected_role="$3"
  local token_variable="$4"
  local id_variable="$5"
  local body token user_id

  body=$(jq -n \
    --arg user_name_or_email "$user_name_or_email" \
    --arg password "$password" \
    '{user_name_or_email:$user_name_or_email,password:$password}')

  http_post "${AUTH_URL}/login" "$body"

  assert_http_status "200"                               || return 1
  assert_json_field ".user.role" "$expected_role"        || return 1

  token=$(jq_val ".access_token")
  user_id=$(jq_val ".user.id")
  assert_ne "${expected_role} token" "$token" "null"     || return 1
  assert_ne "${expected_role} token" "$token" ""         || return 1
  assert_ne "${expected_role} id" "$user_id" "null"      || return 1
  assert_ne "${expected_role} id" "$user_id" ""          || return 1

  printf -v "$token_variable" "%s" "$token"
  printf -v "$id_variable" "%s" "$user_id"
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
# TEST 2: Register customer and login seeded carrier
# =============================================================================
test_support_requesters_login() {
  local body
  body=$(jq -n \
    --arg user_name "$CUSTOMER_USER_NAME" \
    --arg email "$CUSTOMER_EMAIL" \
    --arg password "$CUSTOMER_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${AUTH_URL}/register-customer" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".user.role" "Customer"              || return 1

  CUSTOMER_TOKEN=$(jq_val ".access_token")
  CUSTOMER_ID=$(jq_val ".user.id")
  assert_ne "customer token" "$CUSTOMER_TOKEN" "null"    || return 1
  assert_ne "customer token" "$CUSTOMER_TOKEN" ""        || return 1
  assert_ne "customer id" "$CUSTOMER_ID" "null"          || return 1
  assert_ne "customer id" "$CUSTOMER_ID" ""              || return 1

  login_actor "$SEED_CARRIER_USER_NAME" "$SEED_CARRIER_PASSWORD" "Carrier" CARRIER_TOKEN CARRIER_ID
}

# =============================================================================
# TEST 3: Anonymous support ticket creation is rejected
# =============================================================================
test_anonymous_support_create_rejected() {
  local body
  body=$(jq -n '{subject:"Anonymous support request",message:"This should be rejected."}')

  http_post "$SUPPORT_URL" "$body"

  assert_http_status "401"                               || return 1
}

# =============================================================================
# TEST 4: Customer creates a support ticket
# =============================================================================
test_customer_creates_support_ticket() {
  local body
  body=$(jq -n \
    --arg subject "  Customer support ticket ${SUPPORT_RUN_ID}  " \
    --arg message "Customer needs help with an order." \
    --arg carrier_id "$CARRIER_ID" \
    '{subject:$subject,message:$message,status:"CLOSED",created_by_user_id:$carrier_id}')

  http_post_customer "$SUPPORT_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".created_by_user_id" "$CUSTOMER_ID" || return 1
  assert_json_field ".subject" "Customer support ticket ${SUPPORT_RUN_ID}" || return 1
  assert_json_field ".message" "Customer needs help with an order." || return 1
  assert_json_field ".status" "OPEN"                     || return 1
  assert_json_field_exists ".created_at"                 || return 1

  CUSTOMER_TICKET_ID=$(jq_val ".id")
  assert_ne "customer ticket id" "$CUSTOMER_TICKET_ID" "null" || return 1
  assert_ne "customer ticket id" "$CUSTOMER_TICKET_ID" "" || return 1
}

# =============================================================================
# TEST 5: Carrier creates a support ticket
# =============================================================================
test_carrier_creates_support_ticket() {
  local body
  body=$(jq -n \
    --arg subject "Carrier support ticket ${SUPPORT_RUN_ID}" \
    --arg message "Carrier needs help with a shipment." \
    --arg customer_id "$CUSTOMER_ID" \
    '{subject:$subject,message:$message,status:"RESOLVED",created_by_user_id:$customer_id}')

  http_post_carrier "$SUPPORT_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".created_by_user_id" "$CARRIER_ID"  || return 1
  assert_json_field ".subject" "Carrier support ticket ${SUPPORT_RUN_ID}" || return 1
  assert_json_field ".message" "Carrier needs help with a shipment." || return 1
  assert_json_field ".status" "OPEN"                     || return 1

  CARRIER_TICKET_ID=$(jq_val ".id")
  assert_ne "carrier ticket id" "$CARRIER_TICKET_ID" "null" || return 1
  assert_ne "carrier ticket id" "$CARRIER_TICKET_ID" ""  || return 1
}

# =============================================================================
# TEST 6: Admin is not part of the shared support requester policy
# =============================================================================
test_admin_support_create_rejected() {
  local body
  body=$(jq -n '{subject:"Admin support request",message:"Admins should use the read surface only."}')

  http_post_admin "$SUPPORT_URL" "$body"

  assert_http_status "403"                               || return 1
}

# =============================================================================
# TEST 7: Customer and carrier cannot read admin support tickets
# =============================================================================
test_non_admin_support_reads_rejected() {
  http_get_customer "${ADMIN_SUPPORT_URL}?limit=1"
  assert_http_status "403"                               || return 1

  http_get_carrier "${ADMIN_SUPPORT_URL}?limit=1"
  assert_http_status "403"                               || return 1
}

# =============================================================================
# TEST 8: Admin lists the customer support ticket
# =============================================================================
test_admin_lists_customer_support_ticket() {
  http_get_admin "${ADMIN_SUPPORT_URL}?created_by_user_id=${CUSTOMER_ID}&fields=id,created_by_user_id,subject,status&limit=5"

  assert_http_status "200"                               || return 1
  assert_items_count "1"                                 || return 1
  assert_json_field ".items[0].id" "$CUSTOMER_TICKET_ID" || return 1
  assert_json_field ".items[0].created_by_user_id" "$CUSTOMER_ID" || return 1
  assert_json_field ".items[0].subject" "Customer support ticket ${SUPPORT_RUN_ID}" || return 1
  assert_json_field ".items[0].status" "OPEN"            || return 1
}

# =============================================================================
# TEST 9: Admin filters and lists the carrier support ticket
# =============================================================================
test_admin_filters_carrier_support_ticket() {
  local listed_id

  http_get_admin "${ADMIN_SUPPORT_URL}?subject[contains]=${SUPPORT_RUN_ID}&status=OPEN&fields=id,created_by_user_id,subject,status&limit=10"

  assert_http_status "200"                               || return 1

  listed_id=$(echo "$HTTP_BODY" | jq -r --arg id "$CARRIER_TICKET_ID" 'first(.items[] | select(.id == $id) | .id) // empty')
  assert_eq "carrier ticket listed by admin filter" "$listed_id" "$CARRIER_TICKET_ID" || return 1
}

# =============================================================================
# TEST 10: Admin reads a support ticket by id
# =============================================================================
test_admin_gets_support_ticket_by_id() {
  http_get_admin "${ADMIN_SUPPORT_URL}/${CUSTOMER_TICKET_ID}"

  assert_http_status "200"                               || return 1
  assert_json_field ".id" "$CUSTOMER_TICKET_ID"          || return 1
  assert_json_field ".created_by_user_id" "$CUSTOMER_ID" || return 1
  assert_json_field ".status" "OPEN"                     || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Bootstrap or Login Admin"                      test_admin_login
run_test "Support Requesters Login"                      test_support_requesters_login
run_test "Anonymous Support Create Rejected"             test_anonymous_support_create_rejected
run_test "Customer Creates Support Ticket"               test_customer_creates_support_ticket
run_test "Carrier Creates Support Ticket"                test_carrier_creates_support_ticket
run_test "Admin Support Create Rejected"                 test_admin_support_create_rejected
run_test "Non-Admin Support Reads Rejected"              test_non_admin_support_reads_rejected
run_test "Admin Lists Customer Support Ticket"           test_admin_lists_customer_support_ticket
run_test "Admin Filters Carrier Support Ticket"          test_admin_filters_carrier_support_ticket
run_test "Admin Gets Support Ticket By ID"               test_admin_gets_support_ticket_by_id

print_summary
exit $?
