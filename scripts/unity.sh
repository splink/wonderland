#!/usr/bin/env bash
# Batch entry points for the Wonderland Unity project. Close any Editor that has this project open first.
#   scripts/unity.sh configure   regenerate Assets/Scenes/Wonderland.unity + Quest project settings
#   scripts/unity.sh build       ARM64 IL2CPP development APK -> Builds/Android/Wonderland.apk
#   scripts/unity.sh test        scripted end-to-end journey + assertions (Logs/test.log)
#   scripts/unity.sh renders     editor renders of assets and an assembled world (docs/renders/, needs graphics)
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
version="$(sed -n 's/^m_EditorVersion: //p' "$project_dir/ProjectSettings/ProjectVersion.txt")"
unity_editor="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/$version/Editor/Unity}"
[[ -x "$unity_editor" ]] || { echo "Unity $version not found at $unity_editor. Set UNITY_EDITOR." >&2; exit 1; }
task="${1:-build}"
graphics=(-nographics)
case "$task" in
  configure) method=Wonderland.Editor.WonderlandBuild.Configure ;;
  build) method=Wonderland.Editor.WonderlandBuild.Build ;;
  test) method=Wonderland.Editor.WonderlandJourney.Test ;;
  interaction) method=Wonderland.Editor.WonderlandInteractionTest.Run ;;
  gallery) method=Wonderland.Editor.WonderlandRenders.Gallery; graphics=() ;;
  renders) method=Wonderland.Editor.WonderlandJourney.Renders; graphics=() ;;
  compile) method="" ;;
  *) echo "Usage: $0 [configure|build|test|interaction|gallery|renders|compile]" >&2; exit 2 ;;
esac
mkdir -p "$project_dir/Logs"
log="$project_dir/Logs/$task.log"
args=(-batchmode "${graphics[@]}" -quit -projectPath "$project_dir" -logFile "$log")
[[ -n "$method" ]] && args+=(-executeMethod "$method")
[[ "$task" == build || "$task" == configure ]] && args+=(-activeBuildProfile "Assets/Settings/Build Profiles/Meta Quest.asset")
status=0
"$unity_editor" "${args[@]}" || status=$?
if grep -q "error CS" "$log"; then grep "error CS" "$log" | sort -u >&2; exit 1; fi
if (( status != 0 )); then
  if [[ "$task" == build ]] && grep -q 'Build Finished, Result: Success' "$log"; then
    echo "Unity exited $status after a successful build; continuing." >&2
  else
    echo "Unity failed ($status). See $log" >&2; tail -30 "$log" >&2; exit "$status"
  fi
fi
if [[ "$task" == build ]] && grep -n '^Shader error' "$log"; then echo "APK rejected: shader errors (see $log)." >&2; exit 1; fi
echo "$task: OK ($log)"
