// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// VS Code client for the Quill language server. Highlighting and editing rules are declarative
// (package.json); this starts the server (a .NET program bundled in ./server) for everything else.

import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import { execFile } from 'child_process';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from 'vscode-languageclient/node';

let client: LanguageClient | undefined;
let output: vscode.OutputChannel | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    output = vscode.window.createOutputChannel('Quill');
    context.subscriptions.push(output);
    context.subscriptions.push(vscode.commands.registerCommand('quill.restartServer', async () => {
        await stop();
        await start(context);
    }));
    context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(async e => {
        if (e.affectsConfiguration('quill.dotnetPath') || e.affectsConfiguration('quill.server.path')) {
            await stop();
            await start(context);
        }
    }));
    await start(context);
}

export async function deactivate(): Promise<void> {
    await stop();
}

async function start(context: vscode.ExtensionContext): Promise<void> {
    const config = vscode.workspace.getConfiguration('quill');
    const serverDll = config.get<string>('server.path') || context.asAbsolutePath(path.join('server', 'Quill.LanguageServer.dll'));
    if (!fs.existsSync(serverDll)) {
        vscode.window.showErrorMessage(`Quill: language server not found at ${serverDll}.`);
        return;
    }

    const dotnet = await findDotnet(config.get<string>('dotnetPath') || '');
    if (!dotnet) {
        const pick = await vscode.window.showWarningMessage(
            'Quill: completion, diagnostics and go to definition need .NET 8 or later (highlighting works without it). ' +
            'Install .NET, or set "quill.dotnetPath".', 'Download .NET', 'Settings');
        if (pick === 'Download .NET') vscode.env.openExternal(vscode.Uri.parse('https://dotnet.microsoft.com/download'));
        if (pick === 'Settings') vscode.commands.executeCommand('workbench.action.openSettings', 'quill.dotnetPath');
        return;
    }
    output?.appendLine(`Using ${dotnet} to run ${serverDll}`);

    const serverOptions: ServerOptions = {
        run: { command: dotnet, args: [serverDll], transport: TransportKind.stdio },
        debug: { command: dotnet, args: [serverDll], transport: TransportKind.stdio },
    };
    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'quill' }],
        synchronize: { fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{quill,ui}') },
        outputChannel: output,
    };
    client = new LanguageClient('quill', 'Quill', serverOptions, clientOptions);
    try {
        await client.start();
    } catch (e) {
        output?.appendLine(`Failed to start the language server: ${e}`);
        vscode.window.showErrorMessage('Quill: the language server failed to start. See Output ▸ Quill.');
    }
}

async function stop(): Promise<void> {
    if (!client) return;
    try { await client.stop(); } catch { /* already gone */ }
    client = undefined;
}

// ---- Finding a .NET runtime ------------------------------------------------------------------------

async function findDotnet(configured: string): Promise<string | undefined> {
    const exe = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
    const candidates: string[] = [];
    if (configured) candidates.push(configured);
    if (process.env.DOTNET_ROOT) candidates.push(path.join(process.env.DOTNET_ROOT, exe));
    candidates.push(exe);   // on PATH
    if (process.platform === 'win32') {
        candidates.push(path.join(process.env['ProgramFiles'] || 'C:\\Program Files', 'dotnet', exe));
    } else {
        candidates.push('/usr/local/share/dotnet/dotnet', '/usr/local/bin/dotnet', '/opt/homebrew/bin/dotnet',
                        '/usr/share/dotnet/dotnet', '/usr/lib/dotnet/dotnet', '/snap/bin/dotnet');
    }
    candidates.push(path.join(os.homedir(), '.dotnet', exe));

    for (const c of candidates) {
        if (await hasRuntime(c)) return c;
    }

    // The .NET Install Tool extension (installed with the C# extension) can provide one.
    try {
        const result = await vscode.commands.executeCommand<{ dotnetPath: string }>('dotnet.acquire', {
            version: '8.0', requestingExtensionId: 'leochaumartin.quill',
        });
        if (result?.dotnetPath && await hasRuntime(result.dotnetPath)) return result.dotnetPath;
    } catch { /* not installed */ }
    return undefined;
}

/** True if this dotnet can run a .NET 8+ app. */
function hasRuntime(dotnet: string): Promise<boolean> {
    return new Promise(resolve => {
        execFile(dotnet, ['--list-runtimes'], { timeout: 10000 }, (err, stdout) => {
            if (err) return resolve(false);
            const ok = stdout.split('\n').some(line => {
                const m = /^Microsoft\.NETCore\.App (\d+)\./.exec(line.trim());
                return m !== null && parseInt(m[1], 10) >= 8;
            });
            resolve(ok);
        });
    });
}
