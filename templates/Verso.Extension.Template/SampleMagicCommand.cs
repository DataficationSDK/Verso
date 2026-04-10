namespace MyExtension;

/// <summary>
/// Example <see cref="IMagicCommand"/> scaffold.
/// Magic commands are invoked with a prefix (e.g., <c>%greet</c> or <c>#!greet</c>).
/// Replace this with your own magic command logic.
/// </summary>
[VersoExtension]
public sealed class SampleMagicCommand : IMagicCommand
{
    public string ExtensionId => "com.example.myextension.magic";
    public string Name => "greet";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string Description => "Displays a greeting message.";

    public IReadOnlyList<ParameterDefinition> Parameters => new[]
    {
        new ParameterDefinition("name", "The name to greet", typeof(string), IsRequired: false, DefaultValue: "World")
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        var name = string.IsNullOrWhiteSpace(arguments) ? "World" : arguments.Trim();
        await context.WriteOutputAsync(new CellOutput("text/plain", $"Hello, {name}!"));
    }
}
