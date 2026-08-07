using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CSharpAiCli.AppHost.Protocol;

internal sealed class WindowsPseudoConsoleSession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint HandleFlagInherit = 0x00000001;

    private readonly IntPtr pseudoConsole;
    private readonly IntPtr processHandle;
    private readonly IntPtr threadHandle;
    private readonly IntPtr jobHandle;
    private readonly SafeFileHandle input;
    private readonly SafeFileHandle output;
    private readonly SafeFileHandle pseudoConsoleInput;
    private readonly SafeFileHandle pseudoConsoleOutput;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task outputPump;
    private readonly Task exitWatcher;
    private readonly Action<ReadOnlyMemory<byte>> outputHandler;
    private readonly Action<int> exitHandler;
    private int disposed;

    private WindowsPseudoConsoleSession(
        IntPtr pseudoConsole,
        IntPtr processHandle,
        IntPtr threadHandle,
        IntPtr jobHandle,
        SafeFileHandle inputWrite,
        SafeFileHandle outputRead,
        SafeFileHandle inputRead,
        SafeFileHandle outputWrite,
        Action<ReadOnlyMemory<byte>> outputHandler,
        Action<int> exitHandler)
    {
        this.pseudoConsole = pseudoConsole;
        this.processHandle = processHandle;
        this.threadHandle = threadHandle;
        this.jobHandle = jobHandle;
        this.outputHandler = outputHandler;
        this.exitHandler = exitHandler;
        input = inputWrite;
        output = outputRead;
        pseudoConsoleInput = inputRead;
        pseudoConsoleOutput = outputWrite;
        outputPump = Task.Run(PumpOutput);
        exitWatcher = Task.Run(WatchExit);
    }

    public static WindowsPseudoConsoleSession Start(
        string commandLine,
        string workingDirectory,
        int cols,
        int rows,
        IReadOnlyDictionary<string, string> environment,
        Action<ReadOnlyMemory<byte>> outputHandler,
        Action<int> exitHandler)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ConPTY is available only on Windows.");
        IntPtr inputRead = IntPtr.Zero;
        IntPtr inputWrite = IntPtr.Zero;
        IntPtr outputRead = IntPtr.Zero;
        IntPtr outputWrite = IntPtr.Zero;
        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        ProcessInformation process = default;
        IntPtr job = IntPtr.Zero;
        try
        {
            ThrowLastErrorIfFalse(CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0));
            ThrowLastErrorIfFalse(CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0));
            ThrowLastErrorIfFalse(SetHandleInformation(inputWrite, HandleFlagInherit, 0));
            ThrowLastErrorIfFalse(SetHandleInformation(outputRead, HandleFlagInherit, 0));

            ThrowHResult(CreatePseudoConsole(
                new Coord((short)cols, (short)rows),
                inputRead,
                outputWrite,
                0,
                out pseudoConsole));

            nuint attributeBytes = 0;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
            if (attributeBytes == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributeList = Marshal.AllocHGlobal(checked((int)attributeBytes));
            ThrowLastErrorIfFalse(InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeBytes));
            ThrowLastErrorIfFalse(UpdateProcThreadAttribute(
                attributeList,
                0,
                (IntPtr)ProcThreadAttributePseudoConsole,
                pseudoConsole,
                (nuint)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero));

            StartupInfoEx startup = new();
            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startup.StartupInfo.dwFlags = StartfUseStdHandles;
            startup.lpAttributeList = attributeList;
            environmentBlock = CreateEnvironmentBlock(environment);
            SecurityAttributes processAttributes = new() { nLength = Marshal.SizeOf<SecurityAttributes>() };
            SecurityAttributes threadAttributes = new() { nLength = Marshal.SizeOf<SecurityAttributes>() };
            bool created = CreateProcessW(
                null,
                commandLine,
                ref processAttributes,
                ref threadAttributes,
                false,
                ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                environmentBlock,
                workingDirectory,
                ref startup,
                out process);
            ThrowLastErrorIfFalse(created);

            job = CreateKillOnCloseJob();
            ThrowLastErrorIfFalse(AssignProcessToJobObject(job, process.hProcess));

            SafeFileHandle ownedInput = new(inputWrite, ownsHandle: true);
            inputWrite = IntPtr.Zero;
            SafeFileHandle ownedOutput = new(outputRead, ownsHandle: true);
            outputRead = IntPtr.Zero;
            SafeFileHandle ownedPtyInput = new(inputRead, ownsHandle: true);
            inputRead = IntPtr.Zero;
            SafeFileHandle ownedPtyOutput = new(outputWrite, ownsHandle: true);
            outputWrite = IntPtr.Zero;
            WindowsPseudoConsoleSession result = new(
                pseudoConsole,
                process.hProcess,
                process.hThread,
                job,
                ownedInput,
                ownedOutput,
                ownedPtyInput,
                ownedPtyOutput,
                outputHandler,
                exitHandler);
            pseudoConsole = IntPtr.Zero;
            process = default;
            job = IntPtr.Zero;
            return result;
        }
        catch
        {
            if (process.hProcess != IntPtr.Zero) TerminateProcess(process.hProcess, 1);
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero) DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
            if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
            if (inputRead != IntPtr.Zero) CloseHandle(inputRead);
            if (inputWrite != IntPtr.Zero) CloseHandle(inputWrite);
            if (outputRead != IntPtr.Zero) CloseHandle(outputRead);
            if (outputWrite != IntPtr.Zero) CloseHandle(outputWrite);
            if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
            if (process.hProcess != IntPtr.Zero) CloseHandle(process.hProcess);
            if (pseudoConsole != IntPtr.Zero) ClosePseudoConsole(pseudoConsole);
            if (job != IntPtr.Zero) CloseHandle(job);
        }
    }

    public void Send(ReadOnlySpan<byte> bytes)
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(WindowsPseudoConsoleSession));
        byte[] buffer = bytes.ToArray();
        ThrowLastErrorIfFalse(WriteFile(input, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero));
        if (written != buffer.Length) throw new IOException("ConPTY input pipe accepted only part of the input.");
    }

    public void Resize(int cols, int rows)
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(WindowsPseudoConsoleSession));
        ThrowHResult(ResizePseudoConsole(pseudoConsole, new Coord((short)cols, (short)rows)));
    }

    public void Interrupt() => Send([0x03]);

    public Exception? OutputFailure => outputPump.IsFaulted ? outputPump.Exception?.GetBaseException() : null;

    private void PumpOutput()
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (!ReadFile(output, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error is 109 or 232) break;
                    throw new Win32Exception(error);
                }
                if (read == 0) break;
                byte[] snapshot = buffer[..checked((int)read)].ToArray();
                outputHandler(snapshot);
            }
        }
        catch (IOException) when (Volatile.Read(ref disposed) != 0)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
        }
        catch (Win32Exception) when (Volatile.Read(ref disposed) != 0)
        {
        }
    }

    private void WatchExit()
    {
        _ = WaitForSingleObject(processHandle, Infinite);
        if (GetExitCodeProcess(processHandle, out uint code)) exitHandler(unchecked((int)code));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        input.Dispose();
        ClosePseudoConsole(pseudoConsole);
        pseudoConsoleInput.Dispose();
        pseudoConsoleOutput.Dispose();
        CloseHandle(jobHandle);
        CloseHandle(threadHandle);
        CloseHandle(processHandle);
        output.Dispose();
        try { Task.WaitAll([outputPump, exitWatcher], 5_000); } catch (AggregateException) { }
        cancellation.Dispose();
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        JobObjectExtendedLimitInformation information = new();
        information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        int bytes = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr pointer = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.StructureToPtr(information, pointer, fDeleteOld: false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, pointer, (uint)bytes))
            {
                int error = Marshal.GetLastWin32Error();
                CloseHandle(job);
                throw new Win32Exception(error);
            }
            return job;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr CreateEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        string value = string.Join('\0', environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
        byte[] bytes = Encoding.Unicode.GetBytes(value);
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static void ThrowLastErrorIfFalse(bool succeeded)
    {
        if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void ThrowHResult(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
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

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, IntPtr pipeAttributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, uint bytesToRead, out uint bytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, uint bytesToWrite, out uint bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr pseudoConsole, Coord size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? applicationName,
        string commandLine,
        ref SecurityAttributes processAttributes,
        ref SecurityAttributes threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        [In] ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
