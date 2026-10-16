# Génère les captures d'écran de toutes les pages dans artifacts\screens (StdMachine.Hmi.exe --screenshot-all) et affiche le crash.log éventuel.
param([string]$Scenario = "")
$root = Split-Path $PSScriptRoot -Parent
$exe = "$root\src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe"
$dir = "$root\artifacts\screens"
Remove-Item "$dir" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$root\src\StdMachine.Hmi\bin\Debug\net48\crash.log" -ErrorAction SilentlyContinue
$args = @('--screenshot-all', $dir)
if ($Scenario) { $args += @('--scenario', $Scenario) }
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $exe -ArgumentList $args -PassThru -Wait
"code=$($p.ExitCode) en $([int]$sw.Elapsed.TotalSeconds) s"
if (Test-Path "$dir") { Get-ChildItem $dir -Filter *.png | Select-Object Name, Length }
if (Test-Path "$dir\capture.log") { Get-Content "$dir\capture.log" }
if (Test-Path "$root\src\StdMachine.Hmi\bin\Debug\net48\crash.log") { Get-Content "$root\src\StdMachine.Hmi\bin\Debug\net48\crash.log" -Encoding UTF8 | Select-Object -First 25 }
