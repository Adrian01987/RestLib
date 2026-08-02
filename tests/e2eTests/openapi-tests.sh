#!/usr/bin/env bash

# RestLib OpenAPI E2E smoke tests.
# Validates representative public contracts without storing a brittle full-document snapshot.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/e2e-lib.sh"

header "OpenAPI Document — E2E Tests"

check_prerequisites
wait_for_server

OPENAPI_DOCUMENT=""

assert_openapi_filter() {
  local label="$1"
  local filter="$2"

  if [ -z "$OPENAPI_DOCUMENT" ]; then
    fail "$label: OpenAPI document has not been loaded"
    return 1
  fi

  if printf '%s' "$OPENAPI_DOCUMENT" | jq -e "$filter" >/dev/null 2>&1; then
    pass "$label"
  else
    fail "$label"
    echo "  jq filter: $filter"
    return 1
  fi
}

test_document_is_valid_openapi_json() {
  local content_type
  http_get "${BASE_URL}/openapi/v1.json"

  assert_http_status "200"                                      || return 1
  content_type=$(get_header "Content-Type")
  assert_contains "OpenAPI content type" "$content_type" "application/json" || return 1

  if ! printf '%s' "$HTTP_BODY" | jq -e 'type == "object"' >/dev/null 2>&1; then
    fail "OpenAPI response is not a valid JSON object"
    return 1
  fi
  pass "OpenAPI response is a valid JSON object"

  OPENAPI_DOCUMENT="$HTTP_BODY"
  assert_openapi_filter "OpenAPI version is 3.1.1" '.openapi == "3.1.1"' || return 1
  assert_openapi_filter "Document identity is configured by the sample" '.info.title == "RestLib Sample API" and .info.version == "v1"' || return 1
  assert_openapi_filter "Document exposes paths and component schemas" '(.paths | type == "object") and (.components.schemas | type == "object")' || return 1
}

test_representative_resource_paths_are_present() {
  assert_openapi_filter "Product collection documents GET and POST" '.paths["/api/products"] as $path | ($path | has("get")) and ($path | has("post"))' || return 1
  assert_openapi_filter "Product item documents GET, PUT, and PATCH but excludes DELETE" '.paths["/api/products/{id}"] as $path | ($path | has("get")) and ($path | has("put")) and ($path | has("patch")) and (($path | has("delete")) | not)' || return 1
  assert_openapi_filter "Product batch route documents POST" '.paths["/api/products/batch"] | has("post")' || return 1
  assert_openapi_filter "Versioned read-only and full resource routes are present" '(.paths["/api/v1/products"] | has("get")) and (.paths["/api/v2/products/{id}"] | has("delete"))' || return 1
  assert_openapi_filter "Mapped and alternate-key customer routes are present" '(.paths["/api/customers"] | has("post")) and (.paths["/api/customer-directory/{id}"] | has("get"))' || return 1
  assert_openapi_filter "Composite-key item route documents its lifecycle" '.paths["/api/tenant-products/{tenantId}/{sku}"] as $path | ($path | has("get")) and ($path | has("put")) and ($path | has("patch")) and ($path | has("delete"))' || return 1
}

test_patch_documents_both_supported_media_types() {
  assert_openapi_filter "PATCH advertises JSON and JSON Merge Patch" '.paths["/api/products/{id}"].patch.requestBody.content | keys == ["application/json", "application/merge-patch+json"]' || return 1
  assert_openapi_filter "Both PATCH media types describe partial objects" '.paths["/api/products/{id}"].patch.requestBody.content as $content | $content["application/json"].schema.type == "object" and $content["application/merge-patch+json"].schema.type == "object"' || return 1
  assert_openapi_filter "PATCH success uses JSON and failures use Problem Details" '(.paths["/api/products/{id}"].patch.responses["200"].content | has("application/json")) and (.paths["/api/products/{id}"].patch.responses["400"].content | has("application/problem+json"))' || return 1
}

test_query_and_composite_parameters_are_documented() {
  assert_openapi_filter "Product collection exposes the configured query surface" '[.paths["/api/products"].get.parameters[] | select(.in == "query") | .name] | sort == ["category_id", "cursor", "fields", "is_active", "limit", "name", "price", "q", "sort"]' || return 1
  assert_openapi_filter "Pagination limit documents bounds and default" '.paths["/api/products"].get.parameters[] | select(.name == "limit" and .in == "query") | .schema.minimum == 1 and .schema.maximum == 100 and .schema.default == 20' || return 1
  assert_openapi_filter "Search parameter identifies the configured fields" '.paths["/api/products"].get.parameters[] | select(.name == "q" and .in == "query") | (.description | contains("name") and contains("description"))' || return 1
  assert_openapi_filter "Composite path parameters preserve order and required status" '[.paths["/api/tenant-products/{tenantId}/{sku}"].get.parameters[] | select(.in == "path") | {name, required}] == [{"name":"tenantId","required":true},{"name":"sku","required":true}]' || return 1
  assert_openapi_filter "Composite parameter schemas retain Guid and string types" '.paths["/api/tenant-products/{tenantId}/{sku}"].get.parameters as $params | ($params[] | select(.name == "tenantId") | .schema.format == "uuid") and ($params[] | select(.name == "sku") | .schema.type == "string")' || return 1
}

test_configured_metadata_and_mapped_schemas_are_present() {
  assert_openapi_filter "Product metadata uses the configured summary and tag" '.paths["/api/products"].get | .summary == "List products" and .tags == ["Product"]' || return 1
  assert_openapi_filter "Order create metadata uses the fluent summary and tag" '.paths["/api/orders"].post | .summary == "Place a new order" and .tags == ["Order"]' || return 1
  assert_openapi_filter "Alternate-key metadata uses its JSON-configured summary and tag" '.paths["/api/customer-directory/{id}"].get | .summary == "Get a directory entry by public email key" and .tags == ["Customer Directory"]' || return 1
  assert_openapi_filter "Composite-key metadata uses its JSON-configured summary and tag" '.paths["/api/tenant-products/{tenantId}/{sku}"].get | .summary == "Get a product by tenant and SKU" and .tags == ["Tenant Products"]' || return 1
  assert_openapi_filter "Mapped customer create uses the public DTO schema" '.paths["/api/customers"].post as $operation | $operation.requestBody.content["application/json"].schema["$ref"] == "#/components/schemas/CustomerDto" and $operation.responses["201"].content["application/json"].schema["$ref"] == "#/components/schemas/CustomerDto"' || return 1
  assert_openapi_filter "Public mapped and composite component schemas are present" '.components.schemas | has("CustomerDto") and has("CustomerDirectoryEntry") and has("TenantProduct")' || return 1
  assert_openapi_filter "Operation IDs are present and unique" '[.paths[] | .[] | select(type == "object") | .operationId? | select(. != null)] as $ids | ($ids | length) > 0 and (($ids | unique | length) == ($ids | length))' || return 1
}

test_responses_headers_and_authorization_metadata_are_present() {
  assert_openapi_filter "Create response documents Location and ETag headers" '.paths["/api/products"].post.responses["201"].headers | has("Location") and has("ETag")' || return 1
  assert_openapi_filter "Conditional PATCH documents 412 Problem Details" '.paths["/api/products/{id}"].patch.responses["412"].content | has("application/problem+json")' || return 1
  assert_openapi_filter "Secured order create documents success, validation, and authorization responses" '.paths["/api/orders"].post.responses | keys == ["201", "400", "401", "403"]' || return 1
  assert_openapi_filter "Unauthorized and forbidden responses use Problem Details" '.paths["/api/orders"].post.responses as $responses | ($responses["401"].content | has("application/problem+json")) and ($responses["403"].content | has("application/problem+json"))' || return 1
  assert_openapi_filter "Authorization Problem Details require RFC members" '.paths["/api/orders"].post.responses["401"].content["application/problem+json"].schema.required | sort == ["status", "title", "type"]' || return 1
  assert_openapi_filter "Batch endpoint documents success, multi-status, and client errors" '.paths["/api/products/batch"].post.responses | keys == ["200", "207", "400", "401", "403"]' || return 1
}

run_test "OpenAPI endpoint returns a valid configured document" test_document_is_valid_openapi_json
run_test "Representative CRUD, batch, versioned, mapped, and composite paths are present" test_representative_resource_paths_are_present
run_test "PATCH advertises JSON and JSON Merge Patch contracts" test_patch_documents_both_supported_media_types
run_test "Configured query and composite path parameters are documented" test_query_and_composite_parameters_are_documented
run_test "Operation metadata and public mapped schemas are present" test_configured_metadata_and_mapped_schemas_are_present
run_test "Responses, headers, and authorization Problem Details are documented" test_responses_headers_and_authorization_metadata_are_present

print_summary
