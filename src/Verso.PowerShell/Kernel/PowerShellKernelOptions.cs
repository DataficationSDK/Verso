namespace Verso.PowerShell.Kernel;

internal sealed record PowerShellKernelOptions
{
    /// <summary>
    /// PowerShell automatic variables that should never be published to the variable store.
    /// These are internal engine variables that would pollute cross-kernel sharing.
    /// </summary>
    public static readonly IReadOnlySet<string> AutomaticVariableExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Engine automatic variables
        "$", "?", "^", "_",
        "args", "ConsoleFileName", "Error", "Event", "EventArgs",
        "EventSubscriber", "ExecutionContext", "false", "foreach",
        "HOME", "Host", "input", "IsCoreCLR", "IsLinux", "IsMacOS", "IsWindows",
        "LASTEXITCODE", "Matches", "MyInvocation", "NestedPromptLevel",
        "null", "PID", "PROFILE", "PSBoundParameters", "PSCmdlet",
        "PSCommandPath", "PSCulture", "PSDebugContext", "PSEdition",
        "PSHOME", "PSItem", "PSScriptRoot", "PSSenderInfo",
        "PSUICulture", "PSVersionTable", "PWD", "Sender",
        "ShellId", "StackTrace", "switch", "this", "true",

        // Preference variables
        "ConfirmPreference", "DebugPreference", "ErrorActionPreference",
        "ErrorView", "FormatEnumerationLimit", "InformationPreference",
        "LogCommandHealthEvent", "LogCommandLifecycleEvent",
        "LogEngineHealthEvent", "LogEngineLifecycleEvent",
        "LogProviderHealthEvent", "LogProviderLifecycleEvent",
        "MaximumHistoryCount", "OFS", "OutputEncoding",
        "ProgressPreference", "PSDefaultParameterValues", "PSEmailServer",
        "PSModuleAutoLoadingPreference", "PSSessionApplicationName",
        "PSSessionConfigurationName", "PSSessionOption",
        "PSStyle", "Transcript", "VerbosePreference",
        "WarningPreference", "WhatIfPreference",

        // Engine-managed variables
        "EnabledExperimentalFeatures", "PSModulePath",
        "PSNativeCommandUseErrorActionPreference",
        "PSNativeCommandArgumentPassing",

        // Verso-injected variables
        "VersoVariables",
    };

    public bool SuppressVerbose { get; init; } = true;
    public bool SuppressDebug { get; init; } = true;
    public bool PublishUnderscorePrefixed { get; init; } = false;
}
