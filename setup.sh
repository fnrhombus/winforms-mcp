#!/bin/bash
# Development environment setup for contributors

set -e

echo "🚀 Setting up development environment..."
echo ""

# Configure git hooks
echo "Configuring git hooks..."
git config core.hooksPath .githooks

if [ $? -eq 0 ]; then
  echo "✓ Git hooks configured"
  echo "  - commit-msg: Validates Conventional Commits format"
  echo "  - pre-commit: Runs dotnet format for code style"
else
  echo "✗ Failed to configure git hooks"
  exit 1
fi

echo ""
echo "✓ Setup complete!"
echo ""
echo "Next steps:"
echo "  dotnet build Rhombus.WinFormsMcp.sln"
echo "  dotnet test Rhombus.WinFormsMcp.sln"
echo ""
echo "See CLAUDE.md for development guidelines."
