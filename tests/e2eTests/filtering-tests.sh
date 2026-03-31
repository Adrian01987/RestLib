#!/usr/bin/env bash
# =============================================================================
# filtering-tests.sh — E2E tests for query parameter filtering
# =============================================================================
# Products: filterable by category_id (Guid), is_active (bool)
# Orders: filterable by status (string), customer_email (string)
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Filtering — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: Filter products by category_id
# =============================================================================
test_filter_by_category() {
  http_get "${BASE_URL}/api/products?category_id=${ELECTRONICS_ID}"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "electronics products count" "$count" "3"    || return 1

  # All items should have the electronics category_id
  local all_cats
  all_cats=$(jq_val '[.items[].category_id] | unique | join(",")')
  assert_eq "all category_ids" "$all_cats" "${ELECTRONICS_ID}" || return 1
}

# =============================================================================
# TEST 2: Filter products by is_active=true
# =============================================================================
test_filter_by_is_active_true() {
  http_get "${BASE_URL}/api/products?is_active=true"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # All 8 seed products are active
  assert_eq "active products count" "$count" "8"         || return 1
}

# =============================================================================
# TEST 3: Filter products by is_active=false (no results)
# =============================================================================
test_filter_by_is_active_false() {
  http_get "${BASE_URL}/api/products?is_active=false"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "inactive products count" "$count" "0"       || return 1
}

# =============================================================================
# TEST 4: Combine multiple filters (category + is_active)
# =============================================================================
test_combined_filters() {
  http_get "${BASE_URL}/api/products?category_id=${BOOKS_ID}&is_active=true"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # 3 books, all active
  assert_eq "books + active count" "$count" "3"          || return 1

  # All should be books
  local all_cats
  all_cats=$(jq_val '[.items[].category_id] | unique | join(",")')
  assert_eq "all category_ids" "$all_cats" "${BOOKS_ID}" || return 1
}

# =============================================================================
# TEST 5: Filter orders by status
# =============================================================================
test_filter_orders_by_status() {
  http_get "${BASE_URL}/api/orders?status=Pending"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_ge "pending orders" "$count" "1"                || return 1

  # All items should have Pending status
  local statuses
  statuses=$(jq_val '[.items[].status] | unique | join(",")')
  assert_eq "all statuses" "$statuses" "Pending"         || return 1
}

# =============================================================================
# TEST 6: Filter orders by customer_email
# =============================================================================
test_filter_orders_by_email() {
  http_get "${BASE_URL}/api/orders?customer_email=alice@example.com"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Alice has 2 orders (Completed + Shipped)
  assert_eq "alice orders count" "$count" "2"            || return 1

  local emails
  emails=$(jq_val '[.items[].customer_email] | unique | join(",")')
  assert_eq "all emails" "$emails" "alice@example.com"   || return 1
}

# =============================================================================
# TEST 7: Filter with no matching results
# =============================================================================
test_filter_no_results() {
  http_get "${BASE_URL}/api/orders?status=Cancelled"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "cancelled orders count" "$count" "0"        || return 1
}

# =============================================================================
# TEST 8: Unregistered filter property (silently ignored by design)
#   The filter parser only processes configured properties; unknown query
#   parameters are ignored and the request succeeds normally.
# =============================================================================
test_invalid_filter_property() {
  http_get "${BASE_URL}/api/products?description=test"

  assert_http_status "200"                               || return 1
  pass "Unknown query parameter 'description' was silently ignored"

  # All products are still returned (no filtering happened)
  local count
  count=$(jq_len ".items")
  assert_ge "products count (unfiltered)" "$count" "8"   || return 1
}

# =============================================================================
# TEST 9: Invalid filter value (bad Guid for category_id)
# =============================================================================
test_invalid_filter_value_guid() {
  http_get "${BASE_URL}/api/products?category_id=not-a-guid"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-filter"         || return 1
}

# =============================================================================
# TEST 10: Invalid filter value (bad bool for is_active)
# =============================================================================
test_invalid_filter_value_bool() {
  http_get "${BASE_URL}/api/products?is_active=maybe"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-filter"         || return 1
}

# =============================================================================
# TEST 11: Filter + pagination combined
# =============================================================================
test_filter_with_pagination() {
  http_get "${BASE_URL}/api/products?category_id=${ELECTRONICS_ID}&limit=2"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "2"                   || return 1

  # Should have next link since 3 electronics products > limit 2
  assert_json_field_exists ".next"                       || return 1
}

# =============================================================================
# TEST 12: Filter with non-existent category (valid Guid, no matches)
# =============================================================================
test_filter_nonexistent_category() {
  http_get "${BASE_URL}/api/products?category_id=${NONEXISTENT_ID}"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "0"                   || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Filter Products by category_id"                 test_filter_by_category
run_test "Filter Products by is_active=true"              test_filter_by_is_active_true
run_test "Filter Products by is_active=false"             test_filter_by_is_active_false
run_test "Combined Filters (category + is_active)"        test_combined_filters
run_test "Filter Orders by status"                        test_filter_orders_by_status
run_test "Filter Orders by customer_email"                test_filter_orders_by_email
run_test "Filter with No Matching Results"                test_filter_no_results
run_test "Invalid Filter Property"                        test_invalid_filter_property
run_test "Invalid Filter Value (bad Guid)"                test_invalid_filter_value_guid
run_test "Invalid Filter Value (bad bool)"                test_invalid_filter_value_bool
run_test "Filter + Pagination Combined"                   test_filter_with_pagination
run_test "Filter Non-Existent Category"                   test_filter_nonexistent_category

print_summary
exit $?
