using System.Diagnostics.CodeAnalysis;

namespace Verso.Python.Tests;

/// <summary>
/// Decides what a missing prerequisite means, in one place, so the tests that need a real
/// interpreter cannot quietly stop running.
/// <para>
/// On a developer machine a test that needs Python and cannot find it has nothing useful to say,
/// so it reports inconclusive and gets out of the way. On a build server it fails instead. Those
/// runners exist to cover the platforms one machine cannot, most of all the Windows process
/// launcher, which is the only part of the host that no developer machine here can exercise at
/// all. A skip there is indistinguishable from a pass and hides exactly the code that has no
/// other coverage.
/// </para>
/// <para>
/// Only prerequisites the build establishes deliberately belong here. Anything that has to reach
/// the network at test time stays inconclusive, so a package index having a bad day cannot turn
/// into a red build.
/// </para>
/// </summary>
internal static class Prerequisite
{
    /// <summary>Whether this run is on a build server, which every mainstream one reports.</summary>
    public static bool OnBuildServer
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));

    /// <summary>
    /// Ends the test: failed on a build server, inconclusive elsewhere. The reason is stated as
    /// the thing that was missing, because it is quoted into both outcomes.
    /// </summary>
    [DoesNotReturn]
    public static void Missing(string reason) => Missing(reason, OnBuildServer);

    /// <summary>The decision itself, separated from where it reads so it can be tested.</summary>
    [DoesNotReturn]
    internal static void Missing(string reason, bool onBuildServer)
    {
        if (onBuildServer)
        {
            Assert.Fail(
                $"{reason} The build is expected to provide it, so the tests that need it fail " +
                "here rather than reporting inconclusive, which would leave the run green with " +
                "nothing exercised.");
        }

        Assert.Inconclusive($"{reason} Skipping the tests that need it.");
    }
}
