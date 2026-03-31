#!/usr/bin/env bash
# =============================================================================
# batch-tests.sh — E2E tests for RestLib Batch Operations
# =============================================================================
# Tests: Batch Create, Patch, Delete (Orders & Products), error scenarios
# Resources used: Orders (batch create/delete), Products (batch create/patch/delete)
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

# Additional ID used in batch tests
NONEXISTENT_ID2="99999999-9999-9999-9999-999999999999"

# IDs captured during the test run (populated by earlier tests, used by later ones)
CREATED_ORDER_ID_1=""
CREATED_ORDER_ID_2=""
CREATED_PRODUCT_ID_1=""
CREATED_PRODUCT_ID_2=""

header "Batch Operations — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: Batch Create Orders (Happy Path)
# =============================================================================
test_batch_create_orders() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "create",
    "items": [
      {
        "customer_email": "batch-test-1@example.com",
        "lines": [
          { "product_id": "'"${HEADPHONES_ID}"'", "quantity": 2, "unit_price": 149.99 }
        ],
        "status": "Pending"
      },
      {
        "customer_email": "batch-test-2@example.com",
        "lines": [
          { "product_id": "'"${KEYBOARD_ID}"'", "quantity": 1, "unit_price": 89.99 }
        ],
        "status": "Pending"
      }
    ]
  }'

  assert_http_status "200"           || return 1
  assert_items_count "2"             || return 1
  assert_item_status 0 "201"         || return 1
  assert_item_status 1 "201"         || return 1
  assert_item_has_entity 0           || return 1
  assert_item_has_entity 1           || return 1
  assert_item_no_error 0             || return 1
  assert_item_no_error 1             || return 1

  # Verify entity fields
  local email_0 total_0 email_1 total_1
  email_0=$(jq_val '.items[0].entity.customer_email')
  total_0=$(jq_val '.items[0].entity.total')
  email_1=$(jq_val '.items[1].entity.customer_email')
  total_1=$(jq_val '.items[1].entity.total')

  assert_eq "items[0].entity.customer_email" "$email_0" "batch-test-1@example.com" || return 1
  assert_num_eq "items[0].entity.total (2 * 149.99)" "$total_0" "299.98"           || return 1
  assert_eq "items[1].entity.customer_email" "$email_1" "batch-test-2@example.com"  || return 1
  assert_num_eq "items[1].entity.total (1 * 89.99)" "$total_1" "89.99"             || return 1

  # Verify IDs were generated
  CREATED_ORDER_ID_1=$(jq_val '.items[0].entity.id')
  CREATED_ORDER_ID_2=$(jq_val '.items[1].entity.id')
  assert_ne "items[0].entity.id" "$CREATED_ORDER_ID_1" "null"                       || return 1
  assert_ne "items[1].entity.id" "$CREATED_ORDER_ID_2" "null"                       || return 1

  # Verify created_at is present in the response
  local created_at_0
  created_at_0=$(jq_val '.items[0].entity.created_at')
  assert_ne "items[0].entity.created_at" "$created_at_0" "null"                     || return 1

  info "Captured order IDs: ${CREATED_ORDER_ID_1}, ${CREATED_ORDER_ID_2}"
}

# =============================================================================
# TEST 2: Batch Delete Orders (Happy Path)
# =============================================================================
test_batch_delete_orders() {
  if [ -z "$CREATED_ORDER_ID_1" ] || [ -z "$CREATED_ORDER_ID_2" ]; then
    warn "Skipping: no order IDs from Test 1"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "delete",
    "items": ["'"${CREATED_ORDER_ID_1}"'", "'"${CREATED_ORDER_ID_2}"'"]
  }'

  assert_http_status "200"       || return 1
  assert_items_count "2"         || return 1
  assert_item_status 0 "204"     || return 1
  assert_item_status 1 "204"     || return 1
  assert_item_no_entity 0        || return 1
  assert_item_no_entity 1        || return 1
  assert_item_no_error 0         || return 1
  assert_item_no_error 1         || return 1

  # Confirm they are actually gone
  http_get "${BASE_URL}/api/orders/${CREATED_ORDER_ID_1}"
  assert_http_status "404" || return 1
  pass "Deleted order 1 confirmed gone"
}

# =============================================================================
# TEST 3: Batch Delete Non-Existent Orders (404 per item -> 207 envelope)
# =============================================================================
test_batch_delete_not_found() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "delete",
    "items": ["'"${NONEXISTENT_ID}"'", "'"${NONEXISTENT_ID2}"'"]
  }'

  # Envelope returns 207 because all items failed (not 2xx)
  assert_http_status "207"       || return 1
  assert_items_count "2"         || return 1
  assert_item_status 0 "404"     || return 1
  assert_item_status 1 "404"     || return 1
  assert_item_has_error 0        || return 1
  assert_item_has_error 1        || return 1
  assert_item_no_entity 0        || return 1
  assert_item_no_entity 1        || return 1

  # Verify error structure
  local error_type
  error_type=$(jq_val '.items[0].error.type')
  assert_eq "items[0].error.type" "$error_type" "/problems/not-found" || return 1
}

# =============================================================================
# TEST 4: Batch Create Products (Happy Path)
# =============================================================================
test_batch_create_products() {
  http_post "${BASE_URL}/api/products/batch" '{
    "action": "create",
    "items": [
      {
        "name": "Batch Test Product A",
        "price": 19.99,
        "category_id": "'"${ELECTRONICS_ID}"'",
        "is_active": true
      },
      {
        "name": "Batch Test Product B",
        "price": 29.99,
        "category_id": "'"${BOOKS_ID}"'",
        "is_active": false
      }
    ]
  }'

  assert_http_status "200"       || return 1
  assert_items_count "2"         || return 1
  assert_item_status 0 "201"     || return 1
  assert_item_status 1 "201"     || return 1
  assert_item_has_entity 0       || return 1
  assert_item_has_entity 1       || return 1

  local name_0 price_0 name_1 active_1
  name_0=$(jq_val '.items[0].entity.name')
  price_0=$(jq_val '.items[0].entity.price')
  name_1=$(jq_val '.items[1].entity.name')
  active_1=$(jq_val '.items[1].entity.is_active')

  assert_eq "items[0].entity.name"      "$name_0"    "Batch Test Product A" || return 1
  assert_num_eq "items[0].entity.price" "$price_0"   "19.99"               || return 1
  assert_eq "items[1].entity.name"      "$name_1"    "Batch Test Product B" || return 1
  assert_eq "items[1].entity.is_active" "$active_1"  "false"               || return 1

  CREATED_PRODUCT_ID_1=$(jq_val '.items[0].entity.id')
  CREATED_PRODUCT_ID_2=$(jq_val '.items[1].entity.id')
  info "Captured product IDs: ${CREATED_PRODUCT_ID_1}, ${CREATED_PRODUCT_ID_2}"
}

# =============================================================================
# TEST 5: Batch Patch Products (Partial Update)
# =============================================================================
test_batch_patch_products() {
  if [ -z "$CREATED_PRODUCT_ID_1" ] || [ -z "$CREATED_PRODUCT_ID_2" ]; then
    warn "Skipping: no product IDs from Test 4"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_post "${BASE_URL}/api/products/batch" '{
    "action": "patch",
    "items": [
      {
        "id": "'"${CREATED_PRODUCT_ID_1}"'",
        "body": { "price": 999.99 }
      },
      {
        "id": "'"${CREATED_PRODUCT_ID_2}"'",
        "body": { "is_active": true, "name": "Renamed Product B" }
      }
    ]
  }'

  assert_http_status "200"       || return 1
  assert_items_count "2"         || return 1
  assert_item_status 0 "200"     || return 1
  assert_item_status 1 "200"     || return 1
  assert_item_has_entity 0       || return 1
  assert_item_has_entity 1       || return 1

  # Verify patched fields changed
  local price_0 name_1 active_1
  price_0=$(jq_val '.items[0].entity.price')
  name_1=$(jq_val '.items[1].entity.name')
  active_1=$(jq_val '.items[1].entity.is_active')

  assert_num_eq "items[0].entity.price (patched)" "$price_0" "999.99"              || return 1
  assert_eq "items[1].entity.name (patched)"       "$name_1"    "Renamed Product B" || return 1
  assert_eq "items[1].entity.is_active (patched)"  "$active_1"  "true"              || return 1

  # Verify unpatched fields were preserved
  local name_0 cat_0
  name_0=$(jq_val '.items[0].entity.name')
  cat_0=$(jq_val '.items[0].entity.category_id')
  assert_eq "items[0].entity.name (preserved)" "$name_0" "Batch Test Product A"     || return 1
  assert_eq "items[0].entity.category_id (preserved)" "$cat_0" "${ELECTRONICS_ID}"  || return 1
}

# =============================================================================
# TEST 6: Batch Delete Products (Cleanup)
# =============================================================================
test_batch_delete_products() {
  if [ -z "$CREATED_PRODUCT_ID_1" ] || [ -z "$CREATED_PRODUCT_ID_2" ]; then
    warn "Skipping: no product IDs from Test 4"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_post "${BASE_URL}/api/products/batch" '{
    "action": "delete",
    "items": ["'"${CREATED_PRODUCT_ID_1}"'", "'"${CREATED_PRODUCT_ID_2}"'"]
  }'

  assert_http_status "200"       || return 1
  assert_items_count "2"         || return 1
  assert_item_status 0 "204"     || return 1
  assert_item_status 1 "204"     || return 1
}

# =============================================================================
# TEST 7: Disabled Batch Action — Update on Orders (only Create/Delete allowed)
# =============================================================================
test_disabled_action_orders_update() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "update",
    "items": [
      { "id": "'"${NONEXISTENT_ID}"'", "body": {} }
    ]
  }'

  assert_http_status "400"                                                      || return 1
  assert_problem_type "/problems/batch-action-not-enabled"                      || return 1
  assert_contains "detail" "$(jq_val '.detail')" "update"                       || return 1
}

# =============================================================================
# TEST 8: Disabled Batch Action — Update on Products (only Create/Patch/Delete)
# =============================================================================
test_disabled_action_products_update() {
  http_post "${BASE_URL}/api/products/batch" '{
    "action": "update",
    "items": [
      { "id": "'"${HEADPHONES_ID}"'", "body": {} }
    ]
  }'

  assert_http_status "400"                                                      || return 1
  assert_problem_type "/problems/batch-action-not-enabled"                      || return 1
  assert_contains "detail" "$(jq_val '.detail')" "update"                       || return 1
}

# =============================================================================
# TEST 9: Disabled Batch Action — Patch on Orders (only Create/Delete)
# =============================================================================
test_disabled_action_orders_patch() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "patch",
    "items": [
      { "id": "'"${NONEXISTENT_ID}"'", "body": { "status": "Shipped" } }
    ]
  }'

  assert_http_status "400"                                                      || return 1
  assert_problem_type "/problems/batch-action-not-enabled"                      || return 1
  assert_contains "detail" "$(jq_val '.detail')" "patch"                        || return 1
}

# =============================================================================
# TEST 10: Invalid Action Name
# =============================================================================
test_invalid_action_name() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "explode",
    "items": []
  }'

  assert_http_status "400"                                                      || return 1
  assert_problem_type "/problems/invalid-batch-request"                         || return 1
  assert_contains "detail" "$(jq_val '.detail')" "explode"                      || return 1
}

# =============================================================================
# TEST 11: Exceed MaxBatchSize (default 100)
# =============================================================================
test_exceed_max_batch_size() {
  # Build a JSON payload with 101 items using jq
  local items
  items=$(jq -n '[range(101) | {customer_email: ("user\(.)@test.com"), lines: [], status: "Pending"}]')

  http_post "${BASE_URL}/api/orders/batch" "{
    \"action\": \"create\",
    \"items\": ${items}
  }"

  assert_http_status "400"                                                      || return 1
  assert_problem_type "/problems/batch-size-exceeded"                           || return 1
  assert_contains "detail" "$(jq_val '.detail')" "101"                          || return 1
  assert_contains "detail" "$(jq_val '.detail')" "100"                          || return 1
}

# =============================================================================
# TEST 12: Empty Items Array
# =============================================================================
test_empty_items_array() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "create",
    "items": []
  }'

  assert_http_status "400"                                                      || return 1
  assert_problem_type "/problems/invalid-batch-request"                         || return 1
  assert_contains "detail" "$(jq_val '.detail')" "at least one item"            || return 1
}

# =============================================================================
# TEST 13: Resource Without Batch Config (Categories -> 405)
# =============================================================================
test_no_batch_config_categories() {
  http_post "${BASE_URL}/api/categories/batch" '{
    "action": "create",
    "items": [{ "name": "Test Category" }]
  }'

  # "batch" is captured by the {id} parameter, which fails since it's not a Guid
  # This results in 405 Method Not Allowed
  assert_http_status "405" || return 1
}

# =============================================================================
# TEST 14: Mixed Success/Failure in Batch Patch (1 valid + 1 not found)
# =============================================================================
test_mixed_success_failure_patch() {
  http_post "${BASE_URL}/api/products/batch" '{
    "action": "patch",
    "items": [
      {
        "id": "'"${HEADPHONES_ID}"'",
        "body": { "price": 5.00 }
      },
      {
        "id": "'"${NONEXISTENT_ID}"'",
        "body": { "price": 1.00 }
      }
    ]
  }'

  assert_http_status "207"       || return 1
  assert_items_count "2"         || return 1

  # First item succeeds
  assert_item_status 0 "200"     || return 1
  assert_item_has_entity 0       || return 1
  assert_item_no_error 0         || return 1

  local price_0
  price_0=$(jq_val '.items[0].entity.price')
  assert_num_eq "items[0].entity.price" "$price_0" "5" || return 1

  # Second item fails (not found)
  assert_item_status 1 "404"     || return 1
  assert_item_has_error 1        || return 1
  assert_item_no_entity 1        || return 1

  local error_type
  error_type=$(jq_val '.items[1].error.type')
  assert_eq "items[1].error.type" "$error_type" "/problems/not-found"            || return 1

  # Restore original price so the test is idempotent
  http_post "${BASE_URL}/api/products/batch" '{
    "action": "patch",
    "items": [{ "id": "'"${HEADPHONES_ID}"'", "body": { "price": 149.99 } }]
  }'
  info "Restored Headphones price to 149.99"
}

# =============================================================================
# TEST 15: Mixed Success/Failure in Batch Delete (1 valid + 1 not found)
# =============================================================================
test_mixed_success_failure_delete() {
  # First create a temporary order so we have a valid target
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "create",
    "items": [
      {
        "customer_email": "temp-delete@example.com",
        "lines": [],
        "status": "Pending"
      }
    ]
  }'
  local temp_id
  temp_id=$(jq_val '.items[0].entity.id')

  # Now delete: one real, one fake
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "delete",
    "items": ["'"${temp_id}"'", "'"${NONEXISTENT_ID}"'"]
  }'

  # 207 because not all items succeeded
  assert_http_status "207"       || return 1
  assert_items_count "2"         || return 1

  # First item succeeds
  assert_item_status 0 "204"     || return 1
  assert_item_no_entity 0        || return 1
  assert_item_no_error 0         || return 1

  # Second item fails
  assert_item_status 1 "404"     || return 1
  assert_item_has_error 1        || return 1
}

# =============================================================================
# TEST 16: Batch Create with Hook Execution (verify total calculation)
# =============================================================================
test_batch_create_hook_execution() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "create",
    "items": [
      {
        "customer_email": "hook-test@example.com",
        "lines": [
          { "product_id": "'"${HEADPHONES_ID}"'", "quantity": 3, "unit_price": 100.00 },
          { "product_id": "'"${KEYBOARD_ID}"'",   "quantity": 2, "unit_price": 50.00 }
        ],
        "status": "Pending"
      }
    ]
  }'

  assert_http_status "200"       || return 1
  assert_item_status 0 "201"     || return 1

  # The BeforePersist hook calculates: (3*100) + (2*50) = 400
  local total
  total=$(jq_val '.items[0].entity.total')
  assert_num_eq "items[0].entity.total (hook calc: 3*100 + 2*50)" "$total" "400" || return 1

  # Clean up
  local id
  id=$(jq_val '.items[0].entity.id')
  http_post "${BASE_URL}/api/orders/batch" '{"action":"delete","items":["'"${id}"'"]}'
  info "Cleaned up order ${id}"
}

# =============================================================================
# TEST 17: Exactly at MaxBatchSize (100 items — should succeed)
# =============================================================================
test_at_max_batch_size() {
  local items
  items=$(jq -n '[range(100) | {customer_email: ("limit-test-\(.)@test.com"), lines: [], status: "Pending"}]')

  http_post "${BASE_URL}/api/orders/batch" "{
    \"action\": \"create\",
    \"items\": ${items}
  }"

  assert_http_status "200"       || return 1
  assert_items_count "100"       || return 1

  # Verify first and last item
  assert_item_status 0 "201"     || return 1
  assert_item_status 99 "201"    || return 1

  # Clean up: delete all 100 created orders
  local ids
  ids=$(echo "$HTTP_BODY" | jq '[.items[].entity.id]')
  http_post "${BASE_URL}/api/orders/batch" "{\"action\":\"delete\",\"items\":${ids}}"
  info "Cleaned up 100 orders"
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Batch Create Orders (Happy Path)"                     test_batch_create_orders
run_test "Batch Delete Orders (Happy Path)"                     test_batch_delete_orders
run_test "Batch Delete Non-Existent Orders (404 per item)"      test_batch_delete_not_found
run_test "Batch Create Products (Happy Path)"                   test_batch_create_products
run_test "Batch Patch Products (Partial Update)"                test_batch_patch_products
run_test "Batch Delete Products (Cleanup)"                      test_batch_delete_products
run_test "Disabled Action: Update on Orders"                    test_disabled_action_orders_update
run_test "Disabled Action: Update on Products"                  test_disabled_action_products_update
run_test "Disabled Action: Patch on Orders"                     test_disabled_action_orders_patch
run_test "Invalid Action Name"                                  test_invalid_action_name
run_test "Exceed MaxBatchSize (101 items)"                      test_exceed_max_batch_size
run_test "Empty Items Array"                                    test_empty_items_array
run_test "No Batch Config (Categories -> 405)"                  test_no_batch_config_categories
run_test "Mixed Success/Failure: Patch (valid + not found)"     test_mixed_success_failure_patch
run_test "Mixed Success/Failure: Delete (valid + not found)"    test_mixed_success_failure_delete
run_test "Batch Create with Hook Execution (total calc)"        test_batch_create_hook_execution
run_test "Exactly at MaxBatchSize (100 items)"                  test_at_max_batch_size

# =============================================================================
# Results
# =============================================================================
print_summary
exit $?
