#!/usr/bin/env node

/**
 * Rhombus.WinFormsMcp NPM Wrapper
 *
 * This module provides a Node.js wrapper for the .NET-based WinForms automation MCP server.
 * It handles downloading platform-specific binaries and exposing the executable for npx usage.
 */

const path = require('path');
const fs = require('fs');
const os = require('os');

// Verify we're on Windows
if (os.platform() !== 'win32') {
  console.error('Error: @rhom6us/winforms-mcp requires Windows (x64)');
  console.error(`Detected platform: ${os.platform()} ${os.arch()}`);
  process.exit(1);
}

// Verify we're on x64
if (os.arch() !== 'x64') {
  console.error('Error: @rhom6us/winforms-mcp requires Windows x64');
  console.error(`Detected architecture: ${os.arch()}`);
  process.exit(1);
}

const distDir = path.join(__dirname, 'dist');
const exePath = path.join(distDir, 'Rhombus.WinFormsMcp.Server.exe');

// Check if executable exists
if (!fs.existsSync(exePath)) {
  console.error('Error: Rhombus.WinFormsMcp.Server executable not found');
  console.error(`Expected at: ${exePath}`);
  console.error('\nTry reinstalling: npm install @rhom6us/winforms-mcp');
  process.exit(1);
}

// Export the executable path for use in other modules
module.exports = {
  executablePath: exePath,
  distDirectory: distDir,
};

// If called directly from command line, spawn the executable
if (require.main === module) {
  const { spawn } = require('child_process');
  const child = spawn(exePath, process.argv.slice(2), {
    stdio: 'inherit',
    windowsHide: true,
  });

  child.on('error', (err) => {
    console.error('Failed to start Rhombus.WinFormsMcp.Server:', err);
    process.exit(1);
  });

  child.on('exit', (code) => {
    process.exit(code);
  });
}
