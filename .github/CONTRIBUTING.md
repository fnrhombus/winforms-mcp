# Contributing to Rhombus.WinFormsMcp

Thanks for your interest in contributing! Here's how to get started.

## Development Setup

1. **Prerequisites**: Windows 10+, .NET 8.0 SDK
2. **Clone**: `git clone https://github.com/rhom6us/winforms-mcp.git`
3. **Build**: `dotnet build Rhombus.WinFormsMcp.sln`
4. **Test**: `dotnet test Rhombus.WinFormsMcp.sln`

See [CLAUDE.md](../CLAUDE.md) for the full list of build, test, and run commands.

## Branching

- **dev** — active development. Branch from here, PR back here.
- **master** — stable releases only. Never commit directly.
- **feature/** — optional, for multi-push work.

## Submitting Changes

1. Fork the repo and create a branch from `dev`
2. Make your changes with clear, focused commits
3. Ensure `dotnet test` passes
4. Open a PR against `dev`

### Commit Messages

We use [Conventional Commits](https://www.conventionalcommits.org/):

- `feat:` — new feature
- `fix:` — bug fix
- `docs:` — documentation
- `chore:` — maintenance
- `test:` — test additions/changes
- `BREAKING CHANGE:` — in the body for breaking changes

### Code Style

The repo has an `.editorconfig` that enforces style. Run `dotnet format` before committing if your editor doesn't apply it automatically.

## Reporting Issues

- **Bugs**: Use the [bug report template](https://github.com/rhom6us/winforms-mcp/issues/new?template=bug_report.yml)
- **Features**: Use the [feature request template](https://github.com/rhom6us/winforms-mcp/issues/new?template=feature_request.yml)
- **Security**: See [SECURITY.md](SECURITY.md)

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](../LICENSE).
