#!/usr/bin/env bash
# Shared helpers for the aggregate E2E runners.

validate_suite_inventory() {
  local suite_dir="$1"
  local suite_pattern="$2"
  local excluded_file="$3"
  shift 3

  local registered_file discovered_path discovered_file discovered_count
  local errors=0
  local restore_nullglob=false
  local -a discovered_paths=()
  local -A registration_counts=()
  local -A discovery_counts=()

  for registered_file in "$@"; do
    registration_counts["$registered_file"]=$(( ${registration_counts["$registered_file"]:-0} + 1 ))
  done

  if shopt -q nullglob; then
    restore_nullglob=true
  else
    shopt -s nullglob
  fi

  discovered_paths=("${suite_dir}"/${suite_pattern})

  if [ "$restore_nullglob" = false ]; then
    shopt -u nullglob
  fi

  for discovered_path in "${discovered_paths[@]}"; do
    discovered_file="$(basename "$discovered_path")"
    if [ -n "$excluded_file" ] && [ "$discovered_file" = "$excluded_file" ]; then
      continue
    fi

    discovery_counts["$discovered_file"]=$(( ${discovery_counts["$discovered_file"]:-0} + 1 ))
  done

  for registered_file in "${!registration_counts[@]}"; do
    if [ "${registration_counts[$registered_file]}" -ne 1 ]; then
      echo "[ERROR] Suite inventory contains duplicate registration: ${registered_file} (${registration_counts[$registered_file]} entries)." >&2
      errors=$((errors + 1))
    fi

    discovered_count=${discovery_counts["$registered_file"]:-0}
    if [ "$discovered_count" -eq 0 ]; then
      if [ -f "${suite_dir}/${registered_file}" ]; then
        echo "[ERROR] Registered suite does not match inventory pattern '${suite_pattern}': ${registered_file}." >&2
      else
        echo "[ERROR] Registered suite file is missing: ${suite_dir}/${registered_file}." >&2
      fi
      errors=$((errors + 1))
    fi
  done

  for discovered_file in "${!discovery_counts[@]}"; do
    if [ "${registration_counts["$discovered_file"]:-0}" -eq 0 ]; then
      echo "[ERROR] Unregistered suite file: ${suite_dir}/${discovered_file}." >&2
      errors=$((errors + 1))
    fi
  done

  if [ "$errors" -gt 0 ]; then
    echo "[ERROR] Suite inventory validation failed with ${errors} error(s)." >&2
    return 1
  fi

  return 0
}

load_suite_result() {
  local output_file="$1"
  local marker_count result_line token

  SUITE_RESULT_TOTAL=""
  SUITE_RESULT_PASSED=""
  SUITE_RESULT_FAILED=""
  SUITE_RESULT_SKIPPED=""

  marker_count=$(grep -c '^E2E_RESULT ' "$output_file" || true)
  if [ "$marker_count" -ne 1 ]; then
    return 1
  fi

  result_line=$(grep '^E2E_RESULT ' "$output_file")
  for token in $result_line; do
    case "$token" in
      total=*) SUITE_RESULT_TOTAL="${token#total=}" ;;
      passed=*) SUITE_RESULT_PASSED="${token#passed=}" ;;
      failed=*) SUITE_RESULT_FAILED="${token#failed=}" ;;
      skipped=*) SUITE_RESULT_SKIPPED="${token#skipped=}" ;;
    esac
  done

  if ! [[ "$SUITE_RESULT_TOTAL" =~ ^[0-9]+$ ]] ||
     ! [[ "$SUITE_RESULT_PASSED" =~ ^[0-9]+$ ]] ||
     ! [[ "$SUITE_RESULT_FAILED" =~ ^[0-9]+$ ]] ||
     ! [[ "$SUITE_RESULT_SKIPPED" =~ ^[0-9]+$ ]]; then
    return 1
  fi

  if [ "$SUITE_RESULT_TOTAL" -ne $((SUITE_RESULT_PASSED + SUITE_RESULT_FAILED + SUITE_RESULT_SKIPPED)) ]; then
    return 1
  fi

  return 0
}
