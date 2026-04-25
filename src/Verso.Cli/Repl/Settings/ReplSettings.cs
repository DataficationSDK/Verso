namespace Verso.Cli.Repl.Settings;

/// <summary>
/// Runtime-mutable REPL settings. Phase 1/4 treats this as an in-memory object
/// seeded with defaults. Phase 6 adds disk-loaded user settings and
/// per-notebook metadata overrides.
/// </summary>
public sealed class ReplSettings
{
    public string Prompt { get; set; } = "»";
    public bool ConfirmOnExit { get; set; } = true;

    public PreviewSettings Preview { get; set; } = new();

    public sealed class PreviewSettings
    {
        public int Rows { get; set; } = 20;
        public int Lines { get; set; } = 200;
        public int ElapsedThresholdMs { get; set; } = 200;
    }
}
