namespace Verso.E2E.Tests.Infrastructure;

/// <summary>
/// A directory of its own for one test's notebooks, removed when the test is done.
/// </summary>
public sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "verso-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Writes <paramref name="content"/> to <paramref name="fileName"/> here and returns its full path.</summary>
    public string Write(string fileName, string content)
    {
        var path = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
