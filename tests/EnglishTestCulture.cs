using System.Globalization;
// Written out rather than relied on: most test projects import this namespace globally, but
// not all of them, and this file is compiled into every one.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Verso.Tests.Shared;

/// <summary>
/// Pins every test run to English.
/// </summary>
/// <remarks>
/// Assertions are written against the English wording of the interface, and a test process
/// otherwise takes its language from the machine it runs on. Without this, the suite passes in
/// London and fails in Berlin, which is a difference nobody would think to look for.
/// <para>
/// Only the interface language is pinned. Formatting is left alone, so a test that depends on
/// how a number or a date is written still fails on a machine where that differs, which is a
/// real difference worth catching rather than hiding.
/// </para>
/// <para>
/// This file is compiled into every test assembly by <c>tests/Directory.Build.props</c>. Each
/// assembly needs its own copy because the hook runs once per assembly, not once per run.
/// </para>
/// </remarks>
[TestClass]
public static class EnglishTestCulture
{
    // Fully qualified, because bUnit brings a TestContext of its own into scope in the
    // component test assembly and the two names collide.
    [AssemblyInitialize]
    public static void Pin(Microsoft.VisualStudio.TestTools.UnitTesting.TestContext context)
    {
        _ = context;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }
}
