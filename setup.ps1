#!/usr/bin/env pwsh
# Development environment setup for contributors

$ErrorActionPreference = "Stop"

Write-Host "🚀 Setting up development environment..." -ForegroundColor Green
Write-Host ""

# Configure git hooks
Write-Host "Configuring git hooks..." -ForegroundColor Cyan
git config core.hooksPath .githooks

if ($LASTEXITCODE -eq 0) {
  Write-Host "✓ Git hooks configured" -ForegroundColor Green
  Write-Host "  - commit-msg: Validates Conventional Commits format"
  Write-Host "  - pre-commit: Runs dotnet format for code style"
} else {
  Write-Host "✗ Failed to configure git hooks" -ForegroundColor Red
  exit 1
}

Write-Host ""
Write-Host "✓ Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:"
Write-Host "  dotnet build Rhombus.WinFormsMcp.sln"
Write-Host "  dotnet test Rhombus.WinFormsMcp.sln"
Write-Host ""
Write-Host "See CLAUDE.md for development guidelines."
