#!/usr/bin/env bash
set -euo pipefail
dotnet restore Osynix.Ats.slnx
dotnet build Osynix.Ats.slnx -c Release --no-restore
dotnet test Osynix.Ats.slnx -c Release --no-build
