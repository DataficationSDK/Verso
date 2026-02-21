using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class KernelHandler
{
    public static async Task<object> HandleRestartAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<KernelRestartParams>(JsonRpcMessage.SerializerOptions);
        await session.Scaffold!.RestartKernelAsync(p?.KernelId);

        // Notify: restart clears the variable store
        session.SendNotification(MethodNames.VariableChanged);

        return new { success = true };
    }

    public static async Task<CompletionsResult> HandleGetCompletionsAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<CompletionsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for kernel/getCompletions");

        var kernel = ResolveKernelForCell(session, p.CellId);
        if (kernel is null)
            return new CompletionsResult();

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

    public static async Task<DiagnosticsResult> HandleGetDiagnosticsAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<DiagnosticsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for kernel/getDiagnostics");

        var kernel = ResolveKernelForCell(session, p.CellId);
        if (kernel is null)
            return new DiagnosticsResult();

        var diagnostics = await kernel.GetDiagnosticsAsync(p.Code);
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

    public static async Task<HoverResult?> HandleGetHoverInfoAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<HoverParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for kernel/getHoverInfo");

        var kernel = ResolveKernelForCell(session, p.CellId);
        if (kernel is null)
            return null;

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

    private static ILanguageKernel? ResolveKernelForCell(HostSession session, string cellId)
    {
        var cell = session.Scaffold!.GetCell(Guid.Parse(cellId));
        if (cell is null)
            return null;

        var language = cell.Language ?? session.Scaffold.Notebook.DefaultKernelId;
        if (language is null)
            return null;

        return session.Scaffold.GetKernel(language);
    }
}
