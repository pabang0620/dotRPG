#!/usr/bin/env bash
# Downloads Unity reference assemblies from NuGet for the offline compile check.
# UnityEngine modules: UnityEngine.Modules 2021.3.33, UnityEditor: Unity3D.SDK 2021.1.14.1.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p refs
fetch() {
  local id=$1 ver=$2
  local file="refs/$id.$ver.nupkg"
  [ -f "$file" ] || curl -sSfL --retry 4 -o "$file" "https://api.nuget.org/v3-flatcontainer/$id/$ver/$id.$ver.nupkg"
  unzip -qo "$file" -d "refs/$id"
}
fetch unityengine.modules 2021.3.33
fetch unity3d.sdk 2021.1.14.1
echo "Reference assemblies ready in $(pwd)/refs"
