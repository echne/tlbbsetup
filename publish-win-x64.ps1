$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot 'artifacts\win-x64-onefile'

dotnet publish (Join-Path $PSScriptRoot 'src\TLBB.Launcher\TLBB.Launcher.csproj') `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$unexpectedEntries = Get-ChildItem -LiteralPath $outputDirectory -Recurse -Force | Where-Object {
  $_.PSIsContainer -or $_.Name -ne 'TLBB.PhongVanKiem.Launcher.exe'
}
if ($unexpectedEntries) {
  $names = ($unexpectedEntries | ForEach-Object FullName) -join ', '
  Write-Error "Expected only TLBB.PhongVanKiem.Launcher.exe in the publish folder; found: $names"
  exit 1
}
