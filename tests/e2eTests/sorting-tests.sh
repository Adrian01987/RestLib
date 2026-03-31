#!/usr/bin/env bash
# =============================================================================
# sorting-tests.sh — E2E tests for query parameter sorting
# =============================================================================
# Products: sortable by price, name, created_at. Default: name:asc
# Orders: sortable by created_at, total. Default: created_at:desc
# Sort format: ?sort=field:direction (asc/desc), comma-separated for multi-sort
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Sorting — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: Default sort on Products (name:asc)
# =============================================================================
test_default_sort_products() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                               || return 1

  # Verify items are sorted by name ascending
  local names sorted_names
  names=$(jq_val '[.items[].name] | join(",")')
  sorted_names=$(jq_val '[.items[].name] | sort | join(",")')
  assert_eq "name order (asc)" "$names" "$sorted_names"  || return 1
}

# =============================================================================
# TEST 2: Sort products by price ascending
# =============================================================================
test_sort_price_asc() {
  http_get "${BASE_URL}/api/products?sort=price:asc"

  assert_http_status "200"                               || return 1

  # First item should be the cheapest (Cotton T-Shirt $24.99)
  local first_name first_price
  first_name=$(jq_val '.items[0].name')
  first_price=$(jq_val '.items[0].price')
  assert_eq "cheapest product" "$first_name" "Cotton T-Shirt" || return 1
  assert_num_eq "cheapest price" "$first_price" "24.99"  || return 1
}

# =============================================================================
# TEST 3: Sort products by price descending
# =============================================================================
test_sort_price_desc() {
  http_get "${BASE_URL}/api/products?sort=price:desc"

  assert_http_status "200"                               || return 1

  # First item should be the most expensive (Wireless Headphones $149.99)
  local first_name first_price
  first_name=$(jq_val '.items[0].name')
  first_price=$(jq_val '.items[0].price')
  assert_eq "most expensive product" "$first_name" "Wireless Headphones" || return 1
  assert_num_eq "highest price" "$first_price" "149.99"  || return 1
}

# =============================================================================
# TEST 4: Sort products by name descending
# =============================================================================
test_sort_name_desc() {
  http_get "${BASE_URL}/api/products?sort=name:desc"

  assert_http_status "200"                               || return 1

  # Reverse alphabetical: Wireless Headphones should be first
  local first_name
  first_name=$(jq_val '.items[0].name')
  assert_eq "last alphabetically" "$first_name" "Wireless Headphones" || return 1
}

# =============================================================================
# TEST 5: Default sort on Orders (created_at:desc — most recent first)
# =============================================================================
test_default_sort_orders() {
  http_get "${BASE_URL}/api/orders"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "orders count" "$count" "3"                  || return 1

  # created_at:desc means most recent first
  # Bob's order is 1 day ago, Alice's shipped is 3 days ago, Alice's completed is 5 days ago
  local first_email first_status
  first_email=$(jq_val '.items[0].customer_email')
  first_status=$(jq_val '.items[0].status')
  assert_eq "most recent order" "$first_email" "bob@example.com"  || return 1
  assert_eq "most recent status" "$first_status" "Pending"        || return 1
}

# =============================================================================
# TEST 6: Sort orders by total ascending
# =============================================================================
test_sort_orders_total_asc() {
  http_get "${BASE_URL}/api/orders?sort=total:asc"

  assert_http_status "200"                               || return 1

  # Cheapest order first: Bob's ($64.98)
  local first_total
  first_total=$(jq_val '.items[0].total')
  assert_num_eq "cheapest order total" "$first_total" "64.98" || return 1
}

# =============================================================================
# TEST 7: Sort orders by total descending
# =============================================================================
test_sort_orders_total_desc() {
  http_get "${BASE_URL}/api/orders?sort=total:desc"

  assert_http_status "200"                               || return 1

  # Most expensive order first: Alice's shipped ($449.97)
  local first_total
  first_total=$(jq_val '.items[0].total')
  assert_num_eq "most expensive order total" "$first_total" "449.97" || return 1
}

# =============================================================================
# TEST 8: Invalid sort field
# =============================================================================
test_invalid_sort_field() {
  http_get "${BASE_URL}/api/products?sort=description:asc"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-sort"           || return 1
}

# =============================================================================
# TEST 9: Invalid sort direction
# =============================================================================
test_invalid_sort_direction() {
  http_get "${BASE_URL}/api/products?sort=price:upward"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-sort"           || return 1
}

# =============================================================================
# TEST 10: Sort + filter combined
# =============================================================================
test_sort_with_filter() {
  http_get "${BASE_URL}/api/products?category_id=${ELECTRONICS_ID}&sort=price:asc"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "electronics count" "$count" "3"             || return 1

  # Cheapest electronics first (USB-C Hub $49.99)
  local first_name
  first_name=$(jq_val '.items[0].name')
  assert_eq "cheapest electronics" "$first_name" "USB-C Hub" || return 1
}

# =============================================================================
# TEST 11: Sort + pagination combined
# =============================================================================
test_sort_with_pagination() {
  http_get "${BASE_URL}/api/products?sort=price:desc&limit=3"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "3"                   || return 1

  # Most expensive 3: Headphones ($149.99), Keyboard ($89.99), Jeans ($59.99)
  local first_price
  first_price=$(jq_val '.items[0].price')
  assert_num_eq "first price (desc)" "$first_price" "149.99" || return 1

  assert_json_field_exists ".next"                       || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Default Sort on Products (name:asc)"            test_default_sort_products
run_test "Sort Products by price:asc"                     test_sort_price_asc
run_test "Sort Products by price:desc"                    test_sort_price_desc
run_test "Sort Products by name:desc"                     test_sort_name_desc
run_test "Default Sort on Orders (created_at:desc)"       test_default_sort_orders
run_test "Sort Orders by total:asc"                       test_sort_orders_total_asc
run_test "Sort Orders by total:desc"                      test_sort_orders_total_desc
run_test "Invalid Sort Field"                             test_invalid_sort_field
run_test "Invalid Sort Direction"                         test_invalid_sort_direction
run_test "Sort + Filter Combined"                         test_sort_with_filter
run_test "Sort + Pagination Combined"                     test_sort_with_pagination

print_summary
exit $?
