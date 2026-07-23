using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RakionBotHost;

internal sealed class ChildProcessJob : IDisposable
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x00002000;
    private IntPtr _handle;

    private ChildProcessJob(IntPtr handle)
    {
        _handle = handle;
    }

    public static ChildProcessJob Create()
    {
        IntPtr handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject falhou.");
        var job = new ChildProcessJob(handle);
        try
        {
            job.EnableKillOnClose();
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.Handle))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "AssignProcessToJobObject falhou.");
    }

    public void Dispose()
    {
        IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    private void EnableKillOnClose()
    {
        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = KillOnJobClose
            }
        };
        int length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, buffer, false);
            if (!SetInformationJobObject(
                    _handle, ExtendedLimitInformation, buffer, (uint)length))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(), "SetInformationJobObject falhou.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
