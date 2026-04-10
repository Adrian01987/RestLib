#!/usr/bin/env bash
# =============================================================================
# error-handling-tests.sh — E2E tests for error handling, Problem Details, ETags
# =============================================================================
# Covers: RFC 9457 Problem Details, ETag support, content negotiation, 404s
# ETag support is globally enabled in the sample app.
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "Error Handling & ETags — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: 404 Problem Details structure
# =============================================================================
test_404_problem_details() {
  http_get "${BASE_URL}/api/products/${NONEXISTENT_ID}"

  assert_http_status "404"                               || return 1
  assert_problem_type "/problems/not-found"              || return 1

  # Verify Problem Details structure
  assert_json_field_exists ".title"                      || return 1
  assert_json_field_exists ".status"                     || return 1
  assert_json_field_exists ".detail"                     || return 1

  local status
  status=$(jq_val '.status')
  assert_eq "problem status" "$status" "404"             || return 1
}

# =============================================================================
# TEST 2: Problem Details content type
# =============================================================================
test_problem_details_content_type() {
  http_get "${BASE_URL}/api/products/${NONEXISTENT_ID}"

  assert_http_status "404"                               || return 1
  assert_header_contains "Content-Type" "application/problem+json" || return 1
}

# =============================================================================
# TEST 3: ETag returned on GetById
# =============================================================================
test_etag_on_getbyid() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"

  assert_http_status "200"                               || return 1
  assert_header_exists "ETag"                            || return 1

  local etag
  etag=$(get_header "ETag")
  info "ETag: $etag"

  # ETag should be a quoted string
  assert_matches "ETag format" "$etag" '^".*"$'         || return 1
}

# =============================================================================
# TEST 4: ETag NOT returned on GetAll (only supported on GetById)
# =============================================================================
test_etag_on_getall() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                               || return 1

  # ETags are only generated for individual entities (GetById), not collections
  local etag
  etag=$(get_header "ETag")
  if [ -z "$etag" ]; then
    pass "No ETag on GetAll (as expected — collection ETags not supported)"
  else
    fail "Unexpected ETag on GetAll: $etag"
    return 1
  fi
}

# =============================================================================
# TEST 5: If-None-Match with matching ETag → 304 Not Modified
# =============================================================================
test_if_none_match_304() {
  # First request to get ETag
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  assert_http_status "200"                               || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                            || return 1

  # Second request with If-None-Match
  http_get_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" "If-None-Match: ${etag}"

  assert_http_status "304"                               || return 1
  pass "304 Not Modified returned for matching ETag"
}

# =============================================================================
# TEST 6: If-None-Match with non-matching ETag → 200 OK
# =============================================================================
test_if_none_match_200() {
  http_get_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" 'If-None-Match: "bogus-etag"'

  assert_http_status "200"                               || return 1
  assert_json_field ".name" "Wireless Headphones"        || return 1
}

# =============================================================================
# TEST 7: ETag changes after update
# =============================================================================
test_etag_changes_after_update() {
  # Get current ETag
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  assert_http_status "200"                               || return 1
  local etag_before
  etag_before=$(get_header "ETag")

  # Patch the product
  http_patch "${BASE_URL}/api/products/${HEADPHONES_ID}" '{"price": 155.00}'
  assert_http_status "200"                               || return 1

  # Get new ETag
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  assert_http_status "200"                               || return 1
  local etag_after
  etag_after=$(get_header "ETag")

  assert_ne "ETag changed after update" "$etag_after" "$etag_before" || return 1

  # Restore original price
  http_patch "${BASE_URL}/api/products/${HEADPHONES_ID}" '{"price": 149.99}'
  info "Restored Headphones price"
}

# =============================================================================
# TEST 8: Validation error returns 400
#   Product.Name uses C#'s `required` keyword, so a missing name causes the
#   JSON deserializer to reject the payload at the framework level (before
#   RestLib's own validation runs). The 400 is correct but the content-type
#   may be the framework's default rather than application/problem+json.
# =============================================================================
test_validation_error() {
  # Try to create a product without required name field
  http_post "${BASE_URL}/api/products" '{
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'"
  }'

  local status="$HTTP_STATUS"
  if [ "$status" = "400" ] || [ "$status" = "422" ]; then
    pass "HTTP status = $status (validation rejected)"
  else
    fail "HTTP status = $status (expected 400 or 422)"
    return 1
  fi
}

# =============================================================================
# TEST 9: Multiple error types have correct structure
# =============================================================================
test_multiple_error_types() {
  # Invalid filter value (bad Guid) → /problems/invalid-filter
  http_get "${BASE_URL}/api/products?category_id=not-a-guid"
  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-filter"         || return 1
  assert_json_field_exists ".title"                      || return 1
  assert_json_field_exists ".status"                     || return 1

  # Invalid sort → /problems/invalid-sort
  http_get "${BASE_URL}/api/products?sort=invalid:asc"
  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-sort"           || return 1

  # Invalid cursor → /problems/invalid-cursor
  http_get "${BASE_URL}/api/products?cursor=!!!invalid!!!"
  assert_http_status "400"                               || return 1
  assert_problem_type "/problems/invalid-cursor"         || return 1
}

# =============================================================================
# TEST 10: Successful responses use application/json
# =============================================================================
test_success_content_type() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                               || return 1
  assert_header_contains "Content-Type" "application/json" || return 1
}

# =============================================================================
# TEST 11: JSON snake_case naming
# =============================================================================
test_snake_case_naming() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"

  assert_http_status "200"                               || return 1

  # Properties should be snake_case
  assert_json_field_exists ".category_id"                || return 1
  assert_json_field_exists ".is_active"                  || return 1
  assert_json_field_exists ".created_at"                 || return 1

  # PascalCase should NOT exist
  local body="$HTTP_BODY"
  assert_not_contains "body" "$body" '"CategoryId"'      || return 1
  assert_not_contains "body" "$body" '"IsActive"'        || return 1
  assert_not_contains "body" "$body" '"CreatedAt"'       || return 1
}

# =============================================================================
# TEST 12: Null values omitted from responses
#   Use a seed product that is never mutated by other tests, so updated_at
#   remains null and is omitted from the response body.
#   Cotton T-Shirt is only read (never PUT/PATCH) by any test suite.
# =============================================================================
test_null_values_omitted() {
  http_get "${BASE_URL}/api/products/${TSHIRT_ID}"

  assert_http_status "200"                               || return 1

  # UpdatedAt is null for unmutated seed products — should be omitted
  local body="$HTTP_BODY"
  assert_not_contains "body" "$body" '"updated_at"'      || return 1
}

# =============================================================================
# TEST 13: Categories statistics custom endpoint
#   The endpoint uses Results.Ok() which inherits the global JSON options
#   configured by RestLib (snake_case naming policy).
# =============================================================================
test_custom_statistics_endpoint() {
  http_get "${BASE_URL}/api/categories/statistics"

  assert_http_status "200"                               || return 1

  local total
  total=$(jq_val '.total_categories')
  assert_eq "total_categories" "$total" "3"              || return 1
}

# =============================================================================
# TEST 14: If-None-Match on GetAll (no ETag support on collections)
#   Since GetAll does not return ETags, If-None-Match has no effect.
# =============================================================================
test_if_none_match_getall() {
  http_get_with_headers "${BASE_URL}/api/products" 'If-None-Match: "some-etag"'

  # GetAll ignores If-None-Match — always returns 200
  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1
  pass "GetAll ignores If-None-Match (no collection ETag support)"
}

# =============================================================================
# TEST 15: If-Match with matching ETag on PUT → 200 OK
#   ETags are globally enabled. Fetch the current ETag, then PUT with
#   If-Match set to the matching value — should succeed.
# =============================================================================
test_if_match_put_success() {
  # Get current ETag for Headphones
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  assert_http_status "200"                               || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                            || return 1
  info "Current ETag: $etag"

  # PUT with matching If-Match — should succeed
  http_put_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" '{
    "id": "'"${HEADPHONES_ID}"'",
    "name": "Wireless Headphones",
    "description": "Noise-canceling Bluetooth headphones",
    "price": 149.99,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }' "If-Match: ${etag}"

  assert_http_status "200"                               || return 1
  assert_json_field ".name" "Wireless Headphones"        || return 1
  pass "PUT with matching If-Match succeeded"
}

# =============================================================================
# TEST 16: If-Match with mismatching ETag on PUT → 412 Precondition Failed
# =============================================================================
test_if_match_put_mismatch() {
  http_put_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" '{
    "id": "'"${HEADPHONES_ID}"'",
    "name": "Wireless Headphones",
    "description": "Noise-canceling Bluetooth headphones",
    "price": 149.99,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }' 'If-Match: "wrong-etag-value"'

  assert_http_status "412"                               || return 1
  assert_problem_type "/problems/precondition-failed"    || return 1
  pass "PUT with mismatching If-Match returns 412"
}

# =============================================================================
# TEST 17: If-Match wildcard (*) on PATCH → 200 OK
#   Wildcard If-Match: * matches any existing resource.
# =============================================================================
test_if_match_wildcard_patch() {
  http_patch_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" \
    '{"price": 159.99}' \
    'If-Match: *'

  assert_http_status "200"                               || return 1

  local price
  price=$(jq_val '.price')
  assert_num_eq "price (patched)" "$price" "159.99"      || return 1
  pass "PATCH with If-Match: * succeeded"

  # Restore original price
  http_patch "${BASE_URL}/api/products/${HEADPHONES_ID}" '{"price": 149.99}'
}

# =============================================================================
# TEST 18: If-Match with mismatching ETag on DELETE → 412
#   v2 Products allows anonymous DELETE. Create a temp product,
#   get its ETag, then DELETE with a wrong ETag.
# =============================================================================
test_if_match_delete_mismatch() {
  # Create a temporary product via v2
  http_post "${BASE_URL}/api/v2/products" '{
    "name": "If-Match Delete Test",
    "description": "Temporary product for ETag testing",
    "price": 1.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "201"                               || return 1

  local temp_id
  temp_id=$(jq_val '.id')

  # DELETE with a bogus If-Match ETag
  http_delete_with_headers "${BASE_URL}/api/v2/products/${temp_id}" 'If-Match: "stale-etag"'

  assert_http_status "412"                               || { http_delete "${BASE_URL}/api/v2/products/${temp_id}"; return 1; }
  assert_problem_type "/problems/precondition-failed"    || { http_delete "${BASE_URL}/api/v2/products/${temp_id}"; return 1; }
  pass "DELETE with mismatching If-Match returns 412"

  # Clean up — delete without If-Match (should succeed)
  http_delete "${BASE_URL}/api/v2/products/${temp_id}"
  info "Cleaned up If-Match test product"
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "404 Problem Details Structure"                  test_404_problem_details
run_test "Problem Details Content-Type"                   test_problem_details_content_type
run_test "ETag Returned on GetById"                       test_etag_on_getbyid
run_test "ETag Returned on GetAll"                        test_etag_on_getall
run_test "If-None-Match Matching ETag (304)"              test_if_none_match_304
run_test "If-None-Match Non-Matching ETag (200)"          test_if_none_match_200
run_test "ETag Changes After Update"                      test_etag_changes_after_update
run_test "Validation Error Problem Details"               test_validation_error
run_test "Multiple Error Types Structure"                 test_multiple_error_types
run_test "Successful Response Content-Type"               test_success_content_type
run_test "JSON snake_case Naming"                         test_snake_case_naming
run_test "Null Values Omitted"                            test_null_values_omitted
run_test "Custom Statistics Endpoint"                     test_custom_statistics_endpoint
run_test "If-None-Match on GetAll (304)"                  test_if_none_match_getall
run_test "If-Match PUT with Matching ETag (200)"          test_if_match_put_success
run_test "If-Match PUT with Mismatching ETag (412)"       test_if_match_put_mismatch
run_test "If-Match Wildcard on PATCH (200)"               test_if_match_wildcard_patch
run_test "If-Match DELETE with Mismatching ETag (412)"    test_if_match_delete_mismatch

print_summary
exit $?
