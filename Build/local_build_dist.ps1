[CmdletBinding()]
param(
    [string]$Version = "",

    [switch]$UseExistingPublish,
    [switch]$UseCachedWebBuild,
    [switch]$ForceWebBuild,
    [switch]$SkipWebBuild,
    [switch]$NoArchive,
    [switch]$NoOpenOutput,

    [string]$OutputDir = "dist",
    [string]$MapEditorRepo = "https://github.com/huiyadanli/bettergi-map",
    [string]$MapEditorBranch = "main",
    [string]$ScriptWebRepo = "https://github.com/zaodonganqi/bettergi-script-web"
)

$ErrorActionPreference = "Stop"

$rootDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workDir = Join-Path $rootDir "Tmp\local-build-dist"
$versionCachePath = Join-Path $workDir "last-version.txt"
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $rootDir $OutputDir }
$distDir = Join-Path $outputRoot "BetterGI"
$publishDir = Join-Path $rootDir "BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64"
$versionPattern = '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = ""
    )

    if ($WorkingDirectory) {
        Push-Location $WorkingDirectory
    }

    try {
        Write-Host "> $FilePath $($Arguments -join ' ')"
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
        }
    }
    finally {
        if ($WorkingDirectory) {
            Pop-Location
        }
    }
}

function Resolve-CommandPath {
    param(
        [Parameter(Mandatory = $true)][string]$CommandName,
        [string[]]$FallbackPaths = @()
    )

    foreach ($path in $FallbackPaths) {
        if ($path -and (Test-Path $path)) {
            return (Resolve-Path $path).Path
        }
    }

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "Command not found: $CommandName"
}

function Read-VersionFromDialog {
    param([string]$DefaultVersion)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "BetterGI Local Build"
    $form.Width = 520
    $form.Height = 145
    $form.StartPosition = "CenterScreen"
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true

    $label = New-Object System.Windows.Forms.Label
    $label.Text = "Version"
    $label.Left = 12
    $label.Top = 15
    $label.Width = 480
    $form.Controls.Add($label)

    $textBox = New-Object System.Windows.Forms.TextBox
    $textBox.Left = 12
    $textBox.Top = 38
    $textBox.Width = 480
    $textBox.Text = $DefaultVersion
    $textBox.SelectionStart = $textBox.Text.Length
    $textBox.SelectionLength = 0
    $form.Controls.Add($textBox)

    $okButton = New-Object System.Windows.Forms.Button
    $okButton.Text = "Build"
    $okButton.Left = 312
    $okButton.Top = 72
    $okButton.Width = 85
    $okButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($okButton)
    $form.AcceptButton = $okButton

    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Text = "Cancel"
    $cancelButton.Left = 407
    $cancelButton.Top = 72
    $cancelButton.Width = 85
    $cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancelButton)
    $form.CancelButton = $cancelButton

    $form.Add_Shown({ $textBox.Focus() })
    $result = $form.ShowDialog()
    if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
        throw "Build canceled."
    }

    return $textBox.Text.Trim()
}

function Resolve-BuildVersion {
    param([string]$InputVersion)

    $lastVersion = ""
    if (Test-Path $versionCachePath) {
        $lastVersion = (Get-Content -Path $versionCachePath -Raw).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($InputVersion)) {
        $InputVersion = Read-VersionFromDialog $lastVersion
    }
    else {
        $InputVersion = $InputVersion.Trim()
    }

    if ([string]::IsNullOrWhiteSpace($InputVersion)) {
        throw "Version is required."
    }

    if ($InputVersion -notmatch $versionPattern) {
        throw "Invalid version: $InputVersion. Expected semantic version, for example 1.2.3 or 1.2.3-alpha.1."
    }

    return $InputVersion
}

function Ensure-GitRepo {
    param(
        [Parameter(Mandatory = $true)][string]$RepoUrl,
        [Parameter(Mandatory = $true)][string]$TargetDir,
        [string]$Branch = ""
    )

    $git = Resolve-CommandPath "git"

    if (Test-Path (Join-Path $TargetDir ".git")) {
        Invoke-Native $git @("-C", $TargetDir, "fetch", "--prune", "origin")
        if ($Branch) {
            Invoke-Native $git @("-C", $TargetDir, "checkout", $Branch)
            Invoke-Native $git @("-C", $TargetDir, "pull", "--ff-only", "origin", $Branch)
        }
        else {
            Invoke-Native $git @("-C", $TargetDir, "pull", "--ff-only")
        }
        return
    }

    if (Test-Path $TargetDir) {
        throw "Target exists but is not a git repo: $TargetDir"
    }

    $parent = Split-Path $TargetDir -Parent
    New-Item -Path $parent -ItemType Directory -Force | Out-Null

    $args = @("clone", "--depth", "1")
    if ($Branch) {
        $args += @("-b", $Branch)
    }
    $args += @($RepoUrl, $TargetDir)
    Invoke-Native $git $args
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (-not (Test-Path $SourceDir)) {
        throw "Source directory not found: $SourceDir"
    }

    New-Item -Path $DestinationDir -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDir "*") -Destination $DestinationDir -Recurse -Force
}

function Get-GitOutput {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = ""
    )

    $git = Resolve-CommandPath "git"
    if ($WorkingDirectory) {
        Push-Location $WorkingDirectory
    }

    try {
        $output = & $git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
        }

        return ($output | Select-Object -First 1).Trim()
    }
    finally {
        if ($WorkingDirectory) {
            Pop-Location
        }
    }
}

function Get-WebRepoBranch {
    param(
        [Parameter(Mandatory = $true)][string]$RepoDir,
        [string]$ConfiguredBranch = ""
    )

    if ($ConfiguredBranch) {
        return $ConfiguredBranch
    }

    return Get-GitOutput -Arguments @("-C", $RepoDir, "rev-parse", "--abbrev-ref", "HEAD")
}

function Test-DirectoryHasFiles {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Test-Path $Path) -and ($null -ne (Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1))
}

function Build-WebProject {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$RepoUrl,
        [string]$Branch = "",
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    $repoDir = Join-Path $workDir $Name
    $dist = Join-Path $repoDir "dist"
    $commitCachePath = Join-Path $workDir "$Name.commit"

    if ($UseCachedWebBuild) {
        Write-Step "Use cached web build: $Name"
        Copy-DirectoryContent $dist $DestinationDir
        return
    }

    Ensure-GitRepo $RepoUrl $repoDir $Branch

    $branchToBuild = Get-WebRepoBranch $repoDir $Branch
    $targetCommit = Get-GitOutput -Arguments @("-C", $repoDir, "rev-parse", "origin/$branchToBuild")
    $builtCommit = ""
    if (Test-Path $commitCachePath) {
        $builtCommit = (Get-Content -Path $commitCachePath -Raw).Trim()
    }

    if (-not $ForceWebBuild -and $builtCommit -eq $targetCommit -and (Test-DirectoryHasFiles $dist)) {
        Write-Step "Reuse web build cache: $Name $targetCommit"
        Copy-DirectoryContent $dist $DestinationDir
        return
    }

    $npm = Resolve-CommandPath "npm.cmd" @((Join-Path $env:ProgramFiles "nodejs\npm.cmd"))
    Invoke-Native $npm @("install") $repoDir
    Invoke-Native $npm @("run", "build:single") $repoDir
    Copy-DirectoryContent $dist $DestinationDir
    Set-Content -Path $commitCachePath -Value $targetCommit -Encoding ASCII
}

$Version = Resolve-BuildVersion $Version
$archivePath = Join-Path $outputRoot "BetterGI_v$Version.7z"

Write-Step "Prepare output"
New-Item -Path $outputRoot -ItemType Directory -Force | Out-Null
if (Test-Path $distDir) {
    Remove-Item $distDir -Recurse -Force
}
New-Item -Path $distDir -ItemType Directory -Force | Out-Null

if (-not $UseExistingPublish) {
    Write-Step "Publish BetterGI $Version"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    $project = Join-Path $rootDir "BetterGenshinImpact\BetterGenshinImpact.csproj"
    Invoke-Native "dotnet" @(
        "publish",
        $project,
        "-c",
        "Release",
        "-p:PublishProfile=FolderProfile",
        "-p:Version=$Version"
    ) $rootDir
}
else {
    Write-Step "Use existing publish output"
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

Write-Step "Copy publish output"
Copy-DirectoryContent $publishDir $distDir

Write-Step "Clean build-only files"
Get-ChildItem -Path $distDir -Recurse -Filter "*.lib" | Remove-Item -Force
Get-ChildItem -Path $distDir -Recurse -Filter "*ffmpeg*.dll" | Remove-Item -Force
Get-ChildItem -Path $distDir -Recurse -Filter "*.pdb" | Remove-Item -Force

if (-not $SkipWebBuild) {
    Write-Step "Build web map editor"
    Build-WebProject `
        -Name "bettergi-map" `
        -RepoUrl $MapEditorRepo `
        -Branch $MapEditorBranch `
        -DestinationDir (Join-Path $distDir "Assets\Map\Editor")

    Write-Step "Build web scripts list"
    Build-WebProject `
        -Name "bettergi-script-web" `
        -RepoUrl $ScriptWebRepo `
        -DestinationDir (Join-Path $distDir "Assets\Web\ScriptRepo")
}
else {
    Write-Step "Skip web build"
}

if (-not $NoArchive) {
    Write-Step "Create archive"
    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }

    $sevenZip = Resolve-CommandPath "7z.exe" @((Join-Path $rootDir "Build\MicaSetup.Tools\7-Zip\7z.exe"))
    Invoke-Native $sevenZip @(
        "a",
        $archivePath,
        "BetterGI",
        "-t7z",
        "-mx=5",
        "-mf=BCJ2",
        "-r",
        "-y"
    ) $outputRoot

    Write-Host ""
    Write-Host "Done: $archivePath" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Done: $distDir" -ForegroundColor Green
}

New-Item -Path (Split-Path $versionCachePath -Parent) -ItemType Directory -Force | Out-Null
Set-Content -Path $versionCachePath -Value $Version -Encoding ASCII

if (-not $NoOpenOutput) {
    Start-Process explorer.exe $outputRoot
}
