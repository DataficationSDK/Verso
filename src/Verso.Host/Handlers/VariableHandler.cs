using System.Collections;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Contexts;
using Verso.Host.Dto;

namespace Verso.Host.Handlers;

public static class VariableHandler
{
    public static VariableListResult HandleList(HostSession session)
    {
        session.EnsureSession();
        var variables = session.Scaffold!.Variables.GetAll();
        var previewService = new VariablePreviewService(session.ExtensionHost!);

        return new VariableListResult
        {
            Variables = variables.Select(v => new VariableEntryDto
            {
                Name = v.Name,
                TypeName = v.Type.Name,
                ValuePreview = previewService.GetPreview(v.Value),
                IsExpandable = IsExpandable(v.Value)
            }).ToList()
        };
    }

    public static async Task<VariableInspectResult> HandleInspectAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var name = @params?.GetProperty("name").GetString()
            ?? throw new JsonException("Missing name");

        var variables = session.Scaffold!.Variables;
        var all = variables.GetAll();
        var descriptor = all.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
            throw new InvalidOperationException($"Variable '{name}' not found.");

        // Try to format using IDataFormatter
        var formatters = session.ExtensionHost!.GetFormatters();
        var formatterContext = new SimpleFormatterContext(session.ExtensionHost!, variables);

        if (descriptor.Value is not null)
        {
            foreach (var formatter in formatters.OrderByDescending(f => f.Priority))
            {
                if (formatter.CanFormat(descriptor.Value, formatterContext))
                {
                    var output = await formatter.FormatAsync(descriptor.Value, formatterContext);
                    return new VariableInspectResult
                    {
                        Name = name,
                        TypeName = descriptor.Type.Name,
                        MimeType = output.MimeType,
                        Content = output.Content
                    };
                }
            }
        }

        // Fallback to ToString
        var preview = descriptor.Value?.ToString() ?? "null";
        return new VariableInspectResult
        {
            Name = name,
            TypeName = descriptor.Type.Name,
            MimeType = "text/plain",
            Content = preview
        };
    }

    private static bool IsExpandable(object? value)
    {
        if (value is null) return false;
        if (value is string) return false;
        return value is IEnumerable || value.GetType().GetProperties().Length > 0;
    }
}
