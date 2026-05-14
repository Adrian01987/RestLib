#!/usr/bin/env bash
# =============================================================================
# customer-profile.sh - E2E tests for ecommerce customer profile resources
# =============================================================================
# Tests: customer-owned addresses and phone numbers, primary-row demotion, and
# cross-customer row isolation enforced by the sample's EF Core query filters.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

AUTH_URL="${BASE_URL}/auth"
ADDRESSES_URL="${BASE_URL}/api/storefront/addresses"
PHONES_URL="${BASE_URL}/api/storefront/phones"

PROFILE_RUN_ID="${E2E_CUSTOMER_PROFILE_RUN_ID:-$(date +%s)-$$}"
CUSTOMER_A_USER_NAME="${E2E_CUSTOMER_A_USER_NAME:-profile-a-${PROFILE_RUN_ID}}"
CUSTOMER_A_EMAIL="${E2E_CUSTOMER_A_EMAIL:-profile-a-${PROFILE_RUN_ID}@example.com}"
CUSTOMER_B_USER_NAME="${E2E_CUSTOMER_B_USER_NAME:-profile-b-${PROFILE_RUN_ID}}"
CUSTOMER_B_EMAIL="${E2E_CUSTOMER_B_EMAIL:-profile-b-${PROFILE_RUN_ID}@example.com}"
CUSTOMER_PASSWORD="${E2E_CUSTOMER_PROFILE_PASSWORD:-customer-password}"

CUSTOMER_A_TOKEN=""
CUSTOMER_B_TOKEN=""
ADDRESS_ONE_ID=""
ADDRESS_TWO_ID=""
PHONE_ONE_ID=""
PHONE_TWO_ID=""

header "Ecommerce Customer Profile - E2E Tests"
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

http_get_customer_a() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_A_TOKEN}"
}

http_get_customer_b() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_B_TOKEN}"
}

http_post_customer_a() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CUSTOMER_A_TOKEN}"
}

http_patch_customer_a() {
  http_request_with_headers PATCH "$1" "$2" "Authorization: Bearer ${CUSTOMER_A_TOKEN}"
}

http_delete_customer_a() {
  http_request_with_headers DELETE "$1" "" "Authorization: Bearer ${CUSTOMER_A_TOKEN}"
}

cleanup_profile_rows() {
  if [ -n "${CUSTOMER_A_TOKEN}" ]; then
    for id in "$ADDRESS_ONE_ID" "$ADDRESS_TWO_ID"; do
      if [ -n "$id" ]; then
        http_delete_customer_a "${ADDRESSES_URL}/${id}" >/dev/null 2>&1 || true
      fi
    done

    for id in "$PHONE_ONE_ID" "$PHONE_TWO_ID"; do
      if [ -n "$id" ]; then
        http_delete_customer_a "${PHONES_URL}/${id}" >/dev/null 2>&1 || true
      fi
    done
  fi
}
trap cleanup_profile_rows EXIT

register_customer() {
  local user_name="$1"
  local email="$2"
  local token_variable="$3"
  local body token

  body=$(jq -n \
    --arg user_name "$user_name" \
    --arg email "$email" \
    --arg password "$CUSTOMER_PASSWORD" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${AUTH_URL}/register-customer" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".user.role" "Customer"              || return 1
  assert_json_field ".user.user_name" "$user_name"       || return 1

  token=$(jq_val ".access_token")
  assert_ne "customer token" "$token" "null"             || return 1
  assert_ne "customer token" "$token" ""                 || return 1

  printf -v "$token_variable" "%s" "$token"
}

json_item_field_by_id() {
  local id="$1"
  local field="$2"
  echo "$HTTP_BODY" | jq -r --arg id "$id" ".items[] | select(.id == \$id) | .${field}" 2>/dev/null
}

json_items_matching_count() {
  local filter="$1"
  echo "$HTTP_BODY" | jq "[.items[] | select(${filter})] | length" 2>/dev/null
}

# =============================================================================
# TEST 1: Anonymous profile access is rejected
# =============================================================================
test_anonymous_profile_access_rejected() {
  http_get "$ADDRESSES_URL"
  assert_http_status "401"                               || return 1

  http_get "$PHONES_URL"
  assert_http_status "401"                               || return 1
}

# =============================================================================
# TEST 2: Register two customers
# =============================================================================
test_register_two_customers() {
  register_customer "$CUSTOMER_A_USER_NAME" "$CUSTOMER_A_EMAIL" CUSTOMER_A_TOKEN || return 1
  register_customer "$CUSTOMER_B_USER_NAME" "$CUSTOMER_B_EMAIL" CUSTOMER_B_TOKEN || return 1
}

# =============================================================================
# TEST 3: Customer adds two addresses
# =============================================================================
test_customer_adds_two_addresses() {
  local body
  body=$(jq -n \
    '{line1:"100 Profile Street",city:"Seattle",region:"WA",postal_code:"98101",country_code:"US",is_primary:true}')

  http_post_customer_a "$ADDRESSES_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".line1" "100 Profile Street"        || return 1
  assert_json_field ".is_primary" "true"                 || return 1
  ADDRESS_ONE_ID=$(jq_val ".id")
  assert_ne "first address id" "$ADDRESS_ONE_ID" "null"  || return 1
  assert_ne "first address id" "$ADDRESS_ONE_ID" ""      || return 1

  body=$(jq -n \
    '{line1:"200 Profile Avenue",city:"Seattle",region:"WA",postal_code:"98102",country_code:"US",is_primary:false}')

  http_post_customer_a "$ADDRESSES_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".line1" "200 Profile Avenue"        || return 1
  assert_json_field ".is_primary" "false"                || return 1
  ADDRESS_TWO_ID=$(jq_val ".id")
  assert_ne "second address id" "$ADDRESS_TWO_ID" "null" || return 1
  assert_ne "second address id" "$ADDRESS_TWO_ID" ""     || return 1
}

# =============================================================================
# TEST 4: Marking second address primary demotes the first
# =============================================================================
test_second_address_becomes_only_primary() {
  http_patch_customer_a "${ADDRESSES_URL}/${ADDRESS_TWO_ID}" '{"is_primary":true}'

  assert_http_status "200"                               || return 1
  assert_json_field ".is_primary" "true"                 || return 1

  http_get_customer_a "${ADDRESSES_URL}?fields=id,is_primary&limit=20"

  assert_http_status "200"                               || return 1

  local count primary_count first_primary second_primary
  count=$(jq_len ".items")
  primary_count=$(json_items_matching_count '.is_primary == true')
  first_primary=$(json_item_field_by_id "$ADDRESS_ONE_ID" "is_primary")
  second_primary=$(json_item_field_by_id "$ADDRESS_TWO_ID" "is_primary")

  assert_eq "address count" "$count" "2"                 || return 1
  assert_eq "primary address count" "$primary_count" "1" || return 1
  assert_eq "first address primary" "$first_primary" "false" || return 1
  assert_eq "second address primary" "$second_primary" "true" || return 1
}

# =============================================================================
# TEST 5: Another customer cannot read the first customer's addresses
# =============================================================================
test_other_customer_cannot_read_addresses() {
  http_get_customer_b "${ADDRESSES_URL}/${ADDRESS_ONE_ID}"
  assert_http_status "404"                               || return 1

  http_get_customer_b "${ADDRESSES_URL}/${ADDRESS_TWO_ID}"
  assert_http_status "404"                               || return 1
}

# =============================================================================
# TEST 6: Customer adds two phone numbers
# =============================================================================
test_customer_adds_two_phones() {
  local body
  body=$(jq -n \
    '{number:"+1-206-555-0201",type:"Mobile",is_primary:true}')

  http_post_customer_a "$PHONES_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".number" "+1-206-555-0201"          || return 1
  assert_json_field ".is_primary" "true"                 || return 1
  PHONE_ONE_ID=$(jq_val ".id")
  assert_ne "first phone id" "$PHONE_ONE_ID" "null"      || return 1
  assert_ne "first phone id" "$PHONE_ONE_ID" ""          || return 1

  body=$(jq -n \
    '{number:"+1-206-555-0202",type:"Home",is_primary:false}')

  http_post_customer_a "$PHONES_URL" "$body"

  assert_http_status "201"                               || return 1
  assert_json_field ".number" "+1-206-555-0202"          || return 1
  assert_json_field ".is_primary" "false"                || return 1
  PHONE_TWO_ID=$(jq_val ".id")
  assert_ne "second phone id" "$PHONE_TWO_ID" "null"     || return 1
  assert_ne "second phone id" "$PHONE_TWO_ID" ""         || return 1
}

# =============================================================================
# TEST 7: Marking second phone primary demotes the first
# =============================================================================
test_second_phone_becomes_only_primary() {
  http_patch_customer_a "${PHONES_URL}/${PHONE_TWO_ID}" '{"is_primary":true}'

  assert_http_status "200"                               || return 1
  assert_json_field ".is_primary" "true"                 || return 1

  http_get_customer_a "${PHONES_URL}?fields=id,is_primary&limit=20"

  assert_http_status "200"                               || return 1

  local count primary_count first_primary second_primary
  count=$(jq_len ".items")
  primary_count=$(json_items_matching_count '.is_primary == true')
  first_primary=$(json_item_field_by_id "$PHONE_ONE_ID" "is_primary")
  second_primary=$(json_item_field_by_id "$PHONE_TWO_ID" "is_primary")

  assert_eq "phone count" "$count" "2"                   || return 1
  assert_eq "primary phone count" "$primary_count" "1"   || return 1
  assert_eq "first phone primary" "$first_primary" "false" || return 1
  assert_eq "second phone primary" "$second_primary" "true" || return 1
}

# =============================================================================
# TEST 8: Another customer cannot read the first customer's phones
# =============================================================================
test_other_customer_cannot_read_phones() {
  http_get_customer_b "${PHONES_URL}/${PHONE_ONE_ID}"
  assert_http_status "404"                               || return 1

  http_get_customer_b "${PHONES_URL}/${PHONE_TWO_ID}"
  assert_http_status "404"                               || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Anonymous Profile Access Rejected"             test_anonymous_profile_access_rejected
run_test "Register Two Customers"                        test_register_two_customers
run_test "Customer Adds Two Addresses"                   test_customer_adds_two_addresses
run_test "Second Address Becomes Only Primary"           test_second_address_becomes_only_primary
run_test "Other Customer Cannot Read Addresses"          test_other_customer_cannot_read_addresses
run_test "Customer Adds Two Phones"                      test_customer_adds_two_phones
run_test "Second Phone Becomes Only Primary"             test_second_phone_becomes_only_primary
run_test "Other Customer Cannot Read Phones"             test_other_customer_cannot_read_phones

print_summary
exit $?
