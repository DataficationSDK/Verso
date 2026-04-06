using System.Collections.ObjectModel;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class PropertiesHandler
{
    public static async Task<PropertiesGetSectionsResult> HandleGetSectionsAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<PropertiesGetSectionsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for properties/getSections");

        if (!Guid.TryParse(p.CellId, out var cellId))
            throw new JsonException($"Invalid cell ID: {p.CellId}");

        var cell = ns.Scaffold.Cells.FirstOrDefault(c => c.Id == cellId)
            ?? throw new InvalidOperationException($"Cell '{p.CellId}' not found.");

        var context = new HostCellRenderContext(ns.Scaffold, cell);
        var providers = ns.ExtensionHost.GetPropertyProviders()
            .Where(pr => pr.AppliesTo(cell, context))
            .OrderBy(pr => pr.Order)
            .ToList();

        var results = new List<PropertySectionResultDto>();
        foreach (var provider in providers)
        {
            try
            {
                var section = await provider.GetPropertiesSectionAsync(cell, context);
                results.Add(new PropertySectionResultDto
                {
                    ProviderExtensionId = provider.ExtensionId,
                    Section = MapSection(section)
                });
            }
            catch
            {
                // Non-fatal: skip provider on failure
            }
        }

        return new PropertiesGetSectionsResult { Sections = results };
    }

    public static async Task<object?> HandleUpdatePropertyAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<PropertiesUpdatePropertyParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for properties/updateProperty");

        if (!Guid.TryParse(p.CellId, out var cellId))
            throw new JsonException($"Invalid cell ID: {p.CellId}");

        var cell = ns.Scaffold.Cells.FirstOrDefault(c => c.Id == cellId)
            ?? throw new InvalidOperationException($"Cell '{p.CellId}' not found.");

        var provider = ns.ExtensionHost.GetPropertyProviders()
            .FirstOrDefault(pr => string.Equals(
                pr.ExtensionId, p.ProviderExtensionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No property provider found for extension '{p.ProviderExtensionId}'.");

        var context = new HostCellRenderContext(ns.Scaffold, cell);
        await provider.OnPropertyChangedAsync(cell, p.PropertyName, p.Value, context);

        return null;
    }

    public static PropertiesGetSupportedResult HandleGetSupported(NotebookSession ns)
    {
        var supported = ns.Scaffold.LayoutManager?.ActiveLayout?.SupportsPropertiesPanel ?? false;
        return new PropertiesGetSupportedResult { Supported = supported };
    }

    private static PropertySectionDto MapSection(PropertySection section)
    {
        return new PropertySectionDto
        {
            Title = section.Title,
            Description = section.Description,
            Fields = section.Fields.Select(f => new PropertyFieldDto
            {
                Name = f.Name,
                DisplayName = f.DisplayName,
                FieldType = f.FieldType.ToString(),
                CurrentValue = f.CurrentValue,
                Description = f.Description,
                IsReadOnly = f.IsReadOnly,
                Options = f.Options?.Select(o => new PropertyFieldOptionDto
                {
                    Value = o.Value,
                    DisplayName = o.DisplayName
                }).ToList()
            }).ToList()
        };
    }

    private sealed class HostCellRenderContext : ICellRenderContext
    {
        private readonly Scaffold _scaffold;

        public HostCellRenderContext(Scaffold scaffold, CellModel cell)
        {
            _scaffold = scaffold;
            CellId = cell.Id;
            CellMetadata = new ReadOnlyDictionary<string, object>(cell.Metadata);
        }

        public Guid CellId { get; }
        public IReadOnlyDictionary<string, object> CellMetadata { get; }
        public (double Width, double Height) Dimensions => (800, 400);
        public bool IsSelected => false;
        public IVariableStore Variables => _scaffold.Variables;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IThemeContext Theme => _scaffold.ThemeContext;
        public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
        public INotebookMetadata NotebookMetadata => new HostNotebookMetadata(_scaffold);
        public INotebookOperations Notebook => _scaffold.NotebookOps;
        public string? ActiveLayoutId => _scaffold.LayoutManager?.ActiveLayout?.LayoutId;

        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

        private sealed class HostNotebookMetadata : INotebookMetadata
        {
            private readonly Scaffold _s;
            public HostNotebookMetadata(Scaffold s) => _s = s;
            public string? Title => _s.Title;
            public string? DefaultKernelId => _s.DefaultKernelId;
            public string? FilePath => null;
            public Dictionary<string, NotebookParameterDefinition>? Parameters => _s.Notebook.Parameters;
        }
    }
}
