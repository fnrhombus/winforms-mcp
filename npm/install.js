/**
 * Post-install script for @fnrhombus/winforms-mcp
 *
 * This script ensures the .NET executable binaries are available.
 * In a published package, binaries are included in the dist/ directory.
 * During local development, this script can optionally download them.
 */

const fs = require('fs');
const path = require('path');

const distDir = path.join(__dirname, 'dist');
const exePath = path.join(distDir, 'winformsmcp.exe');

// Check if dist directory exists and has binaries
if (fs.existsSync(distDir)) {
  const files = fs.readdirSync(distDir);
  if (files.length > 0) {
    console.log('✓ Binaries found in dist/ directory');
    process.exit(0);
  }
}

// If no binaries found, this is expected during development
// In CI/CD, binaries will be copied to dist/ during the publish workflow
console.log('Note: Binaries will be included during package publishing');
process.exit(0);
