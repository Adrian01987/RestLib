#!/usr/bin/env bash

# RestLib Collection Search E2E Tests
# Covers the HTTP-visible search contract for a JSON-configured resource.
#
# Usage:
#   ./search-tests.sh
#   BASE_URL=http://localhost:5000 ./search-tests.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/e2e-lib.sh"

header "Collection Search — E2E Tests"

check_prerequisites
wait_for_server

test_search_matches_name() {
    http_get "$BASE_URL/api/products?q=wireless"
    assert_http_status "200"
    assert_items_count "1"
    assert_json_field ".total_count" "1"
    assert_json_field ".items[0].name" "Wireless Headphones"
}

test_search_matches_description() {
    http_get "$BASE_URL/api/products?q=multiport"
    assert_http_status "200"
    assert_items_count "1"
    assert_json_field ".total_count" "1"
    assert_json_field ".items[0].name" "USB-C Hub"
    assert_json_field ".items[0].description" "7-in-1 USB-C multiport adapter"
}

test_search_is_case_insensitive_by_default() {
    http_get "$BASE_URL/api/products?q=BLUETOOTH"
    assert_http_status "200"
    assert_items_count "1"
    assert_json_field ".total_count" "1"
    assert_json_field ".items[0].name" "Wireless Headphones"
}

test_search_with_no_matches_returns_empty_collection() {
    http_get "$BASE_URL/api/products?q=term-that-does-not-exist"
    assert_http_status "200"
    assert_items_count "0"
    assert_json_field ".total_count" "0"
    assert_json_field_null ".next"
}

test_search_combines_with_sorting_and_pagination() {
    http_get "$BASE_URL/api/products?q=software&sort=price:desc&limit=1"
    assert_http_status "200"
    assert_items_count "1"
    assert_json_field ".total_count" "2"
    assert_json_field ".items[0].name" "Design Patterns"

    local self_link
    local first_link
    local next_link
    self_link=$(jq_val ".self")
    first_link=$(jq_val ".first")
    next_link=$(jq_val ".next")

    assert_contains "self link preserves search" "$self_link" "q=software"
    assert_contains "first link preserves search" "$first_link" "q=software"
    assert_contains "next link preserves search" "$next_link" "q=software"
    assert_contains "next link preserves sorting" "$next_link" "sort=price"
    assert_contains "next link preserves page size" "$next_link" "limit=1"

    http_get "$next_link"
    assert_http_status "200"
    assert_items_count "1"
    assert_json_field ".total_count" "2"
    assert_json_field ".items[0].name" "Clean Code"
    assert_json_field_null ".next"
}

test_multiple_search_values_return_problem_details() {
    http_get "$BASE_URL/api/products?q=software&q=keyboard"
    assert_http_status "400"
    assert_problem_type "/problems/invalid-search"
    assert_json_field_exists ".errors.q[0]"
    assert_contains \
        "search validation message" \
        "$(jq_val ".errors.q[0]")" \
        "Multiple values"
}

run_test "Search matches a configured name field" test_search_matches_name
run_test "Search matches a configured description field" test_search_matches_description
run_test "Search is case-insensitive by default" test_search_is_case_insensitive_by_default
run_test "Search with no matches returns an empty collection" test_search_with_no_matches_returns_empty_collection
run_test "Search combines with sorting and pagination" test_search_combines_with_sorting_and_pagination
run_test "Multiple search values return invalid-search Problem Details" test_multiple_search_values_return_problem_details

print_summary
