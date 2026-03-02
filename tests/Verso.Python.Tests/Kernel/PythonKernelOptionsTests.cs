using Verso.Python.Kernel;

namespace Verso.Python.Tests.Kernel;

[TestClass]
public sealed class PythonKernelOptionsTests
{
    [TestMethod]
    public void Default_PythonDll_IsNull()
    {
        var options = new PythonKernelOptions();
        Assert.IsNull(options.PythonDll);
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
            PythonDll = "/usr/lib/libpython3.12.so",
            PublishVariables = false
        };

        Assert.AreEqual("/usr/lib/libpython3.12.so", options.PythonDll);
        Assert.IsFalse(options.PublishVariables);
        Assert.IsTrue(options.InjectVariables); // unchanged
    }
}
