#!/usr/bin/env pwsh
# Configure git to use project hooks

git config core.hooksPath .githooks

if ($LASTEXITCODE -eq 0) {
  Write-Host "✓ Git hooks configured successfully" -ForegroundColor Green
  Write-Host "  Hooks are now enabled for this repository"
} else {
  Write-Host "✗ Failed to configure git hooks" -ForegroundColor Red
  exit 1
}
