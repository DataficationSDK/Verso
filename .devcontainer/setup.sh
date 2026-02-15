#!/usr/bin/env bash
set -euo pipefail

echo "Building Verso .NET solution..."
dotnet build Verso.sln --configuration Debug

echo "Building VS Code extension..."
cd vscode
npm install
npm run build
npm run package
cd ..

echo "Setup complete."
