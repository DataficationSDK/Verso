using Verso.Python.PackageManagement;

namespace Verso.Python.Tests.PackageManagement;

/// <summary>
/// Covers reading a cell's inline script metadata block. Only the two keys the install path acts
/// on are read, and anything the reader cannot make sense of reads as no block at all: a broken
/// block must never stop a cell from running.
/// </summary>
[TestClass]
public sealed class Pep723BlockTests
{
    [TestMethod]
    public void Read_FindsAMultiLineDependencyArray()
    {
        var code = "# /// script\n# dependencies = [\n#   \"requests<3\",\n#   \"rich\",\n# ]\n# ///\n\nimport requests";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(
            new[] { "requests<3", "rich" }, metadata.Requirements.ToArray());
    }

    [TestMethod]
    public void Read_FindsASingleLineDependencyArray()
    {
        var code = "# /// script\n# dependencies = [\"httpx\", \"typer\"]\n# ///";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(new[] { "httpx", "typer" }, metadata.Requirements.ToArray());
    }

    [TestMethod]
    public void Read_AcceptsSingleQuotes()
    {
        var code = "# /// script\n# dependencies = ['httpx']\n# ///";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(new[] { "httpx" }, metadata.Requirements.ToArray());
    }

    [TestMethod]
    public void Read_AcceptsATrailingComma()
    {
        var code = "# /// script\n# dependencies = [\"httpx\",]\n# ///";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(new[] { "httpx" }, metadata.Requirements.ToArray());
    }

    [TestMethod]
    public void Read_FindsTheInterpreterFloor()
    {
        var code = "# /// script\n# requires-python = \">=3.11\"\n# dependencies = [\"httpx\"]\n# ///";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        Assert.AreEqual(">=3.11", metadata.RequiresPython);
    }

    [TestMethod]
    public void Read_IgnoresKeysItDoesNotActOn()
    {
        var code = "# /// script\n# requires-python = \">=3.9\"\n# [tool.something]\n# setting = \"value\"\n# ///";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        Assert.AreEqual(">=3.9", metadata.RequiresPython);
        Assert.AreEqual(0, metadata.Requirements.Count);
    }

    [TestMethod]
    public void Read_DoesNotConfuseASimilarlyNamedKey()
    {
        var code = "# /// script\n# dependencies-extra = [\"nope\"]\n# ///";

        Assert.IsFalse(Pep723Block.TryRead(code, out _));
    }

    [TestMethod]
    public void Read_ReportsNoBlockWhenThereIsNone()
    {
        Assert.IsFalse(Pep723Block.TryRead("import os", out _));
        Assert.IsFalse(Pep723Block.TryRead("", out _));
        Assert.IsFalse(Pep723Block.TryRead(null, out _));
    }

    [TestMethod]
    public void Read_ReportsNoBlockWhenItIsNeverClosed()
    {
        // Reading it anyway would let an unterminated block swallow the rest of the cell.
        var code = "# /// script\n# dependencies = [\"httpx\"]\nimport httpx";

        Assert.IsFalse(Pep723Block.TryRead(code, out _));
    }

    [TestMethod]
    public void Read_ReportsNoBlockWhenTheArrayIsNeverClosed()
    {
        // A partial list would install what the author did not finish declaring.
        var code = "# /// script\n# dependencies = [\n#   \"httpx\",\n# ///";

        Assert.IsFalse(Pep723Block.TryRead(code, out _));
    }

    [TestMethod]
    public void Read_ReportsNoBlockForAnEmptyDependencyList()
    {
        var code = "# /// script\n# dependencies = []\n# ///";

        Assert.IsFalse(Pep723Block.TryRead(code, out _));
    }

    [TestMethod]
    public void Read_ReportsNoBlockWhenCodeInterruptsIt()
    {
        // The block is contiguous comment lines terminated by the marker. Code before the
        // marker means it was never closed, so nothing is read rather than part of it.
        var code = "# /// script\n# dependencies = [\"httpx\"]\nimport os\n# ///";

        Assert.IsFalse(Pep723Block.TryRead(code, out _));
    }

    [TestMethod]
    public void Read_StopsAtTheClosingMarkerAndIgnoresTheCodeBelow()
    {
        var code = "# /// script\n# dependencies = [\"httpx\"]\n# ///\nimport os\n# dependencies = [\"nope\"]";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(new[] { "httpx" }, metadata.Requirements.ToArray());
    }

    [TestMethod]
    public void Read_HandlesWindowsLineEndings()
    {
        var code = "# /// script\r\n# dependencies = [\"httpx\"]\r\n# ///\r\n";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(new[] { "httpx" }, metadata.Requirements.ToArray());
    }

    [TestMethod]
    public void Read_IgnoresTheBlockWhenItIsNotFirstInTheCell()
    {
        // Placement is not enforced: a block anywhere in the cell still declares the cell's
        // dependencies, which is the only thing the install path uses it for.
        var code = "import os\n\n# /// script\n# dependencies = [\"httpx\"]\n# ///";

        Assert.IsTrue(Pep723Block.TryRead(code, out var metadata));
        CollectionAssert.AreEqual(new[] { "httpx" }, metadata.Requirements.ToArray());
    }
}
