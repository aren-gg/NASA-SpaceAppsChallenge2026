param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor')
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$outputDir = Join-Path $PSScriptRoot '.artifacts'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$engine = Join-Path $workspace 'My project/Assets/Scripts'
$sources = @(
    (Join-Path $engine 'Simulation/MissionContracts.cs'),
    (Join-Path $engine 'Simulation/MissionSimulation.cs'),
    (Join-Path $engine 'Simulation/MissionSimulation.Power.cs'),
    (Join-Path $engine 'Simulation/MissionSimulation.Needs.cs'),
    (Join-Path $engine 'Simulation/MissionSimulation.Tasks.cs'),
    (Join-Path $engine 'Simulation/SurvivalNeeds.cs'),
    (Join-Path $engine 'Research/ResearchCatalog.cs'),
    (Join-Path $PSScriptRoot 'SimulationChecks.cs')
)
# The astronaut's needs use the MarsTrainer survival model (pure C#, no UnityEngine).
$sources += Get-ChildItem (Join-Path $engine 'Sim') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName }
$mono = Join-Path $UnityEditor 'Data/MonoBleedingEdge/bin/mono.exe'
$compiler = Join-Path $UnityEditor 'Data/MonoBleedingEdge/lib/mono/4.5/csc.exe'
$executable = Join-Path $outputDir 'SimulationChecks.exe'
& $mono $compiler -nologo -langversion:9 "-out:$executable" $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $mono $executable @args
exit $LASTEXITCODE
