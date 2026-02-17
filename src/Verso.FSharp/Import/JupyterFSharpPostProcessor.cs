using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.FSharp.Import;

/// <summary>
/// Post-processor that converts Polyglot Notebooks F# patterns in imported Jupyter notebooks
/// into Verso F# cell types and magic commands.
/// </summary>
[VersoExtension]
public sealed class JupyterFSharpPostProcessor : INotebookPostProcessor
{
    private static readonly Regex FSharpMagicPattern = new(
        @"^#!(fsharp|f#)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SetMagicPattern = new(
        @"^#!set\s+--name\s+(\S+)\s+--value\s+@fsharp:(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ShareMagicPattern = new(
        @"^#!share\s+--from\s+(\S+)\s+(\S+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // --- IExtension ---

    public string ExtensionId => "verso.fsharp.postprocessor.jupyter-fsharp";
    string IExtension.Name => "Jupyter F# Import Hook";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Converts Polyglot Notebooks F# patterns to Verso F# cells on Jupyter import.";

    // --- INotebookPostProcessor ---

    public int Priority => 10;

    public bool CanProcess(string? filePath, string formatId)
    {
        if (string.Equals(formatId, "jupyter", StringComparison.OrdinalIgnoreCase))
            return true;

        if (filePath is not null && filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<NotebookModel> PostDeserializeAsync(NotebookModel notebook, string? filePath)
    {
        bool anyTransformed = false;

        // Pass 1 — detect F# presence via kernel spec
        if (notebook.DefaultKernelId is not null &&
            notebook.DefaultKernelId.Contains("fsharp", StringComparison.OrdinalIgnoreCase))
        {
            notebook.DefaultKernelId = "fsharp";
            anyTransformed = true;
        }

        // Pass 2 — transform cells
        foreach (var cell in notebook.Cells)
        {
            if (cell.Type != "code")
                continue;

            var source = cell.Source;

            // Check cell metadata for dotnet_interactive.language
            if (cell.Metadata.TryGetValue("dotnet_interactive", out var diObj))
            {
                if (TryGetLanguage(diObj, out var lang) &&
                    string.Equals(lang, "fsharp", StringComparison.OrdinalIgnoreCase))
                {
                    cell.Language = "fsharp";
                    anyTransformed = true;
                }
            }

            // Check for #!set pattern
            var setMatch = SetMagicPattern.Match(source.Trim());
            if (setMatch.Success)
            {
                var name = setMatch.Groups[1].Value;
                var expr = setMatch.Groups[2].Value.Trim();
                cell.Language = "fsharp";
                cell.Source = $"Variables.Set(\"{name}\", {expr})";
                anyTransformed = true;
                continue;
            }

            // Check for #!share pattern
            var shareMatch = ShareMagicPattern.Match(source.Trim());
            if (shareMatch.Success)
            {
                var fromKernel = shareMatch.Groups[1].Value;
                var varName = shareMatch.Groups[2].Value;
                cell.Language = "fsharp";
                cell.Source = $"let {varName} = Variables.Get<obj>(\"{varName}\") // TODO: add type annotation (shared from {fromKernel})";
                anyTransformed = true;
                continue;
            }

            // Check for #!fsharp / #!f# magic lines
            var lines = source.Split('\n');
            if (lines.Length > 0)
            {
                var firstLine = lines[0].Trim();
                var magicMatch = FSharpMagicPattern.Match(firstLine);
                if (magicMatch.Success)
                {
                    cell.Language = "fsharp";
                    cell.Source = string.Join('\n', lines.Skip(1));
                    anyTransformed = true;
                }
            }
        }

        if (anyTransformed)
        {
            if (!notebook.RequiredExtensions.Contains("verso.fsharp"))
            {
                notebook.RequiredExtensions.Add("verso.fsharp");
            }
        }

        return Task.FromResult(notebook);
    }

    public Task<NotebookModel> PreSerializeAsync(NotebookModel notebook, string? filePath)
    {
        return Task.FromResult(notebook);
    }

    // --- Helpers ---

    private static bool TryGetLanguage(object metadataValue, out string language)
    {
        language = "";

        // Handle Dictionary<string, object> from JSON deserialization
        if (metadataValue is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue("language", out var langObj) && langObj is string langStr)
            {
                language = langStr;
                return true;
            }
        }

        // Handle System.Text.Json.JsonElement
        if (metadataValue is System.Text.Json.JsonElement jsonElement &&
            jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (jsonElement.TryGetProperty("language", out var langProp) &&
                langProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                language = langProp.GetString() ?? "";
                return !string.IsNullOrEmpty(language);
            }
        }

        return false;
    }
}
