$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$editor = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Data'
$output = Join-Path $root 'Artifacts'
New-Item -ItemType Directory -Force $output | Out-Null
$references = @()
$references += Get-ChildItem (Join-Path $editor 'NetStandard/ref/2.1.0') -Filter '*.dll'
$references += Get-ChildItem (Join-Path $editor 'Managed/UnityEngine') -Filter '*.dll'
$references += Get-ChildItem (Join-Path $root 'My project/Library/ScriptAssemblies') -Filter '*.dll' | Where-Object { $_.Name -notlike 'Assembly-CSharp*' }
$sources = Get-ChildItem (Join-Path $root 'My project/Assets/Scripts') -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '\\Editor\\' }
$arguments = @('-nologo','-noconfig','-nostdlib+','-target:library',('-out:' + (Join-Path $output 'GameCompile.dll')), '-define:ENABLE_LEGACY_INPUT_MANAGER')
$arguments += $references | ForEach-Object { '-r:' + $_.FullName }
$arguments += $sources.FullName
& (Join-Path $editor 'MonoBleedingEdge/bin/mono.exe') (Join-Path $editor 'MonoBleedingEdge/lib/mono/4.5/csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$editorReferences = @($references) + @(Get-ChildItem (Join-Path $editor 'Managed') -Filter 'UnityEditor*.dll' | Where-Object { $_.Name -ne 'UnityEditor.dll' })
$editorSources = Get-ChildItem (Join-Path $root 'My project/Assets') -Filter '*.cs' -Recurse | Where-Object { $_.FullName -match '\\Editor\\' -and $_.FullName -notmatch '\\MarsWeather\\' }
$editorArguments = @('-nologo','-noconfig','-nostdlib+','-target:library',('-out:' + (Join-Path $output 'EditorCompile.dll')), '-define:UNITY_EDITOR,ENABLE_LEGACY_INPUT_MANAGER', ('-r:' + (Join-Path $output 'GameCompile.dll')))
$editorArguments += $editorReferences | ForEach-Object { '-r:' + $_.FullName }
$editorArguments += $editorSources.FullName
& (Join-Path $editor 'MonoBleedingEdge/bin/mono.exe') (Join-Path $editor 'MonoBleedingEdge/lib/mono/4.5/csc.exe') $editorArguments
exit $LASTEXITCODE
