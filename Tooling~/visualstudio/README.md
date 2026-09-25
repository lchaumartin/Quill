# Quill for Visual Studio

A Visual Studio 2022+ (Windows) extension for `.quill` files: TextMate highlighting (the grammar
shared with VS Code) and the Quill language server through Visual Studio's LSP client —
diagnostics, completion, hover, go to definition.

## Build

On Windows, with the **Visual Studio extension development** workload installed:

1. Build the language server once: `dotnet publish ../LanguageServer/Quill.LanguageServer.csproj -c Release -o ../dist/server -p:DebugType=none`
   (or run `../build.sh` from Git Bash / WSL).
2. `msbuild /restore Quill.VisualStudio.csproj /p:Configuration=Release`
3. Double-click `bin/Release/Quill.VisualStudio.vsix` to install.

The repository's GitHub workflow (`.github/workflows/tooling.yml`) builds it on a Windows runner and
uploads the `.vsix` as an artifact.

## Requirements

A .NET 8 (or later) runtime on `PATH`, or set the `QUILL_DOTNET` environment variable to a `dotnet`
executable.
