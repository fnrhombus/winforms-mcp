#!/bin/bash
#
# Merge dev branch to master for stable release
# This script:
# 1. Verifies dev branch CI is passing
# 2. Merges dev to master
# 3. Pushes to trigger release workflow
#

set -e

FORCE=false
DRY_RUN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --force)
            FORCE=true
            shift
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--force] [--dry-run]"
            exit 1
            ;;
    esac
done

echo "=================================="
echo "Merge Dev to Master Release Script"
echo "=================================="
echo ""

# Check if gh CLI is installed
if ! command -v gh &> /dev/null; then
    echo "ERROR: GitHub CLI (gh) is not installed."
    echo "Please install it from https://cli.github.com/"
    exit 1
fi

# Get current branch
CURRENT_BRANCH=$(git branch --show-current)
echo "Current branch: $CURRENT_BRANCH"

# Ensure we're on dev branch
if [ "$CURRENT_BRANCH" != "dev" ]; then
    echo "Switching to dev branch..."
    git checkout dev
fi

# Fetch latest changes
echo "Fetching latest changes..."
git fetch origin

# Check if dev is up to date with origin
LOCAL_COMMIT=$(git rev-parse dev)
REMOTE_COMMIT=$(git rev-parse origin/dev)
if [ "$LOCAL_COMMIT" != "$REMOTE_COMMIT" ]; then
    echo "ERROR: Local dev branch is not in sync with origin/dev."
    echo "Please pull latest changes."
    exit 1
fi

# Check CI status for dev branch
echo "Checking CI status for dev branch..."
CI_JSON=$(gh run list --branch dev --limit 1 --json conclusion,status,workflowName)
CI_COUNT=$(echo "$CI_JSON" | jq '. | length')

if [ "$CI_COUNT" -eq 0 ]; then
    echo "WARNING: No CI runs found for dev branch"
    if [ "$FORCE" != "true" ]; then
        echo "ERROR: Use --force to proceed anyway"
        exit 1
    fi
else
    CI_CONCLUSION=$(echo "$CI_JSON" | jq -r '.[0].conclusion')
    CI_STATUS=$(echo "$CI_JSON" | jq -r '.[0].status')
    CI_WORKFLOW=$(echo "$CI_JSON" | jq -r '.[0].workflowName')

    if [ "$CI_CONCLUSION" != "success" ] && [ "$CI_STATUS" == "completed" ]; then
        echo "ERROR: Latest CI run on dev branch failed!"
        echo "  Workflow: $CI_WORKFLOW"
        echo "  Status: $CI_STATUS"
        echo "  Conclusion: $CI_CONCLUSION"
        if [ "$FORCE" != "true" ]; then
            echo "ERROR: Fix CI failures before merging to master."
            echo "Use --force to override (not recommended)."
            exit 1
        fi
        echo "WARNING: Proceeding anyway due to --force flag"
    elif [ "$CI_STATUS" != "completed" ]; then
        echo "WARNING: Latest CI run is still in progress: $CI_STATUS"
        if [ "$FORCE" != "true" ]; then
            echo "ERROR: Wait for CI to complete."
            echo "Use --force to proceed anyway (not recommended)."
            exit 1
        fi
        echo "WARNING: Proceeding anyway due to --force flag"
    else
        echo "✓ CI is passing on dev branch"
    fi
fi

# Read current version
VERSION=$(cat VERSION | tr -d '\n\r')
STABLE_VERSION=$(echo "$VERSION" | sed 's/-beta$//')
echo ""
echo "Current version: $VERSION"
echo "This will be released as stable version: $STABLE_VERSION"
echo ""

# Confirm merge
if [ "$DRY_RUN" == "true" ]; then
    echo ""
    echo "DRY RUN - Would perform the following:"
    echo "  1. Switch to master branch"
    echo "  2. Pull latest master"
    echo "  3. Merge dev into master"
    echo "  4. Push to origin/master"
    echo "  5. Trigger release workflow"
    echo ""
    exit 0
fi

read -p "Proceed with merge to master? (yes/no): " CONFIRM
if [ "$CONFIRM" != "yes" ]; then
    echo "Merge cancelled."
    exit 0
fi

# Switch to master
echo "Switching to master branch..."
git checkout master

# Pull latest master
echo "Pulling latest master..."
git pull origin master

# Merge dev into master
echo "Merging dev into master..."
git merge dev --no-ff -m "chore: merge dev to master for release $STABLE_VERSION"

# Push to master
echo "Pushing to master..."
git push origin master

echo ""
echo "✓ Successfully merged dev to master!"
echo ""
echo "The release workflow will now:"
echo "  1. Remove -beta suffix from version"
echo "  2. Create GitHub release"
echo "  3. Publish to NuGet and NPM"
echo ""
echo "Monitor the workflow at: https://github.com/rhom6us/winforms-mcp/actions"
echo ""
