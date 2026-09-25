# Quill in JetBrains Rider

Rider gets Quill support from two standard pieces, no Quill-specific plugin needed:
**TextMate** for highlighting and **LSP4IJ** (a free JetBrains Marketplace plugin) to run the Quill
language server for diagnostics, completion, hover and go to definition.

## 1. Highlighting (TextMate bundle)

1. **Settings ▸ Editor ▸ TextMate Bundles**, click **+**.
2. Pick the `Tooling~/vscode` folder of the Quill package (in an embedded package:
   `Packages/Quill/Tooling~/vscode`; otherwise `Library/PackageCache/com.leochaumartin.quill@…/Tooling~/vscode`).
   Rider reads the VS Code extension's `package.json` and registers `.quill` with its grammar.

## 2. Language server (LSP4IJ)

1. **Settings ▸ Plugins ▸ Marketplace**: install **LSP4IJ** (by Red Hat), restart.
2. **Settings ▸ Languages & Frameworks ▸ Language Servers**, click **+** (New Language Server):
   - **Server** tab — Name: `Quill`; Command:
     `dotnet "<package>/Tooling~/dist/server/Quill.LanguageServer.dll"`
     (use the full path; on Windows, `dotnet.exe` if `dotnet` isn't on PATH).
   - **Mappings** tab ▸ **File name patterns**: add `*.quill`, Language Id `quill`.
3. Open a `.quill` file: the server starts (status in the **Language Servers** tool window).

Requires a .NET 8 (or later) runtime — Rider users usually have the SDK already.

## Unity side

The Quill package adds `quill` to **Project Settings ▸ Editor ▸ Additional extensions to include**,
so `.quill` files appear in the solution Rider opens from Unity, and double-clicking one in Unity
opens it in Rider.
