#!/usr/bin/env bash
# =============================================================================
# storefront-catalog.sh - E2E tests for the ecommerce storefront catalog
# =============================================================================
# Tests: list, filter, sort, sparse fields, pagination, 404, invalid filter.
# Resources used: storefront categories and storefront v1 products.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "${SCRIPT_DIR}/../e2e-lib.sh"

ECOMMERCE_ELECTRONICS_ID="10000000-0000-0000-0000-000000000001"
ECOMMERCE_UNKNOWN_ID="00000000-0000-0000-0000-000000000000"
STOREFRONT_CATEGORIES_URL="${BASE_URL}/api/storefront/categories"
STOREFRONT_PRODUCTS_URL="${BASE_URL}/api/v1/storefront/products"

header "Ecommerce Storefront Catalog - E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: List storefront categories
# =============================================================================
test_list_categories() {
  http_get "${STOREFRONT_CATEGORIES_URL}"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1

  local count names
  count=$(jq_len ".items")
  names=$(jq_val '[.items[].name] | sort | join(",")')

  assert_eq "categories count" "$count" "3"              || return 1
  assert_eq "category names" "$names" "Electronics,Home,Outdoors" || return 1
}

# =============================================================================
# TEST 2: List storefront products
# =============================================================================
test_list_products() {
  http_get "${STOREFRONT_PRODUCTS_URL}"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1

  local count sorted
  count=$(jq_len ".items")
  sorted=$(jq_val '[.items[].name] == ([.items[].name] | sort)')

  assert_ge "products count" "$count" "12"               || return 1
  assert_eq "default sort by name" "$sorted" "true"      || return 1
}

# =============================================================================
# TEST 3: Filter storefront products by category
# =============================================================================
test_filter_products_by_category() {
  http_get "${STOREFRONT_PRODUCTS_URL}?category_id=${ECOMMERCE_ELECTRONICS_ID}"

  assert_http_status "200"                               || return 1

  local count all_electronics
  count=$(jq_len ".items")
  all_electronics=$(jq_val 'all(.items[]; .category_id == "10000000-0000-0000-0000-000000000001")')

  assert_ge "electronics count" "$count" "4"             || return 1
  assert_eq "all products are electronics" "$all_electronics" "true" || return 1
}

# =============================================================================
# TEST 4: Filter storefront products by comparison operator
# =============================================================================
test_filter_products_by_price_operator() {
  http_get "${STOREFRONT_PRODUCTS_URL}?price[gte]=100"

  assert_http_status "200"                               || return 1

  local count all_at_least_100
  count=$(jq_len ".items")
  all_at_least_100=$(jq_val 'all(.items[]; .price >= 100)')

  assert_ge "products priced at least 100" "$count" "3"  || return 1
  assert_eq "all prices are >= 100" "$all_at_least_100" "true" || return 1
}

# =============================================================================
# TEST 5: Sort storefront products by price descending
# =============================================================================
test_sort_products_by_price_desc() {
  http_get "${STOREFRONT_PRODUCTS_URL}?sort=price:desc"

  assert_http_status "200"                               || return 1

  local sorted first_name
  sorted=$(jq_val '[.items[].price] == ([.items[].price] | sort | reverse)')
  first_name=$(jq_val '.items[0].name')

  assert_eq "price sort desc" "$sorted" "true"           || return 1
  assert_eq "highest-priced product" "$first_name" "Noise-Canceling Headphones" || return 1
}

# =============================================================================
# TEST 6: Sparse field selection
# =============================================================================
test_sparse_fields() {
  http_get "${STOREFRONT_PRODUCTS_URL}?fields=id,name,stock_on_hand&limit=1"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items[0].id"                || return 1
  assert_json_field_exists ".items[0].name"              || return 1
  assert_json_field_exists ".items[0].stock_on_hand"     || return 1
  assert_json_field_null ".items[0].price"               || return 1
  assert_json_field_null ".items[0].category_id"         || return 1
}

# =============================================================================
# TEST 7: Cursor pagination
# =============================================================================
test_pagination() {
  http_get "${STOREFRONT_PRODUCTS_URL}?limit=5"

  assert_http_status "200"                               || return 1

  local page1_count page1_first_id next_link
  page1_count=$(jq_len ".items")
  page1_first_id=$(jq_val ".items[0].id")
  next_link=$(jq_val ".next")

  assert_eq "page 1 count" "$page1_count" "5"            || return 1
  assert_ne "next link" "$next_link" "null"              || return 1

  http_get "$next_link"
  assert_http_status "200"                               || return 1

  local page2_count page2_first_id
  page2_count=$(jq_len ".items")
  page2_first_id=$(jq_val ".items[0].id")

  assert_ge "page 2 count" "$page2_count" "1"            || return 1
  assert_ne "page 2 first id" "$page2_first_id" "$page1_first_id" || return 1
}

# =============================================================================
# TEST 8: Unknown product id returns 404
# =============================================================================
test_unknown_product_returns_404() {
  http_get "${STOREFRONT_PRODUCTS_URL}/${ECOMMERCE_UNKNOWN_ID}"

  assert_http_status "404"                               || return 1
  assert_problem_type "/problems/not-found"              || return 1
}

# =============================================================================
# TEST 9: Disallowed filter operator returns 400
# =============================================================================
test_disallowed_filter_operator_returns_400() {
  http_get "${STOREFRONT_PRODUCTS_URL}?is_active[gte]=true"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-filter"         || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "List Storefront Categories"                     test_list_categories
run_test "List Storefront Products"                       test_list_products
run_test "Filter Products by Category"                    test_filter_products_by_category
run_test "Filter Products by Price Operator"              test_filter_products_by_price_operator
run_test "Sort Products by price:desc"                    test_sort_products_by_price_desc
run_test "Sparse Field Selection"                         test_sparse_fields
run_test "Cursor Pagination"                              test_pagination
run_test "Unknown Product Returns 404"                    test_unknown_product_returns_404
run_test "Disallowed Filter Operator Returns 400"         test_disallowed_filter_operator_returns_400

print_summary
exit $?
