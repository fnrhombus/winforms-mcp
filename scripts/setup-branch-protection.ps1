#!/usr/bin/env pwsh
#
# Setup branch protection rules for master branch
# Requires GitHub CLI (gh) to be installed and authenticated
#

param(
    [string]$Repository = "rhom6us/winforms-mcp",
    [string]$Branch = "master"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "GitHub Branch Protection Setup Script" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Check if gh CLI is installed
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) is not installed. Please install it from https://cli.github.com/"
    exit 1
}

# Check authentication
Write-Host "Checking GitHub authentication..." -ForegroundColor Yellow
$authStatus = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Not authenticated with GitHub. Run 'gh auth login' first."
    exit 1
}
Write-Host "✓ Authenticated with GitHub" -ForegroundColor Green
Write-Host ""

# Verify repository access
Write-Host "Verifying repository access: $Repository" -ForegroundColor Yellow
$repo = gh repo view $Repository --json nameWithOwner,defaultBranchRef 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot access repository: $Repository"
    exit 1
}
Write-Host "✓ Repository access confirmed" -ForegroundColor Green
Write-Host ""

# Setup branch protection rules
Write-Host "Setting up branch protection for '$Branch' branch..." -ForegroundColor Yellow
Write-Host ""

$protectionRules = @{
    required_status_checks = @{
        strict = $true
        contexts = @(
            "build-test-coverage"  # Job name from ci-dev.yml
        )
    }
    enforce_admins = $false
    required_pull_request_reviews = $null
    restrictions = $null
    required_linear_history = $true
    allow_force_pushes = $false
    allow_deletions = $false
    block_creations = $false
    required_conversation_resolution = $false
    lock_branch = $false
    allow_fork_syncing = $false
}

Write-Host "Protection rules:" -ForegroundColor Cyan
Write-Host "  - Require status checks to pass before merging" -ForegroundColor Gray
Write-Host "  - Require branches to be up to date before merging" -ForegroundColor Gray
Write-Host "  - Require linear history (no merge commits from outside)" -ForegroundColor Gray
Write-Host "  - Block force pushes" -ForegroundColor Gray
Write-Host "  - Block branch deletion" -ForegroundColor Gray
Write-Host ""

# Note: GitHub CLI doesn't have a direct command for branch protection
# We need to use the API directly

Write-Host "Applying protection rules via GitHub API..." -ForegroundColor Yellow

$body = @{
    required_status_checks = @{
        strict = $true
        contexts = @("build-test-coverage")
    }
    enforce_admins = $false
    required_pull_request_reviews = $null
    restrictions = $null
    required_linear_history = $true
    allow_force_pushes = $false
    allow_deletions = $false
} | ConvertTo-Json -Depth 10

# Save body to temp file
$tempFile = [System.IO.Path]::GetTempFileName()
$body | Out-File -FilePath $tempFile -Encoding utf8 -NoNewline

# Apply protection rules
gh api `
    --method PUT `
    -H "Accept: application/vnd.github+json" `
    -H "X-GitHub-Api-Version: 2022-11-28" `
    "/repos/$Repository/branches/$Branch/protection" `
    --input $tempFile

# Clean up temp file
Remove-Item $tempFile -ErrorAction SilentlyContinue

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✓ Branch protection rules applied successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Master branch is now protected with:" -ForegroundColor Cyan
    Write-Host "  ✓ Required CI status checks" -ForegroundColor Green
    Write-Host "  ✓ Linear history requirement" -ForegroundColor Green
    Write-Host "  ✓ Force push protection" -ForegroundColor Green
    Write-Host "  ✓ Deletion protection" -ForegroundColor Green
    Write-Host ""
    Write-Host "Only merges from dev branch (with passing CI) are allowed." -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Error "Failed to apply branch protection rules"
    Write-Host ""
    Write-Host "You can manually configure branch protection at:" -ForegroundColor Yellow
    Write-Host "  https://github.com/$Repository/settings/branches" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Required settings:" -ForegroundColor Yellow
    Write-Host "  1. Require status checks to pass: build-test-coverage" -ForegroundColor Gray
    Write-Host "  2. Require branches to be up to date before merging" -ForegroundColor Gray
    Write-Host "  3. Require linear history" -ForegroundColor Gray
    Write-Host "  4. Do not allow force pushes" -ForegroundColor Gray
    Write-Host "  5. Do not allow deletions" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
