#!/usr/bin/env bash
# Runs an xcodebuild command for CI, keeps the full log, and on failure publishes the compiler
# errors as GitHub annotations so they are readable on the run page (and through the public
# API) without downloading the log.
# Usage: scripts/ci-run.sh <log-name> <command> [args...]
set -uo pipefail

log="build/ci-logs/$1.log"
shift
mkdir -p "$(dirname "$log")"

"$@" > "$log" 2>&1
status=$?
if [[ $status -ne 0 ]]; then
  echo "Command failed with exit code $status. Errors:"
  # GitHub shows up to 10 error annotations per step: the first distinct compiler errors.
  grep -E "(error|fatal error):" "$log" | sed -E 's#^/Users/runner/work/[^/]+/[^/]+/##' | awk '!seen[$0]++' | head -10 |
    while IFS= read -r line; do
      echo "::error::${line}"
    done
  # Test failures do not always contain "error:"; show the summary too.
  grep -E "Failing tests:|\*\* (BUILD|TEST) FAILED \*\*| failed \(" "$log" | head -20
  tail -40 "$log"
fi
exit $status
