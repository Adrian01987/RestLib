#!/usr/bin/env bash

# RestLib alternate-key E2E tests.
# Exercises an email-keyed public DTO mapped to an EF entity whose primary key
# is an internal GUID that is absent from the public representation and routes.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/e2e-lib.sh"

header "Alternate Resource Keys — E2E Tests"

check_prerequisites
wait_for_server

DIRECTORY_URL="${BASE_URL}/api/customer-directory"
CUSTOMERS_URL="${BASE_URL}/api/customers"
RUN_ID="$(date +%s)-$$-${RANDOM}"
PUBLIC_KEY="alternate-${RUN_ID}@example.com"
MISMATCH_KEY="alternate-mismatch-${RUN_ID}@example.com"
BATCH_MISMATCH_KEY="alternate-batch-mismatch-${RUN_ID}@example.com"
PATCH_MISMATCH_KEY="alternate-patch-mismatch-${RUN_ID}@example.com"
PUBLIC_KEY_ENCODED="$(printf '%s' "$PUBLIC_KEY" | jq -sRr @uri)"
MISMATCH_KEY_ENCODED="$(printf '%s' "$MISMATCH_KEY" | jq -sRr @uri)"
BATCH_MISMATCH_KEY_ENCODED="$(printf '%s' "$BATCH_MISMATCH_KEY" | jq -sRr @uri)"
PATCH_MISMATCH_KEY_ENCODED="$(printf '%s' "$PATCH_MISMATCH_KEY" | jq -sRr @uri)"
INTERNAL_ID=""
SELF_HREF=""

cleanup_directory_entry() {
  local encoded_key
  for encoded_key in \
    "$PUBLIC_KEY_ENCODED" \
    "$MISMATCH_KEY_ENCODED" \
    "$BATCH_MISMATCH_KEY_ENCODED" \
    "$PATCH_MISMATCH_KEY_ENCODED"; do
    http_delete "${DIRECTORY_URL}/${encoded_key}" >/dev/null 2>&1 || true
  done

  if [ -n "$INTERNAL_ID" ]; then
    http_delete "${CUSTOMERS_URL}/${INTERNAL_ID}" >/dev/null 2>&1 || true
  fi
}

trap cleanup_directory_entry EXIT

assert_directory_entry() {
  local path="$1"
  local expected_name="$2"
  local expected_city="$3"
  local expected_active="$4"

  assert_json_field "${path}.email" "$PUBLIC_KEY"              || return 1
  assert_json_field "${path}.name" "$expected_name"            || return 1
  assert_json_field "${path}.city" "$expected_city"            || return 1
  assert_json_field "${path}.is_active" "$expected_active"     || return 1
  assert_json_field_null "${path}.id"                            || return 1
  assert_json_field_null "${path}.created_at"                    || return 1
}

test_create_uses_public_key_in_representation_and_links() {
  local payload location self_href
  payload=$(jq -n \
    --arg email "$PUBLIC_KEY" \
    '{email:$email,name:"Alternate Key Customer",city:"Berlin",is_active:true}')

  http_post "$DIRECTORY_URL" "$payload"

  assert_http_status "201"                                      || return 1
  assert_directory_entry "" "Alternate Key Customer" "Berlin" "true" || return 1

  location=$(get_header "Location")
  assert_contains "Location uses public key" "$location" "/api/customer-directory/${PUBLIC_KEY_ENCODED}" || return 1

  assert_json_field_exists "._links.self.href"                   || return 1
  assert_json_field_exists "._links.collection.href"             || return 1
  self_href=$(jq_val '._links.self.href')
  assert_contains "self link uses public key" "$self_href" "/api/customer-directory/${PUBLIC_KEY_ENCODED}" || return 1
  SELF_HREF="$self_href"
}

test_get_by_public_key_and_generated_self_link() {
  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"

  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Alternate Key Customer" "Berlin" "true" || return 1

  http_get "$SELF_HREF"
  assert_http_status "200"                                      || return 1
  assert_json_field ".email" "$PUBLIC_KEY"                     || return 1
}

test_internal_primary_key_is_not_a_route_key() {
  http_get "${CUSTOMERS_URL}?limit=100"
  assert_http_status "200"                                      || return 1

  INTERNAL_ID=$(echo "$HTTP_BODY" | jq -r --arg email "$PUBLIC_KEY" \
    'first(.items[] | select(.email == $email) | .id) // empty')
  assert_ne "internal primary key" "$INTERNAL_ID" ""           || return 1
  assert_ne "internal primary key differs from public key" "$INTERNAL_ID" "$PUBLIC_KEY" || return 1

  http_get "${DIRECTORY_URL}/${INTERNAL_ID}"
  assert_http_status "404"                                      || return 1
  assert_problem_type "/problems/not-found"                     || return 1
}

test_put_mismatched_body_key_preserves_route_identity() {
  local payload
  payload=$(jq -n \
    --arg email "$MISMATCH_KEY" \
    '{email:$email,name:"Route Key Wins",city:"Hamburg",is_active:false}')

  http_put "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}" "$payload"

  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Route Key Wins" "Hamburg" "false" || return 1

  http_get "${DIRECTORY_URL}/${MISMATCH_KEY_ENCODED}"
  assert_http_status "404"                                      || return 1

  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Route Key Wins" "Hamburg" "false" || return 1
}

test_patch_updates_non_key_field_by_public_key() {
  http_patch "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}" '{"city":"Munich"}'

  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Route Key Wins" "Munich" "false" || return 1

  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Route Key Wins" "Munich" "false" || return 1
}

test_patch_rejects_public_key_change() {
  local payload detail
  payload=$(jq -n \
    --arg email "$PATCH_MISMATCH_KEY" \
    '{email:$email,name:"Should Not Persist"}')

  http_patch "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}" "$payload"

  assert_http_status "400"                                      || return 1
  assert_problem_type "/problems/bad-request"                   || return 1
  detail=$(jq_val '.detail')
  assert_contains "immutable key detail" "$detail" "email"    || return 1

  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Route Key Wins" "Munich" "false" || return 1

  http_get "${DIRECTORY_URL}/${PATCH_MISMATCH_KEY_ENCODED}"
  assert_http_status "404"                                      || return 1
}

test_batch_update_uses_envelope_public_key() {
  local payload self_href
  payload=$(jq -n \
    --arg id "$PUBLIC_KEY" \
    --arg body_email "$BATCH_MISMATCH_KEY" \
    '{
      action:"update",
      items:[{
        id:$id,
        body:{email:$body_email,name:"Batch Route Key Wins",city:"Cologne",is_active:true}
      }]
    }')

  http_post "${DIRECTORY_URL}/batch" "$payload"

  assert_http_status "200"                                      || return 1
  assert_items_count "1"                                       || return 1
  assert_item_status 0 "200"                                   || return 1
  assert_item_has_entity 0                                      || return 1
  assert_item_no_error 0                                        || return 1
  assert_directory_entry ".items[0].entity" "Batch Route Key Wins" "Cologne" "true" || return 1
  self_href=$(jq_val '.items[0].entity._links.self.href')
  assert_contains "batch self link uses public key" "$self_href" "/api/customer-directory/${PUBLIC_KEY_ENCODED}" || return 1

  http_get "${DIRECTORY_URL}/${BATCH_MISMATCH_KEY_ENCODED}"
  assert_http_status "404"                                      || return 1

  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Batch Route Key Wins" "Cologne" "true" || return 1
}

test_batch_patch_rejects_public_key_change() {
  local payload detail
  payload=$(jq -n \
    --arg id "$PUBLIC_KEY" \
    --arg body_email "$PATCH_MISMATCH_KEY" \
    '{action:"patch",items:[{id:$id,body:{email:$body_email,city:"Should Not Persist"}}]}')

  http_post "${DIRECTORY_URL}/batch" "$payload"

  assert_http_status "207"                                      || return 1
  assert_items_count "1"                                       || return 1
  assert_item_status 0 "400"                                   || return 1
  assert_json_field ".items[0].error.type" "/problems/bad-request" || return 1
  detail=$(jq_val '.items[0].error.detail')
  assert_contains "batch immutable key detail" "$detail" "email" || return 1

  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "200"                                      || return 1
  assert_directory_entry "" "Batch Route Key Wins" "Cologne" "true" || return 1
}

test_delete_by_public_key_and_missing_key_behavior() {
  http_delete "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "204"                                      || return 1

  http_get "${DIRECTORY_URL}/${PUBLIC_KEY_ENCODED}"
  assert_http_status "404"                                      || return 1
  assert_problem_type "/problems/not-found"                     || return 1

  http_get "${CUSTOMERS_URL}/${INTERNAL_ID}"
  assert_http_status "404"                                      || return 1
}

run_test "Create uses the public key in representations and links" test_create_uses_public_key_in_representation_and_links
run_test "GET works through the public key and generated self link" test_get_by_public_key_and_generated_self_link
run_test "Internal EF primary key is not accepted as a route key" test_internal_primary_key_is_not_a_route_key
run_test "PUT body-key mismatch preserves route identity" test_put_mismatched_body_key_preserves_route_identity
run_test "PATCH updates a non-key field through the public key" test_patch_updates_non_key_field_by_public_key
run_test "PATCH rejects an attempted public-key change" test_patch_rejects_public_key_change
run_test "Batch update uses the envelope public key" test_batch_update_uses_envelope_public_key
run_test "Batch PATCH rejects an attempted public-key change" test_batch_patch_rejects_public_key_change
run_test "DELETE and missing-key behavior use the public key" test_delete_by_public_key_and_missing_key_behavior

print_summary
