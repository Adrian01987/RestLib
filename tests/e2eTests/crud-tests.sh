#!/usr/bin/env bash
# =============================================================================
# crud-tests.sh — E2E tests for basic CRUD operations
# =============================================================================
# Tests: Create, GetAll, GetById, Update (PUT), Patch, Delete
# Resources used: Products (all CRUD except Delete), Categories (read-only)
# =============================================================================

set -euo pipefail
source "$(dirname "$0")/e2e-lib.sh"

CREATED_PRODUCT_ID=""

header "CRUD Operations — E2E Tests"
check_prerequisites
wait_for_server

# =============================================================================
# TEST 1: GetAll Categories (read-only resource)
# =============================================================================
test_getall_categories() {
  http_get "${BASE_URL}/api/categories"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "categories count" "$count" "3"              || return 1

  # Verify all 3 seed categories are present
  local names
  names=$(jq_val '[.items[].name] | sort | join(",")')
  assert_eq "category names" "$names" "Books,Clothing,Electronics" || return 1
}

# =============================================================================
# TEST 2: GetById Category
# =============================================================================
test_getbyid_category() {
  http_get "${BASE_URL}/api/categories/${ELECTRONICS_ID}"

  assert_http_status "200"                               || return 1
  assert_json_field ".name" "Electronics"                 || return 1
  assert_json_field ".id" "${ELECTRONICS_ID}"             || return 1
}

# =============================================================================
# TEST 3: GetById Not Found
# =============================================================================
test_getbyid_not_found() {
  http_get "${BASE_URL}/api/categories/${NONEXISTENT_ID}"

  assert_http_status "404"                               || return 1
  assert_problem_type "/problems/not-found"              || return 1
}

# =============================================================================
# TEST 4: GetAll Products (with seed data)
# =============================================================================
test_getall_products() {
  http_get "${BASE_URL}/api/products"

  assert_http_status "200"                               || return 1
  assert_json_field_exists ".items"                      || return 1

  local count
  count=$(jq_len ".items")
  assert_eq "products count" "$count" "8"                || return 1

  # Verify default sort is name:asc (first product alphabetically)
  local first_name
  first_name=$(jq_val '.items[0].name')
  assert_eq "first product (name:asc)" "$first_name" "Clean Code" || return 1
}

# =============================================================================
# TEST 5: Create Product
# =============================================================================
test_create_product() {
  http_post "${BASE_URL}/api/products" '{
    "name": "E2E Test Widget",
    "description": "Created by E2E test",
    "price": 42.50,
    "category_id": "'"${ELECTRONICS_ID}"'",
    "is_active": true
  }'

  assert_http_status "201"                               || return 1
  assert_json_field ".name" "E2E Test Widget"            || return 1
  assert_json_field ".description" "Created by E2E test" || return 1

  local price
  price=$(jq_val '.price')
  assert_num_eq "price" "$price" "42.50"                 || return 1

  CREATED_PRODUCT_ID=$(jq_val '.id')
  assert_ne "id" "$CREATED_PRODUCT_ID" "null"            || return 1
  info "Created product: ${CREATED_PRODUCT_ID}"
}

# =============================================================================
# TEST 6: GetById Created Product
# =============================================================================
test_getbyid_created_product() {
  if [ -z "$CREATED_PRODUCT_ID" ]; then
    warn "Skipping: no product from Test 5"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_get "${BASE_URL}/api/products/${CREATED_PRODUCT_ID}"

  assert_http_status "200"                               || return 1
  assert_json_field ".id" "${CREATED_PRODUCT_ID}"        || return 1
  assert_json_field ".name" "E2E Test Widget"            || return 1
}

# =============================================================================
# TEST 7: Update Product (PUT — full replacement)
# =============================================================================
test_update_product() {
  if [ -z "$CREATED_PRODUCT_ID" ]; then
    warn "Skipping: no product from Test 5"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_put "${BASE_URL}/api/products/${CREATED_PRODUCT_ID}" '{
    "id": "'"${CREATED_PRODUCT_ID}"'",
    "name": "E2E Updated Widget",
    "description": "Updated by E2E test",
    "price": 55.00,
    "category_id": "'"${BOOKS_ID}"'",
    "is_active": false
  }'

  assert_http_status "200"                               || return 1
  assert_json_field ".name" "E2E Updated Widget"         || return 1
  assert_json_field ".description" "Updated by E2E test" || return 1
  assert_json_field ".is_active" "false"                 || return 1
  assert_json_field ".category_id" "${BOOKS_ID}"         || return 1

  local price
  price=$(jq_val '.price')
  assert_num_eq "price" "$price" "55.00"                 || return 1
}

# =============================================================================
# TEST 8: Patch Product (partial update)
# =============================================================================
test_patch_product() {
  if [ -z "$CREATED_PRODUCT_ID" ]; then
    warn "Skipping: no product from Test 5"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_patch "${BASE_URL}/api/products/${CREATED_PRODUCT_ID}" '{
    "price": 99.99,
    "is_active": true
  }'

  assert_http_status "200"                               || return 1

  # Patched fields changed
  local price active
  price=$(jq_val '.price')
  active=$(jq_val '.is_active')
  assert_num_eq "price (patched)" "$price" "99.99"       || return 1
  assert_eq "is_active (patched)" "$active" "true"       || return 1

  # Unpatched fields preserved
  assert_json_field ".name" "E2E Updated Widget"         || return 1
  assert_json_field ".category_id" "${BOOKS_ID}"         || return 1
}

# =============================================================================
# TEST 9: Delete Not Allowed on Products (excluded operation)
# =============================================================================
test_delete_product_not_allowed() {
  if [ -z "$CREATED_PRODUCT_ID" ]; then
    warn "Skipping: no product from Test 5"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_delete "${BASE_URL}/api/products/${CREATED_PRODUCT_ID}"

  # Delete is excluded for Products — should get 405
  assert_http_status "405"                               || return 1
}

# =============================================================================
# TEST 10: Patch Not Allowed on Orders (excluded operation)
# =============================================================================
test_patch_order_not_allowed() {
  # Get a real order ID first
  http_get "${BASE_URL}/api/orders"
  local order_id
  order_id=$(jq_val '.items[0].id')

  http_patch "${BASE_URL}/api/orders/${order_id}" '{"status": "Shipped"}'

  # Patch is excluded for Orders — should get 405
  assert_http_status "405"                               || return 1
}

# =============================================================================
# TEST 11: Create with missing required field
# =============================================================================
test_create_missing_required_field() {
  # Product requires "name" (C# required keyword)
  http_post "${BASE_URL}/api/products" '{
    "price": 10.00,
    "category_id": "'"${ELECTRONICS_ID}"'"
  }'

  # Should fail — either 400 (validation) or 422
  # The exact behavior depends on JSON deserialization of 'required' keyword
  local status="$HTTP_STATUS"
  if [ "$status" = "400" ] || [ "$status" = "422" ]; then
    pass "HTTP status = $status (rejected missing required field)"
  else
    fail "HTTP status = $status (expected 400 or 422)"
    return 1
  fi
}

# =============================================================================
# TEST 12: Cleanup — delete test product via batch (since direct DELETE is 405)
# =============================================================================
test_cleanup_product() {
  if [ -z "$CREATED_PRODUCT_ID" ]; then
    warn "Skipping: no product to clean up"
    SKIP_COUNT=$((SKIP_COUNT + 1))
    return 0
  fi

  http_post "${BASE_URL}/api/products/batch" '{
    "action": "delete",
    "items": ["'"${CREATED_PRODUCT_ID}"'"]
  }'

  assert_http_status "200"                               || return 1
  assert_item_status 0 "204"                             || return 1

  # Confirm it's gone
  http_get "${BASE_URL}/api/products/${CREATED_PRODUCT_ID}"
  assert_http_status "404"                               || return 1
  pass "Cleaned up product ${CREATED_PRODUCT_ID}"
}

# =============================================================================
# Run all tests
# =============================================================================

run_test "GetAll Categories"                              test_getall_categories
run_test "GetById Category"                               test_getbyid_category
run_test "GetById Not Found"                              test_getbyid_not_found
run_test "GetAll Products (seed data)"                    test_getall_products
run_test "Create Product"                                 test_create_product
run_test "GetById Created Product"                        test_getbyid_created_product
run_test "Update Product (PUT)"                           test_update_product
run_test "Patch Product (partial update)"                 test_patch_product
run_test "Delete Not Allowed on Products"                 test_delete_product_not_allowed
run_test "Patch Not Allowed on Orders"                    test_patch_order_not_allowed
run_test "Create with Missing Required Field"             test_create_missing_required_field
run_test "Cleanup Test Product (via batch delete)"        test_cleanup_product

print_summary
exit $?
