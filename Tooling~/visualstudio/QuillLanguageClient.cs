// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace Quill.VisualStudio
{
    /// <summary>The .quill content type: TextMate-coloured (CodeRemote base) and served by the LSP client.</summary>
    public static class QuillContentDefinition
    {
        [Export]
        [Name("quill")]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        internal static ContentTypeDefinition QuillContentType;

        [Export]
        [FileExtension(".quill")]
        [ContentType("quill")]
        internal static FileExtensionToContentTypeDefinition QuillFileExtension;
    }

    /// <summary>Starts the Quill language server (dotnet Quill.LanguageServer.dll) for .quill files.</summary>
    [ContentType("quill")]
    [Export(typeof(ILanguageClient))]
    [RunOnContext(RunningContext.RunOnHost)]
    public sealed class QuillLanguageClient : ILanguageClient
    {
        public string Name => "Quill";
        public IEnumerable<string> ConfigurationSections => null;
        public object InitializationOptions => null;
        public IEnumerable<string> FilesToWatch => new[] { "**/*.quill" };
        public bool ShowNotificationOnInitializeFailed => true;

        public event AsyncEventHandler<EventArgs> StartAsync;
#pragma warning disable CS0067   // raised by Visual Studio's client infrastructure, not by us
        public event AsyncEventHandler<EventArgs> StopAsync;
#pragma warning restore CS0067

        public Task<Connection> ActivateAsync(CancellationToken token)
        {
            string dir = Path.GetDirectoryName(typeof(QuillLanguageClient).Assembly.Location);
            string server = Path.Combine(dir, "server", "Quill.LanguageServer.dll");
            string dotnet = Environment.GetEnvironmentVariable("QUILL_DOTNET");
            if (string.IsNullOrEmpty(dotnet)) dotnet = "dotnet";

            var info = new ProcessStartInfo(dotnet, "\"" + server + "\"")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = new Process { StartInfo = info };
            if (!process.Start()) return Task.FromResult<Connection>(null);
            process.BeginErrorReadLine();   // server logs go to stderr; keep the pipe drained
            return Task.FromResult(new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream));
        }

        public async Task OnLoadedAsync()
        {
            if (StartAsync != null) await StartAsync.InvokeAsync(this, EventArgs.Empty);
        }

        public Task OnServerInitializedAsync() => Task.CompletedTask;

        public Task<InitializationFailureContext> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
            => Task.FromResult(new InitializationFailureContext
            {
                FailureMessage = "Quill: the language server failed to start. It needs a .NET 8 (or later) runtime on PATH "
                               + "(or set the QUILL_DOTNET environment variable to a dotnet executable). "
                               + initializationState?.InitializationException?.Message,
            });
    }
}
