using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BuildnBits.Usage.Core.JsonRpc;

/// <summary>
/// Best-effort Windows job ownership for provider processes. Closing the job
/// handle terminates any provider descendants left behind by an app crash.
/// </summary>
internal sealed class WindowsProcessJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private SafeFileHandle? _handle;

    private WindowsProcessJob(SafeFileHandle handle) => _handle = handle;

    public static WindowsProcessJob? Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var raw = CreateJobObject(IntPtr.Zero, null);
        if (raw == IntPtr.Zero)
        {
            return null;
        }

        var handle = new SafeFileHandle(raw, ownsHandle: true);
        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        if (!SetInformationJobObject(
                handle,
                JobObjectExtendedLimitInformationClass,
                ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            handle.Dispose();
            return null;
        }

        return new WindowsProcessJob(handle);
    }

    public bool TryAssign(Process process)
    {
        var handle = _handle;
        if (handle is null || handle.IsInvalid)
        {
            return false;
        }

        try
        {
            return AssignProcessToJobObject(handle, process.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A process already attached to a restrictive parent job cannot
            // always be reassigned. The normal Process tree cleanup remains.
            return false;
        }
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeHandle job,
        uint jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
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
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
