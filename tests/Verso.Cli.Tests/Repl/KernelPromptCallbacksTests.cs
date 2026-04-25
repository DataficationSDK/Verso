using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Cli.Repl.Prompt;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class KernelPromptCallbacksTests
{
    [TestMethod]
    public void ShouldSubmitNow_BareContent_WaitsForTwoTrailingNewlines()
    {
        // Content typed, first Enter (no trailing newlines yet) must NOT submit —
        // the user expects a newline and the chance to keep typing.
        Assert.IsFalse(KernelPromptCallbacks.ShouldSubmitNow("1+1", caret: 3));

        // After the first Enter produces one trailing newline, the second Enter
        // still adds another blank line rather than submitting.
        Assert.IsFalse(KernelPromptCallbacks.ShouldSubmitNow("1+1\n", caret: 4));

        // With two trailing newlines already present, the THIRD Enter submits.
        Assert.IsTrue(KernelPromptCallbacks.ShouldSubmitNow("1+1\n\n", caret: 5));
    }

    [TestMethod]
    public void ShouldSubmitNow_MetaCommand_SubmitsImmediately()
    {
        // .exit and friends are single-line and users want a single Enter to dispatch them.
        Assert.IsTrue(KernelPromptCallbacks.ShouldSubmitNow(".exit", caret: 5));
        Assert.IsTrue(KernelPromptCallbacks.ShouldSubmitNow(".list kernels", caret: 13));
    }

    [TestMethod]
    public void ShouldSubmitNow_MultilineMetaCommand_DoesNotSubmit()
    {
        // If the user somehow injected a newline into a meta-command buffer,
        // treat it like code and wait for the double-blank signal.
        Assert.IsFalse(KernelPromptCallbacks.ShouldSubmitNow(".help\nextra", caret: 11));
    }

    [TestMethod]
    public void ShouldSubmitNow_CaretMidText_DoesNotSubmit()
    {
        // An Enter in the middle of the buffer is for splitting lines — never submits.
        Assert.IsFalse(KernelPromptCallbacks.ShouldSubmitNow("1+1\n\n", caret: 3));
    }

    [TestMethod]
    public void ShouldSubmitNow_CrlfIsEquivalentToLf()
    {
        Assert.IsTrue(KernelPromptCallbacks.ShouldSubmitNow("1+1\r\n\r\n", caret: 7));
    }

    [TestMethod]
    public void ShouldSubmitNow_OnlyWhitespace_DoesNotSubmit()
    {
        // Empty or whitespace-only buffer never produces a submission, regardless of newlines.
        Assert.IsFalse(KernelPromptCallbacks.ShouldSubmitNow("\n\n", caret: 2));
        Assert.IsFalse(KernelPromptCallbacks.ShouldSubmitNow("", caret: 0));
    }
}
