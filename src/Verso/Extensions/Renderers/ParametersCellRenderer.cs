using System.Net;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Parameters;

namespace Verso.Extensions.Renderers;

/// <summary>
/// Renders notebook parameters as an interactive HTML form and handles parameter
/// interactions (update, add, remove, submit) via <see cref="ICellInteractionHandler"/>.
/// </summary>
[VersoExtension]
public sealed class ParametersCellRenderer : ICellRenderer, ICellInteractionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public string ExtensionId => "verso.renderer.parameters";
    public string Name => "Parameters Renderer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders parameter definitions as an interactive form with type-aware inputs.";

    public string CellTypeId => "parameters";
    public string DisplayName => "Parameters";
    public bool CollapsesInputOnExecute => false;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        var parameters = context.NotebookMetadata.Parameters;
        var html = RenderParametersHtml(parameters, context.Variables);
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        return Task.FromResult(new RenderResult(output.MimeType, output.Content));
    }

    public string? GetEditorLanguage() => null;

    public async Task<string?> OnCellInteractionAsync(CellInteractionContext context)
    {
        return context.InteractionType switch
        {
            "parameter-update" => HandleParameterUpdate(context),
            "parameter-add" => await HandleParameterAdd(context),
            "parameter-remove" => HandleParameterRemove(context),
            "parameter-submit" => HandleParameterSubmit(context),
            "parameter-toggle-required" => HandleToggleRequired(context),
            _ => null
        };
    }

    // --- Interaction handlers ---

    private static string HandleParameterUpdate(CellInteractionContext context)
    {
        var payload = JsonSerializer.Deserialize<ParameterUpdatePayload>(context.Payload, JsonOptions);
        if (payload is null || string.IsNullOrEmpty(payload.Name))
            return RenderError("Invalid parameter-update payload.");

        var parameters = context.NotebookModel?.Parameters;
        if (parameters is null || !parameters.TryGetValue(payload.Name, out var def))
            return RenderError($"Parameter '{payload.Name}' not found.");

        if (!ParameterValueParser.TryParse(def.Type, payload.Value ?? "", out var typed, out var error))
            return RenderErrorForField(payload.Name, error ?? "Invalid value.");

        def.Default = typed;
        context.Variables?.Set(payload.Name, typed!);

        return RenderParametersHtml(parameters, context.Variables);
    }

    private static async Task<string> HandleParameterAdd(CellInteractionContext context)
    {
        var payload = JsonSerializer.Deserialize<ParameterAddPayload>(context.Payload, JsonOptions);
        if (payload is null || string.IsNullOrEmpty(payload.Name))
            return RenderError("Invalid parameter-add payload: name is required.");

        var notebook = context.NotebookModel;
        if (notebook is null)
            return RenderError("Notebook model not available.");

        notebook.Parameters ??= new Dictionary<string, NotebookParameterDefinition>();
        var typeId = payload.Type ?? "string";
        object? defaultValue = null;
        if (!string.IsNullOrEmpty(payload.DefaultValue))
        {
            if (ParameterValueParser.TryParse(typeId, payload.DefaultValue, out var parsed, out _))
                defaultValue = parsed;
        }

        notebook.Parameters[payload.Name] = new NotebookParameterDefinition
        {
            Type = typeId,
            Description = payload.Description,
            Required = payload.Required,
            Default = defaultValue,
            Order = null
        };

        if (defaultValue is not null)
            context.Variables?.Set(payload.Name, defaultValue);

        // Auto-insert a parameters cell at index 0 if none exists
        if (context.Notebook is not null &&
            !notebook.Cells.Any(c => string.Equals(c.Type, "parameters", StringComparison.OrdinalIgnoreCase)))
        {
            await context.Notebook.InsertCellAsync(0, "parameters");
        }

        return RenderParametersHtml(notebook.Parameters, context.Variables);
    }

    private static string HandleParameterRemove(CellInteractionContext context)
    {
        var payload = JsonSerializer.Deserialize<ParameterRemovePayload>(context.Payload, JsonOptions);
        if (payload is null || string.IsNullOrEmpty(payload.Name))
            return RenderError("Invalid parameter-remove payload.");

        var parameters = context.NotebookModel?.Parameters;
        if (parameters is null)
            return RenderError("No parameters defined.");

        parameters.Remove(payload.Name);
        context.Variables?.Remove(payload.Name);

        return RenderParametersHtml(parameters, context.Variables);
    }

    private static string HandleParameterSubmit(CellInteractionContext context)
    {
        var payload = JsonSerializer.Deserialize<ParameterSubmitPayload>(context.Payload, JsonOptions);
        if (payload?.Values is null)
            return RenderError("Invalid parameter-submit payload.");

        var parameters = context.NotebookModel?.Parameters;
        if (parameters is null)
            return RenderError("No parameters defined.");

        var errors = new Dictionary<string, string>();
        var parsed = new Dictionary<string, object>();

        foreach (var (name, value) in payload.Values)
        {
            if (!parameters.TryGetValue(name, out var def))
            {
                // Unknown parameter, inject as string
                parsed[name] = value;
                continue;
            }

            if (!ParameterValueParser.TryParse(def.Type, value, out var typed, out var error))
            {
                errors[name] = error ?? "Invalid value.";
                continue;
            }

            parsed[name] = typed!;
        }

        if (errors.Count > 0)
            return RenderParametersHtmlWithErrors(parameters, context.Variables, errors);

        // Inject all validated values into the variable store
        foreach (var (name, value) in parsed)
        {
            context.Variables?.Set(name, value);
        }

        return RenderParametersHtml(parameters, context.Variables, submitted: true);
    }

    private static string HandleToggleRequired(CellInteractionContext context)
    {
        var payload = JsonSerializer.Deserialize<ParameterUpdatePayload>(context.Payload, JsonOptions);
        if (payload is null || string.IsNullOrEmpty(payload.Name))
            return RenderError("Invalid parameter-toggle-required payload.");

        var parameters = context.NotebookModel?.Parameters;
        if (parameters is null || !parameters.TryGetValue(payload.Name, out var def))
            return RenderError($"Parameter '{payload.Name}' not found.");

        def.Required = string.Equals(payload.Value, "true", StringComparison.OrdinalIgnoreCase);

        return RenderParametersHtml(parameters, context.Variables);
    }

    // --- HTML rendering ---

    internal static string RenderParametersHtml(
        Dictionary<string, NotebookParameterDefinition>? parameters,
        IVariableStore? variables,
        bool submitted = false)
    {
        if (parameters is null || parameters.Count == 0)
            return RenderEmptyState();

        var sorted = parameters
            .OrderBy(p => p.Value.Order ?? int.MaxValue)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return RenderExpandedForm(sorted, variables, errors: null, submitted);
    }

    internal static string RenderParametersHtmlWithErrors(
        Dictionary<string, NotebookParameterDefinition> parameters,
        IVariableStore? variables,
        Dictionary<string, string> errors)
    {
        var sorted = parameters
            .OrderBy(p => p.Value.Order ?? int.MaxValue)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return RenderExpandedForm(sorted, variables, errors, submitted: false);
    }

    private static string RenderEmptyState()
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-parameters verso-parameters-empty\">");
        sb.Append("<p>No parameters defined.</p>");

        // Hidden inline form (same structure as RenderExpandedForm) so the
        // JS handler for data-action="parameter-add" can reveal it.
        sb.Append("<table class=\"verso-parameters-table\" style=\"display:none;\">");
        sb.Append("<tbody>");
        sb.Append("<tr class=\"verso-parameter-row verso-parameter-add-row\">");
        sb.Append("<td class=\"verso-parameter-name\">");
        sb.Append("<input type=\"text\" class=\"verso-add-name\" placeholder=\"name\" />");
        sb.Append("</td>");
        sb.Append("<td>");
        sb.Append("<select class=\"verso-add-type\">");
        sb.Append("<option value=\"string\">string</option>");
        sb.Append("<option value=\"int\">int</option>");
        sb.Append("<option value=\"float\">float</option>");
        sb.Append("<option value=\"bool\">bool</option>");
        sb.Append("<option value=\"date\">date</option>");
        sb.Append("<option value=\"datetime\">datetime</option>");
        sb.Append("</select>");
        sb.Append("</td>");
        sb.Append("<td class=\"verso-parameter-description\">");
        sb.Append("<input type=\"text\" class=\"verso-add-description\" placeholder=\"description\" />");
        sb.Append("</td>");
        sb.Append("<td class=\"verso-parameter-value\">");
        sb.Append("<input type=\"text\" class=\"verso-add-default\" placeholder=\"default value\" />");
        sb.Append("</td>");
        sb.Append("<td class=\"verso-parameter-required-cell\">");
        sb.Append("<label class=\"verso-parameter-bool\"><input type=\"checkbox\" class=\"verso-add-required\" /></label>");
        sb.Append("</td>");
        sb.Append("<td>");
        sb.Append("<button class=\"verso-btn verso-btn-confirm-add\" data-action=\"parameter-confirm-add\" title=\"Add\">&#x2713;</button>");
        sb.Append("<button class=\"verso-btn verso-btn-cancel-add\" data-action=\"parameter-cancel-add\" title=\"Cancel\">&#x2715;</button>");
        sb.Append("</td>");
        sb.Append("</tr>");
        sb.Append("</tbody></table>");

        sb.Append("<button class=\"verso-btn verso-btn-add\" data-action=\"parameter-add\">Add Parameter</button>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string RenderExpandedForm(
        List<KeyValuePair<string, NotebookParameterDefinition>> sorted,
        IVariableStore? variables,
        Dictionary<string, string>? errors,
        bool submitted)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-parameters\">");
        sb.Append("<div class=\"verso-parameters-header\">");
        sb.Append("<span class=\"verso-parameters-title\">Parameters</span>");
        sb.Append("</div>");

        if (submitted)
        {
            sb.Append("<div class=\"verso-parameters-success\">Parameters applied successfully.</div>");
        }

        sb.Append("<table class=\"verso-parameters-table\">");
        sb.Append("<thead><tr>");
        sb.Append("<th>Name</th><th>Type</th><th>Description</th><th>Default</th><th>Required</th><th></th>");
        sb.Append("</tr></thead>");
        sb.Append("<tbody>");

        foreach (var (name, def) in sorted)
        {
            var currentValue = GetCurrentValue(name, def, variables);
            var fieldError = errors is not null && errors.TryGetValue(name, out var err) ? err : null;

            sb.Append("<tr class=\"verso-parameter-row\">");

            // Name
            sb.Append("<td class=\"verso-parameter-name\">");
            sb.Append(Encode(name));
            sb.Append("</td>");

            // Type badge
            sb.Append("<td><span class=\"verso-parameter-type-badge verso-type-");
            sb.Append(Encode(def.Type));
            sb.Append("\">");
            sb.Append(Encode(def.Type));
            sb.Append("</span></td>");

            // Description
            sb.Append("<td class=\"verso-parameter-description\">");
            sb.Append(Encode(def.Description ?? ""));
            sb.Append("</td>");

            // Default value input
            sb.Append("<td class=\"verso-parameter-value\">");
            sb.Append(RenderInputField(name, def.Type, currentValue));
            if (fieldError is not null)
            {
                sb.Append("<div class=\"verso-parameter-error\">");
                sb.Append(Encode(fieldError));
                sb.Append("</div>");
            }
            sb.Append("</td>");

            // Required checkbox
            sb.Append("<td class=\"verso-parameter-required-cell\">");
            sb.Append("<label class=\"verso-parameter-bool\"><input type=\"checkbox\" data-param=\"");
            sb.Append(Encode(name));
            sb.Append("\" data-action=\"parameter-toggle-required\"");
            if (def.Required)
                sb.Append(" checked");
            sb.Append(" /></label>");
            sb.Append("</td>");

            // Remove button
            sb.Append("<td><button class=\"verso-btn verso-btn-remove\" data-action=\"parameter-remove\" data-param=\"");
            sb.Append(Encode(name));
            sb.Append("\" title=\"Remove parameter\">&#x2715;</button></td>");

            sb.Append("</tr>");
        }

        // Inline "add parameter" row (hidden by default, shown via JS)
        sb.Append("<tr class=\"verso-parameter-row verso-parameter-add-row\" style=\"display:none;\">");
        sb.Append("<td class=\"verso-parameter-name\">");
        sb.Append("<input type=\"text\" class=\"verso-add-name\" placeholder=\"name\" />");
        sb.Append("</td>");
        sb.Append("<td>");
        sb.Append("<select class=\"verso-add-type\">");
        sb.Append("<option value=\"string\">string</option>");
        sb.Append("<option value=\"int\">int</option>");
        sb.Append("<option value=\"float\">float</option>");
        sb.Append("<option value=\"bool\">bool</option>");
        sb.Append("<option value=\"date\">date</option>");
        sb.Append("<option value=\"datetime\">datetime</option>");
        sb.Append("</select>");
        sb.Append("</td>");
        sb.Append("<td class=\"verso-parameter-description\">");
        sb.Append("<input type=\"text\" class=\"verso-add-description\" placeholder=\"description\" />");
        sb.Append("</td>");
        sb.Append("<td class=\"verso-parameter-value\">");
        sb.Append("<input type=\"text\" class=\"verso-add-default\" placeholder=\"default value\" />");
        sb.Append("</td>");
        sb.Append("<td class=\"verso-parameter-required-cell\">");
        sb.Append("<label class=\"verso-parameter-bool\"><input type=\"checkbox\" class=\"verso-add-required\" /></label>");
        sb.Append("</td>");
        sb.Append("<td>");
        sb.Append("<button class=\"verso-btn verso-btn-confirm-add\" data-action=\"parameter-confirm-add\" title=\"Add\">&#x2713;</button>");
        sb.Append("<button class=\"verso-btn verso-btn-cancel-add\" data-action=\"parameter-cancel-add\" title=\"Cancel\">&#x2715;</button>");
        sb.Append("</td>");
        sb.Append("</tr>");

        sb.Append("</tbody></table>");

        sb.Append("<div class=\"verso-parameters-actions\">");
        sb.Append("<button class=\"verso-btn verso-btn-add\" data-action=\"parameter-add\">Add Parameter</button>");
        sb.Append("</div>");
        sb.Append("</div>");

        return sb.ToString();
    }

    internal static string RenderInputField(string name, string typeId, object? currentValue)
    {
        var encodedName = Encode(name);
        var valueStr = FormatValue(currentValue);
        var encodedValue = Encode(valueStr);

        return typeId switch
        {
            "bool" => $"<label class=\"verso-parameter-bool\"><input type=\"checkbox\" name=\"{encodedName}\" data-param=\"{encodedName}\" "
                     + $"data-action=\"parameter-update\"{(currentValue is true ? " checked" : "")} /></label>",
            "int" or "float" =>
                $"<input type=\"number\" name=\"{encodedName}\" data-param=\"{encodedName}\" "
                + $"data-action=\"parameter-update\" value=\"{encodedValue}\""
                + (typeId == "float" ? " step=\"any\"" : "")
                + " />",
            "date" =>
                $"<input type=\"date\" name=\"{encodedName}\" data-param=\"{encodedName}\" "
                + $"data-action=\"parameter-update\" value=\"{encodedValue}\" />",
            "datetime" =>
                $"<input type=\"datetime-local\" name=\"{encodedName}\" data-param=\"{encodedName}\" "
                + $"data-action=\"parameter-update\" value=\"{encodedValue}\" />",
            _ => // string and unknown types
                $"<input type=\"text\" name=\"{encodedName}\" data-param=\"{encodedName}\" "
                + $"data-action=\"parameter-update\" value=\"{encodedValue}\" />"
        };
    }

    // --- Helpers ---

    private static object? GetCurrentValue(string name, NotebookParameterDefinition def, IVariableStore? variables)
    {
        if (variables is not null && variables.TryGet<object>(name, out var fromStore) && fromStore is not null)
            return fromStore;

        return def.Default;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            bool b => b ? "true" : "false",
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss"),
            _ => value.ToString() ?? ""
        };
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string RenderError(string message)
    {
        return $"<div class=\"verso-parameters-error\">{Encode(message)}</div>";
    }

    private static string RenderErrorForField(string fieldName, string message)
    {
        return $"<div class=\"verso-parameters-error\" data-field=\"{Encode(fieldName)}\">{Encode(message)}</div>";
    }

    // --- Payload types ---

    internal sealed class ParameterUpdatePayload
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    internal sealed class ParameterAddPayload
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Description { get; set; }
        public string? DefaultValue { get; set; }
        public bool Required { get; set; }
    }

    internal sealed class ParameterRemovePayload
    {
        public string? Name { get; set; }
    }

    internal sealed class ParameterSubmitPayload
    {
        public Dictionary<string, string>? Values { get; set; }
    }
}
