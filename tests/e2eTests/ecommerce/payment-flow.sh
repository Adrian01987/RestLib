#!/usr/bin/env bash
# =============================================================================
# payment-flow.sh - E2E tests for ecommerce payment flow
# =============================================================================
# Tests: pay an order, observe status change and notification log, then run
# against a forced-failure payment processor and observe Problem Details.
#
# By default this script starts two isolated ecommerce sample instances:
#   BASE_URL                    success client URL, default http://127.0.0.1:5064
#   PAYMENT_SUCCESS_SERVER_URL  success bind URL, default BASE_URL
#   DOTNET_CMD                  dotnet executable override, default dotnet
#   PAYMENT_FAILURE_BASE_URL    failure client URL, default http://127.0.0.1:5065
#   PAYMENT_FAILURE_SERVER_URL  failure bind URL, default PAYMENT_FAILURE_BASE_URL
#
# Use --no-server to run against already-started servers. In that mode,
# PAYMENT_SUCCESS_LOG must point at the success server log file so the
# notification assertion can inspect it.
# =============================================================================

set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5064}"
PAYMENT_SUCCESS_SERVER_URL="${PAYMENT_SUCCESS_SERVER_URL:-$BASE_URL}"
DOTNET_CMD="${DOTNET_CMD:-dotnet}"
PAYMENT_FAILURE_BASE_URL="${PAYMENT_FAILURE_BASE_URL:-http://127.0.0.1:5065}"
PAYMENT_FAILURE_SERVER_URL="${PAYMENT_FAILURE_SERVER_URL:-$PAYMENT_FAILURE_BASE_URL}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
SAMPLE_PROJECT="samples/RestLib.Sample.Ecommerce/RestLib.Sample.Ecommerce.csproj"
RESULTS_DIR="${SCRIPT_DIR}/../TestResults/ecommerce"

source "${SCRIPT_DIR}/../e2e-lib.sh"

NO_BUILD=false
NO_SERVER=false
SERVER_PIDS=()
PAYMENT_SUCCESS_LOG="${PAYMENT_SUCCESS_LOG:-}"
PAYMENT_FAILURE_LOG="${PAYMENT_FAILURE_LOG:-}"

ACTIVE_BASE_URL=""
AUTH_URL=""
STOREFRONT_PRODUCTS_URL=""
CARTS_URL=""
CHECKOUT_URL=""
STOREFRONT_ORDERS_URL=""

CUSTOMER_TOKEN=""
CUSTOMER_ID=""
CART_ID=""
PRODUCT_ID=""
ORDER_ID=""
SUCCESS_ORDER_ID=""
FAILURE_ORDER_ID=""

for arg in "$@"; do
  case "$arg" in
    --no-build)  NO_BUILD=true ;;
    --no-server) NO_SERVER=true ;;
    *)           echo "Unknown flag: $arg"; exit 1 ;;
  esac
done

header "Ecommerce Payment Flow - E2E Tests"

# =============================================================================
# Server lifecycle helpers
# =============================================================================
cleanup_servers() {
  local pid
  for pid in "${SERVER_PIDS[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
      info "Stopping ecommerce sample server (PID ${pid})..."
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
}
trap cleanup_servers EXIT

check_payment_prerequisites() {
  check_prerequisites

  if [ "$NO_SERVER" = false ] && ! command -v "$DOTNET_CMD" &>/dev/null && [ ! -x "$DOTNET_CMD" ]; then
    fail "Required command '${DOTNET_CMD}' not found. Please install the .NET SDK."
    exit 1
  fi
}

build_sample() {
  header "Building ecommerce sample"
  info "Building ${REPO_ROOT}/${SAMPLE_PROJECT}..."

  if ! (
    cd "$REPO_ROOT"
    "$DOTNET_CMD" build "$SAMPLE_PROJECT" --configuration Release --verbosity quiet
  ); then
    fail "Ecommerce sample build failed."
    exit 1
  fi

  pass "Ecommerce sample build succeeded"
}

wait_for_url() {
  local url="$1"
  local log_file="$2"
  local pid="$3"
  local max_wait=60
  local waited=0

  info "Waiting for ecommerce sample at ${url} ..."
  while ! curl -sf --max-time 3 -o /dev/null "${url}/health" 2>/dev/null; do
    sleep 1
    waited=$((waited + 1))

    if ! kill -0 "$pid" 2>/dev/null; then
      fail "Server process ${pid} died before becoming ready. Log: ${log_file}"
      tail -40 "$log_file" 2>/dev/null || true
      exit 1
    fi

    if [ "$waited" -ge "$max_wait" ]; then
      fail "Server at ${url} did not become ready within ${max_wait}s. Log: ${log_file}"
      tail -40 "$log_file" 2>/dev/null || true
      exit 1
    fi
  done

  pass "Server at ${url} is ready"
}

start_payment_server() {
  local label="$1"
  local client_url="$2"
  local server_url="$3"
  local failure_rate="$4"
  local log_variable="$5"
  local timestamp db_file log_file pid

  timestamp=$(date +"%Y%m%d_%H%M%S")
  mkdir -p "$RESULTS_DIR"
  db_file="${RESULTS_DIR}/${label}-${timestamp}.db"
  log_file="${RESULTS_DIR}/${label}-${timestamp}.log"

  info "Starting ${label} ecommerce sample at ${server_url} (payment failure rate ${failure_rate})..."
  (
    cd "$REPO_ROOT"
    ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Ecommerce="Data Source=${db_file}" \
    RestLibSample__Payments__FakeExternalClient__LatencyMilliseconds=0 \
    RestLibSample__Payments__FakeExternalClient__FailureRate="${failure_rate}" \
      "$DOTNET_CMD" run --project "$SAMPLE_PROJECT" --configuration Release --no-build --urls "$server_url"
  ) > "$log_file" 2>&1 &

  pid=$!
  SERVER_PIDS+=("$pid")
  printf -v "$log_variable" "%s" "$log_file"
  wait_for_url "$client_url" "$log_file" "$pid"
}

start_servers_if_needed() {
  if [ "$NO_SERVER" = true ]; then
    wait_for_url "$BASE_URL" "${PAYMENT_SUCCESS_LOG:-/dev/null}" "$$"
    wait_for_url "$PAYMENT_FAILURE_BASE_URL" "${PAYMENT_FAILURE_LOG:-/dev/null}" "$$"
    return 0
  fi

  if [ "$BASE_URL" = "$PAYMENT_FAILURE_BASE_URL" ]; then
    fail "BASE_URL and PAYMENT_FAILURE_BASE_URL must be different when this script starts the servers."
    exit 1
  fi

  start_payment_server "payment-success" "$BASE_URL" "$PAYMENT_SUCCESS_SERVER_URL" "0" PAYMENT_SUCCESS_LOG
  start_payment_server "payment-failure" "$PAYMENT_FAILURE_BASE_URL" "$PAYMENT_FAILURE_SERVER_URL" "1" PAYMENT_FAILURE_LOG
}

# =============================================================================
# HTTP helpers with bearer tokens
# =============================================================================
set_flow_urls() {
  ACTIVE_BASE_URL="$1"
  AUTH_URL="${ACTIVE_BASE_URL}/auth"
  STOREFRONT_PRODUCTS_URL="${ACTIVE_BASE_URL}/api/v1/storefront/products"
  CARTS_URL="${ACTIVE_BASE_URL}/api/storefront/carts"
  CHECKOUT_URL="${ACTIVE_BASE_URL}/api/storefront/checkout"
  STOREFRONT_ORDERS_URL="${ACTIVE_BASE_URL}/api/storefront/orders"
}

http_request_with_headers() {
  local method="$1"
  local url="$2"
  local body="$3"
  shift 3
  local extra_headers=("$@")
  local tmpbody tmpheaders

  tmpbody=$(mktemp)
  tmpheaders=$(mktemp)

  local curl_args=(-s -g -D "$tmpheaders" -o "$tmpbody" -w "%{http_code}" -X "$method")
  if [ -n "$body" ]; then
    curl_args+=(-H "Content-Type: application/json" -d "$body")
  fi
  for header in "${extra_headers[@]}"; do
    curl_args+=(-H "$header")
  done
  curl_args+=("$url")

  HTTP_STATUS=$(curl "${curl_args[@]}")
  HTTP_BODY=$(cat "$tmpbody")
  HTTP_HEADERS=$(cat "$tmpheaders")
  rm -f "$tmpbody" "$tmpheaders"
}

http_get_customer() {
  http_request_with_headers GET "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer() {
  http_request_with_headers POST "$1" "$2" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

http_post_customer_empty() {
  http_request_with_headers POST "$1" "" "Authorization: Bearer ${CUSTOMER_TOKEN}"
}

resolve_url() {
  local href="$1"
  case "$href" in
    http://*|https://*) printf "%s" "$href" ;;
    /*) printf "%s%s" "$ACTIVE_BASE_URL" "$href" ;;
    *) printf "%s/%s" "$ACTIVE_BASE_URL" "$href" ;;
  esac
}

assert_order_link() {
  local rel="$1"
  local method="$2"
  local path="._links.${rel}"

  assert_json_field_exists "${path}.href"                || return 1
  assert_json_field "${path}.method" "$method"           || return 1
}

assert_order_link_absent() {
  local rel="$1"
  local path="._links.${rel}"

  assert_json_field_null "$path"
}

wait_for_log_contains() {
  local log_file="$1"
  local needle="$2"
  local max_wait="${3:-10}"
  local waited=0

  while [ "$waited" -lt "$max_wait" ]; do
    if [ -f "$log_file" ] && grep -qF "$needle" "$log_file"; then
      pass "log contains '${needle}'"
      return 0
    fi

    sleep 1
    waited=$((waited + 1))
  done

  fail "log ${log_file} did not contain '${needle}'"
  tail -40 "$log_file" 2>/dev/null || true
  return 1
}

create_customer_order() {
  local prefix="$1"
  local run_id body product_name

  run_id="${prefix}-$(date +%s)-$$-${RANDOM}"
  body=$(jq -n \
    --arg user_name "$run_id" \
    --arg email "${run_id}@example.com" \
    --arg password "customer-password" \
    '{user_name:$user_name,email:$email,password:$password}')

  http_post "${AUTH_URL}/register-customer" "$body"
  assert_http_status "201"                               || return 1
  assert_json_field ".user.role" "Customer"              || return 1

  CUSTOMER_ID=$(jq_val ".user.id")
  CUSTOMER_TOKEN=$(jq_val ".access_token")
  assert_ne "customer id" "$CUSTOMER_ID" "null"          || return 1
  assert_ne "customer token" "$CUSTOMER_TOKEN" ""        || return 1

  http_get "${STOREFRONT_PRODUCTS_URL}?is_active=true&fields=id,name,price,stock_on_hand&limit=20"
  assert_http_status "200"                               || return 1

  PRODUCT_ID=$(echo "$HTTP_BODY" | jq -r 'first(.items[] | select(.stock_on_hand > 0) | .id) // empty')
  product_name=$(echo "$HTTP_BODY" | jq -r --arg id "$PRODUCT_ID" '.items[] | select(.id == $id) | .name')
  assert_ne "in-stock product id" "$PRODUCT_ID" ""       || return 1
  assert_ne "in-stock product name" "$product_name" ""   || return 1

  http_post_customer "$CARTS_URL" '{}'
  assert_http_status "201"                               || return 1
  assert_json_field ".customer_id" "$CUSTOMER_ID"        || return 1
  assert_json_field ".status" "ACTIVE"                   || return 1

  CART_ID=$(jq_val ".id")
  assert_ne "cart id" "$CART_ID" ""                      || return 1

  body=$(jq -n --arg product_id "$PRODUCT_ID" '{product_id:$product_id,quantity:1}')
  http_post_customer "${CARTS_URL}/${CART_ID}/items" "$body"
  assert_http_status "201"                               || return 1
  assert_json_field ".cart_id" "$CART_ID"                || return 1
  assert_json_field ".product_id" "$PRODUCT_ID"          || return 1

  http_post_customer "$CHECKOUT_URL" '{"payment_method":"card"}'
  assert_http_status "201"                               || return 1
  assert_json_field ".payment_method" "card"             || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1

  ORDER_ID=$(jq_val ".order_id")
  assert_ne "order id" "$ORDER_ID" "null"                || return 1
  assert_ne "order id" "$ORDER_ID" ""                    || return 1
}

# =============================================================================
# TEST 1: Successful payment updates the order
# =============================================================================
test_successful_payment_updates_order() {
  local pay_href pay_url

  set_flow_urls "$BASE_URL"
  create_customer_order "payment-success"                || return 1
  SUCCESS_ORDER_ID="$ORDER_ID"

  http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1
  assert_order_link "pay" "POST"                         || return 1

  pay_href=$(jq_val "._links.pay.href")
  pay_url=$(resolve_url "$pay_href")
  http_post_customer_empty "$pay_url"

  assert_http_status "200"                               || return 1
  assert_json_field ".order_id" "$ORDER_ID"              || return 1
  assert_json_field ".status" "PAID"                     || return 1
  assert_json_field ".payment_method" "card"             || return 1
  assert_json_field_exists ".payment_id"                 || return 1
  assert_json_field_exists ".paid_at"                    || return 1
  assert_json_field_exists ".external_reference"         || return 1

  http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  assert_json_field ".status" "PAID"                     || return 1
  assert_json_field_exists ".paid_at"                    || return 1
  assert_order_link_absent "pay"                         || return 1
}

# =============================================================================
# TEST 2: Successful payment emits a notification log line
# =============================================================================
test_payment_notification_logged() {
  if [ -z "$PAYMENT_SUCCESS_LOG" ]; then
    fail "PAYMENT_SUCCESS_LOG must point at the success server log."
    return 1
  fi

  wait_for_log_contains "$PAYMENT_SUCCESS_LOG" "\"kind\":\"order_paid_customer\"" || return 1
  wait_for_log_contains "$PAYMENT_SUCCESS_LOG" "\"order_id\":\"${SUCCESS_ORDER_ID}\"" || return 1
  wait_for_log_contains "$PAYMENT_SUCCESS_LOG" "\"order_status\":\"PAID\"" || return 1
}

# =============================================================================
# TEST 3: Forced payment failure returns Problem Details
# =============================================================================
test_forced_payment_failure_returns_problem_details() {
  local pay_href pay_url

  set_flow_urls "$PAYMENT_FAILURE_BASE_URL"
  create_customer_order "payment-failure"                || return 1
  FAILURE_ORDER_ID="$ORDER_ID"

  http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1
  assert_order_link "pay" "POST"                         || return 1

  pay_href=$(jq_val "._links.pay.href")
  pay_url=$(resolve_url "$pay_href")
  http_post_customer_empty "$pay_url"

  assert_http_status "402"                               || return 1
  assert_header_contains "Content-Type" "application/problem+json" || return 1
  assert_json_field ".type" "/problems/payment_declined" || return 1
  assert_json_field ".status" "402"                      || return 1
  assert_json_field ".error_code" "payment_declined"     || return 1
  assert_json_field ".order_id" "$FAILURE_ORDER_ID"      || return 1
  assert_json_field ".payment_method" "card"             || return 1

  http_get_customer "${STOREFRONT_ORDERS_URL}/${ORDER_ID}"
  assert_http_status "200"                               || return 1
  assert_json_field ".status" "ASSIGNED"                 || return 1
  assert_json_field_null ".paid_at"                      || return 1
  assert_order_link "pay" "POST"                         || return 1
}

# =============================================================================
# Run all tests
# =============================================================================
check_payment_prerequisites

if [ "$NO_BUILD" = false ] && [ "$NO_SERVER" = false ]; then
  build_sample
fi

start_servers_if_needed

run_test "Successful Payment Updates Order"              test_successful_payment_updates_order
run_test "Payment Notification Logged"                   test_payment_notification_logged
run_test "Forced Payment Failure Returns Problem Details" test_forced_payment_failure_returns_problem_details

print_summary
exit $?
