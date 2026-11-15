param(
    [string]$LabelPath = 'Artifacts/REMS/sol224.xml',
    [string]$TablePath = 'Artifacts/REMS/sol224.tab'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not [IO.Path]::IsPathRooted($LabelPath)) { $LabelPath = Join-Path $root $LabelPath }
if (-not [IO.Path]::IsPathRooted($TablePath)) { $TablePath = Join-Path $root $TablePath }
[xml]$label = Get-Content -LiteralPath $LabelPath -Raw
$record = $label.Product_Observational.File_Area_Observational.Table_Character.Record_Character
$fields = @{}
foreach ($field in $record.Field_Character) { $fields[$field.name] = $field }
foreach ($required in @('LMST', 'AMBIENT_TEMP', 'AMBIENT_TEMP_CONFIDENCE_LEVEL')) {
    if (-not $fields.ContainsKey($required)) { throw "Missing label field: $required" }
}
if ($fields.AMBIENT_TEMP.unit -ne 'KELVIN') { throw 'Unexpected ambient temperature unit' }
function Read-Field([string]$Row, [string]$Name) {
    $field = $fields[$Name]
    return $Row.Substring(([int]$field.field_location.InnerText - 1), [int]$field.field_length.InnerText).Trim(' ', '"')
}
$culture = [Globalization.CultureInfo]::InvariantCulture
$bins = @{}
$allFlags = @{}
$acceptedFlags = @{}
$rows = 0
$invalid = 0
$rejectedQuality = 0
$previousHour = -1.0
foreach ($row in [IO.File]::ReadLines($TablePath)) {
    $rows++
    if ($row.Length -ne ([int]$record.record_length.InnerText - 2)) { throw "Unexpected row length at row $rows" }
    $lmst = Read-Field $row 'LMST'
    if ($lmst -notmatch '^00224M(\d{2}):(\d{2}):(\d{2}\.\d{3})$') { throw "Invalid sol/time at row $rows" }
    $hour = [double]::Parse($Matches[1], $culture) + [double]::Parse($Matches[2], $culture) / 60 + [double]::Parse($Matches[3], $culture) / 3600
    if ($hour -lt $previousHour -or $hour -ge 24) { throw "Unordered/invalid timestamp at row $rows" }
    $previousHour = $hour
    $kelvin = [double]::Parse((Read-Field $row 'AMBIENT_TEMP'), $culture)
    $confidence = Read-Field $row 'AMBIENT_TEMP_CONFIDENCE_LEVEL'
    if (-not $allFlags.ContainsKey($confidence)) { $allFlags[$confidence] = 0 }
    $allFlags[$confidence]++
    if ($kelvin -le 150 -or $kelvin -ge 300 -or [double]::IsNaN($kelvin)) { $invalid++; continue }
    # Keep known instrument conditions, operational range and reliability; retain unknown wind factors in provenance.
    if ($confidence.Length -ne 9 -or $confidence.Substring(0, 3) -ne '111' -or $confidence.Substring(7, 2) -ne '11') {
        $rejectedQuality++
        continue
    }
    if (-not $acceptedFlags.ContainsKey($confidence)) { $acceptedFlags[$confidence] = 0 }
    $acceptedFlags[$confidence]++
    $bin = [int][Math]::Floor($hour)
    if (-not $bins.ContainsKey($bin)) { $bins[$bin] = [Collections.Generic.List[object]]::new() }
    $bins[$bin].Add([PSCustomObject]@{ hour = $hour; kelvin = $kelvin; flags = $confidence })
}
if ($rows -ne [int]$label.Product_Observational.File_Area_Observational.Table_Character.records) { throw 'Record count differs from label' }
if ($bins.Count -ne 24) { throw 'All 24 local-hour bins must have quality-filtered observations; no silent gap filling' }
$hourly = @()
foreach ($bin in ($bins.Keys | Sort-Object)) {
    $values = $bins[$bin]
    $temperature = ($values | Measure-Object -Property kelvin -Average -Minimum -Maximum)
    $hourly += [PSCustomObject][ordered]@{
        hour = [Math]::Round(($values | Measure-Object -Property hour -Average).Average, 6)
        temperatureK = [Math]::Round($temperature.Average, 6)
        temperatureC = [Math]::Round($temperature.Average - 273.15, 6)
        acceptedSamples = $values.Count
        minimumK = $temperature.Minimum
        maximumK = $temperature.Maximum
        confidenceCodes = (($values.flags | Sort-Object -Unique) -join ';')
    }
}
$maximumGap = 0.0
for ($i = 1; $i -lt $hourly.Count; $i++) { $maximumGap = [Math]::Max($maximumGap, $hourly[$i].hour - $hourly[$i - 1].hour) }
$wrapGap = $hourly[0].hour + 24 - $hourly[-1].hour
$maximumGap = [Math]::Max($maximumGap, $wrapGap)
if ($maximumGap -gt 2) { throw 'Interpolation gap exceeds two local hours' }
$boundaryC = $hourly[-1].temperatureC + ($hourly[0].temperatureC - $hourly[-1].temperatureC) * (24 - $hourly[-1].hour) / $wrapGap
$points = @([ordered]@{ hour = 0; temperatureC = [Math]::Round($boundaryC, 6) })
foreach ($point in $hourly) { $points += [ordered]@{ hour = $point.hour; temperatureC = $point.temperatureC } }
$points += [ordered]@{ hour = 24; temperatureC = [Math]::Round($boundaryC, 6) }
$profile = [ordered]@{
    id = 'rems-sol224-hourly-representative-v1'
    temperatureSource = 'REMS sol 224 (23-24 March 2013), quality-filtered hourly air temperatures; seasonal/event offsets modeled'
    backgroundMicrograyPerEarthDay = 210
    simulatedParticlePeakMicrograyPerHour = 500
    temperatures = $points
}
$out = Join-Path $root 'My project/Assets/Data/Environment'
New-Item -ItemType Directory -Path (Join-Path $out 'Resources') -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$profilePath = Join-Path $out 'Resources/mission_environment_profile.json'
[IO.File]::WriteAllText($profilePath, (($profile | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8)
$hourlyPath = Join-Path $out 'rems_sol224_hourly.csv'
$hourly | Export-Csv -LiteralPath $hourlyPath -NoTypeInformation -Encoding UTF8
$provenancePath = Join-Path $out 'environment-provenance.json'
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
$provenance.profileId = $profile.id
$provenance.classification = 'quality-filtered measured REMS profile, historical radiation reference, and explicitly simulated modifiers/hazards'
$provenance.numericRemsDataImported = $true
$tableUrl = 'https://atmos.nmsu.edu/PDS/data/PDS4/msl_rems_bundle/data_calibrated/SOL_00180_00269/SOL00224/RME_417331939RMD02240000000_______P8.TAB'
$provenance.temperature = [ordered]@{
    classification = 'hourly means derived from measured calibrated ambient air temperature; representative replay with modeled offsets'
    source = $tableUrl
    sourceLabel = $tableUrl.Replace('.TAB', '.xml')
    productIdentifier = $label.Product_Observational.Identification_Area.logical_identifier
    version = $label.Product_Observational.Identification_Area.version_id
    instrument = 'Curiosity REMS'
    location = 'Gale Crater, Curiosity sol 224; not a global temperature map'
    observationStartUtc = $label.Product_Observational.Observation_Area.Time_Coordinates.start_date_time
    observationEndUtc = $label.Product_Observational.Observation_Area.Time_Coordinates.stop_date_time
    localDownloadCompletedUtc = (Get-Item -LiteralPath $TablePath).LastWriteTimeUtc.ToString('o')
    preparedUtc = [DateTime]::UtcNow.ToString('o')
    inputUnit = 'Kelvin'
    unit = 'degrees Celsius'
    localTimeUnit = 'LMST hours; one sol divided into 24 hours'
    downloadedTableBytes = (Get-Item -LiteralPath $TablePath).Length
    downloadedLabelBytes = (Get-Item -LiteralPath $LabelPath).Length
    sourceTableSha256 = (Get-FileHash -LiteralPath $TablePath -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceLabelSha256 = (Get-FileHash -LiteralPath $LabelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    bundledRuntimeProfileBytes = (Get-Item -LiteralPath $profilePath).Length
    bundledRuntimeProfileSha256 = (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    preparedHourlyCsvSha256 = (Get-FileHash -LiteralPath $hourlyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceRows = $rows
    rejectedInvalidTemperature = $invalid
    rejectedQuality = $rejectedQuality
    acceptedRows = ($rows - $invalid - $rejectedQuality)
    confidenceCodeCounts = $allFlags
    acceptedConfidenceCodeCounts = $acceptedFlags
    qualityRule = 'Nine-character code; bytes 0,1,2,7,8 must equal 1. Wind factors 3/4 may be X; retain all codes. Numeric temperature must be inside 150-300K.'
    processing = 'Use label fixed-width byte offsets; verify sol, monotonic LMST and record count; hourly unweighted arithmetic mean of accepted values and their LMST; Celsius=Kelvin-273.15; cyclic linear interpolation; endpoints0/24 interpolated across midnight.'
    maximumInterpolationGapLocalHours = $maximumGap
    gapPolicy = 'Preparation fails if any hourly bin is empty or adjacent means are more than two local hours apart. No fallback samples are mixed into this prepared series.'
    limitation = 'One historical sol reused as representative daily shape; modeled seasonal offset and thermal-stress increments are not new observations. Unknown wind quality factors remain a limitation.'
    attribution = 'NASA/JPL-Caltech, Centro de Astrobiologia REMS team, NASA PDS Atmospheres Node; credit original data providers. No NASA endorsement is implied.'
}
[IO.File]::WriteAllText($provenancePath, (($provenance | ConvertTo-Json -Depth 12) + [Environment]::NewLine), $utf8)
Write-Output "Prepared $($hourly.Count) hourly means from $($rows - $invalid - $rejectedQuality)/$rows quality-filtered REMS rows. Maximum gap: $maximumGap local hours."
