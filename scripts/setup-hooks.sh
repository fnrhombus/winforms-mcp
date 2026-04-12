#!/bin/bash
# Configure git to use project hooks

git config core.hooksPath .githooks

if [ $? -eq 0 ]; then
  echo "✓ Git hooks configured successfully"
  echo "  Hooks are now enabled for this repository"
else
  echo "✗ Failed to configure git hooks"
  exit 1
fi
