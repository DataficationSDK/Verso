using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Verso.PowerShell.Kernel.Host;

internal sealed partial class VersoPowerShellHostUserInterface : PSHostUserInterface
{
    private const string PlainText = "text/plain";
    private const string InteractiveInputMessage = "Interactive PowerShell input is not supported by Verso yet.";

    private readonly PowerShellHostOutputCallbackProvider _outputCallbackProvider;
    private readonly PowerShellHostInputCallbackProvider _inputCallbackProvider;
    private readonly VersoPowerShellRawUserInterface _rawUI = new();
    private readonly object _promptLock = new();

    public VersoPowerShellHostUserInterface(
        PowerShellHostOutputCallbackProvider outputCallbackProvider,
        PowerShellHostInputCallbackProvider inputCallbackProvider)
    {
        _outputCallbackProvider = outputCallbackProvider ?? throw new ArgumentNullException(nameof(outputCallbackProvider));
        _inputCallbackProvider = inputCallbackProvider ?? throw new ArgumentNullException(nameof(inputCallbackProvider));
    }

    public override PSHostRawUserInterface RawUI => _rawUI;

    public override bool SupportsVirtualTerminal => false;

    public override string ReadLine() => ReadInput(string.Empty, isPassword: false);

    public override SecureString ReadLineAsSecureString()
    {
        var value = ReadInput(string.Empty, isPassword: true);
        var secure = new SecureString();
        foreach (var ch in value)
            secure.AppendChar(ch);
        secure.MakeReadOnly();
        return secure;
    }

    public override Dictionary<string, PSObject> Prompt(
        string caption,
        string message,
        Collection<FieldDescription> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);

        lock (_promptLock)
        {
            if (!string.IsNullOrEmpty(caption))
                WriteLine(caption);
            if (!string.IsNullOrEmpty(message))
                WriteLine(message);

            var result = new Dictionary<string, PSObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var description in descriptions)
            {
                if (description is null)
                    continue;

                var fieldType = ResolveFieldType(description);
                object? value;

                if (fieldType == typeof(SecureString))
                {
                    value = ReadSecureString($"{description.Name}: ");
                }
                else if (fieldType == typeof(PSCredential))
                {
                    WriteLine($"{description.Name}:");
                    value = PromptForCredential(
                        caption: string.Empty,
                        message: string.Empty,
                        userName: string.Empty,
                        targetName: string.Empty);
                }
                else
                {
                    value = ReadAndConvert(description.Name, fieldType);
                }

                result[description.Name] = PSObject.AsPSObject(value);
            }

            return result;
        }
    }

    public override PSCredential PromptForCredential(
        string caption,
        string message,
        string userName,
        string targetName) =>
        PromptForCredential(
            caption,
            message,
            userName,
            targetName,
            PSCredentialTypes.Default,
            PSCredentialUIOptions.Default);

    public override PSCredential PromptForCredential(
        string caption,
        string message,
        string userName,
        string targetName,
        PSCredentialTypes allowedCredentialTypes,
        PSCredentialUIOptions options)
    {
        lock (_promptLock)
        {
            if (!string.IsNullOrEmpty(caption))
                WriteLine(caption);
            if (!string.IsNullOrEmpty(message))
                WriteLine(message);

            while (string.IsNullOrEmpty(userName))
                userName = ReadInput("User: ", isPassword: false);

            var password = ReadSecureString($"Password for user {userName}: ");
            return new PSCredential(userName, password);
        }
    }

    public override int PromptForChoice(
        string caption,
        string message,
        Collection<ChoiceDescription> choices,
        int defaultChoice)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
            throw new ArgumentException("At least one choice is required.", nameof(choices));
        if (defaultChoice < -1 || defaultChoice >= choices.Count)
            throw new ArgumentOutOfRangeException(nameof(defaultChoice));

        lock (_promptLock)
        {
            if (!string.IsNullOrEmpty(caption))
                WriteLine(caption);
            if (!string.IsNullOrEmpty(message))
                WriteLine(message);

            var labels = BuildChoiceLabels(choices);
            while (true)
            {
                WriteLine(BuildChoicePrompt(labels, defaultChoice));
                var response = ReadInput("Select: ", isPassword: false).Trim();

                if (response.Length == 0 && defaultChoice >= 0)
                    return defaultChoice;

                if (response == "?")
                {
                    for (var i = 0; i < choices.Count; i++)
                        WriteLine($"{labels[i].Key} - {choices[i].HelpMessage}");
                    continue;
                }

                for (var i = 0; i < labels.Count; i++)
                {
                    if (string.Equals(response, labels[i].Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(response, labels[i].Label, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
        }
    }

    public override void Write(string value)
    {
        Emit(value);
    }

    public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
    {
        Emit(value);
    }

    public override void WriteLine()
    {
        Emit(Environment.NewLine);
    }

    public override void WriteLine(string value)
    {
        Emit(value);
    }

    public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
    {
        Emit(value);
    }

    public override void WriteErrorLine(string value)
    {
        Emit(value, isError: true, errorName: "PSError");
    }

    public override void WriteDebugLine(string message)
    {
        Emit($"DEBUG: {message}");
    }

    public override void WriteProgress(long sourceId, ProgressRecord record)
    {
        // Progress records are intentionally suppressed for now. Streaming every
        // update would create noisy notebook output and needs a dedicated UI shape.
    }

    public override void WriteVerboseLine(string message)
    {
        Emit($"VERBOSE: {message}");
    }

    public override void WriteWarningLine(string message)
    {
        Emit($"WARNING: {message}");
    }

    public override void WriteInformation(InformationRecord record)
    {
        // Write-Host reaches the host UI through Write/WriteLine. Emitting
        // InformationRecord as well would duplicate the same visible output.
    }

    private void Emit(string? content, bool isError = false, string? errorName = null)
    {
        if (string.IsNullOrEmpty(content))
            return;

        content = StripAnsiEscapeSequences(content);
        if (string.IsNullOrEmpty(content))
            return;

        var callback = _outputCallbackProvider();
        if (callback is null)
            return;

        var output = new PowerShellHostOutput(PlainText, content, isError, errorName);
        callback(output).GetAwaiter().GetResult();
    }

    private string ReadInput(string prompt, bool isPassword)
    {
        var callback = _inputCallbackProvider();
        if (callback is null)
            throw CreateInteractiveInputException();

        var result = callback(new PowerShellHostInputRequest(prompt, isPassword))
            .GetAwaiter()
            .GetResult();

        if (result is null)
            throw new OperationCanceledException("PowerShell input was cancelled.");

        return result;
    }

    private SecureString ReadSecureString(string prompt)
    {
        var value = ReadInput(prompt, isPassword: true);
        var secure = new SecureString();
        foreach (var ch in value)
            secure.AppendChar(ch);
        secure.MakeReadOnly();
        return secure;
    }

    private static Type ResolveFieldType(FieldDescription description)
    {
        if (LanguagePrimitives.TryConvertTo(description.ParameterAssemblyFullName, out Type type) ||
            LanguagePrimitives.TryConvertTo(description.ParameterTypeFullName, out type))
        {
            return type;
        }

        return typeof(string);
    }

    private object? ReadAndConvert(string fieldName, Type fieldType)
    {
        while (true)
        {
            var raw = ReadInput($"{fieldName}: ", isPassword: false);
            try
            {
                return LanguagePrimitives.ConvertTo(raw, fieldType, CultureInfo.InvariantCulture);
            }
            catch (PSInvalidCastException ex)
            {
                WriteLine(ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                WriteLine(ex.Message);
            }
        }
    }

    private static List<(string Key, string Label)> BuildChoiceLabels(Collection<ChoiceDescription> choices)
    {
        var result = new List<(string Key, string Label)>();
        foreach (var choice in choices)
        {
            var label = choice.Label;
            var amp = label.IndexOf('&');
            string key;
            if (amp >= 0 && amp + 1 < label.Length)
            {
                key = char.ToUpperInvariant(label[amp + 1]).ToString();
                label = label.Remove(amp, 1).Trim();
            }
            else
            {
                key = label.Length > 0 ? char.ToUpperInvariant(label[0]).ToString() : string.Empty;
            }

            result.Add((key, label));
        }

        return result;
    }

    private static string BuildChoicePrompt(IReadOnlyList<(string Key, string Label)> choices, int defaultChoice)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < choices.Count; i++)
        {
            if (i > 0)
                builder.Append(' ');
            builder.Append('[').Append(choices[i].Key).Append("] ").Append(choices[i].Label);
        }

        builder.Append(" [?] Help");
        if (defaultChoice >= 0)
            builder.Append(" (default is '").Append(choices[defaultChoice].Key).Append("')");

        return builder.ToString();
    }

    private static string StripAnsiEscapeSequences(string value) =>
        AnsiEscapeSequenceRegex().Replace(value, string.Empty);

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiEscapeSequenceRegex();

    private static PSNotSupportedException CreateInteractiveInputException() =>
        new(InteractiveInputMessage);
}
