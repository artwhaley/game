#!/usr/bin/env bash
#
# Character rig spike runner (batch mode).
#
# Runs the ticket 01 rig steps headlessly — pin the model's import settings,
# report what actually imported, build the spike fixtures, and (optionally)
# re-measure the clip-replay diagnostics — writing the editor log for each step
# into Logs/, following the naming already used there:
# Logs/<label>-<step>-<timestamp>.log
#
# Why batch mode: the rig questions are all measurement questions ("is the
# skeleton humanoid?", "does a composed pose replay?", "does the avatar retarget
# correctly?"), and every one of them is answered by the log rather than by
# opening the editor and eyeballing a pose.
#
# Why the lock check exists: an editor with the project open and a batch-mode
# Unity cannot share a project directory, and the failure is unhelpful (Unity
# just exits, or worse, fights over Library/). Temp/UnityLockfile is the
# project-scoped signal, so this refuses up front with a clear message.
#
# Usage:
#   scripts/run-rig-spike.sh [options]
#
#     --label LABEL   Log filename prefix (default: RigSpike)
#     --diagnose      Also run the clip-sampling and bake diagnostics, which
#                     re-measure why hand-authored clips behave as they do
#     -h, --help      Show this help
#
# Environment:
#   UNITY_EDITOR   Full path to the Unity executable. Overrides the version
#                  lookup, which reads ProjectSettings/ProjectVersion.txt.
#
# Exit codes:
#   0  every step ran and the fixtures verified
#   2  a step ran but reported a failure
#   3  the run could not start
#
set -u -o pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LABEL="RigSpike"
DIAGNOSE=0

usage() {
    sed -n '3,29p' "${BASH_SOURCE[0]}" | sed -e 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --label)    LABEL="${2:-}"; shift 2 ;;
        --diagnose) DIAGNOSE=1; shift ;;
        -h|--help)  usage; exit 0 ;;
        *) echo "error: unknown argument '$1'" >&2; echo >&2; usage >&2; exit 3 ;;
    esac
done

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

# --- steps -----------------------------------------------------------------

STEPS=(
    "configure:TruthCardGame.EditorTools.CharacterRigSetup.ConfigureImport"
    "report:TruthCardGame.EditorTools.CharacterRigSetup.ReportRig"
    "fixtures:TruthCardGame.EditorTools.RigSpikeBuilder.BuildFixtures"
)
if [ "$DIAGNOSE" = 1 ]; then
    STEPS+=("clips:TruthCardGame.EditorTools.RigClipDiagnostics.Diagnose")
    STEPS+=("bake:TruthCardGame.EditorTools.RigBakeDiagnostics.Diagnose")
fi

mkdir -p "$ROOT/Logs"
TIMESTAMP="$(date +%Y%m%d%H%M%S)"

echo "Unity:   $EDITOR ($VERSION)"
echo "Project: $ROOT"
echo "Steps:   ${#STEPS[@]}"
echo

FAILED=0
for step in "${STEPS[@]}"; do
    NAME="${step%%:*}"
    METHOD="${step#*:}"
    LOG="$ROOT/Logs/$LABEL-$NAME-$TIMESTAMP.log"

    echo "--- $NAME: $METHOD"
    echo "    log: $LOG"

    "$EDITOR" -batchmode -nographics \
        -projectPath "$(to_native_path "$ROOT")" \
        -executeMethod "$METHOD" \
        -logFile "$(to_native_path "$LOG")" \
        -quit
    STATUS=$?

    if grep -q "error CS" "$LOG" 2>/dev/null; then
        echo "    FAILED — the project had compile errors; see $LOG" >&2
        FAILED=1
        continue
    fi

    if [ "$STATUS" -ne 0 ]; then
        echo "    FAILED — Unity exited $STATUS; see $LOG" >&2
        FAILED=1
        continue
    fi

    if grep -q "\[RIG\].*threw exception" "$LOG" 2>/dev/null; then
        echo "    FAILED — a step threw; see $LOG" >&2
        FAILED=1
        continue
    fi

    grep -a "\[RIG\]" "$LOG" 2>/dev/null | sed 's/\r$//' | sed 's/^/    /'
done

echo
if [ "$FAILED" -ne 0 ]; then
    echo "RIG SPIKE FAILED — logs under $ROOT/Logs/$LABEL-*-$TIMESTAMP.log" >&2
    exit 2
fi

echo "RIG SPIKE PASSED — logs under $ROOT/Logs/$LABEL-*-$TIMESTAMP.log"
exit 0
