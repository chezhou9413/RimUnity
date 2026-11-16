#!/bin/sh
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec dotnet run --project "$repo/Tools/Build/RimUnity.Build.csproj" --configuration Release -- "$@"
