using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests;

/// <summary>
/// Covers how a client's answer to an input prompt reaches the cell that asked. A dismissed prompt
/// has to arrive as null, the same as end of input in a terminal and the same as the server host
/// gives, while interrupting the cell that is waiting has to stop it.
/// </summary>
[TestClass]
[DoNotParallelize] // Console.In is process-wide
public class InputResponseTests
{
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(30);

    private sealed class Notifications
    {
        private readonly object _gate = new();
        private readonly List<string> _sent = new();

        public void Add(string text)
        {
            lock (_gate)
                _sent.Add(text);
        }

        /// <summary>
        /// The request id of the first input prompt the client was sent, waiting for one to arrive
        /// because the cell asks from its own thread.
        /// </summary>
        public async Task<string> WaitForInputRequestAsync()
        {
            var deadline = DateTime.UtcNow + PromptTimeout;
            while (DateTime.UtcNow < deadline)
            {
                string[] snapshot;
                lock (_gate)
                    snapshot = _sent.ToArray();

                foreach (var text in snapshot)
                {
                    var root = JsonDocument.Parse(text).RootElement;
                    if (root.TryGetProperty("method", out var method)
                        && method.GetString() == MethodNames.InputRequest)
                    {
                        return root.GetProperty("params").GetProperty("requestId").GetString()!;
                    }
                }

                await Task.Delay(20);
            }

            Assert.Fail("The cell never asked for input.");
            return "";
        }
    }

    private static async Task<(HostSession Session, string NotebookId, Notifications Sent)> OpenAsync()
    {
        var sent = new Notifications();
        var session = new HostSession(sent.Add);

        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" }, JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);

        return (session, result.NotebookId, sent);
    }

    /// <summary>Sends the response the VS Code client sends when its input box is dismissed.</summary>
    private static Task<string> SendCancelledResponseAsync(
        HostSession session, string notebookId, string requestId)
    {
        var @params = JsonSerializer.SerializeToElement(new
        {
            notebookId,
            requestId,
            value = (string?)null,
            cancelled = true,
        });

        return session.DispatchAsync(1, MethodNames.InputResponse, @params);
    }

    private static Task<ExecutionResultDto> RunAsync(NotebookSession notebook, Guid cellId)
    {
        var @params = JsonSerializer.SerializeToElement(
            new ExecutionRunParams { CellId = cellId.ToString() }, JsonRpcMessage.SerializerOptions);

        return ExecutionHandler.HandleRunAsync(notebook, @params);
    }

    [TestMethod]
    public async Task ACancelledResponseAnswersTheRequestWithNull()
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            var request = session.GetSession(notebookId)
                .RequestInputAsync(Guid.NewGuid(), "Name:", false, CancellationToken.None);

            var requestId = await sent.WaitForInputRequestAsync();
            await SendCancelledResponseAsync(session, notebookId, requestId);

            Assert.IsNull(await request);
        }
    }

    [TestMethod]
    public async Task CancellingTheCallerStillCancelsTheRequest()
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            using var cts = new CancellationTokenSource();
            var request = session.GetSession(notebookId)
                .RequestInputAsync(Guid.NewGuid(), "Name:", false, cts.Token);

            await sent.WaitForInputRequestAsync();
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => request);
        }
    }

    [DataTestMethod]
    [DataRow("Console.ReadLine() is null")]
    [DataRow("await GetInputAsync(\"User:\") is null")]
    [DataRow("await GetPasswordAsync(\"Password:\") is null")]
    public async Task ADismissedPromptGivesTheCSharpCellNull(string code)
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            var notebook = session.GetSession(notebookId);
            var cell = notebook.Scaffold.AddCell("code", "csharp", code);

            var run = RunAsync(notebook, cell.Id);
            var requestId = await sent.WaitForInputRequestAsync();
            await SendCancelledResponseAsync(session, notebookId, requestId);
            var result = await run;

            Assert.AreEqual("completed", result.Status, result.ErrorMessage);
            Assert.AreEqual("True", result.Outputs[^1].Content);
        }
    }

    [TestMethod]
    public async Task InterruptingACellWaitingForInputStopsIt()
    {
        var (session, notebookId, sent) = await OpenAsync();
        await using (session)
        {
            var notebook = session.GetSession(notebookId);
            var cell = notebook.Scaffold.AddCell(
                "code", "csharp", "var line = Console.ReadLine();\nConsole.WriteLine(\"kept going\");");

            var run = RunAsync(notebook, cell.Id);
            await sent.WaitForInputRequestAsync();
            ExecutionHandler.HandleCancel(notebook);
            var result = await run;

            Assert.AreEqual("cancelled", result.Status, result.ErrorMessage);
            Assert.IsFalse(result.Outputs.Any(o => o.Content.Contains("kept going")));
        }
    }
}
