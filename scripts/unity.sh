#!/usr/bin/env bash
# Batch entry points for the Wonderland Unity project. Close any Editor that has this project open first.
#   scripts/unity.sh configure   regenerate Assets/Scenes/Wonderland.unity + Quest project settings
#   scripts/unity.sh build       ARM64 IL2CPP development APK -> Builds/Android/Wonderland.apk
#   scripts/unity.sh test        scripted end-to-end journey + assertions (Logs/test.log)
#   scripts/unity.sh village     just the village life and work journeys (Logs/village-results.txt)
#   scripts/unity.sh questworld  benchmark on a player's saved world, Tests/Saves/quest-world.json (Logs/questworld-results.txt)
#   scripts/unity.sh rail        the railway scenarios drawn freely and on the grid (Logs/rail-results.txt; gridrail: grid only)
#   scripts/unity.sh window      the endless world: window, streaming, undo, out of sight, the map (Logs/window-results.txt)
#   scripts/unity.sh town        just the livelier town: traffic, emergencies, pets, CCTV, sound (Logs/town-results.txt)
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
  village) method=Wonderland.Editor.WonderlandJourney.TestVillage ;;
  town) method=Wonderland.Editor.WonderlandJourney.TestTown ;;
  rail) method=Wonderland.Editor.WonderlandJourney.TestRail ;;
  gridrail) method=Wonderland.Editor.WonderlandJourney.TestGridRail ;;
  roads) method=Wonderland.Editor.WonderlandJourney.TestRoads ;;
  window) method=Wonderland.Editor.WonderlandJourney.TestWindow ;;
  windowrender) method=Wonderland.Editor.WonderlandJourney.WindowRenders; graphics=() ;;
  questworld) method=Wonderland.Editor.WonderlandJourney.QuestWorld ;;
  interaction) method=Wonderland.Editor.WonderlandInteractionTest.Run ;;
  cards) method=Wonderland.Editor.WonderlandInteractionTest.CardRenders; graphics=() ;;
  pond) method=Wonderland.Editor.WonderlandJourney.PondRenders; graphics=() ;;
  railrender) method=Wonderland.Editor.WonderlandJourney.RailRenders; graphics=() ;;
  gridrender) method=Wonderland.Editor.WonderlandJourney.GridRailRenders; graphics=() ;;
  roadrender) method=Wonderland.Editor.WonderlandJourney.RoadRenders; graphics=() ;;
  joinrender) method=Wonderland.Editor.WonderlandJourney.JoinRenders; graphics=() ;;
  terrainrender) method=Wonderland.Editor.WonderlandJourney.TerrainModeRenders; graphics=() ;;
  simprofile) method=Wonderland.Editor.WonderlandJourney.SimProfile ;;
  followrender) method=Wonderland.Editor.WonderlandJourney.FollowRenders; graphics=() ;;
  walkprobe) method=Wonderland.Editor.WonderlandJourney.WalkProbe ;;
  trainflow) method=Wonderland.Editor.WonderlandJourney.TrainFlow ;;
  trackshape) method=Wonderland.Editor.WonderlandJourney.TrackShape ;;
  trackshaperender) method=Wonderland.Editor.WonderlandJourney.TrackShapeRenders; graphics=() ;;
  living) method=Wonderland.Editor.WonderlandJourney.TestLiving ;;
  livingrender) method=Wonderland.Editor.WonderlandJourney.LivingRenders; graphics=() ;;
  livinggallery) method=Wonderland.Editor.WonderlandJourney.LivingGallery; graphics=() ;;
  shops) method=Wonderland.Editor.WonderlandTownGallery.Run; graphics=() ;;
  trackprobe) method=Wonderland.Editor.WonderlandTrackProbe.Run ;;
  buildprobe) method=Wonderland.Editor.WonderlandBuildProbe.Run ;;
  budget) method=Wonderland.Editor.WonderlandTrackProbe.Budget ;;
  gallery) method=Wonderland.Editor.WonderlandRenders.Gallery; graphics=() ;;
  renders) method=Wonderland.Editor.WonderlandJourney.Renders; graphics=() ;;
  compile) method="" ;;
  *) echo "Usage: $0 [configure|build|test|village|town|rail|gridrail|roads|window|windowrender|questworld|interaction|cards|pond|railrender|gridrender|roadrender|joinrender|terrainrender|simprofile|followrender|walkprobe|trainflow|trackshape|trackshaperender|shops|trackprobe|buildprobe|budget|gallery|renders|compile]" >&2; exit 2 ;;
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
