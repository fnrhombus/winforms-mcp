#!/usr/bin/env pwsh
#
# Merge dev branch to master for stable release
# This script:
# 1. Verifies dev branch CI is passing
# 2. Merges dev to master
# 3. Pushes to trigger release workflow
#

param(
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Merge Dev to Master Release Script" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Check if gh CLI is installed
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) is not installed. Please install it from https://cli.github.com/"
    exit 1
}

# Get current branch
$currentBranch = git branch --show-current
Write-Host "Current branch: $currentBranch" -ForegroundColor Yellow

# Ensure we're on dev branch
if ($currentBranch -ne "dev") {
    Write-Host "Switching to dev branch..." -ForegroundColor Yellow
    git checkout dev
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to switch to dev branch"
        exit 1
    }
}

# Fetch latest changes
Write-Host "Fetching latest changes..." -ForegroundColor Yellow
git fetch origin
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to fetch from origin"
    exit 1
}

# Check if dev is up to date with origin
$localCommit = git rev-parse dev
$remoteCommit = git rev-parse origin/dev
if ($localCommit -ne $remoteCommit) {
    Write-Error "Local dev branch is not in sync with origin/dev. Please pull latest changes."
    exit 1
}

# Check CI status for dev branch
Write-Host "Checking CI status for dev branch..." -ForegroundColor Yellow
$ciStatus = gh run list --branch dev --limit 1 --json conclusion,status,workflowName | ConvertFrom-Json
if ($ciStatus.Count -eq 0) {
    Write-Warning "No CI runs found for dev branch"
    if (-not $Force) {
        Write-Error "Use -Force to proceed anyway"
        exit 1
    }
} elseif ($ciStatus[0].conclusion -ne "success" -and $ciStatus[0].status -eq "completed") {
    Write-Error "Latest CI run on dev branch failed!"
    Write-Host "  Workflow: $($ciStatus[0].workflowName)" -ForegroundColor Red
    Write-Host "  Status: $($ciStatus[0].status)" -ForegroundColor Red
    Write-Host "  Conclusion: $($ciStatus[0].conclusion)" -ForegroundColor Red
    if (-not $Force) {
        Write-Error "Fix CI failures before merging to master. Use -Force to override (not recommended)."
        exit 1
    }
    Write-Warning "Proceeding anyway due to -Force flag"
} elseif ($ciStatus[0].status -ne "completed") {
    Write-Warning "Latest CI run is still in progress: $($ciStatus[0].status)"
    if (-not $Force) {
        Write-Error "Wait for CI to complete. Use -Force to proceed anyway (not recommended)."
        exit 1
    }
    Write-Warning "Proceeding anyway due to -Force flag"
} else {
    Write-Host "✓ CI is passing on dev branch" -ForegroundColor Green
}

# Read current version
$version = (Get-Content VERSION).Trim()
Write-Host ""
Write-Host "Current version: $version" -ForegroundColor Cyan
Write-Host "This will be released as stable version: $($version -replace '-beta$', '')" -ForegroundColor Cyan
Write-Host ""

# Confirm merge
if (-not $DryRun) {
    $confirm = Read-Host "Proceed with merge to master? (yes/no)"
    if ($confirm -ne "yes") {
        Write-Host "Merge cancelled." -ForegroundColor Yellow
        exit 0
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY RUN - Would perform the following:" -ForegroundColor Yellow
    Write-Host "  1. Switch to master branch" -ForegroundColor Gray
    Write-Host "  2. Pull latest master" -ForegroundColor Gray
    Write-Host "  3. Merge dev into master" -ForegroundColor Gray
    Write-Host "  4. Push to origin/master" -ForegroundColor Gray
    Write-Host "  5. Trigger release workflow" -ForegroundColor Gray
    Write-Host ""
    exit 0
}

# Switch to master
Write-Host "Switching to master branch..." -ForegroundColor Yellow
git checkout master
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to switch to master branch"
    exit 1
}

# Pull latest master
Write-Host "Pulling latest master..." -ForegroundColor Yellow
git pull origin master
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to pull latest master"
    exit 1
}

# Merge dev into master
Write-Host "Merging dev into master..." -ForegroundColor Yellow
git merge dev --no-ff -m "chore: merge dev to master for release $($version -replace '-beta$', '')"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Merge failed! Please resolve conflicts manually."
    exit 1
}

# Push to master
Write-Host "Pushing to master..." -ForegroundColor Yellow
git push origin master
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to push to master"
    exit 1
}

Write-Host ""
Write-Host "✓ Successfully merged dev to master!" -ForegroundColor Green
Write-Host ""
Write-Host "The release workflow will now:" -ForegroundColor Cyan
Write-Host "  1. Remove -beta suffix from version" -ForegroundColor Gray
Write-Host "  2. Create GitHub release" -ForegroundColor Gray
Write-Host "  3. Publish to NuGet and NPM" -ForegroundColor Gray
Write-Host ""
Write-Host "Monitor the workflow at: https://github.com/rhom6us/winforms-mcp/actions" -ForegroundColor Cyan
Write-Host ""
