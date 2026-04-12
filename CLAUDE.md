# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Rhombus.WinFormsMcp** is a Model Context Protocol (MCP) server that provides headless automation for WinForms applications using FlaUI with the UIA2 backend. The project structure follows the renamed pattern from `fnWindowsMCP` to `Rhombus.WinFormsMcp`.

## Build Commands

```bash
# Build the solution
dotnet build Rhombus.WinFormsMcp.sln

# Build for release
dotnet build Rhombus.WinFormsMcp.sln --configuration Release

# Restore dependencies
dotnet restore Rhombus.WinFormsMcp.sln

# Publish the server
dotnet publish src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release -o publish
```

## Test Commands

```bash
# Run all tests
dotnet test Rhombus.WinFormsMcp.sln

# Run tests with detailed output
dotnet test Rhombus.WinFormsMcp.sln --logger "console;verbosity=detailed"

# Run tests with code coverage
dotnet test Rhombus.WinFormsMcp.sln --collect:"XPlat Code Coverage"

# Run tests for release configuration
dotnet test Rhombus.WinFormsMcp.sln --configuration Release --no-build
```

## Run Commands

```bash
# Run the MCP server
dotnet run --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj

# Run the test WinForms application
dotnet run --project src/Rhombus.WinFormsMcp.TestApp/Rhombus.WinFormsMcp.TestApp.csproj
```

## Package Commands

```bash
# Create NuGet package (auto-generated on build)
dotnet build src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release

# Pack explicitly
dotnet pack src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj -c Release
```

## Architecture

### Core Components

1. **Rhombus.WinFormsMcp.Server** (src/Rhombus.WinFormsMcp.Server/)
   - `Program.cs`: MCP server implementation with JSON-RPC 2.0 over stdio transport. Contains 33 tool implementations and SessionManager for element caching.
   - `Automation/AutomationHelper.cs`: Core FlaUI wrapper with 40+ automation methods. Provides process management, element discovery, UI interaction, validation, window management, clipboard, and event capabilities.

2. **Rhombus.WinFormsMcp.TestApp** (src/Rhombus.WinFormsMcp.TestApp/)
   - Sample WinForms application with various controls for testing automation capabilities.

3. **Rhombus.WinFormsMcp.Tests** (tests/Rhombus.WinFormsMcp.Tests/)
   - NUnit test suite covering AutomationHelper functionality, process lifecycle, element operations, and resource cleanup.

### Key Technical Decisions

- **Framework**: .NET 8.0 Windows-specific (net8.0-windows) for WinForms compatibility
- **UI Automation**: FlaUI 4.0.0 with UIA2 backend for maximum WinForms compatibility without visual requirements
- **Testing**: NUnit 3.14.0 with Moq for mocking
- **Protocol**: MCP with stdio transport, single-line JSON-RPC 2.0 messages
- **Package Distribution**: NuGet (Rhombus.WinFormsMcp), NPM (@fnrhombus/winforms-mcp)

### Code Organization

**File-per-class rule:** One public class per file is strongly preferred. Multiple classes in a single file are only acceptable when:
- The classes are tightly coupled (e.g., a public class and its direct helper)
- The types are single-use (e.g., enums, markers, small value objects used nowhere else)

**Avoid monolithic files.** Keep files focused and navigable. If a file grows to contain multiple unrelated public classes or becomes difficult to navigate, it should be split.

### MCP Tools Available

The server implements 33 tools via JSON-RPC:
- Process Management: `launch_app`, `attach_to_process`, `close_app`, `get_process_status`
- Element Discovery: `find_element`, `find_elements`, `element_exists`, `wait_for_element`, `get_element_tree`
- UI Interaction: `click_element`, `type_text`, `set_value`, `drag_drop`, `send_keys`, `select_item`, `click_menu_item`, `toggle_element`
- Property & State: `get_property`, `wait_for_condition`, `get_focused_element`
- Data Controls: `scroll_element`, `get_table_data`, `set_table_cell`
- Window Management: `manage_window`, `list_windows`
- Events: `raise_event`, `listen_for_event`, `open_context_menu`
- Visual: `take_screenshot`, `render_form`
- Clipboard & Misc: `get_clipboard`, `set_clipboard`, `read_tooltip`

### Session Management

The server maintains session state across tool calls:
- Cached automation elements with generated IDs (elem_1, elem_2, etc.)
- Active AutomationHelper instance
- Process tracking with PIDs
- Per-process desktop routing (hidden vs default desktop)
- Native process handles for exit code access (CreateProcess-launched processes)

### Headless Mode (Hidden Desktop)

When `HEADLESS=true`, the server creates a hidden desktop via `CreateDesktop("McpAutomation")` within `WinSta0`. Key implementation details:

- **Process launch:** Uses `CreateProcess` P/Invoke with `STARTUPINFO.lpDesktop = "WinSta0\\McpAutomation"` (cannot use `Process.Start`)
- **Element discovery:** Requires `SetThreadDesktop(hDesktop)` before FlaUI/UIA calls; handled transparently by `OnProcessDesktop()`
- **Screenshots:** Uses `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` — flag 2, not flag 0 (flag 0 returns blank on hidden desktops)
- **Interaction:** Uses UIA patterns (InvokePattern, ValuePattern, etc.) instead of input simulation (SendKeys/mouse)
- **`send_keys`/`drag_drop`:** Only work on the default desktop (input simulation targets the active desktop's input queue)
- **Mixed sessions:** `attach_to_process` always uses the default desktop; `launch_app` uses the hidden desktop. Desktop routing is per-process, per-call.

See `docs/HEADLESS_MODE.md` for the full technical reference aimed at AI agent consumers.

### Error Handling

- All operations wrapped in try-catch blocks
- Default timeout: 5000ms for find operations, 10000ms for async waits
- Retry mechanism: 100ms intervals for element discovery
- Resource cleanup via IDisposable pattern

## Git Workflow

This project follows a **dev/master branching strategy** with **Semantic Versioning (SemVer)** according to https://semver.org/.

### Git Hooks (Auto-Configured)

Git hooks are automatically configured on first `dotnet build` or `dotnet test`. Two hooks enforce project standards:
- **commit-msg**: Validates commits follow [Conventional Commits](https://www.conventionalcommits.org/) format (`feat:`, `fix:`, etc.). Rejects non-compliant messages.
- **pre-commit**: Runs `dotnet format` before each commit to ensure code style compliance. Rejects commits with style violations.

No manual setup required — hooks are configured automatically by `Directory.Build.props` on restore.

### Root Checkout

**NEVER switch the branch of the root working directory.** The root checkout must stay on `dev` at all times. All feature/fix branch work must be done in git worktrees (`git worktree add` or `isolation: "worktree"` for agents). Switching the root checkout disrupts the working environment and risks losing uncommitted state.

**Before creating a worktree**, always pull the latest changes: `git pull` — worktrees are created from the current HEAD, and an outdated dev branch will create outdated worktrees.

### Branch Strategy

- **dev** - Integration branch
  - Receives merges from feature branches via PR
  - **No meaningful work directly on dev** — only trivial changes that don't have a GitHub issue
  - Triggers beta releases on push

- **master** - Stable release branch
  - Only receives merges from dev
  - Protected: no direct commits allowed
  - Triggers stable releases on push
  - Requires passing CI from dev before merge

- **feature/** - Feature branches (required for all issues)
  - **All bugs and features must have a GitHub issue first**
  - Create from dev, work in a worktree, merge back to dev via PR
  - Branch naming: `feature/<issue-number>-short-description` or `fix/<issue-number>-short-description`
  - Use `isolation: "worktree"` when spawning agents for implementation work

### Issue Selection

When asked to pick an issue to work on, **skip** any issue with these labels:
- `blocked` — waiting on an external dependency
- `in progress` — already being worked on
- `wontfix` — intentionally declined
- `duplicate` — already covered by another issue
- `invalid` — not a real issue
- `question` — needs discussion, not implementation

Only pick issues that are open, unlabelled or labelled `enhancement`/`bug`/`good first issue`/`help wanted`.

### Development Workflow

1. **Write up the issue** in GitHub (bug or feature), with appropriate labels
2. **Label the issue `in progress`** when starting work (`gh issue edit N --add-label "in progress"`)
3. **Create a feature branch** from dev (e.g., `feature/42-add-widget`)
4. **Do all work in a worktree** on that branch
5. **Before opening a PR, sync with dev** — pull latest from dev into feature branch to catch conflicts early
6. **Open a PR** against dev, referencing the issue
7. **Merge the PR** into dev
8. **Immediately clean up the worktree and branch** — mandatory, before any other work

```bash
# Feature development (always via worktree + PR)
git checkout dev
git checkout -b feature/42-add-widget
# ... make changes in worktree ...
git push -u origin feature/42-add-widget

# Before opening PR, sync with latest dev (in feature branch)
git fetch origin
git rebase origin/dev
git push -f origin feature/42-add-widget

gh pr create --base dev --title "feat: add widget" --body "Closes #42"

# After PR is merged — IMMEDIATELY clean up (GitHub auto-deletes remote branch)
git checkout dev && git pull
git branch -d feature/42-add-widget
git worktree remove <worktree-path> --force

# Verify cleanup is complete
git worktree list  # Should only show root checkout
git branch         # Should not contain worktree-* or feature/42-* branches

# Release to production (when ready)
./scripts/merge-to-master.ps1  # PowerShell
# OR
./scripts/merge-to-master.sh   # Bash
```

**MANDATORY CLEANUP:** After every PR merge, delete the local branch and worktree immediately — before moving to the next task. GitHub auto-deletes the remote branch, but you must handle the local cleanup. This is non-negotiable.

**CRITICAL: Stale worktrees prevent branch switching and clutter the repo. Clean up immediately after every merge — no exceptions.**

## Version Management

Versions follow **Semantic Versioning (SemVer)**:
- **MAJOR**: Breaking changes, incompatible API changes
- **MINOR**: New functionality, backwards compatible
- **PATCH**: Bug fixes, backwards compatible

### Version Bumping

- **Dev branch**: GitHub Actions analyzes commit messages (conventional commits) to determine bump type
  - `BREAKING CHANGE:` in commit → `major` bump
  - `feat:` prefix → `minor` bump
  - Default → `patch` bump
  - Versions have `-beta` suffix (e.g., 1.2.3-beta)
  - Auto-bumped on every push to dev via `.github/workflows/release-beta.yml`

- **Master branch**: Version comes from dev, `-beta` suffix removed
  - Creates stable release (e.g., 1.2.3)
  - Published to NuGet and NPM as stable

Version stored in three places (auto-synced by CI):
1. `VERSION` file (source of truth)
2. `npm/package.json`
3. `src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj`

## Code Coverage

### Expectations

Target meaningful coverage of critical paths, not a percentage. What "covered" means:

- **Core business logic** (renderer, process pool, TFM mapping) — must have tests
- **Protocol/integration tests** (MCP handshake, tool discovery, render_form E2E) — must exist
- **Shared utilities** (FormRenderingHelpers, etc.) — must have basic tests
- **Edge cases for user-facing features** (unknown control types, malformed input) — should be covered
- **Tests run in CI and block merges on failure** — required

When writing tests: keep them lean and fast. ~10 cases per utility class is plenty. Don't chase edge cases that can't happen in practice.

### Running Coverage Locally

```bash
# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Generate HTML report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:./coverage/**/coverage.cobertura.xml -targetdir:./coverage/report -reporttypes:Html

# Open report
start ./coverage/report/index.html  # Windows
open ./coverage/report/index.html   # Mac/Linux
```

## CI/CD

### Dev Branch CI (.github/workflows/release-beta.yml)
Triggers on push to `dev` branch:
1. Build and test
2. Analyze commits using conventional commit prefixes to determine version bump type (`BREAKING CHANGE:` → major, `feat:` → minor, else → patch)
3. Increment version with `-beta` suffix
4. Publish beta to NuGet and NPM
5. Commit version bump back to dev

### Master Branch CI (.github/workflows/release-stable.yml)
Triggers on push to `master` branch (merge from dev):
1. Remove `-beta` suffix from version
2. Build and test
3. Generate changelog from git log (grouped by conventional commit type)
4. Create GitHub release with auto-generated changelog
5. Publish stable version to NuGet and NPM
6. Tag with version number

### Merge Script Usage

```powershell
# PowerShell (Windows)
./scripts/merge-to-master.ps1          # Interactive, with CI check
./scripts/merge-to-master.ps1 -Force   # Skip CI check (not recommended)
./scripts/merge-to-master.ps1 -DryRun  # Preview without executing
```

```bash
# Bash (Mac/Linux/WSL)
./scripts/merge-to-master.sh           # Interactive, with CI check
./scripts/merge-to-master.sh --force   # Skip CI check (not recommended)
./scripts/merge-to-master.sh --dry-run # Preview without executing
```

The merge script:
- Verifies dev branch CI is passing
- Confirms version number
- Merges dev to master
- Pushes to trigger release workflow

## Commit Guidelines

**Commit messages must follow [Conventional Commits](https://www.conventionalcommits.org/) format.** The GitHub Actions version bumping script parses these prefixes to auto-determine version bumps. Correct format is required for version management to work.

- `feat:` - New features → MINOR bump
- `fix:` - Bug fixes → PATCH bump
- `BREAKING CHANGE:` in message body - Breaking API changes → MAJOR bump
- `chore:` - Maintenance tasks, non-functional changes
- `docs:` - Documentation changes (no version bump)
- `test:` - Test additions/changes (no version bump)
- `refactor:`, `style:`, `perf:` - Non-functional changes (no version bump)

### Issue References

**Always reference relevant GitHub issues in commit messages.** If a commit relates to an open issue, include `#N` in the message body. Use closing keywords when the commit fully resolves the issue:
- `fixes #N` or `closes #N` — auto-closes the issue when merged to the default branch
- `refs #N` or just `#N` — links without closing

This enables automated changelog generation and traceability.

## Release Changelog

**Every release must include a changelog.** This is automated in `release-stable.yml`:
- Scans all commits since the last release tag
- Groups by conventional commit type (features, fixes, docs, other)
- Writes the changelog as the GitHub Release body (not a separate file)
- Issue references (`#N`) in commit messages become clickable links automatically

This is why issue references in commits matter — they flow into the release notes for free.

## Querying Microsoft Documentation

You have access to MCP tools called `microsoft_docs_search`, `microsoft_docs_fetch`, and `microsoft_code_sample_search` — these tools allow you to search through and fetch Microsoft's latest official documentation and code samples, and that information might be more detailed or newer than what's in your training data set.

When handling questions around how to work with native Microsoft technologies, such as C#, F#, ASP.NET Core, Microsoft.Extensions, NuGet, Entity Framework, the `dotnet` runtime — please use these tools for research purposes when dealing with specific / narrowly defined questions that may occur.

## Plugin Repo

The Claude Code plugin is in a separate repo: [`fnrhombus/winforms-mcp-plugin`](https://github.com/fnrhombus/winforms-mcp-plugin). The plugin uses `npx -y @fnrhombus/winforms-mcp` which always fetches the latest version at runtime, so nothing in the plugin repo needs updating on release. **README syncing is manual** — when this repo's README changes, the plugin repo's README must be updated to match (same content, but with `/plugin install` instead of MCP JSON config, and relative doc links replaced with absolute URLs to this repo). See the plugin repo's CLAUDE.md for the exact diff pattern.

## Important Notes

1. The project was renamed from `fnWindowsMCP` to `Rhombus.WinFormsMcp` - all namespaces and project references use the new naming.
2. Git status shows multiple deleted files from the old `fnWindowsMCP` structure - these deletions represent the rename refactoring.
3. The solution file is `Rhombus.WinFormsMcp.sln` (not fnWindowsMCP.sln).
4. NuGet package ID is `Rhombus.WinFormsMcp`, NPM package is `@fnrhombus/winforms-mcp`.
5. All project files use the `Rhombus.WinFormsMcp` namespace prefix.