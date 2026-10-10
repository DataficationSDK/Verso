using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class KernelHandler
{
    public static async Task<object> HandleRestartAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<KernelRestartParams>(JsonRpcMessage.SerializerOptions);
        await ns.Scaffold.RestartKernelAsync(p?.KernelId);

        // Notify: restart clears the variable store
        ns.SendNotification(MethodNames.VariableChanged);

        return new { success = true };
    }

    public static async Task<CompletionsResult> HandleGetCompletionsAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<CompletionsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for kernel/getCompletions");

        var kernel = ResolveKernelForCell(ns, p.CellId);
        if (kernel is null)
            return new CompletionsResult();

        await ns.Scaffold.WarmUpKernelAsync(kernel.LanguageId);
        var completions = await kernel.GetCompletionsAsync(p.Code, p.CursorPosition);
        return new CompletionsResult
        {
            Items = completions.Select(c => new CompletionDto
            {
                DisplayText = c.DisplayText,
                InsertText = c.InsertText,
                Kind = c.Kind,
                Description = c.Description,
                SortText = c.SortText
            }).ToList()
        };
    }

    public static async Task<DiagnosticsResult?> HandleGetDiagnosticsAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<DiagnosticsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for kernel/getDiagnostics");

        var kernel = ResolveKernelForCell(ns, p.CellId);
        if (kernel is null)
            return new DiagnosticsResult();

        // Diagnostics are advisory. A kernel that cannot start (a runtime it needs is missing) or
        // that fails mid-analysis reports nothing rather than an error on every keystroke; the
        // failure surfaces where it matters, when the cell runs. A null result rather than an
        // empty one, so the editor keeps the markers it already shows, and rather than an error
        // response, so a kernel that cannot start does not log a failure on every request.
        IReadOnlyList<Diagnostic> diagnostics;
        try
        {
            await ns.Scaffold.WarmUpKernelAsync(kernel.LanguageId);
            diagnostics = await kernel.GetDiagnosticsAsync(p.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return new DiagnosticsResult
        {
            Items = diagnostics.Select(d => new DiagnosticDto
            {
                Severity = d.Severity.ToString(),
                Message = d.Message,
                StartLine = d.StartLine,
                StartColumn = d.StartColumn,
                EndLine = d.EndLine,
                EndColumn = d.EndColumn,
                Code = d.Code
            }).ToList()
        };
    }

    public static async Task<HoverResult?> HandleGetHoverInfoAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<HoverParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for kernel/getHoverInfo");

        var kernel = ResolveKernelForCell(ns, p.CellId);
        if (kernel is null)
            return null;

        await ns.Scaffold.WarmUpKernelAsync(kernel.LanguageId);
        var info = await kernel.GetHoverInfoAsync(p.Code, p.CursorPosition);
        if (info is null)
            return null;

        return new HoverResult
        {
            Content = info.Content,
            MimeType = info.MimeType,
            Range = info.Range is { } r ? new RangeDto
            {
                StartLine = r.StartLine,
                StartColumn = r.StartColumn,
                EndLine = r.EndLine,
                EndColumn = r.EndColumn
            } : null
        };
    }

    private static ILanguageKernel? ResolveKernelForCell(NotebookSession ns, string cellId)
    {
        return ns.Scaffold.ResolveKernelForCell(Guid.Parse(cellId));
    }
}
