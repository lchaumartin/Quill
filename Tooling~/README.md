# Quill editor tooling

Code-editor support for `.quill` documents. One **language server** does the work — diagnostics,
completion, hover, go to definition, outline, folding — and a thin client plugs it into each editor.

| Editor | How | Folder |
|---|---|---|
| **VS Code** | Install `dist/quill-0.1.0.vsix` (Extensions ▸ ⋯ ▸ Install from VSIX…) | `vscode/` |
| **Rider** | TextMate bundle + the LSP4IJ plugin — see `rider/README.md` | `rider/` |
| **Visual Studio 2022+** | Build the VSIX on Windows — see `visualstudio/README.md` | `visualstudio/` |

All three need a **.NET 8 (or later) runtime** to run the server (highlighting works without it).

## What you get

- **Highlighting** (a TextMate grammar shared by all three editors).
- **Diagnostics as you type**: syntax errors; unknown elements, properties, handlers (`onClickd`),
  names and members (`volume.valeu`, `Theme.acent`, `Easing.OutCubc`) with "did you mean"
  suggestions; duplicate ids; unknown property types.
- **Completion**: element types and the project's components (theme controls included, as
  snippets); the current object's properties and `on…` handlers; `anchors.` / `font.` / `border.`
  groups; ids and their members; the `Theme` palette (colours shown as swatches); enums (`Easing.`,
  `Text.`, `Font.`, `Qt.`…) and `Math.` / `Qt.` functions; locals and signal parameters in handlers.
  It keeps working while the file is mid-edit and doesn't parse.
- **Hover**: built-in element and property docs, a component's header comment and API, signal
  parameters, and a `Theme.x` value in every installed theme.
- **Go to definition**: component types (every theme's version), ids, a component's properties,
  functions and signals, and `Theme.x` in each `Theme.quill`.
- **Outline** and **folding**.

The server indexes every `.quill` file under the workspace: `Assets/`, embedded packages under
`Packages/`, and installed packages in `Library/PackageCache/` — so open the Unity project folder.

## Unity side

The package makes `.quill` files open in your external code editor on double-click (Project window
and Console), and adds `quill` to **Project Settings ▸ Editor ▸ Additional extensions to include** so
they appear in the generated solution (Rider / Visual Studio).

## Building

`./build.sh` (needs the .NET 8 SDK and Node.js 18+) builds `dist/server/` and the VS Code `.vsix`.
The GitHub workflow `.github/workflows/tooling.yml` also builds the Visual Studio `.vsix` on Windows.

Command-line check (CI, pre-commit): `dotnet dist/server/Quill.LanguageServer.dll --check <folder>`
prints diagnostics like a compiler and exits non-zero on errors or warnings.

## Layout

- `LanguageServer/` — the server (C#, no NuGet dependencies). It compiles the engine's own parser and
  element registry from `../Runtime`, so the editor and the runtime always agree on the language.
  `src/Schema.cs` documents the built-in elements; `--schema-check` verifies it covers every property
  the engine seeds.
- `vscode/` — the VS Code extension: grammar, editing rules, and the client.
- `visualstudio/` — the Visual Studio extension (MEF language client + the same grammar).
- `rider/` — setup notes.
- `dist/` — built server and VS Code extension.
