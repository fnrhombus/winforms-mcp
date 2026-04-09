# GitHub Setup Instructions

## Step 1: Create the GitHub Repository

1. Go to [GitHub](https://github.com/new)
2. Create a new public repository with these settings:
   - **Repository name:** `winforms-mcp`
   - **Owner:** `fnrhombus`
   - **Description:** WinForms automation MCP server with headless UI automation
   - **Public/Private:** Public
   - **Do NOT initialize with README, .gitignore, or License** (we have these already)

3. Click "Create repository"

## Step 2: Push the Local Repository

After creating the repo on GitHub, run:

```bash
cd /c/dev/WinFormsMcp
git remote add origin https://github.com/fnrhombus/winforms-mcp.git
git branch -M master
git push -u origin master
```

If prompted for credentials, use:
- **Username:** Your GitHub username
- **Password:** A Personal Access Token (PAT) from https://github.com/settings/tokens
  - Requires `repo` scope (full control of private repositories)

## Step 3: Configure NuGet Trusted Publishing

NuGet uses trusted publishing with GitHub OIDC tokens instead of long-lived API keys (more secure!).

1. Go to https://www.nuget.org/account/Trusted
2. Click "Add new policy"
3. Enter the following trusted policy details:
   - **Repository Owner:** `fnrhombus`
   - **Repository:** `winforms-mcp`
   - **Workflow File:** `publish.yml`
   - **Environment:** (leave empty - not using GitHub environments)
4. Click "Create"

The policy will then be temporarily active for 7 days. This is normal for new policies.

## Step 4: Add GitHub Secrets for Publishing

Go to **Repository Settings → Secrets and variables → Actions** and add these secrets:

### Required Secrets

1. **NPM_TOKEN**
   - Obtain from: https://www.npmjs.com/settings/fnrhombus/tokens
   - Create automation token (read and write)
   - Must have publish permissions for `@fnrhombus` scope
   - Secret value: The full token with `npm_` prefix

**NOTE:** NuGet API key is NOT needed! The workflow uses GitHub OIDC tokens for secure, keyless publishing.

## Step 5: Configure Branch Protection (Optional but Recommended)

Go to **Repository Settings → Branches** and create a rule for `master`:

1. Click "Add rule"
2. Branch name pattern: `master`
3. Configure protection:
   - ✅ **Require a pull request before merging**
     - Require approvals: 1
     - Require review from code owners
   - ✅ **Require status checks to pass before merging**
     - Require branches to be up to date before merging
     - Select the "CI" status check
   - ✅ **Restrict who can push to matching branches**
     - Include administrators
4. Click "Create"

## Step 6: Trigger the Initial Workflow

Create and push the first tag to trigger the publish workflow:

```bash
cd /c/dev/WinFormsMcp
git tag v1.0.0
git push origin v1.0.0
```

This will:
- Run the CI workflow (build and test)
- Run the publish workflow which will:
  - Publish to NuGet.org as `Rhombus.WinFormsMcp` (using OIDC trusted publishing)
  - Publish to NPM as `@fnrhombus/winforms-mcp`
  - Create a GitHub Release with release notes

## Workflow Triggers

### CI Workflow (`ci.yml`)
Triggers on:
- Any push to `master` or `main` branch
- Any pull request to `master` or `main` branch

What it does:
- Builds the solution on Windows
- Runs 75+ tests
- Uploads test coverage

### Publish Workflow (`publish.yml`)
Triggers on:
- Push to `master` or `main` branch (uses VERSION file)
- Push of version tags like `v1.0.0`, `v2.0.0`, etc.
- Manual workflow dispatch

What it does (if secrets are configured):
- Publishes to NuGet.org (using OIDC tokens)
- Publishes to NPM Registry
- Creates GitHub Release with binaries

## Publishing a New Version

### Minor Update (Patch Release)

1. Update `VERSION` file:
   ```
   1.0.1
   ```

2. Update `CHANGELOG.md` with changes

3. Commit and push:
   ```bash
   git add VERSION CHANGELOG.md
   git commit -m "chore: bump version to 1.0.1"
   git push origin master
   ```

4. The publish workflow will automatically publish to all platforms

### Major or Minor Release

Use tags for semantic versioning:

```bash
# Update version
echo "1.1.0" > VERSION

# Update CHANGELOG.md
git add VERSION CHANGELOG.md
git commit -m "chore: release version 1.1.0"

# Create and push tag
git tag v1.1.0
git push origin master
git push origin v1.1.0

# Publish workflow triggers automatically
```

## Package Installation

### NuGet
```
Install-Package Rhombus.WinFormsMcp -Version 1.0.0
```

### NPM
```bash
npm install @fnrhombus/winforms-mcp@1.0.0
npx @fnrhombus/winforms-mcp
```

### Direct Download
Download from: https://github.com/fnrhombus/winforms-mcp/releases

## Troubleshooting

### Workflow Fails with "No matching trust policy found"
- Ensure you've created a NuGet trusted publishing policy at https://www.nuget.org/account/Trusted
- Policy details must match exactly:
  - Repository Owner: `fnrhombus`
  - Repository: `winforms-mcp`
  - Workflow File: `publish.yml` (file name only, no path)
- Wait 7 days for permanent activation, or temporarily active policies work immediately

### Workflow Fails with "Repository not found"
- Check that the repository URL is correct
- Ensure GitHub secrets are properly configured
- Check that the branch name is `master` (not `main`)

### NPM Publish Fails
- Verify NPM_TOKEN is set (should start with `npm_`)
- Ensure `@fnrhombus` scope exists on npmjs.com
- Check that you have publish permissions

## Security Notes

This project uses **NuGet Trusted Publishing** with GitHub OIDC tokens:
- ✅ No long-lived API keys to manage
- ✅ Automatic token rotation (1 hour expiry)
- ✅ Tokens include cryptographic proof of the workflow
- ✅ Token exchange happens securely with nuget.org
- ✅ Follows OpenSSF best practices for keyless publishing

This is much more secure than traditional API keys and requires zero secret rotation.

## Reference

- Repository: https://github.com/fnrhombus/winforms-mcp
- NuGet Package: https://www.nuget.org/packages/Rhombus.WinFormsMcp
- NPM Package: https://www.npmjs.com/package/@fnrhombus/winforms-mcp
- NuGet Trusted Publishing: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
