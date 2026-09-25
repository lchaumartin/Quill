# Quill for Unity — VS Code

Language support for [Quill](https://github.com/leochaumartin/quill) `.quill` UI documents.

- **Highlighting** for elements, properties, handlers, ids, colours and expressions.
- **Diagnostics as you type**: syntax errors, unknown elements, properties, signals and names —
  with "did you mean" suggestions.
- **Completion**: elements and the project's components (theme controls included), each type's
  properties and `on…` handlers, ids and their members, the `Theme` palette (with colours), enums
  (`Easing.`, `Text.`, `Font.`…), `Math.` and `Qt.` functions.
- **Hover** docs for elements, components (their header comment), properties, signals and palette
  values in every theme.
- **Go to definition** for components, ids, properties and `Theme.*`; **outline** and **folding**.

The language server indexes every `.quill` file in the workspace — `Assets`, embedded packages and
`Library/PackageCache` — so open your Unity project folder.

## Requirements

A .NET 8 (or later) runtime, found on `PATH` or in the usual install locations (set
`quill.dotnetPath` otherwise). Highlighting works without it.

## Settings

| Setting | |
|---|---|
| `quill.dotnetPath` | The `dotnet` executable to run the server with. |
| `quill.server.path` | Use another `Quill.LanguageServer.dll` (e.g. one you built). |
| `quill.trace.server` | Log the LSP traffic to **Output ▸ Quill**. |
