namespace Verso.Host.Dto;

// --- Properties Panel ---

// properties/getSections

public sealed class PropertiesGetSectionsParams
{
    public string CellId { get; set; } = "";
}

public sealed class PropertiesGetSectionsResult
{
    public List<PropertySectionResultDto> Sections { get; set; } = new();
}

public sealed class PropertySectionResultDto
{
    public string ProviderExtensionId { get; set; } = "";
    public PropertySectionDto Section { get; set; } = new();
}

public sealed class PropertySectionDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public List<PropertyFieldDto> Fields { get; set; } = new();
}

public sealed class PropertyFieldDto
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FieldType { get; set; } = "";
    public object? CurrentValue { get; set; }
    public string? Description { get; set; }
    public bool IsReadOnly { get; set; }
    public List<PropertyFieldOptionDto>? Options { get; set; }
}

public sealed class PropertyFieldOptionDto
{
    public string Value { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

// properties/updateProperty

public sealed class PropertiesUpdatePropertyParams
{
    public string CellId { get; set; } = "";
    public string ProviderExtensionId { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public string? Value { get; set; }
}

// properties/getSupported

public sealed class PropertiesGetSupportedResult
{
    public bool Supported { get; set; }
}
