using System.Runtime.InteropServices;

namespace EPFOptimizerPro.Services;

public sealed class SystemMetrics
{
    private FileTime _previousIdle;
    private FileTime _previousKernel;
    private FileTime _previousUser;
    private bool _hasPrevious;

    public double CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        if (!_hasPrevious)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasPrevious = true;
            return 0;
        }

        ulong idleDiff = ToUInt64(idle) - ToUInt64(_previousIdle);
        ulong kernelDiff = ToUInt64(kernel) - ToUInt64(_previousKernel);
        ulong userDiff = ToUInt64(user) - ToUInt64(_previousUser);
        ulong total = kernelDiff + userDiff;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        if (total == 0)
        {
            return 0;
        }

        double cpu = (double)(total - idleDiff) * 100.0 / total;
        return Math.Clamp(Math.Round(cpu, 0), 0, 100);
    }

    public double MemoryPercent()
    {
        var memory = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memory))
        {
            return 0;
        }

        return Math.Clamp(memory.MemoryLoad, 0, 100);
    }

    private static ulong ToUInt64(FileTime time)
    {
        return ((ulong)time.HighDateTime << 32) | time.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
