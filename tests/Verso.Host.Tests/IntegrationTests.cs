using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Protocol;

namespace Verso.Host.Tests;

[TestClass]
public class IntegrationTests
{
    private static string? _hostDllPath;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var candidates = Directory.GetFiles(searchDir, "Verso.Host.dll", SearchOption.AllDirectories);
        _hostDllPath = candidates.FirstOrDefault(p =>
            p.Contains(Path.Combine("Verso.Host", "bin")) && !p.Contains("Tests"));
    }

    [TestMethod]
    public async Task HostProcess_EmitsReadySignal()
    {
        if (_hostDllPath is null)
            Assert.Inconclusive("Verso.Host.dll not found in build output");

        using var process = StartHost();
        try
        {
            var readyLine = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(30));
            Assert.IsNotNull(readyLine, "Host did not emit ready signal");

            using var doc = JsonDocument.Parse(readyLine);
            var root = doc.RootElement;
            Assert.AreEqual("host/ready", root.GetProperty("method").GetString());
            Assert.AreEqual("1.0.0", root.GetProperty("params").GetProperty("version").GetString());
        }
        finally
        {
            KillSafe(process);
        }
    }

    [TestMethod]
    public async Task HostProcess_NotebookOpenAndCellAdd()
    {
        if (_hostDllPath is null)
            Assert.Inconclusive("Verso.Host.dll not found in build output");

        using var process = StartHost();
        try
        {
            // Wait for ready
            var readyLine = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(30));
            Assert.IsNotNull(readyLine, "Host did not emit ready signal");

            // Open empty notebook (Roslyn init may take time)
            await SendRequest(process, new { jsonrpc = "2.0", id = 1, method = "notebook/open", @params = new { content = "" } });
            var openResponse = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(30));
            Assert.IsNotNull(openResponse, $"No response to notebook/open. Stderr: {await ReadStderr(process)}");
            using var openDoc = JsonDocument.Parse(openResponse);
            Assert.IsTrue(openDoc.RootElement.TryGetProperty("result", out var openResult),
                $"notebook/open returned error: {openResponse}");
            var notebookId = openResult.GetProperty("notebookId").GetString();

            // Add a cell
            await SendRequest(process, new { jsonrpc = "2.0", id = 2, method = "cell/add", @params = new { type = "code", language = "csharp", source = "1+1", notebookId } });
            var addResponse = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(10));
            Assert.IsNotNull(addResponse, "No response to cell/add");
            using var addDoc = JsonDocument.Parse(addResponse);
            var result = addDoc.RootElement.GetProperty("result");
            Assert.AreEqual("code", result.GetProperty("type").GetString());
            Assert.AreEqual("1+1", result.GetProperty("source").GetString());

            // List cells
            await SendRequest(process, new { jsonrpc = "2.0", id = 3, method = "cell/list", @params = new { notebookId } });
            var listResponse = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(10));
            Assert.IsNotNull(listResponse, "No response to cell/list");
            using var listDoc = JsonDocument.Parse(listResponse);
            var cells = listDoc.RootElement.GetProperty("result").GetProperty("cells");
            Assert.AreEqual(1, cells.GetArrayLength());
        }
        finally
        {
            KillSafe(process);
        }
    }

    [TestMethod]
    public async Task HostProcess_UnknownMethod_ReturnsError()
    {
        if (_hostDllPath is null)
            Assert.Inconclusive("Verso.Host.dll not found in build output");

        using var process = StartHost();
        try
        {
            var readyLine = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(30));
            Assert.IsNotNull(readyLine, "Host did not emit ready signal");

            // Open a notebook to get a valid notebookId
            await SendRequest(process, new { jsonrpc = "2.0", id = 1, method = "notebook/open", @params = new { content = "" } });
            var openResponse = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(30));
            Assert.IsNotNull(openResponse, "No response to notebook/open");
            using var openDoc = JsonDocument.Parse(openResponse);
            var notebookId = openDoc.RootElement.GetProperty("result").GetProperty("notebookId").GetString();

            await SendRequest(process, new { jsonrpc = "2.0", id = 2, method = "does/not/exist", @params = new { notebookId } });
            var response = await ReadLineWithTimeout(process.StandardOutput, TimeSpan.FromSeconds(10));
            Assert.IsNotNull(response, $"No response to unknown method. Stderr: {await ReadStderr(process)}");
            using var doc = JsonDocument.Parse(response);
            Assert.IsTrue(doc.RootElement.TryGetProperty("error", out var error));
            Assert.AreEqual(-32601, error.GetProperty("code").GetInt32());
        }
        finally
        {
            KillSafe(process);
        }
    }

    private Process StartHost()
    {
        var psi = new ProcessStartInfo("dotnet", _hostDllPath!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var process = Process.Start(psi)!;
        return process;
    }

    private static async Task SendRequest(Process process, object request)
    {
        if (process.HasExited)
            Assert.Fail($"Host process exited with code {process.ExitCode} before request could be sent");

        var json = JsonSerializer.Serialize(request, JsonRpcMessage.SerializerOptions);
        await process.StandardInput.WriteLineAsync(json);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<string> ReadStderr(Process process)
    {
        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (await Task.WhenAny(stderrTask, Task.Delay(1000)) == stderrTask)
                return await stderrTask;
            return "(timeout reading stderr)";
        }
        catch
        {
            return "(error reading stderr)";
        }
    }

    private static async Task<string?> ReadLineWithTimeout(StreamReader reader, TimeSpan timeout)
    {
        var readTask = reader.ReadLineAsync();
        var delayTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(readTask, delayTask);
        if (completed == delayTask)
            return null;
        return await readTask;
    }

    private static void KillSafe(Process process)
    {
        try { if (!process.HasExited) process.Kill(); } catch { }
    }
}
