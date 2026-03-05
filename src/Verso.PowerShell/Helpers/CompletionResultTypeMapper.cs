using System.Management.Automation;

namespace Verso.PowerShell.Helpers;

internal static class CompletionResultTypeMapper
{
    public static string Map(CompletionResultType resultType) => resultType switch
    {
        CompletionResultType.Command => "Method",
        CompletionResultType.Variable => "Variable",
        CompletionResultType.Property => "Property",
        CompletionResultType.Method => "Method",
        CompletionResultType.Keyword => "Keyword",
        CompletionResultType.Type => "Class",
        CompletionResultType.Namespace => "Namespace",
        CompletionResultType.ParameterName => "Property",
        CompletionResultType.ParameterValue => "Constant",
        CompletionResultType.ProviderItem => "Field",
        CompletionResultType.ProviderContainer => "Module",
        CompletionResultType.Text => "Text",
        CompletionResultType.History => "Text",
        CompletionResultType.DynamicKeyword => "Keyword",
        _ => "Text",
    };
}
