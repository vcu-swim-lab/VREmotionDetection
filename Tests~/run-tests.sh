#!/usr/bin/env bash
#
# Runs every test of the VR Emotion Detection package without Unity.
#
#   bash Tests~/run-tests.sh                      all stages, from anywhere
#   bash Tests~/run-tests.sh --filter JevText     only tests whose name matches (dotnet test --filter syntax)
#   bash Tests~/run-tests.sh --stage compile      only the compile checks
#   bash Tests~/run-tests.sh --compare 6416b0c    also diff the golden transcript against an older git ref
#
# Stages (all run, in this order; the exit code is non-zero if any of them FAILs):
#   preflight  find dotnet, report the library state (never modifies it)
#   restore    NuGet restore; falls back to the local package cache when offline
#   compile    every package assembly on its own (so a forbidden cross-assembly dependency fails), the
#              Android-only and Unity-only compile checks, the README/UsageGuide/Samples~ compile check, the tests
#   tests      dotnet test: Tests~/EditMode + Tests~/DotnetHarness/Tests
#   golden     only with --compare: end-to-end transcript of an older version vs this one (informational)
#
# The Unity EditMode run itself needs a Unity install and is not part of this script; see Tests~/README.md.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HARNESS="$SCRIPT_DIR/DotnetHarness"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RESULTS_DIR="$HARNESS/artifacts/test-results"
LOG_DIR="$HARNESS/artifacts/logs"

ASSEMBLY_PROJECTS=(Calibration/Calibration.csproj Backends/Backends.csproj SparseVr/SparseVr.csproj
                   Jev/Jev.csproj Runtime/Runtime.csproj Editor/Editor.csproj)
CHECK_PROJECTS=(UnityOnlyCheck/UnityOnlyCheck.csproj AndroidCheck/AndroidCheck.csproj ReadmeExamples/ReadmeExamples.csproj)
TEST_PROJECT=Tests/Tests.csproj
GOLDEN_PROJECT=Golden/Golden.csproj
ALL_STAGES=(preflight restore compile tests golden)

FILTER=""
COMPARE_REF=""
KEEP_TEMP=0
OFFLINE=0
SELECTED=()

usage() {
  awk 'NR >= 3 { if ($0 !~ /^#/) exit; sub(/^# ?/, ""); print }' "${BASH_SOURCE[0]}"
  cat <<'EOF'

Options:
  --filter <expr>    dotnet test filter, e.g. "FullyQualifiedName~Jev" or "Name=Some_test_name"
  --stage <name>     run only this stage (repeatable): preflight restore compile tests golden
  --compare <ref>    git ref (tag, branch, commit) to compare the golden transcript with; enables the golden stage
  --offline          restore from ~/.nuget/packages only (no network)
  --keep-temp        keep the per-run temp folder the tests write into
  -h, --help         this text
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --filter)    FILTER="${2:?--filter needs a value}"; shift 2 ;;
    --stage)     SELECTED+=("${2:?--stage needs a value}"); shift 2 ;;
    --compare)   COMPARE_REF="${2:?--compare needs a git ref}"; shift 2 ;;
    --offline)   OFFLINE=1; shift ;;
    --keep-temp) KEEP_TEMP=1; shift ;;
    -h|--help)   usage; exit 0 ;;
    *)           echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

for s in ${SELECTED[@]+"${SELECTED[@]}"}; do
  case " ${ALL_STAGES[*]} " in *" $s "*) ;; *) echo "unknown stage: $s (expected: ${ALL_STAGES[*]})" >&2; exit 2 ;; esac
done

wants() {  # is this stage selected?
  if [[ ${#SELECTED[@]} -gt 0 ]]; then
    case " ${SELECTED[*]} " in *" $1 "*) return 0 ;; *) return 1 ;; esac
  fi
  [[ "$1" != golden || -n "$COMPARE_REF" ]]
}

if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
  GREEN=$'\033[32m'; RED=$'\033[31m'; YELLOW=$'\033[33m'; BOLD=$'\033[1m'; OFF=$'\033[0m'
else
  GREEN=""; RED=""; YELLOW=""; BOLD=""; OFF=""
fi

RUN_TMP="$(mktemp -d "${TMPDIR:-/tmp}/vre-tests.XXXXXX")"
cleanup() {
  [[ -n "${DOTNET:-}" ]] && "$DOTNET" build-server shutdown >/dev/null 2>&1
  if [[ $KEEP_TEMP -eq 1 ]]; then
    echo "temp folder kept: $RUN_TMP"
  else
    rm -rf "$RUN_TMP"
  fi
}
trap cleanup EXIT
trap 'echo; echo "interrupted"; exit 130' INT TERM

mkdir -p "$RESULTS_DIR" "$LOG_DIR"

STAGE_NAMES=(); STAGE_RESULTS=(); STAGE_TIMES=(); STAGE_DETAILS=()
DETAIL=""
RESTORED=0
COMPILED=0

short() { sed "s#$REPO_ROOT/##g"; }

find_dotnet() {
  local candidate
  if [[ -n "${DOTNET:-}" && -x "$DOTNET" ]]; then echo "$DOTNET"; return 0; fi
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return 0; fi
  for candidate in "${DOTNET_ROOT:-/nonexistent}/dotnet" "$HOME/dotnet-sdk/dotnet" "$HOME/.dotnet/dotnet" \
                   /usr/share/dotnet/dotnet /usr/lib/dotnet/dotnet /usr/local/share/dotnet/dotnet; do
    if [[ -x "$candidate" ]]; then echo "$candidate"; return 0; fi
  done
  return 1
}

# ---- stages: each sets DETAIL and returns 0 (PASS), 1 (FAIL), 2 (INFO) or 3 (SKIP) ------------------------------------

stage_preflight() {
  DOTNET="$(find_dotnet)" || {
    DETAIL="no dotnet found: put it on PATH or set DOTNET=/path/to/dotnet (needs the .NET 8 SDK)"
    return 1
  }
  export DOTNET
  export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$DOTNET")}"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1

  local version
  version="$("$DOTNET" --version 2>/dev/null)" || { DETAIL="'$DOTNET --version' failed"; return 1; }
  case "$version" in
    8.*|9.*|1[0-9].*) ;;
    *) DETAIL="dotnet $version found, the harness targets net8.0 and needs SDK 8 or newer"; return 1 ;;
  esac

  local library="library untouched"
  if git -C "$REPO_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
    if [[ -n "$(git -C "$REPO_ROOT" status --porcelain -- Runtime Editor 'Samples~' 2>/dev/null)" ]]; then
      library="Runtime/Editor/Samples~ have uncommitted changes (tests run against them)"
    fi
  fi
  DETAIL="dotnet $version; $library"
  return 0
}

restore_one() {  # restore_one <project>
  local project="$1" log="$LOG_DIR/restore-$(basename "$1" .csproj).log"
  if [[ $OFFLINE -eq 0 ]] && "$DOTNET" restore "$HARNESS/$project" -nologo -v:q >"$log" 2>&1; then
    return 0
  fi
  # offline (or the feed is unreachable): the exact package versions are pinned in the projects and cached locally
  "$DOTNET" restore "$HARNESS/$project" -nologo -v:q --source "$HOME/.nuget/packages" >>"$log" 2>&1
}

stage_restore() {
  local project failed=0 count=0
  for project in "$TEST_PROJECT" "$GOLDEN_PROJECT" "${CHECK_PROJECTS[@]}" "${ASSEMBLY_PROJECTS[@]}"; do
    count=$((count + 1))
    if ! restore_one "$project"; then
      failed=$((failed + 1))
      echo "  restore failed: $project (see $LOG_DIR/restore-$(basename "$project" .csproj).log)"
    fi
  done
  DETAIL="$count projects restored, $failed failed"
  [[ $failed -eq 0 ]] && RESTORED=1
  [[ $failed -eq 0 ]]
}

stage_compile() {
  local project name log failed=0 count=0
  local args=(-nologo -v:q -nodeReuse:false -p:UseSharedCompilation=false)
  [[ $RESTORED -eq 1 ]] && args+=(--no-restore)

  for project in "${ASSEMBLY_PROJECTS[@]}" "${CHECK_PROJECTS[@]}" "$TEST_PROJECT" "$GOLDEN_PROJECT"; do
    name="$(basename "$project" .csproj)"
    log="$LOG_DIR/build-$name.log"
    count=$((count + 1))
    if "$DOTNET" build "$HARNESS/$project" "${args[@]}" >"$log" 2>&1; then
      echo "  ${GREEN}ok${OFF}     $name"
    else
      failed=$((failed + 1))
      echo "  ${RED}FAILED${OFF} $name"
      grep -E ' error ' "$log" | short | sed -E 's# \[[^]]*\]$##' | sort -u | head -12 | sed 's/^/           /'
    fi
  done
  DETAIL="$count projects built, $failed failed"
  [[ $failed -eq 0 ]] && COMPILED=1
  [[ $failed -eq 0 ]]
}

summary_field() {  # summary_field <name> <log>   e.g. Passed, Failed, Skipped, Total
  sed -nE "s/^(Passed|Failed)!.*[ ,]$1: +([0-9]+).*/\2/p" "$2" | tail -1
}

stage_tests() {
  local log="$LOG_DIR/tests.log"
  local args=(test "$HARNESS/$TEST_PROJECT" -nologo --results-directory "$RESULTS_DIR"
              --logger "trx;LogFileName=tests.trx" -nodeReuse:false -p:UseSharedCompilation=false)
  [[ $COMPILED -eq 1 ]] && args+=(--no-build)
  [[ $COMPILED -eq 0 && $RESTORED -eq 1 ]] && args+=(--no-restore)
  [[ -n "$FILTER" ]] && args+=(--filter "$FILTER")

  TMPDIR="$RUN_TMP" "$DOTNET" "${args[@]}" >"$log" 2>&1
  local code=$?

  local passed failed skipped total
  passed="$(summary_field Passed "$log")"; failed="$(summary_field Failed "$log")"
  skipped="$(summary_field Skipped "$log")"; total="$(summary_field Total "$log")"

  if [[ -z "$total" ]]; then
    if grep -q 'No test matches' "$log"; then
      DETAIL="no test matches the filter '$FILTER'"
    else
      DETAIL="dotnet test produced no summary (exit $code); see $LOG_DIR/tests.log"
      grep -E ' error |error:' "$log" | short | sort -u | head -8 | sed 's/^/           /'
    fi
    return 1
  fi

  if [[ "$failed" != "0" || $code -ne 0 ]]; then
    echo "  failing tests:"
    grep -E '^\s+Failed ' "$log" | sed -E 's/^\s+Failed /    - /; s/ \[[0-9 a-z<]+\]$//' | head -40
    echo "  details: $log"
  fi
  DETAIL="$passed passed, $failed failed, $skipped skipped (of $total); trx: ${RESULTS_DIR#$REPO_ROOT/}/tests.trx"
  [[ "$failed" == "0" && $code -eq 0 && "$total" -gt 0 ]]
}

run_golden() {  # run_golden <outfile> <label> [extra dotnet args...]
  local out="$1" label="$2"; shift 2
  GOLDEN_OUT="$out" TMPDIR="$RUN_TMP" "$DOTNET" test "$HARNESS/$GOLDEN_PROJECT" -nologo -nodeReuse:false \
    -p:UseSharedCompilation=false "$@" >"$LOG_DIR/golden-$label.log" 2>&1
}

stage_golden() {
  local ref="$COMPARE_REF" dir="$RUN_TMP/ref" before="$RUN_TMP/golden-ref.json" now="$RUN_TMP/golden-now.json"
  if [[ -z "$ref" ]]; then
    DETAIL="the golden stage needs --compare <git ref>"
    return 1
  fi
  if ! git -C "$REPO_ROOT" rev-parse --verify --quiet "$ref^{commit}" >/dev/null; then
    DETAIL="'$ref' is not a git ref of this repository"
    return 1
  fi
  mkdir -p "$dir"
  git -C "$REPO_ROOT" archive "$ref" Runtime Editor 2>/dev/null | tar -x -C "$dir"
  if [[ ! -d "$dir/Runtime/Scripts" ]]; then
    DETAIL="'$ref' has no Runtime/Scripts folder, so there is nothing to compare"
    return 3
  fi

  local extra=() prefix file
  prefix="$(sed -nE 's/^namespace ([A-Za-z0-9_]+)\.VREmotionDetection.*/\1/p' "$dir/Runtime/Scripts/Emotion.cs" 2>/dev/null | head -1)"
  if [[ -z "$prefix" ]]; then
    extra+=(-p:LegacyGlobalNamespace=true)   # a version from before the types moved into a namespace
  elif [[ "$prefix" != VcuSeCenter ]]; then
    # a version with an older namespace prefix: rename it in the extracted copy so the scenario compiles against it
    while IFS= read -r file; do
      sed -E "s/(^|[^A-Za-z0-9_])$prefix\./\1VcuSeCenter./g" "$file" >"$file.tmp" && mv "$file.tmp" "$file"
    done < <(grep -rl --include='*.cs' "$prefix\." "$dir")
  fi

  if ! run_golden "$before" ref -p:PackageRoot="$dir" -p:ArtifactsPath="$RUN_TMP/artifacts-ref" "${extra[@]}"; then
    DETAIL="the golden scenario does not run against '$ref' (see $LOG_DIR/golden-ref.log); compare skipped"
    return 3
  fi
  if ! run_golden "$now" now; then
    DETAIL="the golden scenario fails on the current tree (see $LOG_DIR/golden-now.log)"
    return 1
  fi

  if cmp -s "$before" "$now"; then
    DETAIL="end-to-end transcript identical to '$ref'"
    return 0
  fi
  echo "  transcript differs from '$ref' (- $ref, + now):"
  diff -u --label "$ref" --label "now" "$before" "$now" | head -80 | sed 's/^/    /'
  DETAIL="behaviour differs from '$ref' in the end-to-end scenario (review the diff above)"
  return 2
}

run_stage() {
  local name="$1" start=$SECONDS code
  echo "${BOLD}== $name${OFF}"
  "stage_$name"
  code=$?
  local result
  case $code in
    0) result="PASS" ;;
    2) result="INFO" ;;
    3) result="SKIP" ;;
    *) result="FAIL" ;;
  esac
  STAGE_NAMES+=("$name"); STAGE_RESULTS+=("$result"); STAGE_TIMES+=("$((SECONDS - start))s"); STAGE_DETAILS+=("$DETAIL")
  echo
  return 0
}

# ---- main -------------------------------------------------------------------------------------------------------------

echo "${BOLD}VR Emotion Detection - test runner${OFF}"
echo "package: $REPO_ROOT"
echo

run_stage preflight
if [[ "${STAGE_RESULTS[0]}" == "FAIL" ]]; then
  :
else
  for stage in restore compile tests golden; do
    wants "$stage" && run_stage "$stage"
  done
fi

echo "${BOLD}Summary${OFF}"
status=0
for i in "${!STAGE_NAMES[@]}"; do
  case "${STAGE_RESULTS[$i]}" in
    PASS) color="$GREEN" ;;
    FAIL) color="$RED"; status=1 ;;
    *)    color="$YELLOW" ;;
  esac
  printf '  %-10s %s%-5s%s %5s  %s\n' "${STAGE_NAMES[$i]}" "$color" "${STAGE_RESULTS[$i]}" "$OFF" "${STAGE_TIMES[$i]}" "${STAGE_DETAILS[$i]}"
done
echo
if [[ $status -eq 0 ]]; then echo "${GREEN}${BOLD}ALL GOOD${OFF}"; else echo "${RED}${BOLD}FAILED${OFF} (logs: ${LOG_DIR#$REPO_ROOT/})"; fi
exit $status
