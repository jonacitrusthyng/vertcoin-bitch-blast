#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace IdleGpu;

internal sealed class MinerWorker : IDisposable
{
    private readonly string executable;
    private readonly string[] arguments;
    private readonly object gate = new();
    private SafeFileHandle? job;
    private SafeFileHandle? process;
    private int processId;
    private bool disposed;

    public MinerWorker(string executable, params string[] arguments)
    {
        this.executable = Path.GetFullPath(executable);
        this.arguments = arguments;
    }

    public bool Running
    {
        get { lock (gate) return IsRunning(); }
    }

    public int? ProcessId
    {
        get { lock (gate) return IsRunning() ? processId : null; }
    }

    private bool IsRunning()
    {
        if (process == null) return false;
        return WaitForSingleObject(process, 0) switch
        {
            WaitObject => false,
            WaitTimeout => true,
            _ => throw NativeError("checking the miner process")
        };
    }

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRunning()) return;
            StopCore();
            if (!File.Exists(executable))
                throw new FileNotFoundException("Miner executable missing.", executable);

            var newJob = CreateJobObjectW(0, null);
            try
            {
                if (newJob.IsInvalid) throw NativeError("creating the miner job");
                var limits = new ExtendedLimits();
                limits.Basic.LimitFlags = KillOnJobClose;
                if (!SetInformationJobObject(newJob, JobExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
                    throw NativeError("setting miner job cleanup");

                var started = LaunchInJob(newJob);
                process = new SafeFileHandle(started.Process, ownsHandle: true);
                processId = unchecked((int)started.ProcessId);
                CloseHandle(started.Thread);
                job = newJob;
            }
            catch
            {
                newJob.Dispose();
                throw;
            }
        }
    }

    private ProcessInformation LaunchInJob(SafeFileHandle newJob)
    {
        // Creation-time assignment prevents an orphan if the controller exits
        // between starting the worker and attaching it to the job.
        nuint bytes = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
        if (bytes == 0) throw NativeError("sizing the process attribute list");
        nint attributes = Marshal.AllocHGlobal(checked((nint)bytes));
        nint handleSlot = 0;
        bool initialized = false;
        try
        {
            initialized = InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes);
            if (!initialized) throw NativeError("initializing the process attribute list");
            handleSlot = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(handleSlot, newJob.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, JobListAttribute, handleSlot, (nuint)IntPtr.Size, 0, 0))
                throw NativeError("assigning the miner job at launch");

            var startup = new StartupInfoEx { AttributeList = attributes };
            startup.Startup.Size = (uint)Marshal.SizeOf<StartupInfoEx>();
            // These are ordinary Windows file paths. CreateProcess receives the
            // executable separately, and the quoted argv contains no shell syntax.
            var command = new StringBuilder(Quote(executable) + " " + string.Join(" ", arguments.Select(Quote)));
            if (!CreateProcessW(executable, command, 0, 0, false,
                ExtendedStartupInfoPresent | CreateNoWindow, 0, Path.GetDirectoryName(executable)!,
                ref startup, out var result))
                throw NativeError("launching VerthashMiner");
            return result;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(handleSlot);
            Marshal.FreeHGlobal(attributes);
            GC.KeepAlive(newJob);
        }
    }

    public void Stop()
    {
        lock (gate) StopCore();
    }

    private void StopCore()
    {
        job?.Dispose();
        job = null;
        if (process == null) return;
        uint status = WaitForSingleObject(process, 15000);
        if (status == WaitTimeout)
            throw new TimeoutException("The miner has not exited within 15 seconds.");
        if (status != WaitObject) throw NativeError("waiting for the miner to exit");
        process.Dispose();
        process = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            try { StopCore(); }
            finally { process?.Dispose(); process = null; }
        }
    }

    internal static string Quote(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("Argument contains a null character.");
        var quoted = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
    private static Win32Exception NativeError(string operation)
    {
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        return new Win32Exception(error.NativeErrorCode, $"Error {operation}: {error.Message}");
    }

    private const uint WaitObject = 0, WaitTimeout = 258;
    private const uint KillOnJobClose = 0x2000, JobExtendedLimitInformation = 9;
    private const uint ExtendedStartupInfoPresent = 0x00080000, CreateNoWindow = 0x08000000;
    private const nuint JobListAttribute = 0x0002000D;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCharacterCount, YCharacterCount, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public nint ReservedBuffer, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, uint infoClass, ref ExtendedLimits data, uint size);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(nint list, uint count, uint flags, ref nuint bytes);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint outputSize);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string executable, StringBuilder command, nint processSecurity, nint threadSecurity,
        bool inheritHandles, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation result);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool CloseHandle(nint handle);
}


