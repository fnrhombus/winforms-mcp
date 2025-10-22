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
   - `Program.cs`: MCP server implementation with JSON-RPC 2.0 over stdio transport. Contains 14 tool implementations and SessionManager for element caching.
   - `Automation/AutomationHelper.cs`: Core FlaUI wrapper with 25+ automation methods. Provides process management, element discovery, UI interaction, and validation capabilities.

2. **Rhombus.WinFormsMcp.TestApp** (src/Rhombus.WinFormsMcp.TestApp/)
   - Sample WinForms application with various controls for testing automation capabilities.

3. **Rhombus.WinFormsMcp.Tests** (tests/Rhombus.WinFormsMcp.Tests/)
   - NUnit test suite covering AutomationHelper functionality, process lifecycle, element operations, and resource cleanup.

### Key Technical Decisions

- **Framework**: .NET 8.0 Windows-specific (net8.0-windows) for WinForms compatibility
- **UI Automation**: FlaUI 4.0.0 with UIA2 backend for maximum WinForms compatibility without visual requirements
- **Testing**: NUnit 3.14.0 with Moq for mocking
- **Protocol**: MCP with stdio transport, single-line JSON-RPC 2.0 messages
- **Package Distribution**: NuGet (Rhombus.WinFormsMcp), NPM (@rhom6us/winforms-mcp)

### MCP Tools Available

The server implements 14 tools via JSON-RPC:
- Process Management: `launch_app`, `attach_to_process`, `close_app`
- Element Discovery: `find_element`, `element_exists`, `wait_for_element`
- UI Interaction: `click_element`, `type_text`, `set_value`, `drag_drop`, `send_keys`
- Property Access: `get_property`
- Validation: `take_screenshot`
- Future/Placeholder: `raise_event`, `listen_for_event` (not yet implemented)

### Session Management

The server maintains session state across tool calls:
- Cached automation elements with generated IDs (elem_1, elem_2, etc.)
- Active AutomationHelper instance
- Process tracking with PIDs

### Error Handling

- All operations wrapped in try-catch blocks
- Default timeout: 5000ms for find operations, 10000ms for async waits
- Retry mechanism: 100ms intervals for element discovery
- Resource cleanup via IDisposable pattern

## Git Workflow

This project follows a **dev/master branching strategy** with **Semantic Versioning (SemVer)** according to https://semver.org/.

### Branch Strategy

- **dev** - Development branch for active work
  - All feature development happens here
  - Commit after each completed task
  - Push after each completed feature
  - Triggers beta releases on push

- **master** - Stable release branch
  - Only receives merges from dev
  - Protected: no direct commits allowed
  - Triggers stable releases on push
  - Requires passing CI from dev before merge

- **feature/** - Optional feature branches
  - Use when work spans multiple pushes
  - Create from dev, merge back to dev
  - Prevents premature beta releases

### Workflow Commands

```bash
# Daily development on dev branch
git checkout dev
# ... make changes ...
git add .
git commit -m "feat: add new feature"  # Commit after task completion
git push  # Push after feature completion - triggers beta release

# For multi-push work, use feature branches
git checkout -b feature/my-feature
# ... make multiple commits and pushes ...
git checkout dev
git merge feature/my-feature
git push  # Single beta release when feature is done

# Release to production (when ready)
./scripts/merge-to-master.ps1  # PowerShell
# OR
./scripts/merge-to-master.sh   # Bash
```

## Version Management

Versions follow **Semantic Versioning (SemVer)**:
- **MAJOR**: Breaking changes, incompatible API changes
- **MINOR**: New functionality, backwards compatible
- **PATCH**: Bug fixes, backwards compatible

### Version Bumping

- **Dev branch**: Claude Haiku AI agent analyzes commits to determine bump type (major/minor/patch)
  - Versions have `-beta` suffix (e.g., 1.2.3-beta)
  - Auto-incremented on every push to dev

- **Master branch**: Version comes from dev, `-beta` suffix removed
  - Creates stable release (e.g., 1.2.3)
  - Published to NuGet and NPM as stable

Version stored in three places (auto-synced by CI):
1. `VERSION` file (source of truth)
2. `npm/package.json`
3. `src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj`

## Code Coverage Requirements

**100% code coverage is required** for all pushes to dev branch.

### Coverage Exceptions

Lines that cannot be tested must have a coverage exception comment:

```csharp
// COVERAGE_EXCEPTION: Platform-specific code only runs on Windows Server 2019+
if (Environment.OSVersion.Version.Major >= 10) {
    // This line is excluded from coverage requirements
}
```

Place the `// COVERAGE_EXCEPTION: <reason>` comment either:
- On the same line as the code
- On the line immediately before the code

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

### Dev Branch CI (.github/workflows/ci-dev.yml)
Triggers on push to `dev` branch:
1. Build and test
2. Check 100% code coverage (with exception support)
3. Analyze commits with Claude Haiku to determine version bump type
4. Increment version with `-beta` suffix
5. Publish beta to NuGet and NPM
6. Commit version bump back to dev

### Master Branch CI (.github/workflows/ci-master.yml)
Triggers on push to `master` branch (merge from dev):
1. Remove `-beta` suffix from version
2. Build and test
3. Create GitHub release with release notes
4. Publish stable version to NuGet and NPM
5. Tag with version number

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

While not strictly enforced, following conventional commits helps the AI version analyzer:

- `feat:` - New features (likely MINOR bump)
- `fix:` - Bug fixes (likely PATCH bump)
- `BREAKING CHANGE:` - Breaking changes (likely MAJOR bump)
- `chore:` - Maintenance tasks
- `docs:` - Documentation changes
- `test:` - Test additions/changes

## Important Notes

1. The project was renamed from `fnWindowsMCP` to `Rhombus.WinFormsMcp` - all namespaces and project references use the new naming.
2. Git status shows multiple deleted files from the old `fnWindowsMCP` structure - these deletions represent the rename refactoring.
3. The solution file is `Rhombus.WinFormsMcp.sln` (not fnWindowsMCP.sln).
4. NuGet package ID is `Rhombus.WinFormsMcp`, NPM package is `@rhom6us/winforms-mcp`.
5. All project files use the `Rhombus.WinFormsMcp` namespace prefix.