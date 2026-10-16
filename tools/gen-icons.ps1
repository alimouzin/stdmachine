# Régénère src\StdMachine.Hmi\Controls\IconCodes.g.cs depuis assets\fonts\MaterialSymbolsOutlined.ttf et tools\IconTableGen\icons.txt
$root = Split-Path $PSScriptRoot -Parent
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild "$root\tools\IconTableGen\IconTableGen.csproj" '/t:Restore;Build' /nologo /v:q
New-Item -ItemType Directory -Force "$root\src\StdMachine.Hmi\Controls" | Out-Null
& "$root\tools\IconTableGen\bin\Debug\net48\IconTableGen.exe" "$root\assets\fonts\MaterialSymbolsOutlined.ttf" "$root\tools\IconTableGen\icons.txt" "$root\src\StdMachine.Hmi\Controls\IconCodes.g.cs"
