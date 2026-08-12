#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${1:-Release}"
output_dir="$project_root/artifacts/package"
package_name="ime-olendril-patch"
vintage_story_path="${VintageStoryPath:-${VINTAGE_STORY_PATH:?Set VintageStoryPath or VINTAGE_STORY_PATH first}}"

dotnet build "$project_root/src/InterestingMeMaterialNeedsFurnacePatch.csproj" \
  --configuration "$configuration" \
  -p:VintageStoryPath="$vintage_story_path"

rm -rf "$output_dir"
mkdir -p "$output_dir/stage"
cp "$project_root/modinfo.json" "$output_dir/stage/modinfo.json"
cp "$project_root/src/bin/$configuration/net10.0/InterestingMeMaterialNeedsFurnacePatch.dll" "$output_dir/stage/InterestingMeMaterialNeedsFurnacePatch.dll"
cp -R "$project_root/assets" "$output_dir/stage/assets"

(cd "$output_dir/stage" && if command -v zip >/dev/null 2>&1; then
  zip -q -r "$output_dir/$package_name.zip" modinfo.json InterestingMeMaterialNeedsFurnacePatch.dll assets
else
  7z a -tzip -bd -y "$output_dir/$package_name.zip" modinfo.json InterestingMeMaterialNeedsFurnacePatch.dll assets >/dev/null
fi)
rm -rf "$output_dir/stage"
printf 'Created %s\n' "$output_dir/$package_name.zip"
