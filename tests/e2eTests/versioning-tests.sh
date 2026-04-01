#!/usr/bin/env bash
# =============================================================================
# versioning-tests.sh — E2E tests for versioned API groups
# =============================================================================
# The sample app registers Product at two versioned prefixes:
#   /api/v1/products — read-only (GetAll + GetById), filtering by category_id
#   /api/v2/products — full CRUD, filtering, sorting, field selection
#
# These tests verify that both version groups serve the same underlying data
# with independent feature sets.
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Versioning — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: v1 GET /api/v1/products returns 200 with items
# =============================================================================
test_v1_get_all() {
  http_get "${BASE_URL}/api/v1/products"

  assert_http_status "200"                                          || return 1

  local count
  count=$(jq_len ".items")
  assert_gt "v1 item count" "$count" "0"                            || return 1
}

# =============================================================================
# TEST 2: v2 GET /api/v2/products returns 200 with items
# =============================================================================
test_v2_get_all() {
  http_get "${BASE_URL}/api/v2/products"

  assert_http_status "200"                                          || return 1

  local count
  count=$(jq_len ".items")
  assert_gt "v2 item count" "$count" "0"                            || return 1
}

# =============================================================================
# TEST 3: v1 and v2 return the same number of items (same underlying data)
# =============================================================================
test_v1_v2_same_data() {
  http_get "${BASE_URL}/api/v1/products?limit=100"
  local v1_count
  v1_count=$(jq_len ".items")

  http_get "${BASE_URL}/api/v2/products?limit=100"
  local v2_count
  v2_count=$(jq_len ".items")

  assert_eq "v1 vs v2 item count" "$v1_count" "$v2_count"          || return 1
}

# =============================================================================
# TEST 4: v1 GET by ID returns 200
# =============================================================================
test_v1_get_by_id() {
  # Grab the first product ID from v1
  http_get "${BASE_URL}/api/v1/products?limit=1"
  local id
  id=$(jq_val ".items[0].id")

  http_get "${BASE_URL}/api/v1/products/${id}"

  assert_http_status "200"                                          || return 1
  assert_json_field ".id" "$id"                                     || return 1
}

# =============================================================================
# TEST 5: v1 POST is rejected (read-only — method not allowed)
# =============================================================================
test_v1_post_rejected() {
  http_post "${BASE_URL}/api/v1/products" '{"name":"New","price":9.99}'

  assert_http_status "405"                                          || return 1
}

# =============================================================================
# TEST 6: v1 PUT is rejected (read-only)
# =============================================================================
test_v1_put_rejected() {
  # Grab a product ID
  http_get "${BASE_URL}/api/v1/products?limit=1"
  local id
  id=$(jq_val ".items[0].id")

  http_put "${BASE_URL}/api/v1/products/${id}" '{"name":"Updated","price":1.00}'

  assert_http_status "405"                                          || return 1
}

# =============================================================================
# TEST 7: v1 DELETE is rejected (read-only)
# =============================================================================
test_v1_delete_rejected() {
  http_get "${BASE_URL}/api/v1/products?limit=1"
  local id
  id=$(jq_val ".items[0].id")

  http_delete "${BASE_URL}/api/v1/products/${id}"

  assert_http_status "405"                                          || return 1
}

# =============================================================================
# TEST 8: v1 filtering by category_id works
# =============================================================================
test_v1_filtering() {
  http_get "${BASE_URL}/api/v1/products?category_id=${ELECTRONICS_ID}"

  assert_http_status "200"                                          || return 1

  local count
  count=$(jq_len ".items")
  assert_gt "filtered v1 count" "$count" "0"                        || return 1

  # All returned items should have the electronics category
  local all_match
  all_match=$(echo "$HTTP_BODY" | jq "[.items[].category_id] | all(. == \"${ELECTRONICS_ID}\")")
  assert_eq "all items match category" "$all_match" "true"          || return 1
}

# =============================================================================
# TEST 9: v2 sorting by price ascending works
# =============================================================================
test_v2_sorting() {
  http_get "${BASE_URL}/api/v2/products?sort=price:asc&limit=100"

  assert_http_status "200"                                          || return 1

  # Verify prices are in ascending order
  local prices sorted_prices
  prices=$(echo "$HTTP_BODY" | jq '[.items[].price]')
  sorted_prices=$(echo "$HTTP_BODY" | jq '[.items[].price] | sort')
  assert_eq "price order (asc)" "$prices" "$sorted_prices"          || return 1
}

# =============================================================================
# TEST 10: v2 field selection works
# =============================================================================
test_v2_field_selection() {
  http_get "${BASE_URL}/api/v2/products?fields=name,price&limit=1"

  assert_http_status "200"                                          || return 1

  # Should have name and price but not description or category_id
  assert_json_field_exists ".items[0].name"                         || return 1
  assert_json_field_exists ".items[0].price"                        || return 1
  assert_json_field_null ".items[0].description"                    || return 1
  assert_json_field_null ".items[0].category_id"                    || return 1
}

# =============================================================================
# TEST 11: v2 POST creates a product (full CRUD)
# =============================================================================
test_v2_post_creates() {
  http_post "${BASE_URL}/api/v2/products" "{\"name\":\"E2E Versioned Product\",\"price\":77.77,\"category_id\":\"${ELECTRONICS_ID}\"}"

  assert_http_status "201"                                          || return 1
  assert_json_field ".name" "E2E Versioned Product"                 || return 1

  # Verify it shows up in v2 listing
  local new_id
  new_id=$(jq_val ".id")

  http_get "${BASE_URL}/api/v2/products/${new_id}"
  assert_http_status "200"                                          || return 1
  assert_json_field ".name" "E2E Versioned Product"                 || return 1

  # Verify it also shows up in v1 listing (same underlying repo)
  http_get "${BASE_URL}/api/v1/products/${new_id}"
  assert_http_status "200"                                          || return 1
  assert_json_field ".name" "E2E Versioned Product"                 || return 1

  # Cleanup — delete via v2
  http_delete "${BASE_URL}/api/v2/products/${new_id}"
  assert_http_status "204"                                          || return 1
}

# =============================================================================
# TEST 12: v2 default sort is name:asc
# =============================================================================
test_v2_default_sort() {
  http_get "${BASE_URL}/api/v2/products?limit=100"

  assert_http_status "200"                                          || return 1

  # Items should be sorted by name ascending (default sort)
  local names sorted_names
  names=$(jq_val '[.items[].name] | join(",")')
  sorted_names=$(jq_val '[.items[].name] | sort | join(",")')
  assert_eq "name order (asc)" "$names" "$sorted_names"             || return 1
}

# =============================================================================
# TEST 13: v2 filtering by is_active works (v2-only filter)
# =============================================================================
test_v2_filtering_is_active() {
  http_get "${BASE_URL}/api/v2/products?is_active=true"

  assert_http_status "200"                                          || return 1

  # All returned items should have is_active = true
  local all_active
  all_active=$(echo "$HTTP_BODY" | jq '[.items[].is_active] | all')
  assert_eq "all items active" "$all_active" "true"                 || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "v1 GET all returns 200"                     test_v1_get_all
run_test "v2 GET all returns 200"                     test_v2_get_all
run_test "v1 and v2 return same data"                 test_v1_v2_same_data
run_test "v1 GET by ID returns 200"                   test_v1_get_by_id
run_test "v1 POST rejected (read-only)"               test_v1_post_rejected
run_test "v1 PUT rejected (read-only)"                test_v1_put_rejected
run_test "v1 DELETE rejected (read-only)"             test_v1_delete_rejected
run_test "v1 filtering by category_id"                test_v1_filtering
run_test "v2 sorting by price ascending"              test_v2_sorting
run_test "v2 field selection"                         test_v2_field_selection
run_test "v2 POST creates product"                    test_v2_post_creates
run_test "v2 default sort is name:asc"                test_v2_default_sort
run_test "v2 filtering by is_active"                  test_v2_filtering_is_active

print_summary
exit $?
