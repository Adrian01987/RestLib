#!/usr/bin/env bash
# =============================================================================
# filter-operator-tests.sh — E2E tests for filter operators (bracket syntax)
# =============================================================================
# Products: Price  — comparison operators (eq, neq, gt, lt, gte, lte)
#           Name   — string operators (eq, neq, contains, starts_with, ends_with)
# Orders:   Total  — comparison operators
#           CustomerEmail — string operators (contains, starts_with, ends_with)
#           Status — equality only (default)
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Filter Operators — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: price[gte] — greater than or equal
# =============================================================================
test_price_gte() {
  http_get "${BASE_URL}/api/products?price[gte]=50"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Products with price >= 50: Headphones (149.99), Keyboard (89.99),
  # Design Patterns (54.99), Denim Jeans (59.99) = 4
  assert_eq "price >= 50 count" "$count" "4"             || return 1
}

# =============================================================================
# TEST 2: price[lt] — less than
# =============================================================================
test_price_lt() {
  http_get "${BASE_URL}/api/products?price[lt]=50"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Products with price < 50: USB-C Hub (49.99), Clean Code (39.99),
  # Pragmatic Programmer (44.99), Cotton T-Shirt (24.99) = 4
  assert_eq "price < 50 count" "$count" "4"              || return 1
}

# =============================================================================
# TEST 3: price[gt] and price[lte] — range query
# =============================================================================
test_price_range() {
  http_get "${BASE_URL}/api/products?price[gt]=40&price[lte]=90"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Products with 40 < price <= 90: USB-C Hub (49.99), Pragmatic (44.99),
  # Design Patterns (54.99), Keyboard (89.99), Denim Jeans (59.99) = 5
  assert_eq "40 < price <= 90 count" "$count" "5"        || return 1
}

# =============================================================================
# TEST 4: name[contains] — substring match
# =============================================================================
test_name_contains() {
  http_get "${BASE_URL}/api/products?name[contains]=code"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # "Clean Code" is the only product containing "code" (case-insensitive)
  assert_eq "name contains 'code' count" "$count" "1"    || return 1

  local name
  name=$(jq_val ".items[0].name")
  assert_eq "product name" "$name" "Clean Code"          || return 1
}

# =============================================================================
# TEST 5: name[starts_with] — prefix match
# =============================================================================
test_name_starts_with() {
  http_get "${BASE_URL}/api/products?name[starts_with]=Cotton"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "name starts_with 'Cotton' count" "$count" "1" || return 1

  local name
  name=$(jq_val ".items[0].name")
  assert_eq "product name" "$name" "Cotton T-Shirt"      || return 1
}

# =============================================================================
# TEST 6: name[contains] — case-insensitive
# =============================================================================
test_name_contains_case_insensitive() {
  http_get "${BASE_URL}/api/products?name[contains]=KEYBOARD"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "name contains 'KEYBOARD' count" "$count" "1" || return 1
}

# =============================================================================
# TEST 7: Bare equality still works (backward compat)
# =============================================================================
test_bare_equality_still_works() {
  http_get "${BASE_URL}/api/products?name=Clean+Code"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "bare name equality count" "$count" "1"      || return 1
}

# =============================================================================
# TEST 8: Explicit [eq] operator equivalent to bare equality
# =============================================================================
test_explicit_eq_operator() {
  http_get "${BASE_URL}/api/products?name[eq]=Clean+Code"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "name[eq] equality count" "$count" "1"       || return 1
}

# =============================================================================
# TEST 9: price[neq] — not equal
# =============================================================================
test_price_neq() {
  http_get "${BASE_URL}/api/products?price[neq]=149.99"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # 8 total products minus 1 headphones (149.99) = 7
  assert_eq "price != 149.99 count" "$count" "7"         || return 1
}

# =============================================================================
# TEST 10: Order total[gte] — comparison on orders
# =============================================================================
test_order_total_gte() {
  http_get "${BASE_URL}/api/orders?total[gte]=100"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Orders with total >= 100: Alice Completed (239.98), Alice Shipped (449.97) = 2
  assert_eq "order total >= 100 count" "$count" "2"      || return 1
}

# =============================================================================
# TEST 11: customer_email[contains] — string operator on orders
# =============================================================================
test_order_email_contains() {
  http_get "${BASE_URL}/api/orders?customer_email[contains]=alice"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Alice has 2 orders
  assert_eq "email contains 'alice' count" "$count" "2"  || return 1
}

# =============================================================================
# TEST 12: customer_email[starts_with] — prefix match on orders
# =============================================================================
test_order_email_starts_with() {
  http_get "${BASE_URL}/api/orders?customer_email[starts_with]=bob"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "email starts_with 'bob' count" "$count" "1" || return 1
}

# =============================================================================
# TEST 13: Unknown operator returns 400
# =============================================================================
test_unknown_operator_returns_400() {
  http_get "${BASE_URL}/api/products?price[regex]=.*"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-filter"         || return 1
}

# =============================================================================
# TEST 14: Disallowed operator returns 400
#   Products is_active only allows eq (default), not gte
# =============================================================================
test_disallowed_operator_returns_400() {
  http_get "${BASE_URL}/api/products?is_active[gte]=true"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-filter"         || return 1
}

# =============================================================================
# TEST 15: Operator + pagination combined
# =============================================================================
test_operator_with_pagination() {
  http_get "${BASE_URL}/api/products?price[gte]=40&limit=2"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "2"                   || return 1

  # Should have next link since there are more items with price >= 40
  assert_json_field_exists ".next"                       || return 1
}

# =============================================================================
# TEST 16: Operator + sorting combined
# =============================================================================
test_operator_with_sorting() {
  http_get "${BASE_URL}/api/products?price[gte]=50&sort=price:asc"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_ge "items with price >= 50" "$count" "2"        || return 1

  # First item should have the lowest price >= 50
  local first_price
  first_price=$(jq_val ".items[0].price")
  local second_price
  second_price=$(jq_val ".items[1].price")
  # First price should be <= second price (ascending order)
  assert_ge "ascending order" "$second_price" "$first_price" || return 1
}

# =============================================================================
# TEST 17: Combination — filter operators + field selection
# =============================================================================
test_operator_with_field_selection() {
  http_get "${BASE_URL}/api/products?price[lt]=50&fields=name,price"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_ge "cheap products" "$count" "1"                || return 1

  # Items should only have name and price fields
  local has_name
  has_name=$(jq_raw ".items[0].name")
  assert_ne "name present" "$has_name" "null"            || return 1

  local has_id
  has_id=$(jq_raw ".items[0].id")
  # id should be absent when field selection excludes it
  assert_eq "id absent" "$has_id" "null"                 || return 1
}

# =============================================================================
# TEST 18: status[in] — set membership filter
#   Orders Status is configured with [eq, neq, in] operators.
# =============================================================================
test_status_in_operator() {
  http_get "${BASE_URL}/api/orders?status[in]=Pending,Shipped"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  # Orders: Pending (Bob), Shipped (Alice) = 2 out of 3 total
  assert_eq "status in [Pending,Shipped] count" "$count" "2" || return 1

  # Verify none are "Completed"
  local statuses
  statuses=$(jq_val '[.items[].status] | join(",")')
  assert_not_contains "statuses" "$statuses" "Completed"  || return 1
}

# =============================================================================
# TEST 17: name[ends_with] — suffix match
# =============================================================================
test_name_ends_with() {
  http_get "${BASE_URL}/api/products?name[ends_with]=Keyboard"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "name ends_with 'Keyboard' count" "$count" "1" || return 1

  local name
  name=$(jq_val ".items[0].name")
  assert_eq "product name" "$name" "Mechanical Keyboard"  || return 1
}

# =============================================================================
# TEST 18: name[ends_with] — case-insensitive
# =============================================================================
test_name_ends_with_case_insensitive() {
  http_get "${BASE_URL}/api/products?name[ends_with]=code"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "name ends_with 'code' count" "$count" "1"  || return 1

  local name
  name=$(jq_val ".items[0].name")
  assert_eq "product name" "$name" "Clean Code"          || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "price[gte] — greater than or equal"                      test_price_gte
run_test "price[lt] — less than"                                   test_price_lt
run_test "price range (gt + lte)"                                  test_price_range
run_test "name[contains] — substring match"                        test_name_contains
run_test "name[starts_with] — prefix match"                        test_name_starts_with
run_test "name[ends_with] — suffix match"                          test_name_ends_with
run_test "name[contains] — case-insensitive"                       test_name_contains_case_insensitive
run_test "name[ends_with] — case-insensitive"                      test_name_ends_with_case_insensitive
run_test "Bare equality backward compat"                           test_bare_equality_still_works
run_test "Explicit [eq] operator"                                  test_explicit_eq_operator
run_test "price[neq] — not equal"                                  test_price_neq
run_test "Order total[gte]"                                        test_order_total_gte
run_test "Order customer_email[contains]"                          test_order_email_contains
run_test "Order customer_email[starts_with]"                       test_order_email_starts_with
run_test "Unknown operator returns 400"                            test_unknown_operator_returns_400
run_test "Disallowed operator returns 400"                         test_disallowed_operator_returns_400
run_test "Operator + pagination combined"                          test_operator_with_pagination
run_test "Operator + sorting combined"                             test_operator_with_sorting
run_test "Operator + field selection combined"                     test_operator_with_field_selection
run_test "status[in] — set membership filter"                      test_status_in_operator

print_summary
exit $?
