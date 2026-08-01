#!/usr/bin/env bash

# RestLib composite-key E2E tests.
# Exercises the documented /api/tenant-products/{tenantId}/{sku} route with
# ordered Guid/string key segments, including a string segment that needs URL encoding.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/e2e-lib.sh"

header "Composite Resource Keys — E2E Tests"

check_prerequisites
wait_for_server

TENANT_PRODUCTS_URL="${BASE_URL}/api/tenant-products"
TENANT_ID="66666666-6666-4666-8666-666666666666"
MISMATCH_TENANT_ID="77777777-7777-4777-8777-777777777777"
RUN_ID="$(date +%s)-$$-${RANDOM}"
SKU="e2e ${RUN_ID}+blue"
MISMATCH_SKU="e2e ${RUN_ID}+mismatch"
MISSING_SKU="e2e ${RUN_ID}+missing"
SKU_ENCODED="$(printf '%s' "$SKU" | jq -sRr @uri)"
MISMATCH_SKU_ENCODED="$(printf '%s' "$MISMATCH_SKU" | jq -sRr @uri)"
MISSING_SKU_ENCODED="$(printf '%s' "$MISSING_SKU" | jq -sRr @uri)"
ITEM_URL="${TENANT_PRODUCTS_URL}/${TENANT_ID}/${SKU_ENCODED}"
SELF_HREF=""

cleanup_tenant_product() {
  local tenant_id encoded_sku
  for tenant_id in "$TENANT_ID" "$MISMATCH_TENANT_ID"; do
    for encoded_sku in "$SKU_ENCODED" "$MISMATCH_SKU_ENCODED"; do
      http_delete "${TENANT_PRODUCTS_URL}/${tenant_id}/${encoded_sku}" >/dev/null 2>&1 || true
    done
  done
}

trap cleanup_tenant_product EXIT

assert_tenant_product() {
  local path="$1"
  local expected_name="$2"
  local expected_price="$3"

  assert_json_field "${path}.tenant_id" "$TENANT_ID"          || return 1
  assert_json_field "${path}.sku" "$SKU"                      || return 1
  assert_json_field "${path}.product_name" "$expected_name"   || return 1
  assert_num_eq "${path}.price" "$(jq_val "${path}.price")" "$expected_price" || return 1
}

test_create_returns_two_segment_location_and_links() {
  local payload location self_href collection_href
  payload=$(jq -n \
    --arg tenant_id "$TENANT_ID" \
    --arg sku "$SKU" \
    '{tenant_id:$tenant_id,sku:$sku,product_name:"Composite Widget",price:24.5}')

  http_post "$TENANT_PRODUCTS_URL" "$payload"

  assert_http_status "201"                                      || return 1
  assert_tenant_product "" "Composite Widget" "24.5"          || return 1

  location=$(get_header "Location")
  assert_contains "Location preserves ordered encoded segments" "$location" "/api/tenant-products/${TENANT_ID}/${SKU_ENCODED}" || return 1

  self_href=$(jq_val '._links.self.href')
  collection_href=$(jq_val '._links.collection.href')
  assert_contains "self link preserves ordered encoded segments" "$self_href" "/api/tenant-products/${TENANT_ID}/${SKU_ENCODED}" || return 1
  assert_contains "collection link targets the collection" "$collection_href" "/api/tenant-products" || return 1
  SELF_HREF="$self_href"
}

test_get_binds_both_segments_and_generated_link_is_navigable() {
  http_get "$ITEM_URL"

  assert_http_status "200"                                      || return 1
  assert_tenant_product "" "Composite Widget" "24.5"          || return 1

  http_get "$SELF_HREF"
  assert_http_status "200"                                      || return 1
  assert_tenant_product "" "Composite Widget" "24.5"          || return 1
}

test_missing_second_segment_returns_client_error() {
  http_get "${TENANT_PRODUCTS_URL}/${TENANT_ID}"

  assert_http_status "404"                                      || return 1
}

test_malformed_first_segment_returns_bad_request() {
  http_get "${TENANT_PRODUCTS_URL}/not-a-guid/${SKU_ENCODED}"

  assert_http_status "400"                                      || return 1
}

test_not_found_reports_both_route_key_parts() {
  local detail
  http_get "${TENANT_PRODUCTS_URL}/${TENANT_ID}/${MISSING_SKU_ENCODED}"

  assert_http_status "404"                                      || return 1
  assert_problem_type "/problems/not-found"                     || return 1
  detail=$(jq_val '.detail')
  assert_contains "not-found detail identifies first key part" "$detail" "tenantId" || return 1
  assert_contains "not-found detail contains first key value" "$detail" "$TENANT_ID" || return 1
  assert_contains "not-found detail identifies second key part" "$detail" "sku" || return 1
  assert_contains "not-found detail contains decoded second key value" "$detail" "$MISSING_SKU" || return 1
}

test_put_uses_route_composite_identity() {
  local payload
  payload=$(jq -n \
    --arg tenant_id "$MISMATCH_TENANT_ID" \
    --arg sku "$MISMATCH_SKU" \
    '{tenant_id:$tenant_id,sku:$sku,product_name:"Route Identity Wins",price:31.75}')

  http_put "$ITEM_URL" "$payload"

  assert_http_status "200"                                      || return 1
  assert_tenant_product "" "Route Identity Wins" "31.75"      || return 1

  http_get "${TENANT_PRODUCTS_URL}/${MISMATCH_TENANT_ID}/${MISMATCH_SKU_ENCODED}"
  assert_http_status "404"                                      || return 1

  http_get "$ITEM_URL"
  assert_http_status "200"                                      || return 1
  assert_tenant_product "" "Route Identity Wins" "31.75"      || return 1
}

test_patch_persists_through_composite_identity() {
  http_patch "$ITEM_URL" '{"product_name":"Patched Composite Widget","price":42.25}'

  assert_http_status "200"                                      || return 1
  assert_tenant_product "" "Patched Composite Widget" "42.25" || return 1

  http_get "$ITEM_URL"
  assert_http_status "200"                                      || return 1
  assert_tenant_product "" "Patched Composite Widget" "42.25" || return 1
}

test_delete_by_both_segments_removes_entity() {
  http_delete "$ITEM_URL"
  assert_http_status "204"                                      || return 1

  http_get "$ITEM_URL"
  assert_http_status "404"                                      || return 1
  assert_problem_type "/problems/not-found"                     || return 1
}

run_test "Create returns ordered, URL-encoded composite-key links" test_create_returns_two_segment_location_and_links
run_test "GET binds both key segments and the generated self link is navigable" test_get_binds_both_segments_and_generated_link_is_navigable
run_test "A missing second key segment returns a client error" test_missing_second_segment_returns_client_error
run_test "A malformed first key segment returns bad request" test_malformed_first_segment_returns_bad_request
run_test "Not found identifies both configured route key parts" test_not_found_reports_both_route_key_parts
run_test "PUT preserves the route composite identity" test_put_uses_route_composite_identity
run_test "PATCH persists through the composite identity" test_patch_persists_through_composite_identity
run_test "DELETE uses both segments and removes the entity" test_delete_by_both_segments_removes_entity

print_summary
