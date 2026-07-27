using Verso.Python.Kernel;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class PythonKernelOptionsTests
{
    /// <summary>
    /// The shared-library option outlived the runtime that read it. It stays settable so an
    /// embedder still compiles, and the kernel says once that it is doing nothing, so the value
    /// has to survive being set.
    /// </summary>
    [TestMethod]
    public void PythonDll_IsObsoleteButStillRoundTrips()
    {
#pragma warning disable CS0618
        Assert.IsNull(new PythonKernelOptions().PythonDll);

        var options = new PythonKernelOptions { PythonDll = "/usr/lib/libpython3.12.so" };

        Assert.AreEqual("/usr/lib/libpython3.12.so", options.PythonDll);
#pragma warning restore CS0618
    }

    [TestMethod]
    public void Default_DefaultImports_ContainsExpectedModules()
    {
        var options = new PythonKernelOptions();
        var imports = options.DefaultImports;

        CollectionAssert.Contains(imports.ToList(), "sys");
        CollectionAssert.Contains(imports.ToList(), "os");
        CollectionAssert.Contains(imports.ToList(), "io");
        CollectionAssert.Contains(imports.ToList(), "math");
        CollectionAssert.Contains(imports.ToList(), "json");
    }

    [TestMethod]
    public void Default_PublishVariables_IsTrue()
    {
        var options = new PythonKernelOptions();
        Assert.IsTrue(options.PublishVariables);
    }

    [TestMethod]
    public void Default_InjectVariables_IsTrue()
    {
        var options = new PythonKernelOptions();
        Assert.IsTrue(options.InjectVariables);
    }

    [TestMethod]
    public void WithExpression_OverridesDefaults()
    {
        var options = new PythonKernelOptions() with
        {
            PythonExecutable = "/usr/bin/python3.12",
            PublishVariables = false
        };

        Assert.AreEqual("/usr/bin/python3.12", options.PythonExecutable);
        Assert.IsFalse(options.PublishVariables);
        Assert.IsTrue(options.InjectVariables); // unchanged
    }
}
