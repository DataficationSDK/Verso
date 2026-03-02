using System.Runtime.InteropServices;
using Verso.Python.Kernel;

namespace Verso.Python.Tests.VenvManager;

[TestClass]
public sealed class VenvManagerTests
{
    [TestMethod]
    public void SitePackagesStoreKey_HasExpectedValue()
    {
        Assert.AreEqual("__verso_pip_site_packages", Verso.Python.Kernel.VenvManager.SitePackagesStoreKey);
    }

    [TestMethod]
    public void GetPythonPath_ContainsVenvDirectory()
    {
        var path = Verso.Python.Kernel.VenvManager.GetPythonPath();
        Assert.IsTrue(path.Contains("venv"),
            $"Expected venv in path, got: {path}");
    }

    [TestMethod]
    public void GetPythonPath_PlatformAppropriate()
    {
        var path = Verso.Python.Kernel.VenvManager.GetPythonPath();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.IsTrue(path.Contains("Scripts") && path.EndsWith("python.exe"),
                $"Expected Windows path with Scripts/python.exe, got: {path}");
        }
        else
        {
            Assert.IsTrue(path.Contains("bin") && path.EndsWith("python3"),
                $"Expected Unix path with bin/python3, got: {path}");
        }
    }
}
