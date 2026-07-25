using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Verso.Python.Host;

/// <summary>
/// Subprocess spawned through the native process API so it can be placed in its own process group
/// at creation time. Everything after creation, output capture, waiting, and termination, is done
/// through managed safe-handle wrappers to keep the native surface small.
/// </summary>
/// <remarks>Instantiated only on Windows; the platform check lives in <see cref="HostProcessLauncher"/>.</remarks>
internal sealed class WindowsHostProcessHandle : IHostProcessHandle
{
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const int HANDLE_FLAG_INHERIT = 0x00000001;
    private const int CREATE_NEW_PROCESS_GROUP = 0x00000200;
    private const int CTRL_BREAK_EVENT = 1;
    private const int CREATE_NO_WINDOW = 0x08000000;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint STILL_ACTIVE = 259;

    private readonly SafeProcessHandle _processHandle;
    private readonly SafeFileHandle _stdinWrite;
    private readonly ManualResetEvent _exitedEvent;
    private readonly RegisteredWaitHandle _registeredWait;
    private readonly TaskCompletionSource _exitedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _sharesConsole;
    private bool _disposed;

    public int Id { get; }
    public TextReader StandardOutput { get; }
    public TextReader StandardError { get; }

    public WindowsHostProcessHandle(HostProcessStartInfo startInfo)
    {
        var security = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = 1,
        };

        CreatePipeOrThrow(out var stdoutRead, out var stdoutWrite, ref security);
        CreatePipeOrThrow(out var stderrRead, out var stderrWrite, ref security);
        CreatePipeOrThrow(out var stdinRead, out var stdinWrite, ref security);

        // The parent-side ends must not be inherited by the child.
        MakeNonInheritable(stdoutRead);
        MakeNonInheritable(stderrRead);
        MakeNonInheritable(stdinWrite);

        var startup = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            dwFlags = STARTF_USESTDHANDLES,
            hStdInput = stdinRead.DangerousGetHandle(),
            hStdOutput = stdoutWrite.DangerousGetHandle(),
            hStdError = stderrWrite.DangerousGetHandle(),
        };

        var commandLine = BuildCommandLine(startInfo.Executable, startInfo.Arguments);
        var environmentBlock = BuildEnvironmentBlock(startInfo.Environment);
        var environmentHandle = GCHandle.Alloc(environmentBlock, GCHandleType.Pinned);

        // A console control event only reaches a child that shares the sender's console, so when
        // this process has one the child inherits it and the interrupt can travel as a signal. No
        // window appears, because the console already exists. Where there is none, asking for one
        // would put a black window on screen for every editor session, so the child is spawned
        // windowless and the interrupt travels as a protocol message instead.
        _sharesConsole = GetConsoleWindow() != IntPtr.Zero;
        var creationFlags = CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT;
        if (!_sharesConsole)
            creationFlags |= CREATE_NO_WINDOW;

        bool created;
        PROCESS_INFORMATION processInfo;
        try
        {
            created = CreateProcess(
                lpApplicationName: null,
                lpCommandLine: commandLine,
                lpProcessAttributes: IntPtr.Zero,
                lpThreadAttributes: IntPtr.Zero,
                bInheritHandles: true,
                dwCreationFlags: creationFlags,
                lpEnvironment: environmentHandle.AddrOfPinnedObject(),
                lpCurrentDirectory: startInfo.WorkingDirectory,
                lpStartupInfo: ref startup,
                lpProcessInformation: out processInfo);
        }
        finally
        {
            environmentHandle.Free();
            GC.KeepAlive(stdinRead);
            GC.KeepAlive(stdoutWrite);
            GC.KeepAlive(stderrWrite);
        }

        if (!created)
        {
            var error = Marshal.GetLastWin32Error();
            stdoutRead.Dispose(); stdoutWrite.Dispose();
            stderrRead.Dispose(); stderrWrite.Dispose();
            stdinRead.Dispose(); stdinWrite.Dispose();
            throw new PythonHostException(
                $"Failed to start the Python subprocess '{startInfo.Executable}' (Win32 error {error}).");
        }

        // The child now owns the write ends of stdout/stderr and the read end of stdin; releasing
        // the parent's copies is what lets the reader see end of stream when the child exits.
        stdoutWrite.Dispose();
        stderrWrite.Dispose();
        stdinRead.Dispose();
        if (processInfo.hThread != IntPtr.Zero)
            CloseHandle(processInfo.hThread);

        Id = processInfo.dwProcessId;
        _processHandle = new SafeProcessHandle(processInfo.hProcess, ownsHandle: true);
        _stdinWrite = stdinWrite;

        StandardOutput = new StreamReader(new FileStream(stdoutRead, FileAccess.Read), Encoding.UTF8);
        StandardError = new StreamReader(new FileStream(stderrRead, FileAccess.Read), Encoding.UTF8);

        // A process handle becomes signaled on exit; register a one-shot wait to complete the task.
        _exitedEvent = new ManualResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle(_processHandle.DangerousGetHandle(), ownsHandle: false),
        };
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _exitedEvent,
            static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            _exitedTcs,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    public bool HasExited
    {
        get
        {
            if (_exitedTcs.Task.IsCompleted) return true;
            return GetExitCodeProcess(_processHandle, out var code) && code != STILL_ACTIVE;
        }
    }

    public int ExitCode
    {
        get
        {
            if (!GetExitCodeProcess(_processHandle, out var code))
                throw new PythonHostException("Unable to read the subprocess exit code.");
            if (code == STILL_ACTIVE && !_exitedTcs.Task.IsCompleted)
                throw new InvalidOperationException("The subprocess has not exited.");
            return unchecked((int)code);
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => _exitedTcs.Task.WaitAsync(cancellationToken);

    public bool TrySendInterrupt()
    {
        // Delivery requires a console shared with the child, and the event is silently dropped
        // without one: the call still succeeds, it simply reaches no process. Reporting that as a
        // delivered interrupt would leave the cell waiting out the grace period before anything
        // stopped it, so a windowless host declines here and the caller uses the message path.
        if (!_sharesConsole || HasExited) return false;

        // CREATE_NEW_PROCESS_GROUP made the child its own group, so a CTRL_BREAK event can be
        // addressed to it by id, and this process, being in a different group, does not receive it.
        try { return GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, Id); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void Kill()
    {
        if (HasExited) return;
        try { TerminateProcess(_processHandle, 1); }
        catch (ObjectDisposedException) { /* already gone */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registeredWait.Unregister(null);
        try { StandardOutput.Dispose(); } catch { /* best effort */ }
        try { StandardError.Dispose(); } catch { /* best effort */ }
        _stdinWrite.Dispose();
        _exitedEvent.Dispose();
        _processHandle.Dispose();
    }

    private static void CreatePipeOrThrow(out SafeFileHandle read, out SafeFileHandle write, ref SECURITY_ATTRIBUTES security)
    {
        if (!CreatePipe(out read, out write, ref security, 0))
            throw new PythonHostException($"Failed to create a pipe (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private static void MakeNonInheritable(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HANDLE_FLAG_INHERIT, 0))
            throw new PythonHostException($"Failed to configure a pipe handle (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private static char[] BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, executable);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }
        builder.Append('\0'); // CreateProcess requires a null-terminated, mutable buffer.
        return builder.ToString().ToCharArray();
    }

    // Quote a single argument following the parsing rules used by the C runtime.
    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        for (var i = 0; ; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(argument[i]);
            }
        }
        builder.Append('"');
    }

    // A supplied environment does not inherit the parent's automatically, so merge the current
    // environment with the overrides. The block is a sorted, double-null-terminated run of
    // key=value entries in UTF-16.
    private static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            merged[(string)entry.Key] = entry.Value as string ?? "";
        foreach (var pair in overrides)
            merged[pair.Key] = pair.Value;

        var builder = new StringBuilder();
        foreach (var pair in merged)
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        builder.Append('\0');
        return builder.ToString().ToCharArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle hObject, int dwMask, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        char[] lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(int dwCtrlEvent, int dwProcessGroupId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
