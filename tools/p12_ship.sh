#!/bin/bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/.." && pwd)"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/2022.3.24f1/Unity.app/Contents/MacOS/Unity}"
export MARS_BALANCE_OUTPUT="${MARS_BALANCE_OUTPUT:-$project_root/P12Results}"
export MARS_BUILD_OUTPUT="${MARS_BUILD_OUTPUT:-$project_root/P12Builds}"
mkdir -p "$MARS_BALANCE_OUTPUT" "$MARS_BUILD_OUTPUT"
case "${1:-all}" in
  tests)
    test_status=0
    "$unity_editor" -batchmode -nographics -projectPath "$project_root/My project" \
      -runTests -testPlatform EditMode -testResults "$MARS_BALANCE_OUTPUT/editmode.xml" \
      -logFile "$MARS_BALANCE_OUTPUT/editmode.log" || test_status=$?
    if [[ -f "$MARS_BALANCE_OUTPUT/balance.csv" ]]; then cat "$MARS_BALANCE_OUTPUT/balance.csv"; fi
    exit "$test_status"
    ;;
  mac|webgl)
    if [[ "$1" == mac ]]; then method=BuildMac; else method=BuildWebGL; fi
    "$unity_editor" -batchmode -nographics -quit -projectPath "$project_root/My project" \
      -executeMethod "MarsTrainer.Shipping.Editor.P12Build.$method" \
      -logFile "$MARS_BUILD_OUTPUT/$1.log"
    ;;
  all)
    MARS_BALANCE_RUNS=1000 "$0" tests
    "$0" mac
    "$0" webgl
    ;;
  *) echo "Usage: $0 [tests|mac|webgl|all]" >&2; exit 2 ;;
esac
