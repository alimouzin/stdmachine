# Compile la solution avec le MSBuild de Visual Studio 2022 (PoC net48, voir POC-NET48.md).
param([string]$Target = "Build", [string]$Configuration = "Debug", [switch]$Restore)
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$root = Split-Path $PSScriptRoot -Parent
if ($Restore) { & $msbuild "$root\StdMachine.sln" /t:Restore /nologo /v:m /p:Configuration=$Configuration; if ($LASTEXITCODE) { exit $LASTEXITCODE } }
& $msbuild "$root\StdMachine.sln" /t:$Target /nologo /v:m /m /p:Configuration=$Configuration /clp:"Summary;WarningsOnly;ErrorsOnly"
exit $LASTEXITCODE
