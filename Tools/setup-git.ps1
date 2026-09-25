<#
.SYNOPSIS
    One-time git setup for this Unity project. Run once after cloning.

.DESCRIPTION
    Registers the Unity Smart Merge driver (UnityYAMLMerge) in the local .git/config.
    .gitattributes marks scenes, prefabs and other Unity YAML assets with
    merge=unityyamlmerge; without this setup Git falls back to a plain text merge.

    Usage (from the repository root):
        powershell -ExecutionPolicy Bypass -File Tools/setup-git.ps1
    or, if the Editor is installed somewhere unusual:
        powershell -ExecutionPolicy Bypass -File Tools/setup-git.ps1 -UnityEditorPath "D:\Unity\6000.3.24f1\Editor"

.PARAMETER UnityEditorPath
    Folder that contains Unity.exe for the project's Editor version.
    Found automatically in the Unity Hub install locations if omitted.
#>
param([string]$UnityEditorPath)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$versionFile = Join-Path $repo 'ProjectSettings\ProjectVersion.txt'
$version = (Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value

if (-not $UnityEditorPath) {
    # Unity Hub default location plus the custom one chosen in Hub preferences
    $roots = @(Join-Path $env:ProgramFiles 'Unity\Hub\Editor')
    $hubInstallPath = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
    if (Test-Path $hubInstallPath) {
        $custom = Get-Content -Raw $hubInstallPath | ConvertFrom-Json
        if ($custom) { $roots = @($custom) + $roots }
    }
    $UnityEditorPath = $roots |
        ForEach-Object { Join-Path $_ "$version\Editor" } |
        Where-Object { Test-Path (Join-Path $_ 'Unity.exe') } |
        Select-Object -First 1
}
if (-not $UnityEditorPath) {
    throw "Unity $version was not found. Pass the Editor folder: -UnityEditorPath '<path>\Editor'"
}

$yamlMerge = Join-Path $UnityEditorPath 'Data\Tools\UnityYAMLMerge.exe'
if (-not (Test-Path $yamlMerge)) {
    throw "UnityYAMLMerge.exe not found: $yamlMerge"
}
$yamlMerge = $yamlMerge -replace '\\', '/'

# %O = base, %B = theirs, %A = ours (and the result)
git -C $repo config --local merge.unityyamlmerge.name 'Unity SmartMerge (UnityYAMLMerge)'
git -C $repo config --local merge.unityyamlmerge.driver "'$yamlMerge' merge -h -p --force %O %B %A %A"
git -C $repo config --local merge.unityyamlmerge.recursive binary
if ($LASTEXITCODE -ne 0) { throw 'git config failed' }

Write-Host "Unity Smart Merge configured for $repo"
Write-Host "  UnityYAMLMerge: $yamlMerge"
