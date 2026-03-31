#!/usr/bin/env bash
# =============================================================================
# field-selection-tests.sh — E2E tests for field selection (?fields=...)
# =============================================================================
# Products: selectable fields — id, name, price, category_id, is_active
# Orders: selectable fields — id, customer_email, status, total, created_at
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Field Selection — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: Select specific fields on GetAll Products
# =============================================================================
test_select_fields_getall() {
  http_get "${BASE_URL}/api/products?fields=id,name,price"

  assert_http_status "200"                               || return 1

  # First item should have id, name, price
  assert_json_field_exists ".items[0].id"                || return 1
  assert_json_field_exists ".items[0].name"              || return 1
  assert_json_field_exists ".items[0].price"             || return 1

  # Should NOT have description, category_id, is_active, created_at
  assert_json_field_null ".items[0].description"         || return 1
  assert_json_field_null ".items[0].category_id"         || return 1
  assert_json_field_null ".items[0].is_active"           || return 1
  assert_json_field_null ".items[0].created_at"          || return 1
}

# =============================================================================
# TEST 2: Select specific fields on GetById Product
# =============================================================================
test_select_fields_getbyid() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}?fields=id,name"

  assert_http_status "200"                               || return 1

  assert_json_field ".id" "${HEADPHONES_ID}"             || return 1
  assert_json_field ".name" "Wireless Headphones"        || return 1

  # Fields not requested should be absent
  assert_json_field_null ".price"                        || return 1
  assert_json_field_null ".description"                  || return 1
}

# =============================================================================
# TEST 3: Select single field
# =============================================================================
test_select_single_field() {
  http_get "${BASE_URL}/api/products?fields=name"

  assert_http_status "200"                               || return 1

  assert_json_field_exists ".items[0].name"              || return 1
  assert_json_field_null ".items[0].id"                  || return 1
  assert_json_field_null ".items[0].price"               || return 1
}

# =============================================================================
# TEST 4: Select all available fields (same as no selection)
# =============================================================================
test_select_all_fields() {
  http_get "${BASE_URL}/api/products?fields=id,name,price,category_id,is_active"

  assert_http_status "200"                               || return 1

  assert_json_field_exists ".items[0].id"                || return 1
  assert_json_field_exists ".items[0].name"              || return 1
  assert_json_field_exists ".items[0].price"             || return 1
  assert_json_field_exists ".items[0].category_id"       || return 1
  assert_json_field_exists ".items[0].is_active"         || return 1
}

# =============================================================================
# TEST 5: Field selection on Orders (GetAll)
# =============================================================================
test_select_fields_orders() {
  http_get "${BASE_URL}/api/orders?fields=id,status,total"

  assert_http_status "200"                               || return 1

  assert_json_field_exists ".items[0].id"                || return 1
  assert_json_field_exists ".items[0].status"            || return 1
  assert_json_field_exists ".items[0].total"             || return 1

  # customer_email and created_at should be absent
  assert_json_field_null ".items[0].customer_email"      || return 1
  assert_json_field_null ".items[0].created_at"          || return 1
}

# =============================================================================
# TEST 6: Field selection on Orders (GetById)
# =============================================================================
test_select_fields_orders_getbyid() {
  # Get first order ID
  http_get "${BASE_URL}/api/orders"
  local order_id
  order_id=$(jq_val '.items[0].id')

  http_get "${BASE_URL}/api/orders/${order_id}?fields=customer_email,total"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".customer_email"             || return 1
  assert_json_field_exists ".total"                      || return 1
  assert_json_field_null ".status"                       || return 1
  assert_json_field_null ".id"                           || return 1
}

# =============================================================================
# TEST 7: Invalid field name
# =============================================================================
test_invalid_field_name() {
  http_get "${BASE_URL}/api/products?fields=id,nonexistent_field"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-fields"         || return 1
}

# =============================================================================
# TEST 8: Field not in allowed list (description is a real property but not selectable)
# =============================================================================
test_field_not_allowed() {
  http_get "${BASE_URL}/api/products?fields=id,description"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-fields"         || return 1
}

# =============================================================================
# TEST 9: Empty fields param
# =============================================================================
test_empty_fields() {
  http_get "${BASE_URL}/api/products?fields="

  # Empty fields should return all fields or error
  if [ "$HTTP_STATUS" = "200" ]; then
    pass "HTTP status = 200 (empty fields treated as no selection)"
    assert_json_field_exists ".items[0].name"            || return 1
  elif [ "$HTTP_STATUS" = "400" ]; then
    pass "HTTP status = 400 (empty fields rejected)"
    assert_problem_type "/problems/invalid-fields"       || return 1
  else
    fail "HTTP status = $HTTP_STATUS (expected 200 or 400)"
    return 1
  fi
}

# =============================================================================
# TEST 10: Field selection + sorting combined
# =============================================================================
test_fields_with_sorting() {
  http_get "${BASE_URL}/api/products?fields=name,price&sort=price:desc"

  assert_http_status "200"                               || return 1

  assert_json_field_exists ".items[0].name"              || return 1
  assert_json_field_exists ".items[0].price"             || return 1
  assert_json_field_null ".items[0].id"                  || return 1

  # First should be most expensive
  local first_name
  first_name=$(jq_val '.items[0].name')
  assert_eq "most expensive (field selected)" "$first_name" "Wireless Headphones" || return 1
}

# =============================================================================
# TEST 11: Field selection + filtering combined
# =============================================================================
test_fields_with_filtering() {
  http_get "${BASE_URL}/api/products?fields=name,price&category_id=${BOOKS_ID}"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "books count" "$count" "3"                   || return 1

  # Should only have name and price
  assert_json_field_exists ".items[0].name"              || return 1
  assert_json_field_exists ".items[0].price"             || return 1
  assert_json_field_null ".items[0].category_id"         || return 1
}

# =============================================================================
# TEST 12: No field selection on Categories (not configured)
# =============================================================================
test_no_field_selection_categories() {
  http_get "${BASE_URL}/api/categories?fields=name"

  # Categories don't have field selection configured — should error or ignore
  if [ "$HTTP_STATUS" = "400" ]; then
    pass "HTTP status = 400 (field selection not configured for categories)"
  elif [ "$HTTP_STATUS" = "200" ]; then
    # If it ignores unknown query param, that's also valid
    pass "HTTP status = 200 (query param ignored)"
  else
    fail "HTTP status = $HTTP_STATUS (expected 400 or 200)"
    return 1
  fi
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Select Fields on GetAll Products"               test_select_fields_getall
run_test "Select Fields on GetById Product"               test_select_fields_getbyid
run_test "Select Single Field"                            test_select_single_field
run_test "Select All Available Fields"                    test_select_all_fields
run_test "Select Fields on Orders (GetAll)"               test_select_fields_orders
run_test "Select Fields on Orders (GetById)"              test_select_fields_orders_getbyid
run_test "Invalid Field Name"                             test_invalid_field_name
run_test "Field Not in Allowed List"                      test_field_not_allowed
run_test "Empty Fields Param"                             test_empty_fields
run_test "Fields + Sorting Combined"                      test_fields_with_sorting
run_test "Fields + Filtering Combined"                    test_fields_with_filtering
run_test "No Field Selection on Categories"               test_no_field_selection_categories

print_summary
exit $?
