#!/usr/bin/env bash
# =============================================================================
# pagination-tests.sh — E2E tests for cursor-based pagination
# =============================================================================
# RestLib uses cursor-based pagination with `cursor` and `limit` query params.
# Response shape: { items, self, first, next, prev }
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Pagination — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: Default pagination (no params)
# =============================================================================
test_default_pagination() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1
  assert_json_field_exists ".self"                       || return 1
  assert_json_field_exists ".first"                      || return 1

  # 8 products, default page size 20, so all fit on one page
  local count
  count=$(jq_len ".items")
  assert_eq "items count (all fit)" "$count" "8"         || return 1

  # next should be null (no more pages)
  assert_json_field_null ".next"                         || return 1
}

# =============================================================================
# TEST 2: Custom limit
# =============================================================================
test_custom_limit() {
  http_get "${BASE_URL}/api/products?limit=3"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "3"                   || return 1

  # Should have a next link since there are 8 products
  assert_json_field_exists ".next"                       || return 1

  # self and first should contain limit=3
  local self_link
  self_link=$(jq_val '.self')
  assert_contains "self link" "$self_link" "limit=3"     || return 1
}

# =============================================================================
# TEST 3: Follow next cursor to page 2
# =============================================================================
test_cursor_next_page() {
  # Get page 1 with limit=3
  http_get "${BASE_URL}/api/products?limit=3"
  assert_http_status "200"                               || return 1

  local page1_names next_link
  page1_names=$(jq_val '[.items[].name] | join(",")')
  next_link=$(jq_val '.next')

  assert_ne "next link" "$next_link" "null"              || return 1

  # Follow the next link
  http_get "$next_link"
  assert_http_status "200"                               || return 1

  local page2_count page2_names
  page2_count=$(jq_len ".items")
  page2_names=$(jq_val '[.items[].name] | join(",")')

  assert_eq "page 2 items count" "$page2_count" "3"     || return 1

  # Pages should have different items
  assert_ne "page 2 names vs page 1" "$page2_names" "$page1_names" || return 1
  pass "Page 2 has different items than page 1"
}

# =============================================================================
# TEST 4: Paginate through all items
# =============================================================================
test_paginate_all() {
  local all_names=""
  local page_count=0
  local url="${BASE_URL}/api/products?limit=3"

  while [ "$url" != "null" ] && [ -n "$url" ]; do
    http_get "$url"
    assert_http_status "200"                             || return 1

    page_count=$((page_count + 1))

    # Collect names
    local names
    names=$(jq_val '[.items[].name] | join(",")')
    if [ -n "$all_names" ]; then
      all_names="${all_names},${names}"
    else
      all_names="$names"
    fi

    url=$(jq_val '.next')
  done

  # 8 products, limit 3: should be 3 pages (3+3+2)
  assert_eq "page count" "$page_count" "3"               || return 1

  # Should have collected all 8 product names
  local total_items
  total_items=$(echo "$all_names" | tr ',' '\n' | wc -l)
  assert_eq "total items across pages" "$total_items" "8" || return 1
}

# =============================================================================
# TEST 5: Limit = 1 (minimum)
# =============================================================================
test_limit_one() {
  http_get "${BASE_URL}/api/products?limit=1"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "1"                   || return 1

  assert_json_field_exists ".next"                       || return 1
}

# =============================================================================
# TEST 6: Limit exceeds item count (should return all)
# =============================================================================
test_limit_exceeds_count() {
  http_get "${BASE_URL}/api/products?limit=100"

  assert_http_status "200"                               || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "items count" "$count" "8"                   || return 1

  assert_json_field_null ".next"                         || return 1
}

# =============================================================================
# TEST 7: Invalid limit (0)
# =============================================================================
test_invalid_limit_zero() {
  http_get "${BASE_URL}/api/products?limit=0"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-limit"          || return 1
}

# =============================================================================
# TEST 8: Invalid limit (negative)
# =============================================================================
test_invalid_limit_negative() {
  http_get "${BASE_URL}/api/products?limit=-5"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-limit"          || return 1
}

# =============================================================================
# TEST 9: Invalid limit (non-numeric)
#   ASP.NET model binding rejects "abc" before the handler runs,
#   returning its own 400 (not RestLib's /problems/invalid-limit).
# =============================================================================
test_invalid_limit_text() {
  http_get "${BASE_URL}/api/products?limit=abc"

  assert_http_status "400"                               || return 1
  pass "Framework rejects non-numeric limit with 400"
}

# =============================================================================
# TEST 10: Invalid cursor
# =============================================================================
test_invalid_cursor() {
  http_get "${BASE_URL}/api/products?cursor=not-a-valid-cursor"

  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-cursor"         || return 1
}

# =============================================================================
# TEST 11: Pagination links preserve query params (sort + filter + limit)
# =============================================================================
test_links_preserve_params() {
  http_get "${BASE_URL}/api/products?limit=2&sort=price:desc&is_active=true"

  assert_http_status "200"                               || return 1

  local next_link
  next_link=$(jq_val '.next')
  assert_ne "next link" "$next_link" "null"              || return 1
  assert_contains "next link" "$next_link" "limit=2"     || return 1
  assert_contains "next link" "$next_link" "sort=price"  || return 1
  assert_contains "next link" "$next_link" "cursor="     || return 1
}

# =============================================================================
# TEST 12: Limit exceeds max (> 100) — should be clamped or error
# =============================================================================
test_limit_exceeds_max() {
  http_get "${BASE_URL}/api/products?limit=999"

  # Should either clamp to 100 or return error
  if [ "$HTTP_STATUS" = "200" ]; then
    pass "HTTP status = 200 (limit clamped)"
  elif [ "$HTTP_STATUS" = "400" ]; then
    assert_problem_type "/problems/invalid-limit"        || return 1
  else
    fail "HTTP status = $HTTP_STATUS (expected 200 or 400)"
    return 1
  fi
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "Default Pagination (no params)"                 test_default_pagination
run_test "Custom Limit"                                   test_custom_limit
run_test "Follow Next Cursor to Page 2"                   test_cursor_next_page
run_test "Paginate Through All Items"                     test_paginate_all
run_test "Limit = 1 (minimum)"                            test_limit_one
run_test "Limit Exceeds Item Count"                       test_limit_exceeds_count
run_test "Invalid Limit (0)"                              test_invalid_limit_zero
run_test "Invalid Limit (negative)"                       test_invalid_limit_negative
run_test "Invalid Limit (non-numeric)"                    test_invalid_limit_text
run_test "Invalid Cursor"                                 test_invalid_cursor
run_test "Links Preserve Query Params"                    test_links_preserve_params
run_test "Limit Exceeds Max (> 100)"                      test_limit_exceeds_max

print_summary
exit $?
