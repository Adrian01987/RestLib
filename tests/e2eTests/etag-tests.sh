#!/usr/bin/env bash
# =============================================================================
# etag-tests.sh — E2E tests for ETag and conditional request handling
# =============================================================================
# Tests: ETag header generation, If-None-Match (304), If-Match (PUT, PATCH,
#        DELETE), stale If-Match (412 Precondition Failed)
# Resources used: Products (GET, PUT, PATCH), v2 Products (DELETE)
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

header "ETag / Conditional Requests — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: GET returns ETag header
# =============================================================================
test_get_returns_etag() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"

  assert_http_status "200"                                 || return 1
  assert_header_exists "ETag"                              || return 1

  local etag
  etag=$(get_header "ETag")
  assert_matches "ETag format" "$etag" '^"[^"]+"$'        || return 1
  pass "ETag value: ${etag}"
}

# =============================================================================
# TEST 2: GET with matching If-None-Match returns 304 Not Modified
# =============================================================================
test_get_if_none_match_304() {
  # First request — get the ETag
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  assert_http_status "200"                                 || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                              || return 1

  # Second request — send If-None-Match with the same ETag
  http_get_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" \
    "If-None-Match: ${etag}"

  assert_http_status "304"                                 || return 1
  pass "Got 304 Not Modified with matching ETag"
}

# =============================================================================
# TEST 3: GET with non-matching If-None-Match returns 200
# =============================================================================
test_get_if_none_match_mismatch() {
  http_get_with_headers "${BASE_URL}/api/products/${HEADPHONES_ID}" \
    "If-None-Match: \"stale-etag-value\""

  assert_http_status "200"                                 || return 1
  assert_header_exists "ETag"                              || return 1
  pass "Got 200 with non-matching ETag"
}

# =============================================================================
# TEST 4: GET collection does NOT return ETag (ETags are per-entity only)
# =============================================================================
test_get_collection_no_etag() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                                 || return 1
  # ETag support is per-entity (GetById), not per-collection (GetAll)
  local etag
  etag=$(get_header "ETag")
  assert_eq "Collection ETag absent" "$etag" ""            || return 1
  pass "Collection GET correctly omits ETag header"
}

# =============================================================================
# TEST 5: PUT with correct If-Match succeeds
# =============================================================================
test_put_if_match_success() {
  # Create a product to update (avoids mutating seed data)
  http_post "${BASE_URL}/api/products" '{
    "name": "ETag PUT Test Widget",
    "description": "Created for ETag PUT test",
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "201"                                 || return 1

  local product_id
  product_id=$(jq_val '.id')

  # GET to obtain ETag
  http_get "${BASE_URL}/api/products/${product_id}"
  assert_http_status "200"                                 || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                              || return 1

  # PUT with correct If-Match
  http_put_with_headers "${BASE_URL}/api/products/${product_id}" \
    '{
      "id": "'"${product_id}"'",
      "name": "ETag PUT Updated Widget",
      "description": "Updated with correct ETag",
      "price": 20.00,
      "category_id": "'"${ELECTRONICS_ID}"'",
      "is_active": true
    }' \
    "If-Match: ${etag}"

  assert_http_status "200"                                 || { _cleanup_product "$product_id"; return 1; }
  assert_json_field ".name" "ETag PUT Updated Widget"      || { _cleanup_product "$product_id"; return 1; }
  pass "PUT with correct If-Match succeeded"

  # Cleanup
  _cleanup_product "$product_id"
}

# =============================================================================
# TEST 6: PUT with stale If-Match returns 412 Precondition Failed
# =============================================================================
test_put_if_match_stale() {
  # Create a product
  http_post "${BASE_URL}/api/products" '{
    "name": "ETag Stale Test Widget",
    "description": "Created for stale ETag test",
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "201"                                 || return 1

  local product_id
  product_id=$(jq_val '.id')

  # GET to obtain the initial ETag
  http_get "${BASE_URL}/api/products/${product_id}"
  assert_http_status "200"                                 || return 1

  local stale_etag
  stale_etag=$(get_header "ETag")

  # Modify the resource so the ETag changes
  http_put "${BASE_URL}/api/products/${product_id}" '{
    "id": "'"${product_id}"'",
    "name": "ETag Modified Widget",
    "description": "Modified to invalidate ETag",
    "price": 15.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "200"                                 || { _cleanup_product "$product_id"; return 1; }

  # PUT with the now-stale ETag
  http_put_with_headers "${BASE_URL}/api/products/${product_id}" \
    '{
      "id": "'"${product_id}"'",
      "name": "ETag Should Fail Widget",
      "description": "This should be rejected",
      "price": 99.00,
      "category_id": "'"${ELECTRONICS_ID}"'",
      "is_active": true
    }' \
    "If-Match: ${stale_etag}"

  assert_http_status "412"                                 || { _cleanup_product "$product_id"; return 1; }
  assert_problem_type "/problems/precondition-failed"      || { _cleanup_product "$product_id"; return 1; }
  pass "PUT with stale If-Match returned 412"

  # Cleanup
  _cleanup_product "$product_id"
}

# =============================================================================
# TEST 7: PATCH with correct If-Match succeeds
# =============================================================================
test_patch_if_match_success() {
  # Create a product to patch
  http_post "${BASE_URL}/api/products" '{
    "name": "ETag PATCH Test Widget",
    "description": "Created for ETag PATCH test",
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "201"                                 || return 1

  local product_id
  product_id=$(jq_val '.id')

  # GET to obtain ETag
  http_get "${BASE_URL}/api/products/${product_id}"
  assert_http_status "200"                                 || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                              || return 1

  # PATCH with correct If-Match
  http_patch_with_headers "${BASE_URL}/api/products/${product_id}" \
    '{"price": 25.00}' \
    "If-Match: ${etag}"

  assert_http_status "200"                                 || { _cleanup_product "$product_id"; return 1; }

  local price
  price=$(jq_val '.price')
  assert_num_eq "price (patched)" "$price" "25.00"         || { _cleanup_product "$product_id"; return 1; }
  pass "PATCH with correct If-Match succeeded"

  # Cleanup
  _cleanup_product "$product_id"
}

# =============================================================================
# TEST 8: DELETE with correct If-Match succeeds
# =============================================================================
test_delete_if_match_success() {
  # Create via v2 products (supports DELETE + AllowAnonymous)
  http_post "${BASE_URL}/api/v2/products" '{
    "name": "ETag DELETE Test Widget",
    "description": "Created for ETag DELETE test",
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "201"                                 || return 1

  local product_id
  product_id=$(jq_val '.id')

  # GET to obtain ETag (use v2 endpoint)
  http_get "${BASE_URL}/api/v2/products/${product_id}"
  assert_http_status "200"                                 || return 1

  local etag
  etag=$(get_header "ETag")
  assert_ne "ETag" "$etag" ""                              || return 1

  # DELETE with correct If-Match
  http_delete_with_headers "${BASE_URL}/api/v2/products/${product_id}" \
    "If-Match: ${etag}"

  assert_http_status "204"                                 || return 1
  pass "DELETE with correct If-Match returned 204"

  # Confirm it's gone
  http_get "${BASE_URL}/api/v2/products/${product_id}"
  assert_http_status "404"                                 || return 1
  pass "Deleted product is gone"
}

# =============================================================================
# TEST 9: ETag changes after resource is modified
# =============================================================================
test_etag_changes_after_update() {
  # Create a product
  http_post "${BASE_URL}/api/products" '{
    "name": "ETag Change Test Widget",
    "description": "Created for ETag change test",
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'
  assert_http_status "201"                                 || return 1

  local product_id
  product_id=$(jq_val '.id')

  # GET initial ETag
  http_get "${BASE_URL}/api/products/${product_id}"
  assert_http_status "200"                                 || return 1

  local etag_before
  etag_before=$(get_header "ETag")

  # Modify the resource
  http_patch "${BASE_URL}/api/products/${product_id}" '{"price": 99.99}'
  assert_http_status "200"                                 || { _cleanup_product "$product_id"; return 1; }

  # GET new ETag
  http_get "${BASE_URL}/api/products/${product_id}"
  assert_http_status "200"                                 || { _cleanup_product "$product_id"; return 1; }

  local etag_after
  etag_after=$(get_header "ETag")

  assert_ne "ETag after update" "$etag_after" "$etag_before" || { _cleanup_product "$product_id"; return 1; }
  pass "ETag changed from ${etag_before} to ${etag_after}"

  # Cleanup
  _cleanup_product "$product_id"
}

# =============================================================================
# Helper: cleanup a product via batch delete (since /api/products excludes DELETE)
# =============================================================================
_cleanup_product() {
  local pid="$1"
  http_post "${BASE_URL}/api/products/batch" '{
    "action": "delete",
    "items": ["'"${pid}"'"]
  }' >/dev/null 2>&1 || true
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "GET returns ETag header"                         test_get_returns_etag
run_test "GET If-None-Match returns 304"                   test_get_if_none_match_304
run_test "GET If-None-Match mismatch returns 200"          test_get_if_none_match_mismatch
run_test "GET collection omits ETag (per-entity only)"     test_get_collection_no_etag
run_test "PUT with correct If-Match"                       test_put_if_match_success
run_test "PUT with stale If-Match returns 412"             test_put_if_match_stale
run_test "PATCH with correct If-Match"                     test_patch_if_match_success
run_test "DELETE with correct If-Match"                    test_delete_if_match_success
run_test "ETag changes after update"                       test_etag_changes_after_update

print_summary
exit $?
