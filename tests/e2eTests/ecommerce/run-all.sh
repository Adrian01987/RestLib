#!/usr/bin/env bash
# =============================================================================
# run-all.sh - E2E test orchestrator for the RestLib ecommerce sample
# =============================================================================
#
# Usage:
#   ./tests/e2eTests/ecommerce/run-all.sh              # build, start servers, run all suites
#   ./tests/e2eTests/ecommerce/run-all.sh --no-build   # skip build, start servers, run suites
#   ./tests/e2eTests/ecommerce/run-all.sh --no-server  # assume required servers already run
#
# Options (via environment):
#   BASE_URL=http://localhost:5000                       # normal-suite client URL
#   SERVER_URL=http://localhost:5000                     # normal-suite bind URL
#   SUITE=storefront-catalog                             # run one suite
#   DOTNET_CMD=dotnet                                    # dotnet executable override
#   PAYMENT_SUCCESS_BASE_URL=http://127.0.0.1:5064       # payment success client URL
#   PAYMENT_SUCCESS_SERVER_URL=http://127.0.0.1:5064     # payment success bind URL
#   PAYMENT_FAILURE_BASE_URL=http://127.0.0.1:5065       # payment failure client URL
#   PAYMENT_FAILURE_SERVER_URL=http://127.0.0.1:5065     # payment failure bind URL
#
# Suites:
#   storefront-catalog, admin-catalog, auth-flow, customer-profile,
#   customer-order-flow, carrier-flow, support-flow, payment-flow
#
# Prerequisites: curl, jq, dotnet SDK 10+
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
SAMPLE_PROJECT="samples/RestLib.Sample.Ecommerce/RestLib.Sample.Ecommerce.csproj"
RESULTS_DIR="${SCRIPT_DIR}/../TestResults/ecommerce"
BASE_URL="${BASE_URL:-http://localhost:5000}"
SERVER_URL="${SERVER_URL:-$BASE_URL}"
DOTNET_CMD="${DOTNET_CMD:-dotnet}"
PAYMENT_FAILURE_BASE_URL="${PAYMENT_FAILURE_BASE_URL:-http://127.0.0.1:5065}"
PAYMENT_FAILURE_SERVER_URL="${PAYMENT_FAILURE_SERVER_URL:-$PAYMENT_FAILURE_BASE_URL}"
SERVER_PID=""
COMMON_SERVER_REQUIRED=false

NO_BUILD=false
NO_SERVER=false

for arg in "$@"; do
  case "$arg" in
    --no-build)  NO_BUILD=true ;;
    --no-server) NO_SERVER=true ;;
    *)           echo "Unknown flag: $arg"; exit 1 ;;
  esac
done

# ---------------------------------------------------------------------------
# Colors
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

info()   { echo -e "${CYAN}[INFO]${RESET}  $*"; }
warn()   { echo -e "${YELLOW}[WARN]${RESET}  $*"; }
error()  { echo -e "${RED}[ERROR]${RESET} $*" >&2; }
header() { echo -e "\n${BOLD}==============================================================${RESET}"; echo -e "${BOLD}  $*${RESET}"; echo -e "${BOLD}==============================================================${RESET}"; }

# ---------------------------------------------------------------------------
# Suite definitions
# ---------------------------------------------------------------------------
ALL_SUITES=(
  "storefront-catalog.sh"
  "admin-catalog.sh"
  "auth-flow.sh"
  "customer-profile.sh"
  "customer-order-flow.sh"
  "carrier-flow.sh"
  "support-flow.sh"
  "payment-flow.sh"
)

available_suites() {
  local suite names=()
  for suite in "${ALL_SUITES[@]}"; do
    names+=("${suite%.sh}")
  done
  printf "%s" "${names[*]}"
}

resolve_suite() {
  local requested="${1%.sh}"

  case "$requested" in
    storefront|storefront-catalog) echo "storefront-catalog.sh" ;;
    admin|admin-catalog) echo "admin-catalog.sh" ;;
    auth|auth-flow) echo "auth-flow.sh" ;;
    customer-profile|profile) echo "customer-profile.sh" ;;
    customer-order|customer-order-flow|order|orders) echo "customer-order-flow.sh" ;;
    carrier|carrier-flow) echo "carrier-flow.sh" ;;
    support|support-flow) echo "support-flow.sh" ;;
    payment|payment-flow) echo "payment-flow.sh" ;;
    *)
      error "Suite not found: ${1}"
      echo "  Available suites: $(available_suites)" >&2
      exit 1
      ;;
  esac
}

suite_requires_common_server() {
  [ "$1" != "payment-flow.sh" ]
}

payment_success_url() {
  if [ -n "${PAYMENT_SUCCESS_BASE_URL:-}" ]; then
    echo "$PAYMENT_SUCCESS_BASE_URL"
    return 0
  fi

  if [ "$COMMON_SERVER_REQUIRED" = true ] && [ "$NO_SERVER" = false ]; then
    echo "http://127.0.0.1:5064"
    return 0
  fi

  echo "$BASE_URL"
}

# ---------------------------------------------------------------------------
# Prerequisite check
# ---------------------------------------------------------------------------
check_prereqs() {
  local missing=()

  for cmd in curl jq; do
    if ! command -v "$cmd" &>/dev/null; then
      missing+=("$cmd")
    fi
  done

  if [ "$NO_SERVER" = false ] && ! command -v "$DOTNET_CMD" &>/dev/null && [ ! -x "$DOTNET_CMD" ]; then
    missing+=("$DOTNET_CMD")
  fi

  if [ ${#missing[@]} -gt 0 ]; then
    error "Missing prerequisites: ${missing[*]}"
    echo "  Install them before running the ecommerce E2E tests."
    exit 1
  fi
}

# ---------------------------------------------------------------------------
# Cleanup - kill the shared ecommerce server on exit
# ---------------------------------------------------------------------------
cleanup() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    info "Stopping ecommerce sample app (PID ${SERVER_PID})..."
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Build and server lifecycle
# ---------------------------------------------------------------------------
build_sample() {
  header "Building ecommerce sample app"
  info "Building ${REPO_ROOT}/${SAMPLE_PROJECT}..."

  if ! (
    cd "$REPO_ROOT"
    "$DOTNET_CMD" build "$SAMPLE_PROJECT" --configuration Release --verbosity quiet
  ); then
    error "Build failed."
    exit 1
  fi

  info "Build succeeded"
}

start_common_server() {
  local timestamp db_file log_file

  header "Starting ecommerce sample app"
  mkdir -p "$RESULTS_DIR"

  timestamp=$(date +"%Y%m%d_%H%M%S")
  db_file="tests/e2eTests/TestResults/ecommerce/ecommerce-${timestamp}.db"
  log_file="${RESULTS_DIR}/server-${timestamp}.log"

  info "Launching normal-suite server on ${SERVER_URL}..."
  info "Server log: ${log_file}"

  (
    cd "$REPO_ROOT"
    ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Ecommerce="Data Source=${db_file}" \
    RestLibSample__Payments__FakeExternalClient__LatencyMilliseconds=0 \
      "$DOTNET_CMD" run --project "$SAMPLE_PROJECT" --configuration Release --no-build --urls "$SERVER_URL"
  ) > "$log_file" 2>&1 &

  SERVER_PID=$!
  info "Server PID: ${SERVER_PID}"

  local max_wait=60
  local waited=0
  while ! curl -sf --max-time 3 -o /dev/null "${BASE_URL}/health" 2>/dev/null; do
    sleep 1
    waited=$((waited + 1))

    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
      error "Server process died. Check ${log_file}"
      tail -40 "$log_file" 2>/dev/null || true
      exit 1
    fi

    if [ "$waited" -ge "$max_wait" ]; then
      error "Server did not become ready within ${max_wait}s"
      tail -40 "$log_file" 2>/dev/null || true
      exit 1
    fi
  done

  info "Server is ready (waited ${waited}s)"
}

# ---------------------------------------------------------------------------
# Run suites
# ---------------------------------------------------------------------------
run_suite() {
  local suite="$1"
  local script="${SCRIPT_DIR}/${suite}"

  if [ "$suite" = "payment-flow.sh" ]; then
    local success_url
    success_url="$(payment_success_url)"

    local payment_args=(--no-build)
    if [ "$NO_SERVER" = true ]; then
      payment_args+=(--no-server)
    fi

    info "Payment success URL: ${success_url}"
    info "Payment failure URL: ${PAYMENT_FAILURE_BASE_URL}"

    BASE_URL="$success_url" \
    PAYMENT_FAILURE_BASE_URL="$PAYMENT_FAILURE_BASE_URL" \
    PAYMENT_FAILURE_SERVER_URL="$PAYMENT_FAILURE_SERVER_URL" \
      bash "$script" "${payment_args[@]}"
    return $?
  fi

  BASE_URL="$BASE_URL" bash "$script"
}

run_suites() {
  local timestamp logfile total_suites passed_suites failed_suites suite_num suite suite_name

  timestamp=$(date +"%Y%m%d_%H%M%S")
  logfile="${RESULTS_DIR}/ecommerce_e2e_${timestamp}.log"

  mkdir -p "$RESULTS_DIR"

  total_suites=${#SUITES_TO_RUN[@]}
  passed_suites=0
  failed_suites=0
  suite_num=0

  header "Running ${total_suites} ecommerce E2E suite(s)"
  info "Base URL: ${BASE_URL}"
  info "Log file: ${logfile}"
  echo ""

  for suite in "${SUITES_TO_RUN[@]}"; do
    suite_num=$((suite_num + 1))
    suite_name="${suite%.sh}"
    echo -e "${BOLD}[${suite_num}/${total_suites}] ${suite_name}${RESET}"

    if run_suite "$suite" 2>&1 | tee -a "$logfile"; then
      passed_suites=$((passed_suites + 1))
      echo -e "  ${GREEN}Suite PASSED${RESET}"
    else
      failed_suites=$((failed_suites + 1))
      echo -e "  ${RED}Suite FAILED${RESET}"
    fi
    echo ""
  done

  header "AGGREGATE ECOMMERCE E2E RESULTS"
  echo ""
  echo -e "  Suites run:    ${BOLD}${total_suites}${RESET}"
  echo -e "  Suites passed: ${GREEN}${passed_suites}${RESET}"
  echo -e "  Suites failed: ${RED}${failed_suites}${RESET}"
  echo ""
  echo -e "  Full log: ${logfile}"
  echo ""

  if [ "$failed_suites" -eq 0 ]; then
    echo -e "  ${GREEN}${BOLD}ALL SUITES PASSED${RESET}"
  else
    echo -e "  ${RED}${BOLD}${failed_suites} SUITE(S) FAILED${RESET}"
  fi
  echo ""

  {
    echo ""
    echo "=== AGGREGATE ECOMMERCE RESULTS ==="
    echo "Suites run:    ${total_suites}"
    echo "Suites passed: ${passed_suites}"
    echo "Suites failed: ${failed_suites}"
    echo "==================================="
  } >> "$logfile"

  return "$failed_suites"
}

# =============================================================================
# Main
# =============================================================================
if [ -n "${SUITE:-}" ]; then
  SUITES_TO_RUN=("$(resolve_suite "$SUITE")")
else
  SUITES_TO_RUN=("${ALL_SUITES[@]}")
fi

for suite in "${SUITES_TO_RUN[@]}"; do
  if suite_requires_common_server "$suite"; then
    COMMON_SERVER_REQUIRED=true
    break
  fi
done

header "RestLib Ecommerce E2E Test Runner"
info "Base URL:  ${BASE_URL}"
info "Server URL: ${SERVER_URL}"
info "Repo root: ${REPO_ROOT}"
info "Results:   ${RESULTS_DIR}"
if [ -n "${SUITE:-}" ]; then
  info "Suite:     ${SUITE}"
fi
if printf "%s\n" "${SUITES_TO_RUN[@]}" | grep -qx "payment-flow.sh"; then
  info "Payment failure URL: ${PAYMENT_FAILURE_BASE_URL}"
  info "Payment failure server URL: ${PAYMENT_FAILURE_SERVER_URL}"
fi
echo ""

check_prereqs

if [ "$NO_BUILD" = false ] && [ "$NO_SERVER" = false ]; then
  build_sample
fi

if [ "$NO_SERVER" = false ] && [ "$COMMON_SERVER_REQUIRED" = true ]; then
  start_common_server
elif [ "$NO_SERVER" = true ] && [ "$COMMON_SERVER_REQUIRED" = true ]; then
  warn "Using existing normal-suite server at ${BASE_URL}"
fi

run_suites
exit $?
