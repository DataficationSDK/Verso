using System.Diagnostics;
using System.Text;
using Verso.Blazor.Shared.Models;
using Verso.Blazor.Shared.Resources;

namespace Verso.Blazor.Services;

/// <summary>
/// Minimal git CLI wrapper used by the notebook diff feature to read committed versions of a
/// notebook file. Degrades gracefully: a missing git executable or a path outside a repository
/// reports as "not available" rather than throwing.
/// </summary>
internal static class GitCliHelper
{
    /// <summary>
    /// Returns the repository root containing <paramref name="startDirectory"/>, or
    /// <see langword="null"/> when the directory is not inside a git work tree or git is not
    /// installed.
    /// </summary>
    public static string? FindRepoRoot(string startDirectory)
    {
        try
        {
            var (exitCode, stdout, _) = Run(startDirectory, "rev-parse", "--show-toplevel");
            var root = stdout.Trim();
            return exitCode == 0 && root.Length > 0 ? root : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the content of <paramref name="filePath"/> as committed at
    /// <paramref name="refName"/>. Throws <see cref="InvalidOperationException"/> with a
    /// user-facing message when the ref is unknown or the file is not tracked at that ref.
    /// </summary>
    public static (string Content, string Label) Show(string filePath, string refName)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            throw new InvalidOperationException(string.Format(UI.Compare_DirectoryUnresolved, filePath));
        }

        if (FindRepoRoot(directory) is null)
        {
            throw new InvalidOperationException(UI.Compare_NotInGitRepo);
        }

        // The "ref:./name" form makes git resolve the repo-relative path itself, from the
        // process working directory. Computing the relative path in .NET instead breaks when
        // the repo root comes back through a resolved symlink (e.g. /private/var vs /var on
        // macOS temp directories).
        var fileName = Path.GetFileName(fullPath);
        var (exitCode, stdout, stderr) = Run(directory, "show", $"{refName}:./{fileName}");
        if (exitCode != 0)
        {
            var detail = stderr.Contains("exists on disk, but not in", StringComparison.OrdinalIgnoreCase)
                || stderr.Contains("does not exist in", StringComparison.OrdinalIgnoreCase)
                ? string.Format(UI.Compare_FileNotTracked, fileName, refName)
                : stderr.Contains("unknown revision", StringComparison.OrdinalIgnoreCase)
                    || stderr.Contains("invalid object name", StringComparison.OrdinalIgnoreCase)
                    ? string.Format(UI.Compare_UnknownRef, refName)
                    : string.Format(UI.Compare_GitReadFailed, fileName, refName);
            throw new InvalidOperationException(detail);
        }

        return (stdout, DiffSources.ResolvedName(DiffSources.GitRef, refName));
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git could not be started. Is it installed?");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            throw new InvalidOperationException("git took too long to respond.");
        }

        return (process.ExitCode, stdout, stderr);
    }
}
