#!/usr/bin/env bash

# RestLib rate-limiting E2E tests.
# Starts an isolated sample process with deterministic one-permit fixed windows.
# All assertions use the local raw_request helper, which deliberately never retries 429.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SAMPLE_DIR="${REPO_ROOT}/samples/RestLib.Sample"
SAMPLE_ASSEMBLY="bin/Release/net10.0/RestLib.Sample.dll"
RESULTS_DIR="${SCRIPT_DIR}/TestResults/rate-limiting"
DOTNET_CMD="${DOTNET_CMD:-dotnet}"
RATE_LIMIT_BASE_URL="${RATE_LIMIT_BASE_URL:-http://127.0.0.1:5076}"
RATE_LIMIT_SERVER_URL="${RATE_LIMIT_SERVER_URL:-$RATE_LIMIT_BASE_URL}"

source "$SCRIPT_DIR/e2e-lib.sh"

BASE_URL="$RATE_LIMIT_BASE_URL"
SERVER_PID=""
SERVER_LOG=""
ORDER_ID=""
RAW_REQUEST_COUNT=0

header "Rate Limiting — E2E Tests"

cleanup_rate_limit_server() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    info "Stopping isolated rate-limit sample (PID ${SERVER_PID})..."
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}

trap cleanup_rate_limit_server EXIT

check_rate_limit_prerequisites() {
  check_prerequisites

  if ! command -v "$DOTNET_CMD" >/dev/null 2>&1 && [ ! -x "$DOTNET_CMD" ]; then
    fail "Required command '${DOTNET_CMD}' not found. Please install the .NET SDK."
    exit 1
  fi

  if [ ! -f "${SAMPLE_DIR}/${SAMPLE_ASSEMBLY}" ]; then
    fail "Release sample assembly not found. Build the sample before running this suite."
    exit 1
  fi
}

wait_for_rate_limit_server() {
  local max_wait=60
  local waited=0

  info "Waiting for isolated sample at ${BASE_URL} ..."
  while ! curl -sf --max-time 3 -o /dev/null "${BASE_URL}/health" 2>/dev/null; do
    sleep 1
    waited=$((waited + 1))

    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
      fail "Isolated sample exited before becoming ready. Log: ${SERVER_LOG}"
      tail -40 "$SERVER_LOG" 2>/dev/null || true
      exit 1
    fi

    if [ "$waited" -ge "$max_wait" ]; then
      fail "Isolated sample did not become ready within ${max_wait}s. Log: ${SERVER_LOG}"
      tail -40 "$SERVER_LOG" 2>/dev/null || true
      exit 1
    fi
  done

  pass "Isolated sample is ready (waited ${waited}s)"
}

start_rate_limit_server() {
  local timestamp
  timestamp=$(date +"%Y%m%d_%H%M%S")
  mkdir -p "$RESULTS_DIR"
  SERVER_LOG="${RESULTS_DIR}/server-${timestamp}.log"

  info "Starting isolated sample at ${RATE_LIMIT_SERVER_URL} with one-permit read/write policies..."
  (
    cd "$SAMPLE_DIR"
    export RestLibSample__RateLimiting__ReadPermitLimit=1
    export RestLibSample__RateLimiting__WritePermitLimit=1
    export RestLibSample__RateLimiting__WindowSeconds=3600
    exec "$DOTNET_CMD" "$SAMPLE_ASSEMBLY" --urls "$RATE_LIMIT_SERVER_URL"
  ) > "$SERVER_LOG" 2>&1 &

  SERVER_PID=$!
  wait_for_rate_limit_server
}

raw_request() {
  local method="$1"
  local url="$2"
  local body="${3:-}"
  local tmpbody tmpheaders
  tmpbody=$(mktemp)
  tmpheaders=$(mktemp)

  local curl_args=(-s -g --max-time 10 -D "$tmpheaders" -o "$tmpbody" -w "%{http_code}" -X "$method")
  if [ -n "$body" ]; then
    curl_args+=(-H "Content-Type: application/json" -d "$body")
  fi
  curl_args+=("$url")

  HTTP_STATUS=$(curl "${curl_args[@]}")
  HTTP_BODY=$(cat "$tmpbody")
  HTTP_HEADERS=$(cat "$tmpheaders")
  rm -f "$tmpbody" "$tmpheaders"
  RAW_REQUEST_COUNT=$((RAW_REQUEST_COUNT + 1))
}

test_first_limited_read_succeeds() {
  raw_request GET "${BASE_URL}/api/orders?limit=1"

  assert_http_status "200"                                      || return 1
  ORDER_ID=$(jq_val '.items[0].id')
  assert_ne "captured order ID" "$ORDER_ID" ""               || return 1
  assert_ne "captured order ID" "$ORDER_ID" "null"           || return 1
}

test_second_limited_read_returns_429_without_retry() {
  local requests_before
  requests_before=$RAW_REQUEST_COUNT

  raw_request GET "${BASE_URL}/api/orders?limit=1"

  assert_http_status "429"                                      || return 1
  assert_eq "raw HTTP attempts" "$((RAW_REQUEST_COUNT - requests_before))" "1" || return 1
}

test_exempt_get_by_id_succeeds_after_read_exhaustion() {
  if [ -z "$ORDER_ID" ]; then
    fail "No order ID was captured before testing the exempt endpoint"
    return 1
  fi

  raw_request GET "${BASE_URL}/api/orders/${ORDER_ID}"

  assert_http_status "200"                                      || return 1
  assert_json_field ".id" "$ORDER_ID"                         || return 1
}

test_first_batch_action_consumes_shared_write_policy() {
  raw_request POST "${BASE_URL}/api/products/batch" \
    '{"action":"delete","items":["00000000-0000-0000-0000-000000000000"]}'

  assert_http_status "207"                                      || return 1
  assert_items_count "1"                                       || return 1
  assert_item_status 0 "404"                                   || return 1
}

test_second_batch_action_returns_429_without_retry() {
  local requests_before
  requests_before=$RAW_REQUEST_COUNT

  raw_request POST "${BASE_URL}/api/products/batch" \
    '{"action":"create","items":[{"name":"Must Not Be Created","price":1,"category_id":"11111111-1111-1111-1111-111111111111"}]}'

  assert_http_status "429"                                      || return 1
  assert_eq "raw HTTP attempts" "$((RAW_REQUEST_COUNT - requests_before))" "1" || return 1
}

test_unlimited_endpoint_remains_available() {
  raw_request GET "${BASE_URL}/api/categories/statistics"

  assert_http_status "200"                                      || return 1
  assert_json_field ".total_categories" "3"                   || return 1
}

check_rate_limit_prerequisites
start_rate_limit_server

run_test "First limited read consumes its permit" test_first_limited_read_succeeds
run_test "Second limited read returns 429 with one raw attempt" test_second_limited_read_returns_429_without_retry
run_test "Explicitly exempt GET-by-ID succeeds after read exhaustion" test_exempt_get_by_id_succeeds_after_read_exhaustion
run_test "First batch action consumes the shared write-policy permit" test_first_batch_action_consumes_shared_write_policy
run_test "A different batch action shares the policy and returns 429 without retry" test_second_batch_action_returns_429_without_retry
run_test "An unrelated unlimited endpoint remains available" test_unlimited_endpoint_remains_available

print_summary
