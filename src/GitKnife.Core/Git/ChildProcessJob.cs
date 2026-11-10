using System.Diagnostics;
using System.Runtime.InteropServices;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Git;

/// <summary>
/// GitKnife が起動した git を 1 つの Job Object に入れ、GitKnife が終わったら（落ちても）その子孫ごと終わらせる
/// （JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE。ジョブのハンドルはプロセスが終わると OS が閉じる）。
/// git が起動した git-remote-https や credential helper もジョブに入るので、認証待ちで止まった fetch が残らない。
/// GitKnife 自身はジョブに入れない（外部のエディタや端末、更新のインストーラは GitKnife の終了で止めない）。
/// </summary>
internal static class ChildProcessJob
{
    private static readonly Lazy<IntPtr> Handle = new(Create);

    /// <summary>起動した直後のプロセスをジョブに入れる。入れられなくても git の実行は続ける。</summary>
    public static void Add(Process process)
    {
        if (!OperatingSystem.IsWindows()) return;
        var job = Handle.Value;
        if (job == IntPtr.Zero) return;
        try
        {
            if (!AssignProcessToJobObject(job, process.Handle))
                Log.Debug($"AssignProcessToJobObject failed: {Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static IntPtr Create()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                Log.Debug($"SetInformationJobObject failed: {Marshal.GetLastWin32Error()}");
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
        return job;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
