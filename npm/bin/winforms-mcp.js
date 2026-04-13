#!/usr/bin/env node

const path = require('path');
const fs = require('fs');
const os = require('os');
const { spawn } = require('child_process');

if (os.platform() !== 'win32') {
  console.error('Error: @fnrhombus/winforms-mcp requires Windows (x64)');
  console.error(`Detected platform: ${os.platform()} ${os.arch()}`);
  process.exit(1);
}

if (os.arch() !== 'x64') {
  console.error('Error: @fnrhombus/winforms-mcp requires Windows x64');
  console.error(`Detected architecture: ${os.arch()}`);
  process.exit(1);
}

const distDir = path.join(__dirname, '..', 'dist');
const exePath = path.join(distDir, 'winformsmcp.exe');

if (!fs.existsSync(exePath)) {
  console.error('Error: winformsmcp executable not found');
  console.error(`Expected at: ${exePath}`);
  console.error('\nTry reinstalling: npm install @fnrhombus/winforms-mcp');
  process.exit(1);
}

const child = spawn(exePath, process.argv.slice(2), {
  stdio: 'inherit',
  windowsHide: true,
});

child.on('error', (err) => {
  console.error('Failed to start winformsmcp:', err);
  process.exit(1);
});

child.on('exit', (code) => {
  process.exit(code);
});
