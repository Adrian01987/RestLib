#!/usr/bin/env bash
# =============================================================================
# hateoas-tests.sh — E2E tests for HATEOAS (HAL-style _links)
# =============================================================================
# Verifies that _links are injected into entity responses for all endpoints.
# Link relations are conditioned on which operations each resource supports:
#   Categories: read-only    -> self, collection
#   Products:   no Delete    -> self, collection, update, patch
#   Orders:     no Patch     -> self, collection, update, delete
#   v1 Products: read-only   -> self, collection
#   v2 Products: full CRUD   -> self, collection, update, patch, delete
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

CREATED_ORDER_ID=""
CREATED_V2_PRODUCT_ID=""

header "HATEOAS (_links) — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# Helpers
# =============================================================================

# assert_link_href <relation> <expected_href>
assert_link_href() {
  local rel="$1" expected="$2"
  assert_json_field "._links.${rel}.href" "$expected"
}

# assert_link_exists <relation>
assert_link_exists() {
  local rel="$1"
  assert_json_field_exists "._links.${rel}.href"
}

# assert_link_absent <relation>
assert_link_absent() {
  local rel="$1"
  assert_json_field_null "._links.${rel}"
}

# assert_link_method <relation> <expected_method>
assert_link_method() {
  local rel="$1" expected="$2"
  assert_json_field "._links.${rel}.method" "$expected"
}

# assert_link_no_method <relation> — GET links omit method
assert_link_no_method() {
  local rel="$1"
  assert_json_field_null "._links.${rel}.method"
}

# assert_item_link_exists <item_index> <relation>
assert_item_link_exists() {
  local idx="$1" rel="$2"
  assert_json_field_exists ".items[${idx}]._links.${rel}.href"
}

# assert_item_link_absent <item_index> <relation>
assert_item_link_absent() {
  local idx="$1" rel="$2"
  assert_json_field_null ".items[${idx}]._links.${rel}"
}

# =============================================================================
# TEST 1: GetById Category — read-only (self, collection only)
# =============================================================================
test_getbyid_category_links() {
  http_get "${BASE_URL}/api/categories/${ELECTRONICS_ID}"

  assert_http_status "200"                                             || return 1

  # _links object should exist
  assert_json_field_exists "._links"                                   || return 1

  # self link — absolute URL containing the entity ID
  assert_link_exists "self"                                            || return 1
  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "self href" "$self_href" "/api/categories/${ELECTRONICS_ID}" || return 1
  assert_matches "self href is absolute" "$self_href" "^https?://"     || return 1
  assert_link_no_method "self"                                         || return 1

  # collection link
  assert_link_exists "collection"                                      || return 1
  local coll_href
  coll_href=$(jq_val '._links.collection.href')
  assert_contains "collection href" "$coll_href" "/api/categories"     || return 1
  assert_not_contains "collection href" "$coll_href" "${ELECTRONICS_ID}" || return 1
  assert_link_no_method "collection"                                   || return 1

  # Categories are read-only — no update, patch, delete links
  assert_link_absent "update"                                          || return 1
  assert_link_absent "patch"                                           || return 1
  assert_link_absent "delete"                                          || return 1
}

# =============================================================================
# TEST 2: GetAll Categories — each item has _links
# =============================================================================
test_getall_categories_links() {
  http_get "${BASE_URL}/api/categories"

  assert_http_status "200"                                             || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "categories count" "$count" "3"                            || return 1

  # All items should have _links.self and _links.collection
  for i in 0 1 2; do
    assert_item_link_exists "$i" "self"                                || return 1
    assert_item_link_exists "$i" "collection"                          || return 1
    assert_item_link_absent "$i" "update"                              || return 1
    assert_item_link_absent "$i" "patch"                               || return 1
    assert_item_link_absent "$i" "delete"                              || return 1
  done
}

# =============================================================================
# TEST 3: GetById Product — has self, collection, update, patch; no delete
# =============================================================================
test_getbyid_product_links() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"

  assert_http_status "200"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1

  # self link
  assert_link_exists "self"                                            || return 1
  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "self href" "$self_href" "/api/products/${HEADPHONES_ID}" || return 1
  assert_matches "self href is absolute" "$self_href" "^https?://"     || return 1

  # collection link
  assert_link_exists "collection"                                      || return 1
  local coll_href
  coll_href=$(jq_val '._links.collection.href')
  assert_contains "collection href" "$coll_href" "/api/products"       || return 1

  # update (PUT) and patch (PATCH) — Products allow these
  assert_link_exists "update"                                          || return 1
  assert_link_method "update" "PUT"                                    || return 1
  assert_link_exists "patch"                                           || return 1
  assert_link_method "patch" "PATCH"                                   || return 1

  # Products exclude Delete — no delete link
  assert_link_absent "delete"                                          || return 1
}

# =============================================================================
# TEST 4: GetAll Products — per-item links
# =============================================================================
test_getall_products_links() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                                             || return 1

  local count
  count=$(jq_len ".items")
  assert_gt "products count" "$count" "0"                              || return 1

  # Check first item has correct links
  assert_item_link_exists 0 "self"                                     || return 1
  assert_item_link_exists 0 "collection"                               || return 1
  assert_item_link_exists 0 "update"                                   || return 1
  assert_item_link_exists 0 "patch"                                    || return 1
  assert_item_link_absent 0 "delete"                                   || return 1
}

# =============================================================================
# TEST 5: GetById Order — has self, collection, update, delete; no patch
# =============================================================================
test_getbyid_order_links() {
  # Get first order ID
  http_get "${BASE_URL}/api/orders"
  local order_id
  order_id=$(jq_val '.items[0].id')

  http_get "${BASE_URL}/api/orders/${order_id}"

  assert_http_status "200"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1

  # self link
  assert_link_exists "self"                                            || return 1
  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "self href" "$self_href" "/api/orders/${order_id}"   || return 1

  # collection link
  assert_link_exists "collection"                                      || return 1

  # update (PUT) and delete — Orders allow these
  assert_link_exists "update"                                          || return 1
  assert_link_method "update" "PUT"                                    || return 1
  assert_link_exists "delete"                                          || return 1
  assert_link_method "delete" "DELETE"                                 || return 1

  # Orders exclude Patch — no patch link
  assert_link_absent "patch"                                           || return 1
}

# =============================================================================
# TEST 6: Create Product — 201 response includes _links
# =============================================================================
test_create_product_links() {
  http_post "${BASE_URL}/api/products" '{
    "name": "HATEOAS Test Widget",
    "description": "Created for HATEOAS E2E test",
    "price": 25.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'

  assert_http_status "201"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1

  local new_id
  new_id=$(jq_val '.id')

  # self link should contain the new ID
  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "self href" "$self_href" "/api/products/${new_id}"   || return 1

  # collection, update, patch links present; no delete (Products exclude Delete)
  assert_link_exists "collection"                                      || return 1
  assert_link_exists "update"                                          || return 1
  assert_link_exists "patch"                                           || return 1
  assert_link_absent "delete"                                          || return 1

  # Clean up via batch delete
  http_post "${BASE_URL}/api/products/batch" '{"action":"delete","items":["'"${new_id}"'"]}'
  info "Cleaned up HATEOAS Test Widget"
}

# =============================================================================
# TEST 7: Update Product (PUT) — response includes _links
# =============================================================================
test_update_product_links() {
  # Use Headphones — update then restore
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  local original_body="$HTTP_BODY"

  http_put "${BASE_URL}/api/products/${HEADPHONES_ID}" '{
    "id": "'"${HEADPHONES_ID}"'",
    "name": "Wireless Headphones",
    "description": "Updated for HATEOAS test",
    "price": 149.99,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'

  assert_http_status "200"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1
  assert_link_exists "self"                                            || return 1
  assert_link_exists "collection"                                      || return 1
  assert_link_exists "update"                                          || return 1
  assert_link_exists "patch"                                           || return 1
  assert_link_absent "delete"                                          || return 1

  # Restore original description
  http_patch "${BASE_URL}/api/products/${HEADPHONES_ID}" '{"description": "Premium noise-cancelling wireless headphones with 30h battery"}'
  info "Restored Headphones description"
}

# =============================================================================
# TEST 8: Patch Product — response includes _links
# =============================================================================
test_patch_product_links() {
  http_patch "${BASE_URL}/api/products/${HEADPHONES_ID}" '{"price": 149.99}'

  assert_http_status "200"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1
  assert_link_exists "self"                                            || return 1

  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "self href" "$self_href" "/api/products/${HEADPHONES_ID}" || return 1
}

# =============================================================================
# TEST 9: v1 Products (read-only) — self and collection only
# =============================================================================
test_v1_products_links() {
  http_get "${BASE_URL}/api/v1/products"

  assert_http_status "200"                                             || return 1

  # Items should have self, collection; no write links
  assert_item_link_exists 0 "self"                                     || return 1
  assert_item_link_exists 0 "collection"                               || return 1
  assert_item_link_absent 0 "update"                                   || return 1
  assert_item_link_absent 0 "patch"                                    || return 1
  assert_item_link_absent 0 "delete"                                   || return 1

  # Self link should contain /api/v1/products/ path
  local self_href
  self_href=$(jq_val '.items[0]._links.self.href')
  assert_contains "v1 self href" "$self_href" "/api/v1/products/"      || return 1
}

# =============================================================================
# TEST 10: v2 Products (full CRUD) — all 5 link relations
# =============================================================================
test_v2_products_links() {
  http_get "${BASE_URL}/api/v2/products"

  assert_http_status "200"                                             || return 1

  # Items should have all 5 relations
  assert_item_link_exists 0 "self"                                     || return 1
  assert_item_link_exists 0 "collection"                               || return 1
  assert_item_link_exists 0 "update"                                   || return 1
  assert_item_link_exists 0 "patch"                                    || return 1
  assert_item_link_exists 0 "delete"                                   || return 1

  # Self link should contain /api/v2/products/ path
  local self_href
  self_href=$(jq_val '.items[0]._links.self.href')
  assert_contains "v2 self href" "$self_href" "/api/v2/products/"      || return 1
}

# =============================================================================
# TEST 11: v2 GetById — all 5 relations with correct methods
# =============================================================================
test_v2_getbyid_links() {
  http_get "${BASE_URL}/api/v2/products/${HEADPHONES_ID}"

  assert_http_status "200"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1

  # self — no method (GET is the default)
  assert_link_exists "self"                                            || return 1
  assert_link_no_method "self"                                         || return 1

  # collection — no method (GET)
  assert_link_exists "collection"                                      || return 1
  assert_link_no_method "collection"                                   || return 1

  # update — PUT
  assert_link_exists "update"                                          || return 1
  assert_link_method "update" "PUT"                                    || return 1

  # patch — PATCH
  assert_link_exists "patch"                                           || return 1
  assert_link_method "patch" "PATCH"                                   || return 1

  # delete — DELETE
  assert_link_exists "delete"                                          || return 1
  assert_link_method "delete" "DELETE"                                 || return 1
}

# =============================================================================
# TEST 12: Field selection + HATEOAS — _links present alongside projected fields
# =============================================================================
test_field_selection_with_links() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}?fields=id,name"

  assert_http_status "200"                                             || return 1

  # Only requested fields + _links should be present
  assert_json_field ".id" "${HEADPHONES_ID}"                           || return 1
  assert_json_field ".name" "Wireless Headphones"                      || return 1
  assert_json_field_null ".price"                                      || return 1
  assert_json_field_null ".description"                                || return 1

  # _links still present even though fields were selected
  assert_json_field_exists "._links"                                   || return 1
  assert_link_exists "self"                                            || return 1
  assert_link_exists "collection"                                      || return 1
}

# =============================================================================
# TEST 13: Field selection on GetAll + HATEOAS
# =============================================================================
test_field_selection_getall_with_links() {
  http_get "${BASE_URL}/api/products?fields=id,name"

  assert_http_status "200"                                             || return 1

  # Items should have id, name, _links — but not price etc.
  assert_json_field_exists ".items[0].id"                              || return 1
  assert_json_field_exists ".items[0].name"                            || return 1
  assert_json_field_null ".items[0].price"                             || return 1

  assert_item_link_exists 0 "self"                                     || return 1
  assert_item_link_exists 0 "collection"                               || return 1
}

# =============================================================================
# TEST 14: Order field selection + HATEOAS — correct links for Orders
# =============================================================================
test_order_field_selection_with_links() {
  http_get "${BASE_URL}/api/orders?fields=id,status"

  assert_http_status "200"                                             || return 1

  assert_json_field_exists ".items[0].id"                              || return 1
  assert_json_field_exists ".items[0].status"                          || return 1
  assert_json_field_null ".items[0].customer_email"                    || return 1

  # Orders: self, collection, update, delete — no patch
  assert_item_link_exists 0 "self"                                     || return 1
  assert_item_link_exists 0 "collection"                               || return 1
  assert_item_link_exists 0 "update"                                   || return 1
  assert_item_link_exists 0 "delete"                                   || return 1
  assert_item_link_absent 0 "patch"                                    || return 1
}

# =============================================================================
# TEST 15: Batch Create Order — entity in batch response includes _links
# =============================================================================
# Direct POST /api/orders requires auth (only GetAll, GetById, BatchCreate are
# anonymous). Use batch create to verify HATEOAS links on created orders.
test_create_order_links() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "create",
    "items": [
      {
        "customer_email": "hateoas-create-test@example.com",
        "lines": [
          { "product_id": "'"${HEADPHONES_ID}"'", "quantity": 1, "unit_price": 149.99 }
        ],
        "status": "Pending"
      }
    ]
  }'

  assert_http_status "200"                                             || return 1
  assert_item_status 0 "201"                                           || return 1
  assert_json_field_exists ".items[0].entity._links"                   || return 1

  CREATED_ORDER_ID=$(jq_val '.items[0].entity.id')

  # Orders: self, collection, update, delete — no patch
  assert_json_field_exists ".items[0].entity._links.self.href"         || return 1
  assert_json_field_exists ".items[0].entity._links.collection.href"   || return 1
  assert_json_field_exists ".items[0].entity._links.update"            || return 1
  assert_json_field_exists ".items[0].entity._links.delete"            || return 1
  assert_json_field_null ".items[0].entity._links.patch"               || return 1
}

# =============================================================================
# TEST 16: GetById on batch-created Order — _links in response
# =============================================================================
# Direct PUT /api/orders requires auth. Verify _links via GetById instead.
test_getbyid_created_order_links() {
  if [ -z "$CREATED_ORDER_ID" ]; then
    warn "Skipping: no order from Test 15"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_get "${BASE_URL}/api/orders/${CREATED_ORDER_ID}"

  assert_http_status "200"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1
  assert_link_exists "self"                                            || return 1

  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "self href" "$self_href" "/api/orders/${CREATED_ORDER_ID}" || return 1

  # Orders: update, delete — no patch
  assert_link_exists "update"                                          || return 1
  assert_link_exists "delete"                                          || return 1
  assert_link_absent "patch"                                           || return 1

  # Clean up via batch delete
  http_post "${BASE_URL}/api/orders/batch" '{"action":"delete","items":["'"${CREATED_ORDER_ID}"'"]}'
  info "Cleaned up order ${CREATED_ORDER_ID}"
}

# =============================================================================
# TEST 17: v2 Create + Delete — full CRUD links on create, 204 on delete
# =============================================================================
test_v2_create_and_delete_links() {
  http_post "${BASE_URL}/api/v2/products" '{
    "name": "HATEOAS v2 Widget",
    "description": "v2 full CRUD test",
    "price": 15.00,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'

  assert_http_status "201"                                             || return 1
  assert_json_field_exists "._links"                                   || return 1

  CREATED_V2_PRODUCT_ID=$(jq_val '.id')

  # Full CRUD — all 5 relations
  assert_link_exists "self"                                            || return 1
  assert_link_exists "collection"                                      || return 1
  assert_link_exists "update"                                          || return 1
  assert_link_exists "patch"                                           || return 1
  assert_link_exists "delete"                                          || return 1

  # Self link points to /api/v2/products/{id}
  local self_href
  self_href=$(jq_val '._links.self.href')
  assert_contains "v2 self href" "$self_href" "/api/v2/products/${CREATED_V2_PRODUCT_ID}" || return 1

  # Delete via the delete link (verify it's 204)
  http_delete "${BASE_URL}/api/v2/products/${CREATED_V2_PRODUCT_ID}"
  assert_http_status "204"                                             || return 1
  pass "Delete returns 204 (no _links needed)"
}

# =============================================================================
# TEST 18: Batch Create Orders — entities include _links
# =============================================================================
test_batch_create_links() {
  http_post "${BASE_URL}/api/orders/batch" '{
    "action": "create",
    "items": [
      {
        "customer_email": "hateoas-batch-1@example.com",
        "lines": [{ "product_id": "'"${HEADPHONES_ID}"'", "quantity": 1, "unit_price": 50.00 }],
        "status": "Pending"
      },
      {
        "customer_email": "hateoas-batch-2@example.com",
        "lines": [{ "product_id": "'"${KEYBOARD_ID}"'", "quantity": 1, "unit_price": 30.00 }],
        "status": "Pending"
      }
    ]
  }'

  assert_http_status "200"                                             || return 1
  assert_items_count "2"                                               || return 1
  assert_item_status 0 "201"                                           || return 1
  assert_item_status 1 "201"                                           || return 1

  # Each batch item entity should have _links
  assert_json_field_exists ".items[0].entity._links"                   || return 1
  assert_json_field_exists ".items[1].entity._links"                   || return 1
  assert_json_field_exists ".items[0].entity._links.self.href"         || return 1
  assert_json_field_exists ".items[1].entity._links.self.href"         || return 1

  # Orders: no patch link
  assert_json_field_exists ".items[0].entity._links.update"            || return 1
  assert_json_field_exists ".items[0].entity._links.delete"            || return 1
  assert_json_field_null ".items[0].entity._links.patch"               || return 1

  # Self links should be absolute and contain the entity ID
  local id_0 self_0
  id_0=$(jq_val '.items[0].entity.id')
  self_0=$(jq_val '.items[0].entity._links.self.href')
  assert_contains "batch item[0] self href" "$self_0" "/api/orders/${id_0}" || return 1
  assert_matches "batch item[0] self is absolute" "$self_0" "^https?://"    || return 1

  # Clean up
  local id_1
  id_1=$(jq_val '.items[1].entity.id')
  http_post "${BASE_URL}/api/orders/batch" '{"action":"delete","items":["'"${id_0}"'","'"${id_1}"'"]}'
  info "Cleaned up batch orders"
}

# =============================================================================
# TEST 19: Batch Patch Products — entities include _links
# =============================================================================
test_batch_patch_links() {
  http_post "${BASE_URL}/api/products/batch" '{
    "action": "patch",
    "items": [
      { "id": "'"${HEADPHONES_ID}"'", "body": { "price": 149.99 } }
    ]
  }'

  assert_http_status "200"                                             || return 1
  assert_item_status 0 "200"                                           || return 1

  # Entity should have _links
  assert_json_field_exists ".items[0].entity._links"                   || return 1
  assert_json_field_exists ".items[0].entity._links.self.href"         || return 1
  assert_json_field_exists ".items[0].entity._links.collection.href"   || return 1

  # Products: update, patch — no delete
  assert_json_field_exists ".items[0].entity._links.update"            || return 1
  assert_json_field_exists ".items[0].entity._links.patch"             || return 1
  assert_json_field_null ".items[0].entity._links.delete"              || return 1
}

# =============================================================================
# TEST 20: Self link href is usable — fetch by self link
# =============================================================================
test_self_link_is_navigable() {
  http_get "${BASE_URL}/api/categories/${ELECTRONICS_ID}"
  assert_http_status "200"                                             || return 1

  local self_href
  self_href=$(jq_val '._links.self.href')

  # Follow the self link
  http_get "$self_href"
  assert_http_status "200"                                             || return 1
  assert_json_field ".name" "Electronics"                              || return 1
  pass "Self link is navigable"
}

# =============================================================================
# TEST 21: Collection link href is usable
# =============================================================================
test_collection_link_is_navigable() {
  http_get "${BASE_URL}/api/products/${HEADPHONES_ID}"
  assert_http_status "200"                                             || return 1

  local coll_href
  coll_href=$(jq_val '._links.collection.href')

  # Follow the collection link
  http_get "$coll_href"
  assert_http_status "200"                                             || return 1
  assert_json_field_exists ".items"                                    || return 1
  pass "Collection link is navigable"
}

# =============================================================================
# TEST 22: v2 field selection + HATEOAS — all 5 links on projected entity
# =============================================================================
test_v2_field_selection_with_links() {
  http_get "${BASE_URL}/api/v2/products/${HEADPHONES_ID}?fields=id,name"

  assert_http_status "200"                                             || return 1

  # Projected fields only
  assert_json_field_exists ".id"                                       || return 1
  assert_json_field_exists ".name"                                     || return 1
  assert_json_field_null ".price"                                      || return 1

  # _links still present with all 5 v2 relations
  assert_json_field_exists "._links"                                   || return 1
  assert_link_exists "self"                                            || return 1
  assert_link_exists "collection"                                      || return 1
  assert_link_exists "update"                                          || return 1
  assert_link_exists "patch"                                           || return 1
  assert_link_exists "delete"                                          || return 1
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "GetById Category — read-only links"                         test_getbyid_category_links
run_test "GetAll Categories — per-item links"                         test_getall_categories_links
run_test "GetById Product — no delete link"                           test_getbyid_product_links
run_test "GetAll Products — per-item links"                           test_getall_products_links
run_test "GetById Order — no patch link"                              test_getbyid_order_links
run_test "Create Product — 201 with _links"                           test_create_product_links
run_test "Update Product (PUT) — _links in response"                  test_update_product_links
run_test "Patch Product — _links in response"                         test_patch_product_links
run_test "v1 Products (read-only) — self+collection only"             test_v1_products_links
run_test "v2 Products (full CRUD) — all 5 relations"                  test_v2_products_links
run_test "v2 GetById — correct HTTP methods on links"                 test_v2_getbyid_links
run_test "Field Selection + HATEOAS (GetById)"                        test_field_selection_with_links
run_test "Field Selection + HATEOAS (GetAll)"                         test_field_selection_getall_with_links
run_test "Order Field Selection + HATEOAS"                            test_order_field_selection_with_links
run_test "Create Order (via batch) — _links in entity"               test_create_order_links
run_test "GetById Created Order — _links in response"                 test_getbyid_created_order_links
run_test "v2 Create + Delete — full lifecycle"                        test_v2_create_and_delete_links
run_test "Batch Create Orders — entities have _links"                 test_batch_create_links
run_test "Batch Patch Products — entities have _links"                test_batch_patch_links
run_test "Self Link is Navigable"                                     test_self_link_is_navigable
run_test "Collection Link is Navigable"                               test_collection_link_is_navigable
run_test "v2 Field Selection + HATEOAS — all 5 links"                 test_v2_field_selection_with_links

print_summary
exit $?
