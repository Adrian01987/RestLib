#!/usr/bin/env bash
# =============================================================================
# e2e-lib.sh — Shared helpers for RestLib E2E test suites
# =============================================================================
# Source this file from any test suite script:
#   source "$(dirname "$0")/e2e-lib.sh"
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
BASE_URL="${BASE_URL:-http://localhost:5000}"
PASS_COUNT=0
FAIL_COUNT=0
SKIP_COUNT=0
TOTAL_COUNT=0

# ---------------------------------------------------------------------------
# Well-known seed data IDs (shared across all suites)
# ---------------------------------------------------------------------------
ELECTRONICS_ID="11111111-1111-1111-1111-111111111111"
BOOKS_ID="22222222-2222-2222-2222-222222222222"
CLOTHING_ID="33333333-3333-3333-3333-333333333333"
HEADPHONES_ID="aaaa1111-1111-1111-1111-111111111111"
KEYBOARD_ID="aaaa2222-2222-2222-2222-222222222222"
CLEAN_CODE_ID="aaaa3333-3333-3333-3333-333333333333"
TSHIRT_ID="aaaa4444-4444-4444-4444-444444444444"
NONEXISTENT_ID="00000000-0000-0000-0000-000000000000"

# ---------------------------------------------------------------------------
# Colors (disabled if stdout is not a terminal)
# ---------------------------------------------------------------------------
if [ -t 1 ]; then
  GREEN='\033[0;32m'
  RED='\033[0;31m'
  YELLOW='\033[0;33m'
  CYAN='\033[0;36m'
  BOLD='\033[1m'
  RESET='\033[0m'
else
  GREEN='' RED='' YELLOW='' CYAN='' BOLD='' RESET=''
fi

# ---------------------------------------------------------------------------
# Logging helpers
# ---------------------------------------------------------------------------
info()  { echo -e "${CYAN}[INFO]${RESET}  $*"; }
pass()  { echo -e "${GREEN}[PASS]${RESET}  $*"; }
fail()  { echo -e "${RED}[FAIL]${RESET}  $*"; }
warn()  { echo -e "${YELLOW}[WARN]${RESET}  $*"; }
header(){ echo -e "\n${BOLD}══════════════════════════════════════════════════════════════${RESET}"; echo -e "${BOLD}  $*${RESET}"; echo -e "${BOLD}══════════════════════════════════════════════════════════════${RESET}"; }
sep()   { echo -e "──────────────────────────────────────────────────────────────"; }

# ---------------------------------------------------------------------------
# wait_for_server — Block until the server is reachable (or timeout)
# ---------------------------------------------------------------------------
wait_for_server() {
  local check_url="${1:-${BASE_URL}/swagger/v1/swagger.json}"
  local max_wait=30
  local waited=0
  info "Waiting for server at ${BASE_URL} ..."
  while ! curl -sf -o /dev/null "$check_url" 2>/dev/null; do
    sleep 1
    waited=$((waited + 1))
    if [ "$waited" -ge "$max_wait" ]; then
      fail "Server did not become ready within ${max_wait}s"
      echo ""
      echo "Start the sample app first:"
      echo "  cd samples/RestLib.Sample && dotnet run"
      exit 1
    fi
  done
  info "Server is ready (waited ${waited}s)"
}

# ---------------------------------------------------------------------------
# HTTP helpers — all set HTTP_STATUS, HTTP_BODY, HTTP_HEADERS
# ---------------------------------------------------------------------------
HTTP_STATUS=""
HTTP_BODY=""
HTTP_HEADERS=""

_http_request() {
  local method="$1"
  local url="$2"
  local body="${3:-}"
  local tmpbody tmpheaders
  tmpbody=$(mktemp)
  tmpheaders=$(mktemp)

  local curl_args=(-s -D "$tmpheaders" -o "$tmpbody" -w "%{http_code}" -X "$method")
  if [ -n "$body" ]; then
    curl_args+=(-H "Content-Type: application/json" -d "$body")
  fi
  curl_args+=("$url")

  HTTP_STATUS=$(curl "${curl_args[@]}")
  HTTP_BODY=$(cat "$tmpbody")
  HTTP_HEADERS=$(cat "$tmpheaders")
  rm -f "$tmpbody" "$tmpheaders"
}

http_get()    { _http_request GET    "$1"; }
http_post()   { _http_request POST   "$1" "$2"; }
http_put()    { _http_request PUT    "$1" "$2"; }
http_patch()  { _http_request PATCH  "$1" "$2"; }
http_delete() { _http_request DELETE "$1"; }

# http_get_with_headers — GET with extra headers (e.g., If-None-Match)
#   Usage: http_get_with_headers <url> <header1> [header2] ...
http_get_with_headers() {
  local url="$1"; shift
  local tmpbody tmpheaders
  tmpbody=$(mktemp)
  tmpheaders=$(mktemp)

  local curl_args=(-s -D "$tmpheaders" -o "$tmpbody" -w "%{http_code}" -X GET)
  for h in "$@"; do
    curl_args+=(-H "$h")
  done
  curl_args+=("$url")

  HTTP_STATUS=$(curl "${curl_args[@]}")
  HTTP_BODY=$(cat "$tmpbody")
  HTTP_HEADERS=$(cat "$tmpheaders")
  rm -f "$tmpbody" "$tmpheaders"
}

# http_put_with_headers — PUT with extra headers (e.g., If-Match)
#   Usage: http_put_with_headers <url> <body> <header1> [header2] ...
http_put_with_headers() {
  local url="$1"; local body="$2"; shift 2
  local tmpbody tmpheaders
  tmpbody=$(mktemp)
  tmpheaders=$(mktemp)

  local curl_args=(-s -D "$tmpheaders" -o "$tmpbody" -w "%{http_code}" -X PUT -H "Content-Type: application/json" -d "$body")
  for h in "$@"; do
    curl_args+=(-H "$h")
  done
  curl_args+=("$url")

  HTTP_STATUS=$(curl "${curl_args[@]}")
  HTTP_BODY=$(cat "$tmpbody")
  HTTP_HEADERS=$(cat "$tmpheaders")
  rm -f "$tmpbody" "$tmpheaders"
}

# ---------------------------------------------------------------------------
# jq helpers — extract values from HTTP_BODY
# ---------------------------------------------------------------------------
jq_val()   { echo "$HTTP_BODY" | jq -r "$1" 2>/dev/null; }
jq_len()   { echo "$HTTP_BODY" | jq "$1 | length" 2>/dev/null; }
jq_raw()   { echo "$HTTP_BODY" | jq "$1" 2>/dev/null; }

# ---------------------------------------------------------------------------
# Header helpers — extract from HTTP_HEADERS
# ---------------------------------------------------------------------------
# get_header <header_name> — case-insensitive
get_header() {
  local name="$1"
  echo "$HTTP_HEADERS" | grep -i "^${name}:" | head -1 | sed "s/^[^:]*: *//" | tr -d '\r'
}

# ---------------------------------------------------------------------------
# Assertion helpers
# ---------------------------------------------------------------------------

# assert_http_status <expected>
assert_http_status() {
  local expected="$1"
  if [ "$HTTP_STATUS" = "$expected" ]; then
    pass "HTTP status = $HTTP_STATUS"
  else
    fail "HTTP status = $HTTP_STATUS (expected $expected)"
    echo "  Response body:"
    echo "$HTTP_BODY" | jq . 2>/dev/null || echo "$HTTP_BODY"
    return 1
  fi
}

# assert_eq <label> <actual> <expected>
assert_eq() {
  local label="$1" actual="$2" expected="$3"
  if [ "$actual" = "$expected" ]; then
    pass "$label = '$actual'"
  else
    fail "$label = '$actual' (expected '$expected')"
    return 1
  fi
}

# assert_ne <label> <actual> <not_expected>
assert_ne() {
  local label="$1" actual="$2" not_expected="$3"
  if [ "$actual" != "$not_expected" ]; then
    pass "$label = '$actual' (not '$not_expected')"
  else
    fail "$label = '$actual' (should not be '$not_expected')"
    return 1
  fi
}

# assert_contains <label> <haystack> <needle>
assert_contains() {
  local label="$1" haystack="$2" needle="$3"
  if echo "$haystack" | grep -qF "$needle"; then
    pass "$label contains '$needle'"
  else
    fail "$label does not contain '$needle'"
    return 1
  fi
}

# assert_not_contains <label> <haystack> <needle>
assert_not_contains() {
  local label="$1" haystack="$2" needle="$3"
  if ! echo "$haystack" | grep -qF "$needle"; then
    pass "$label does not contain '$needle'"
  else
    fail "$label should not contain '$needle'"
    return 1
  fi
}

# assert_num_eq <label> <actual> <expected>
assert_num_eq() {
  local label="$1" actual="$2" expected="$3"
  if awk "BEGIN { exit !(($actual + 0) == ($expected + 0)) }" 2>/dev/null; then
    pass "$label = $actual (numerically equals $expected)"
  else
    fail "$label = $actual (expected numerically equal to $expected)"
    return 1
  fi
}

# assert_gt <label> <actual> <threshold>
assert_gt() {
  local label="$1" actual="$2" threshold="$3"
  if awk "BEGIN { exit !(($actual + 0) > ($threshold + 0)) }" 2>/dev/null; then
    pass "$label = $actual (> $threshold)"
  else
    fail "$label = $actual (expected > $threshold)"
    return 1
  fi
}

# assert_ge <label> <actual> <threshold>
assert_ge() {
  local label="$1" actual="$2" threshold="$3"
  if awk "BEGIN { exit !(($actual + 0) >= ($threshold + 0)) }" 2>/dev/null; then
    pass "$label = $actual (>= $threshold)"
  else
    fail "$label = $actual (expected >= $threshold)"
    return 1
  fi
}

# assert_matches <label> <actual> <regex>
assert_matches() {
  local label="$1" actual="$2" regex="$3"
  if echo "$actual" | grep -qE "$regex"; then
    pass "$label matches /$regex/"
  else
    fail "$label = '$actual' (does not match /$regex/)"
    return 1
  fi
}

# assert_json_field <jq_path> <expected_value>
assert_json_field() {
  local path="$1" expected="$2"
  local actual
  actual=$(jq_val "$path")
  if [ "$actual" = "$expected" ]; then
    pass "$path = '$actual'"
  else
    fail "$path = '$actual' (expected '$expected')"
    return 1
  fi
}

# assert_json_field_exists <jq_path>
assert_json_field_exists() {
  local path="$1"
  local val
  val=$(jq_raw "$path")
  if [ "$val" != "null" ] && [ -n "$val" ]; then
    pass "$path exists"
  else
    fail "$path is null/missing"
    return 1
  fi
}

# assert_json_field_null <jq_path>
assert_json_field_null() {
  local path="$1"
  local val
  val=$(jq_raw "$path")
  if [ "$val" = "null" ] || [ -z "$val" ]; then
    pass "$path is null/absent"
  else
    fail "$path should be null/absent but got: $val"
    return 1
  fi
}

# assert_header <header_name> <expected_value>
assert_header() {
  local name="$1" expected="$2"
  local actual
  actual=$(get_header "$name")
  if [ "$actual" = "$expected" ]; then
    pass "Header $name = '$actual'"
  else
    fail "Header $name = '$actual' (expected '$expected')"
    return 1
  fi
}

# assert_header_exists <header_name>
assert_header_exists() {
  local name="$1"
  local actual
  actual=$(get_header "$name")
  if [ -n "$actual" ]; then
    pass "Header $name is present ('$actual')"
  else
    fail "Header $name is missing"
    return 1
  fi
}

# assert_header_contains <header_name> <needle>
assert_header_contains() {
  local name="$1" needle="$2"
  local actual
  actual=$(get_header "$name")
  if echo "$actual" | grep -qF "$needle"; then
    pass "Header $name contains '$needle'"
  else
    fail "Header $name = '$actual' (expected to contain '$needle')"
    return 1
  fi
}

# ---------------------------------------------------------------------------
# Batch-specific assertion helpers
# ---------------------------------------------------------------------------
assert_item_status()     { local idx="$1" expected="$2"; local actual; actual=$(jq_val ".items[$idx].status"); assert_eq "items[$idx].status" "$actual" "$expected"; }
assert_item_has_entity() { assert_json_field_exists ".items[$1].entity"; }
assert_item_has_error()  { assert_json_field_exists ".items[$1].error"; }
assert_item_no_entity()  { assert_json_field_null ".items[$1].entity"; }
assert_item_no_error()   { assert_json_field_null ".items[$1].error"; }
assert_items_count()     { local expected="$1"; local actual; actual=$(jq_len ".items"); assert_eq "items count" "$actual" "$expected"; }
assert_problem_type()    { assert_json_field ".type" "$1"; }

# ---------------------------------------------------------------------------
# Test runner
# ---------------------------------------------------------------------------
run_test() {
  local name="$1"
  local func="$2"
  TOTAL_COUNT=$((TOTAL_COUNT + 1))
  sep
  info "Test ${TOTAL_COUNT}: ${name}"
  sep
  if $func; then
    PASS_COUNT=$((PASS_COUNT + 1))
    echo -e "  ${GREEN}>>> TEST PASSED${RESET}"
  else
    FAIL_COUNT=$((FAIL_COUNT + 1))
    echo -e "  ${RED}>>> TEST FAILED${RESET}"
  fi
  echo ""
}

# ---------------------------------------------------------------------------
# Summary — returns FAIL_COUNT as exit code
# ---------------------------------------------------------------------------
print_summary() {
  header "TEST SUMMARY"
  echo ""
  echo -e "  Total:   ${BOLD}${TOTAL_COUNT}${RESET}"
  echo -e "  Passed:  ${GREEN}${PASS_COUNT}${RESET}"
  echo -e "  Failed:  ${RED}${FAIL_COUNT}${RESET}"
  if [ "$SKIP_COUNT" -gt 0 ]; then
    echo -e "  Skipped: ${YELLOW}${SKIP_COUNT}${RESET}"
  fi
  echo ""
  if [ "$FAIL_COUNT" -eq 0 ]; then
    echo -e "  ${GREEN}${BOLD}ALL TESTS PASSED${RESET}"
  else
    echo -e "  ${RED}${BOLD}${FAIL_COUNT} TEST(S) FAILED${RESET}"
  fi
  echo ""
  return "$FAIL_COUNT"
}

# ---------------------------------------------------------------------------
# Prerequisite check
# ---------------------------------------------------------------------------
check_prerequisites() {
  for cmd in curl jq; do
    if ! command -v "$cmd" &>/dev/null; then
      fail "Required command '$cmd' not found. Please install it."
      exit 1
    fi
  done
}
