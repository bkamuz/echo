#Requires -Version 5.1
<#
.SYNOPSIS
  Bump version, commit, tag v*, and push to trigger GitHub Release workflow.

.DESCRIPTION
  Updates <Version> in src/Echo.App/Echo.App.csproj, creates an annotated tag,
  and pushes branch + tag to origin. Requires a clean working tree.

.PARAMETER Bump
  Semver bump when a release tag already exists: patch, minor, or major.
  Ignored on the first release (no v* tags yet) unless -Version is set.

.PARAMETER Version
  Explicit version X.Y.Z (skips bump logic).

.PARAMETER DryRun
  Show planned actions without commit, tag, or push.

.PARAMETER AllowDirty
  Allow uncommitted changes in the working tree.

.PARAMETER NoPush
  Create commit and tag locally without pushing.

.EXAMPLE
  .\scripts\new-release.ps1 -Bump patch

.EXAMPLE
  .\scripts\new-release.ps1 -Version 2.0.0
#>
param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "",

    [string]$Version = "",

    [switch]$DryRun,
    [switch]$AllowDirty,
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$CsprojPath = Join-Path $RepoRoot "src\Echo.App\Echo.App.csproj"

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Get-CsprojVersion {
    $content = Get-Content -LiteralPath $CsprojPath -Raw
    if ($content -notmatch "<Version>([^<]+)</Version>") {
        throw "Missing <Version> in $CsprojPath"
    }
    return $Matches[1].Trim()
}

function Set-CsprojVersion {
    param([string]$NewVersion)
    $content = Get-Content -LiteralPath $CsprojPath -Raw
    $updated = $content -replace "(<Version>)[^<]*(</Version>)", "`${1}$NewVersion`${2}"
    if ($updated -eq $content) {
        throw "Failed to update <Version> in $CsprojPath"
    }
    Set-Content -LiteralPath $CsprojPath -Value $updated -NoNewline
}

function Test-SemVer {
    param([string]$Value)
    return $Value -match "^\d+\.\d+\.\d+$"
}

function Get-LatestTagVersion {
    $tags = @(git -C $RepoRoot tag -l "v*" 2>$null)
    if ($tags.Count -eq 0) {
        return $null
    }

    $versions = foreach ($tag in $tags) {
        $v = $tag -replace "^v", ""
        if (Test-SemVer $v) {
            [version]$v
        }
    }

    if (-not $versions -or $versions.Count -eq 0) {
        return $null
    }

    $latest = ($versions | Sort-Object -Descending | Select-Object -First 1)
    return "{0}.{1}.{2}" -f $latest.Major, $latest.Minor, $latest.Build
}

function Bump-SemVer {
    param(
        [string]$Current,
        [string]$Kind
    )

    $v = [version]$Current
    switch ($Kind) {
        "patch" { return "{0}.{1}.{2}" -f $v.Major, $v.Minor, ($v.Build + 1) }
        "minor" { return "{0}.{1}.0" -f $v.Major, ($v.Minor + 1) }
        "major" { return "{0}.0.0" -f ($v.Major + 1) }
        default {
            throw "Unhandled bump kind: $Kind"
        }
    }
}

function Test-TagExists {
    param([string]$TagName)

    $local = git -C $RepoRoot rev-parse -q --verify "refs/tags/$TagName" 2>$null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    $remote = git -C $RepoRoot ls-remote --tags origin $TagName 2>$null
    return -not [string]::IsNullOrWhiteSpace($remote)
}

function Invoke-Git {
    param([string[]]$GitArguments)
    $output = & git -C $RepoRoot @GitArguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArguments -join ' ') failed with exit code $LASTEXITCODE"
    }
    return $output
}

Push-Location $RepoRoot
try {
    Assert-Command git
    Assert-Command gh

    $headRef = git symbolic-ref -q HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($headRef)) {
        throw "Detached HEAD is not supported. Check out a branch first."
    }

    $branch = $headRef -replace "^refs/heads/", ""
    $null = Invoke-Git -GitArguments @("remote", "get-url", "origin")

    if (-not $AllowDirty) {
        $dirty = git status --porcelain
        if ($dirty) {
            throw "Working tree is not clean. Commit or stash changes, or pass -AllowDirty."
        }
    }

    $null = Invoke-Git -GitArguments @("fetch", "--tags", "origin")

    $latestTagVersion = Get-LatestTagVersion
    $csprojVersion = Get-CsprojVersion

    if ($Version) {
        if (-not (Test-SemVer $Version)) {
            throw "Invalid -Version '$Version'. Expected X.Y.Z."
        }
        $nextVersion = $Version
    }
    elseif ($latestTagVersion) {
        if ([string]::IsNullOrWhiteSpace($Bump)) {
            $Bump = Read-Host "Bump type (patch/minor/major)"
            if ($Bump -notin @("patch", "minor", "major")) {
                throw "Invalid bump type '$Bump'. Use patch, minor, or major."
            }
        }
        $nextVersion = Bump-SemVer -Current $latestTagVersion -Kind $Bump
    }
    else {
        $nextVersion = $csprojVersion
        if (-not (Test-SemVer $nextVersion)) {
            throw "Invalid csproj version '$nextVersion'. Expected X.Y.Z."
        }
    }

    $tagName = "v$nextVersion"
    if (Test-TagExists -TagName $tagName) {
        throw "Tag $tagName already exists locally or on origin."
    }

    Write-Host "Branch:       $branch"
    Write-Host "Csproj now:   $csprojVersion"
    if ($latestTagVersion) {
        Write-Host "Latest tag:   v$latestTagVersion"
    }
    else {
        Write-Host "Latest tag:   (none - first release)"
    }
    Write-Host "Next version: $nextVersion"
    Write-Host "Tag:          $tagName"

    if ($DryRun) {
        Write-Host ""
        Write-Host "Dry run - no commit, tag, or push."
        return
    }

    if ($csprojVersion -ne $nextVersion) {
        Set-CsprojVersion -NewVersion $nextVersion
        $null = Invoke-Git -GitArguments @("add", "--", "src/Echo.App/Echo.App.csproj")
    }

    $commitMessage = "Release v$nextVersion"
    $status = git status --porcelain
    if ($status) {
        $null = Invoke-Git -GitArguments @("commit", "-m", $commitMessage)
    }
    else {
        Write-Host "Csproj already at $nextVersion - skipping commit."
    }

    $null = Invoke-Git -GitArguments @("tag", "-a", $tagName, "-m", $commitMessage)

    if ($NoPush) {
        Write-Host ""
        Write-Host "Created tag $tagName locally (-NoPush)."
        return
    }

    $null = Invoke-Git -GitArguments @("push", "origin", "HEAD")
    $null = Invoke-Git -GitArguments @("push", "origin", $tagName)

    $repoUrl = (gh repo view --json url -q .url).Trim()
    Write-Host ""
    Write-Host "Pushed $tagName. Release workflow should start shortly."
    Write-Host "Repository: $repoUrl/releases"
    Write-Host "Actions:    $repoUrl/actions/workflows/release.yml"

    Start-Sleep -Seconds 3
    $run = gh run list --workflow Release --limit 1 --json databaseId,url,status -q '.[0]' 2>$null
    if ($run) {
        $runUrl = (gh run list --workflow Release --limit 1 --json url -q '.[0].url').Trim()
        if ($runUrl) {
            Write-Host "Latest run: $runUrl"
        }
    }
}
finally {
    Pop-Location
}
