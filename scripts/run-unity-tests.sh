#!/usr/bin/env bash
#
# Unity test runner (batch mode).
#
# Runs the project's Unity test suite headlessly and writes the NUnit results
# XML plus the full editor log into Logs/, following the naming already used
# there: Logs/<label>-<platform>-<timestamp>.{xml,log}
#
# Why the lock check exists: an editor with the project open and a batch-mode
# Unity cannot share a project directory, and the failure is unhelpful (Unity
# just exits, or worse, fights over Library/). Temp/UnityLockfile is the
# project-scoped signal, so this refuses up front with a clear message.
#
# Usage:
#   scripts/run-unity-tests.sh [options]
#
#     --platform EditMode|PlayMode   Suite to run (default: EditMode)
#     --filter NAME                  Only tests matching NAME (-testFilter)
#     --label LABEL                  Results/log filename prefix
#                                    (default: UnityTests)
#     --nographics                   Pass -nographics; faster, but unsuitable
#                                    for PlayMode tests needing a graphics device
#     -h, --help                     Show this help
#
# Environment:
#   UNITY_EDITOR   Full path to the Unity executable. Overrides the version
#                  lookup, which reads ProjectSettings/ProjectVersion.txt.
#
# Exit codes:
#   0  every test passed
#   2  the suite ran and at least one test failed
#   3  the run could not start, or produced no results
#
set -u -o pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLATFORM="EditMode"
LABEL="UnityTests"
FILTER=""
NOGRAPHICS=0

usage() {
    sed -n '3,33p' "${BASH_SOURCE[0]}" | sed -e 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --platform) PLATFORM="${2:-}"; shift 2 ;;
        --filter)   FILTER="${2:-}"; shift 2 ;;
        --label)    LABEL="${2:-}"; shift 2 ;;
        --nographics) NOGRAPHICS=1; shift ;;
        -h|--help)  usage; exit 0 ;;
        *) echo "error: unknown argument '$1'" >&2; echo >&2; usage >&2; exit 3 ;;
    esac
done

if [ "$PLATFORM" != "EditMode" ] && [ "$PLATFORM" != "PlayMode" ]; then
    echo "error: --platform must be EditMode or PlayMode (got '$PLATFORM')" >&2
    exit 3
fi

# --- pinned editor version -------------------------------------------------

VERSION="$(sed -n 's/^m_EditorVersion:[[:space:]]*//p' "$ROOT/ProjectSettings/ProjectVersion.txt" \
    | tr -d '\r' | head -1)"
if [ -z "$VERSION" ]; then
    echo "error: could not read m_EditorVersion from ProjectSettings/ProjectVersion.txt" >&2
    exit 3
fi

resolve_editor() {
    if [ -n "${UNITY_EDITOR:-}" ]; then
        if [ ! -x "$UNITY_EDITOR" ]; then
            echo "error: UNITY_EDITOR is set but not executable: $UNITY_EDITOR" >&2
            return 1
        fi
        printf '%s\n' "$UNITY_EDITOR"
        return 0
    fi

    local candidates=(
        "/c/Program Files/Unity/Hub/Editor/$VERSION/Editor/Unity.exe"
        "/c/Program Files/Unity/Hub/Editor/$VERSION/Editor/Unity"
        "$HOME/Unity/Hub/Editor/$VERSION/Editor/Unity.exe"
        "/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
        "$HOME/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
    )

    local candidate
    for candidate in "${candidates[@]}"; do
        if [ -x "$candidate" ]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done

    echo "error: no Unity $VERSION editor found. Looked in:" >&2
    for candidate in "${candidates[@]}"; do
        echo "  $candidate" >&2
    done
    echo "Set UNITY_EDITOR to the full path of the editor executable." >&2
    return 1
}

EDITOR="$(resolve_editor)" || exit 3

# --- project lock ----------------------------------------------------------

if [ -e "$ROOT/Temp/UnityLockfile" ]; then
    echo "error: Unity already has this project open (Temp/UnityLockfile exists)." >&2
    echo "Close the editor and retry: batch mode cannot share a project with it." >&2
    echo "If the editor is already closed, the lock file is stale — delete Temp/UnityLockfile." >&2
    exit 3
fi

# Unity accepts forward-slash paths on Windows, but Git Bash paths like
# /c/Program Files would be misread, so normalise to C:/... there.
to_native_path() {
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -m "$1"
    else
        printf '%s' "$1"
    fi
}

# --- run -------------------------------------------------------------------

mkdir -p "$ROOT/Logs"
TIMESTAMP="$(date +%Y%m%d%H%M%S)"
RESULTS="$ROOT/Logs/$LABEL-$PLATFORM-$TIMESTAMP.xml"
LOG="$ROOT/Logs/$LABEL-$PLATFORM-$TIMESTAMP.log"

ARGS=(
    -batchmode
    -projectPath "$(to_native_path "$ROOT")"
    -runTests
    -testPlatform "$PLATFORM"
    -testResults "$(to_native_path "$RESULTS")"
    -logFile "$(to_native_path "$LOG")"
)
[ -n "$FILTER" ] && ARGS+=(-testFilter "$FILTER")
[ "$NOGRAPHICS" = 1 ] && ARGS+=(-nographics)

echo "Unity:   $EDITOR ($VERSION)"
echo "Project: $ROOT"
echo "Suite:   $PLATFORM${FILTER:+  (filter: $FILTER)}"
echo "Results: $RESULTS"
echo "Log:     $LOG"
echo

"$EDITOR" "${ARGS[@]}"
STATUS=$?

# --- summarise -------------------------------------------------------------

if [ ! -f "$RESULTS" ]; then
    echo "No results file was produced; the run did not reach the test runner." >&2
    echo "Last 20 log lines ($LOG):" >&2
    tail -20 "$LOG" 2>/dev/null | sed 's/^/  /' >&2
    exit 3
fi

echo "Aggregate (<test-run>):"
grep -m1 -o '<test-run[^>]*>' "$RESULTS" \
    | grep -oE '(result|testcasecount|total|passed|failed|skipped|inconclusive)="[^"]*"' \
    | sed 's/^/  /'

FAILED_CASES="$(grep -c '<test-case [^>]*result="Failed"' "$RESULTS" 2>/dev/null || true)"
FAILED_CASES="${FAILED_CASES:-0}"

if [ "$FAILED_CASES" -gt 0 ]; then
    echo
    echo "Failed tests:"
    awk '/<test-case / && /result="Failed"/ {
        if (match($0, /fullname="[^"]*"/)) {
            print "  - " substr($0, RSTART + 10, RLENGTH - 11)
        }
    }' "$RESULTS"

    echo
    echo "Failure messages:"
    grep -oE '<message><!\[CDATA\[[^]]*\]\]>' "$RESULTS" \
        | sed -e 's|^<message><!\[CDATA\[||' -e 's|\]\]>$||' \
        | sed 's/^/  /' \
        | head -20

    echo
    echo "FAILED — $FAILED_CASES test case(s). Full log: $LOG"
    exit 2
fi

if [ "$STATUS" -ne 0 ]; then
    echo
    echo "RUN ERROR — Unity exited $STATUS with no failing test cases recorded." >&2
    echo "Last 20 log lines ($LOG):" >&2
    tail -20 "$LOG" 2>/dev/null | sed 's/^/  /' >&2
    exit 3
fi

echo
echo "PASSED — results: $RESULTS"
exit 0
