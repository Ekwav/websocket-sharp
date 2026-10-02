#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
dotnet build frame-cap.csproj --ignore-failed-sources --verbosity quiet
for receiver in client server; do
  dotnet bin/Debug/net10.0/frame-cap.dll "$receiver"
  dotnet bin/Debug/net10.0/frame-cap.dll "$receiver" burst
done
