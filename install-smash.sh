#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Atelier/Atelier.Build"

dotnet build "$PROJECT" -c Release -p:GeneratePackageOnBuild=true
dotnet tool uninstall --global Atelier.Build 2>/dev/null || true
dotnet tool install --global Atelier.Build --add-source "$PROJECT/bin/Release"
