using Verso.FSharp.Kernel;

namespace Verso.FSharp.Tests.Kernel;

[TestClass]
public class CheckerManagerTests
{
    private const string File = "/verso/notebook.fsx";

    [TestMethod]
    public async Task ParseAndCheck_SameDocument_SharesOneCheck()
    {
        using var manager = new FSharpCheckerManager();
        manager.Initialize();
        var context = new FSharpProjectContext(Array.Empty<string>(), Array.Empty<string>());

        var (source, _, options) = context.BuildDocument("let x = 1");
        var first = manager.ParseAndCheckAsync(File, source, options);
        // Built again, as each editor request builds it, with a fresh load time.
        var (sameSource, _, sameOptions) = context.BuildDocument("let x = 1");
        var second = manager.ParseAndCheckAsync(File, sameSource, sameOptions);

        Assert.AreSame(first, second);
        await first;
    }

    [TestMethod]
    public async Task ParseAndCheck_DifferentText_ChecksAgain()
    {
        using var manager = new FSharpCheckerManager();
        manager.Initialize();
        var context = new FSharpProjectContext(Array.Empty<string>(), Array.Empty<string>());

        var (source, _, options) = context.BuildDocument("let x = 1");
        var first = manager.ParseAndCheckAsync(File, source, options);
        var (other, _, otherOptions) = context.BuildDocument("let x = 2");
        var second = manager.ParseAndCheckAsync(File, other, otherOptions);

        Assert.AreNotSame(first, second);
        await Task.WhenAll(first, second);
    }

    [TestMethod]
    public async Task ParseAndCheck_Cancellable_IsNotShared()
    {
        using var manager = new FSharpCheckerManager();
        manager.Initialize();
        var context = new FSharpProjectContext(Array.Empty<string>(), Array.Empty<string>());
        using var cts = new CancellationTokenSource();

        var (source, _, options) = context.BuildDocument("let x = 1");
        var shared = manager.ParseAndCheckAsync(File, source, options);
        var own = manager.ParseAndCheckAsync(File, source, options, cts.Token);

        Assert.AreNotSame(shared, own);
        await Task.WhenAll(shared, own);
    }

    [TestMethod]
    public async Task ParseAndCheck_LoadedScriptChanged_ChecksAgain()
    {
        using var manager = new FSharpCheckerManager();
        manager.Initialize();
        var context = new FSharpProjectContext(Array.Empty<string>(), Array.Empty<string>());
        var script = Path.Combine(Path.GetTempPath(), $"verso-load-{Guid.NewGuid():N}.fsx");
        try
        {
            System.IO.File.WriteAllText(script, "module Lib\nlet triple x = 3 * x\n");

            var (source, _, options) = context.BuildDocument("let x = 1");
            var unloaded = manager.ParseAndCheckAsync(File, source, options);

            // Asked without waiting: a check that comes back empty is not kept, and would be asked
            // again whatever the key.
            context.AddLoadedFiles(new[] { script });
            (source, _, options) = context.BuildDocument("let x = 1");
            var loaded = manager.ParseAndCheckAsync(File, source, options);
            Assert.AreNotSame(unloaded, loaded, "a newly loaded script changes the check");

            System.IO.File.SetLastWriteTimeUtc(script, DateTime.UtcNow.AddMinutes(1));
            (source, _, options) = context.BuildDocument("let x = 1");
            var rewritten = manager.ParseAndCheckAsync(File, source, options);
            Assert.AreNotSame(loaded, rewritten, "a rewritten loaded script changes the check");

            await Task.WhenAll(unloaded, loaded, rewritten);
        }
        finally
        {
            System.IO.File.Delete(script);
        }
    }
}
