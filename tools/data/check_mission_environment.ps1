$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Data'
$output = Join-Path $env:TEMP 'mars-environment-checks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$scripts = Join-Path $root 'My project/Assets/Scripts'
$sources = @(
    (Join-Path $scripts 'Simulation/MissionContracts.cs'),
    (Join-Path $scripts 'Environment/MarsTime.cs'),
    (Join-Path $scripts 'Environment/MarsWeatherArchive.cs'),
    (Join-Path $scripts 'Environment/MissionEnvironment.cs'),
    (Join-Path $PSScriptRoot 'check_mission_environment.cs')
)
$references = @(Get-ChildItem (Join-Path $editor 'NetStandard/ref/2.1.0') -Filter '*.dll')
$references += Get-ChildItem (Join-Path $editor 'Managed/UnityEngine') -Filter '*.dll'
$arguments = @('-nologo', '-noconfig', '-nostdlib+', ('-out:' + (Join-Path $output 'EnvironmentChecks.exe')))
$arguments += $references | ForEach-Object { '-r:' + $_.FullName }
$arguments += $sources
$mono = Join-Path $editor 'MonoBleedingEdge/bin/mono.exe'
& $mono (Join-Path $editor 'MonoBleedingEdge/lib/mono/4.5/csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = Join-Path $editor 'Managed/UnityEngine'
    $dataDirectory = Join-Path $root 'My project/Assets/Data/Environment'
    $profilePath = Join-Path $dataDirectory 'Resources/mission_environment_profile.json'
    $profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
    $provenance = Get-Content -LiteralPath (Join-Path $dataDirectory 'environment-provenance.json') -Raw | ConvertFrom-Json
    $prepared = @(Import-Csv -LiteralPath (Join-Path $dataDirectory 'rems_sol224_hourly.csv'))
    if ($profile.temperatures.Count -ne 26 -or $prepared.Count -ne 24) { throw 'Prepared temperature profile dimensions' }
    if ((Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $provenance.temperature.bundledRuntimeProfileSha256) { throw 'Profile hash differs from provenance' }
    for ($i = 0; $i -lt 24; $i++) {
        if ($profile.temperatures[$i + 1].hour -ne [double]$prepared[$i].hour -or
            $profile.temperatures[$i + 1].temperatureC -ne [double]$prepared[$i].temperatureC) { throw "Profile/CSV disagreement at hour $i" }
    }
    & $mono (Join-Path $output 'EnvironmentChecks.exe') (Join-Path $dataDirectory 'rems_sol224_hourly.csv')
    exit $LASTEXITCODE
}
finally { $env:MONO_PATH = $previousMonoPath }
