#!/usr/bin/env bash

# RestLib two-model mapping E2E tests.
# Exercises CustomerDto <-> Customer mapping through JSON-configured endpoints
# backed by EF Core and SQLite.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/e2e-lib.sh"

header "Two-Model Mapping — E2E Tests"

check_prerequisites
wait_for_server

CUSTOMERS_URL="${BASE_URL}/api/customers"
RUN_ID="$(date +%s)-$$-${RANDOM}"
PRIMARY_EMAIL="mapped-primary-${RUN_ID}@example.com"
BATCH_EMAIL="mapped-batch-${RUN_ID}@example.com"
PRIMARY_CUSTOMER_ID=""
BATCH_CUSTOMER_ID=""

cleanup_customers() {
  local customer_id
  for customer_id in "$PRIMARY_CUSTOMER_ID" "$BATCH_CUSTOMER_ID"; do
    if [ -n "$customer_id" ]; then
      http_delete "${CUSTOMERS_URL}/${customer_id}" >/dev/null 2>&1 || true
    fi
  done
}

trap cleanup_customers EXIT

assert_public_customer() {
  local path="$1"
  local expected_id="$2"
  local expected_name="$3"
  local expected_email="$4"
  local expected_city="$5"
  local expected_active="$6"

  assert_json_field "${path}.id" "$expected_id"                         || return 1
  assert_json_field "${path}.name" "$expected_name"                     || return 1
  assert_json_field "${path}.email" "$expected_email"                   || return 1
  assert_json_field "${path}.city" "$expected_city"                     || return 1
  assert_json_field "${path}.is_active" "$expected_active"              || return 1
  assert_json_field_null "${path}.created_at"                            || return 1
}

test_create_mapped_customer() {
  local payload
  payload=$(jq -n \
    --arg email "$PRIMARY_EMAIL" \
    '{name: "Mapped Customer", email: $email, city: "Berlin", is_active: true}')

  http_post "$CUSTOMERS_URL" "$payload"

  assert_http_status "201"                                               || return 1
  PRIMARY_CUSTOMER_ID=$(jq_val '.id')
  assert_ne "created customer id" "$PRIMARY_CUSTOMER_ID" "null"         || return 1
  assert_ne "created customer id" "$PRIMARY_CUSTOMER_ID" ""             || return 1
  assert_public_customer \
    "" \
    "$PRIMARY_CUSTOMER_ID" \
    "Mapped Customer" \
    "$PRIMARY_EMAIL" \
    "Berlin" \
    "true"                                                               || return 1

  local location
  location=$(get_header "Location")
  assert_contains "Location header" "$location" "/api/customers/${PRIMARY_CUSTOMER_ID}" || return 1
}

test_get_mapped_customer() {
  http_get "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}"

  assert_http_status "200"                                               || return 1
  assert_public_customer \
    "" \
    "$PRIMARY_CUSTOMER_ID" \
    "Mapped Customer" \
    "$PRIMARY_EMAIL" \
    "Berlin" \
    "true"                                                               || return 1
}

test_update_mapped_customer() {
  local updated_email="mapped-updated-${RUN_ID}@example.com"
  local payload
  payload=$(jq -n \
    --arg email "$updated_email" \
    '{name: "Mapped Customer Updated", email: $email, city: "Hamburg", is_active: false}')

  http_put "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}" "$payload"

  assert_http_status "200"                                               || return 1
  assert_public_customer \
    "" \
    "$PRIMARY_CUSTOMER_ID" \
    "Mapped Customer Updated" \
    "$updated_email" \
    "Hamburg" \
    "false"                                                              || return 1

  http_get "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}"
  assert_http_status "200"                                               || return 1
  assert_public_customer \
    "" \
    "$PRIMARY_CUSTOMER_ID" \
    "Mapped Customer Updated" \
    "$updated_email" \
    "Hamburg" \
    "false"                                                              || return 1

  PRIMARY_EMAIL="$updated_email"
}

test_patch_mapped_customer() {
  http_patch "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}" '{"city":"Munich"}'

  assert_http_status "200"                                               || return 1
  assert_public_customer \
    "" \
    "$PRIMARY_CUSTOMER_ID" \
    "Mapped Customer Updated" \
    "$PRIMARY_EMAIL" \
    "Munich" \
    "false"                                                              || return 1

  http_get "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}"
  assert_http_status "200"                                               || return 1
  assert_public_customer \
    "" \
    "$PRIMARY_CUSTOMER_ID" \
    "Mapped Customer Updated" \
    "$PRIMARY_EMAIL" \
    "Munich" \
    "false"                                                              || return 1
}

test_validation_uses_public_fields() {
  http_post "$CUSTOMERS_URL" '{
    "name": "Invalid Mapped Customer",
    "email": "not-an-email",
    "city": "Berlin",
    "is_active": true
  }'

  assert_http_status "400"                                               || return 1
  assert_problem_type "/problems/validation-failed"                       || return 1
  assert_json_field_exists ".errors.email[0]"                             || return 1
  assert_json_field_null ".errors.created_at"                             || return 1
}

test_batch_create_mapped_customer() {
  local payload
  payload=$(jq -n \
    --arg email "$BATCH_EMAIL" \
    '{
      action: "create",
      items: [
        {name: "Batch Mapped Customer", email: $email, city: "Cologne", is_active: true}
      ]
    }')

  http_post "${CUSTOMERS_URL}/batch" "$payload"

  assert_http_status "200"                                               || return 1
  assert_items_count "1"                                                 || return 1
  assert_item_status 0 "201"                                             || return 1
  assert_item_has_entity 0                                               || return 1
  assert_item_no_error 0                                                 || return 1

  BATCH_CUSTOMER_ID=$(jq_val '.items[0].entity.id')
  assert_ne "batch-created customer id" "$BATCH_CUSTOMER_ID" "null"     || return 1
  assert_ne "batch-created customer id" "$BATCH_CUSTOMER_ID" ""         || return 1
  assert_public_customer \
    ".items[0].entity" \
    "$BATCH_CUSTOMER_ID" \
    "Batch Mapped Customer" \
    "$BATCH_EMAIL" \
    "Cologne" \
    "true"                                                               || return 1

  http_get "${CUSTOMERS_URL}/${BATCH_CUSTOMER_ID}"
  assert_http_status "200"                                               || return 1
  assert_public_customer \
    "" \
    "$BATCH_CUSTOMER_ID" \
    "Batch Mapped Customer" \
    "$BATCH_EMAIL" \
    "Cologne" \
    "true"                                                               || return 1
}

test_delete_mapped_customer() {
  http_delete "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}"
  assert_http_status "204"                                               || return 1

  http_get "${CUSTOMERS_URL}/${PRIMARY_CUSTOMER_ID}"
  assert_http_status "404"                                               || return 1
  PRIMARY_CUSTOMER_ID=""
}

test_batch_delete_mapped_customer() {
  http_post "${CUSTOMERS_URL}/batch" "{
    \"action\": \"delete\",
    \"items\": [\"${BATCH_CUSTOMER_ID}\"]
  }"

  assert_http_status "200"                                               || return 1
  assert_items_count "1"                                                 || return 1
  assert_item_status 0 "204"                                             || return 1
  assert_item_no_entity 0                                                 || return 1
  assert_item_no_error 0                                                  || return 1

  http_get "${CUSTOMERS_URL}/${BATCH_CUSTOMER_ID}"
  assert_http_status "404"                                               || return 1
  BATCH_CUSTOMER_ID=""
}

run_test "Create maps CustomerDto to EF entity and back"              test_create_mapped_customer
run_test "GetById returns only the public customer shape"              test_get_mapped_customer
run_test "PUT preserves the route key and persists mapped state"       test_update_mapped_customer
run_test "PATCH preserves untouched mapped fields"                     test_patch_mapped_customer
run_test "Validation errors use public DTO field names"                test_validation_uses_public_fields
run_test "Batch create maps and persists a public customer"            test_batch_create_mapped_customer
run_test "DELETE removes the mapped customer"                          test_delete_mapped_customer
run_test "Batch delete removes the batch-mapped customer"              test_batch_delete_mapped_customer

print_summary
exit $?
