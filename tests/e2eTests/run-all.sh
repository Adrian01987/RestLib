#!/usr/bin/env bash
# =============================================================================
# run-all.sh — E2E test orchestrator for RestLib
# =============================================================================
#
# Usage:
#   ./tests/e2eTests/run-all.sh              # build, start server, run all suites
#   ./tests/e2eTests/run-all.sh --no-build   # skip build, just start server + run
#   ./tests/e2eTests/run-all.sh --no-server  # assume server already running
#
# Options (via environment):
#   BASE_URL=http://localhost:5000  # override server URL
#   SUITE=crud                      # run only one suite (crud, pagination, etc.)
#
# Prerequisites: curl, jq, dotnet SDK 8+
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SAMPLE_DIR="${REPO_ROOT}/samples/RestLib.Sample"
RESULTS_DIR="${SCRIPT_DIR}/TestResults"
BASE_URL="${BASE_URL:-http://localhost:5000}"
SERVER_PID=""

# Parse flags
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
error()  { echo -e "${RED}[ERROR]${RESET} $*"; }
header() { echo -e "\n${BOLD}══════════════════════════════════════════════════════════════${RESET}"; echo -e "${BOLD}  $*${RESET}"; echo -e "${BOLD}══════════════════════════════════════════════════════════════${RESET}"; }

# ---------------------------------------------------------------------------
# Prerequisite check
# ---------------------------------------------------------------------------
check_prereqs() {
  local missing=()
  for cmd in curl jq dotnet; do
    if ! command -v "$cmd" &>/dev/null; then
      missing+=("$cmd")
    fi
  done
  if [ ${#missing[@]} -gt 0 ]; then
    error "Missing prerequisites: ${missing[*]}"
    echo "  Install them before running the E2E tests."
    exit 1
  fi
}

# ---------------------------------------------------------------------------
# Cleanup — kill server on exit
# ---------------------------------------------------------------------------
cleanup() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    info "Stopping sample app (PID ${SERVER_PID})..."
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# Build the sample app
# ---------------------------------------------------------------------------
build_sample() {
  header "Building sample app"
  info "Building ${SAMPLE_DIR}..."
  if ! dotnet build "${SAMPLE_DIR}/RestLib.Sample.csproj" --configuration Release --verbosity quiet; then
    error "Build failed!"
    exit 1
  fi
  info "Build succeeded"
}

# ---------------------------------------------------------------------------
# Start the sample app in the background
# ---------------------------------------------------------------------------
start_server() {
  header "Starting sample app"
  info "Launching on ${BASE_URL}..."

  dotnet run --project "${SAMPLE_DIR}/RestLib.Sample.csproj" --configuration Release --no-build \
    --urls "${BASE_URL}" > "${RESULTS_DIR}/server.log" 2>&1 &
  SERVER_PID=$!
  info "Server PID: ${SERVER_PID}"

  # Wait for it to be reachable
  local max_wait=60
  local waited=0
  while ! curl -sf -o /dev/null "${BASE_URL}/swagger/v1/swagger.json" 2>/dev/null; do
    sleep 1
    waited=$((waited + 1))
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
      error "Server process died. Check ${RESULTS_DIR}/server.log"
      tail -20 "${RESULTS_DIR}/server.log" 2>/dev/null || true
      exit 1
    fi
    if [ "$waited" -ge "$max_wait" ]; then
      error "Server did not become ready within ${max_wait}s"
      tail -20 "${RESULTS_DIR}/server.log" 2>/dev/null || true
      exit 1
    fi
  done
  info "Server is ready (waited ${waited}s)"
}

# ---------------------------------------------------------------------------
# Suite definitions
# ---------------------------------------------------------------------------
ALL_SUITES=(
  "crud-tests.sh"
  "pagination-tests.sh"
  "filtering-tests.sh"
  "sorting-tests.sh"
  "field-selection-tests.sh"
  "error-handling-tests.sh"
  "batch-tests.sh"
)

# ---------------------------------------------------------------------------
# Run suites
# ---------------------------------------------------------------------------
run_suites() {
  local timestamp
  timestamp=$(date +"%Y%m%d_%H%M%S")
  local logfile="${RESULTS_DIR}/e2e_${timestamp}.log"

  mkdir -p "${RESULTS_DIR}"

  local suites_to_run=()
  if [ -n "${SUITE:-}" ]; then
    # Run a single suite
    local match="${SUITE}-tests.sh"
    if [ ! -f "${SCRIPT_DIR}/${match}" ]; then
      error "Suite not found: ${match}"
      echo "  Available suites: ${ALL_SUITES[*]}"
      exit 1
    fi
    suites_to_run=("$match")
  else
    suites_to_run=("${ALL_SUITES[@]}")
  fi

  local total_suites=${#suites_to_run[@]}
  local passed_suites=0
  local failed_suites=0
  local suite_num=0

  header "Running ${total_suites} E2E test suite(s)"
  info "Log file: ${logfile}"
  echo ""

  for suite in "${suites_to_run[@]}"; do
    suite_num=$((suite_num + 1))
    local suite_name="${suite%.sh}"
    echo -e "${BOLD}[${suite_num}/${total_suites}] ${suite_name}${RESET}"

    if BASE_URL="${BASE_URL}" bash "${SCRIPT_DIR}/${suite}" 2>&1 | tee -a "${logfile}"; then
      passed_suites=$((passed_suites + 1))
      echo -e "  ${GREEN}Suite PASSED${RESET}"
    else
      failed_suites=$((failed_suites + 1))
      echo -e "  ${RED}Suite FAILED${RESET}"
    fi
    echo ""
  done

  # ---------------------------------------------------------------------------
  # Aggregate summary
  # ---------------------------------------------------------------------------
  header "AGGREGATE E2E RESULTS"
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

  # Also append summary to log
  {
    echo ""
    echo "=== AGGREGATE RESULTS ==="
    echo "Suites run:    ${total_suites}"
    echo "Suites passed: ${passed_suites}"
    echo "Suites failed: ${failed_suites}"
    echo "========================="
  } >> "${logfile}"

  return "$failed_suites"
}

# =============================================================================
# Main
# =============================================================================
header "RestLib E2E Test Runner"
info "Base URL:  ${BASE_URL}"
info "Repo root: ${REPO_ROOT}"
info "Results:   ${RESULTS_DIR}"
echo ""

check_prereqs

if [ "$NO_BUILD" = false ] && [ "$NO_SERVER" = false ]; then
  build_sample
fi

if [ "$NO_SERVER" = false ]; then
  start_server
fi

run_suites
exit $?
