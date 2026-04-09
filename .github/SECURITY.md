# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in Rhombus.WinFormsMcp, please report it responsibly using [GitHub's private vulnerability reporting](https://github.com/rhom6us/winforms-mcp/security/advisories/new).

**Please do not open a public issue for security vulnerabilities.**

We will acknowledge your report within 48 hours and provide a timeline for a fix.

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
| < Latest | No       |

## Scope

This project runs as a local MCP server on the user's machine. Security concerns include:
- Arbitrary code execution via malicious MCP requests
- Path traversal in file-based operations (render_form, take_screenshot)
- Process injection via launch_app / attach_to_process
