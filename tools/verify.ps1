$ErrorActionPreference = "Stop"
dotnet restore Osynix.Ats.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build Osynix.Ats.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test Osynix.Ats.slnx -c Release --no-build
exit $LASTEXITCODE
