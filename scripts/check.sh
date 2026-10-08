#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/cloud-env.sh"
cd "$VACATIONAPP_REPO_ROOT"

dotnet restore tests/VacationApp.Core.Tests/VacationApp.Core.Tests.csproj --locked-mode
dotnet test tests/VacationApp.Core.Tests/VacationApp.Core.Tests.csproj --no-restore --configuration Debug
dotnet restore src/VacationApp/VacationApp.csproj --locked-mode
dotnet build src/VacationApp/VacationApp.csproj --no-restore --configuration Debug \
  -f net10.0-android -m:2
