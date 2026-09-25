#!/usr/bin/env bash
# Builds the Quill editor tooling into dist/:
#   dist/server/                  the language server (run with: dotnet Quill.LanguageServer.dll)
#   dist/quill-<version>.vsix     the VS Code extension (server included)
# Needs the .NET 8 SDK and Node.js 18+. The Visual Studio extension builds on Windows: see
# visualstudio/README.md.
set -euo pipefail
cd "$(dirname "$0")"

rm -rf dist/server
dotnet publish LanguageServer/Quill.LanguageServer.csproj -c Release -o dist/server --nologo -p:DebugType=none
dotnet dist/server/Quill.LanguageServer.dll --schema-check

rm -rf vscode/server && mkdir -p vscode/server
cp dist/server/Quill.LanguageServer.dll dist/server/Quill.LanguageServer.runtimeconfig.json dist/server/Quill.LanguageServer.deps.json vscode/server/

cd vscode
npm ci --no-audit --no-fund
npm run typecheck
npm run build
npm run package
