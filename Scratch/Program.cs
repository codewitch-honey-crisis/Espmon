// PerfProbe.cs — standalone diagnostic for SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION.
//
// Drop into a scratch console project (net8.0+, <AllowUnsafeBlocks>true</AllowUnsafeBlocks>)
// and run. Answers, in one shot:
//   1. Is the struct stride what we think it is?
//   2. Does NtQuerySystemInformation actually succeed, and how many records?
//   3. Are the raw counters advancing between samples?
//   4. Does kernel-includes-idle hold on this machine?
//   5. Does our per-CPU math agree with GetSystemTimes (whole-box cross-check)?

using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static unsafe partial class PerfProbe
{
    const int SystemProcessorPerformanceInformation = 8;

    [StructLayout(LayoutKind.Sequential)]
    struct SPPI
    {
        public long IdleTime;
        public long KernelTime;   // expected: includes IdleTime
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(
        int systemInformationClass, void* systemInformation,
        uint systemInformationLength, uint* returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    // Returns record count, or -1 on failure. status/ret reported via out params.
    static int Read(SPPI[] dst, out int status, out uint ret)
    {
        int stride = sizeof(SPPI);
        ret = 0;
        fixed (SPPI* p = dst)
        {
            fixed (uint* r = &ret)
            {
                status = NtQuerySystemInformation(
                    SystemProcessorPerformanceInformation, p, (uint)(dst.Length * stride), r);
            }
        }
        if (status != 0) return -1;
        return (int)(ret / (uint)stride);
    }

    static void Main()
    {
        int n = Environment.ProcessorCount;
        int stride = sizeof(SPPI);

        Console.WriteLine($"ProcessorCount      : {n}");
        Console.WriteLine($"sizeof(SPPI)        : {stride}  (expect 48)");
        Console.WriteLine($"Marshal.SizeOf<SPPI>: {Marshal.SizeOf<SPPI>()}");
        Console.WriteLine($"IntPtr.Size         : {IntPtr.Size}");
        Console.WriteLine();

        // Oversize the buffer deliberately: if the real stride is larger than we think,
        // a tight buffer would return STATUS_INFO_LENGTH_MISMATCH and mask the problem.
        var a = new SPPI[n * 2];
        var b = new SPPI[n * 2];

        int cA = Read(a, out int stA, out uint retA);
        Console.WriteLine($"sample A: status=0x{stA:X8} returnLength={retA} records={cA}");
        if (cA < 0) { Console.WriteLine("NtQuerySystemInformation FAILED — stop here."); return; }
        if (retA % (uint)stride != 0)
            Console.WriteLine($"  !! returnLength is not a multiple of stride — STRUCT LAYOUT IS WRONG");

        GetSystemTimes(out long gsIdle0, out long gsKern0, out long gsUser0);

        // Burn one core so there is guaranteed movement even on an idle box.
        var spin = new Thread(() => { var t = Environment.TickCount64; while (Environment.TickCount64 - t < 1000) { } });
        spin.IsBackground = true;
        spin.Start();
        Thread.Sleep(1000);
        spin.Join();

        int cB = Read(b, out int stB, out uint retB);
        Console.WriteLine($"sample B: status=0x{stB:X8} returnLength={retB} records={cB}");
        Console.WriteLine();
        if (cB < 0) return;

        GetSystemTimes(out long gsIdle1, out long gsKern1, out long gsUser1);

        int m = Math.Min(Math.Min(cA, cB), n);
        Console.WriteLine("cpu |        dIdle |      dKernel |        dUser |  total |  busy | load%");
        Console.WriteLine("----+--------------+--------------+--------------+--------+-------+------");

        long sumTotal = 0, sumBusy = 0;
        int stuck = 0, negative = 0;

        for (int i = 0; i < m; i++)
        {
            long dIdle = b[i].IdleTime - a[i].IdleTime;
            long dKern = b[i].KernelTime - a[i].KernelTime;
            long dUser = b[i].UserTime - a[i].UserTime;
            long total = dKern + dUser;
            long busy = total - dIdle;

            if (total == 0) stuck++;
            if (busy < 0) negative++;

            sumTotal += total;
            sumBusy += Math.Max(busy, 0);

            string load = total > 0 ? (100.0 * Math.Max(busy, 0) / total).ToString("F1") : "n/a";
            Console.WriteLine($"{i,3} | {dIdle,12} | {dKern,12} | {dUser,12} | {total,6} | {busy,5} | {load,5}");
        }

        Console.WriteLine();
        Console.WriteLine($"records with total==0 (counters not advancing): {stuck} / {m}");
        Console.WriteLine($"records with busy<0   (idle > kernel+user)   : {negative} / {m}");
        Console.WriteLine();

        // Expected: total per CPU ~= 10,000,000 (1s in 100ns ticks). Big deviation => layout wrong.
        if (m > 0)
        {
            long expected = 10_000_000;
            long actual = sumTotal / m;
            Console.WriteLine($"avg total per cpu   : {actual:N0} ticks (expect ~{expected:N0} for a 1s gap)");
            if (Math.Abs(actual - expected) > expected / 2)
                Console.WriteLine("  !! way off — buffer stride / field offsets are suspect");
        }

        Console.WriteLine($"aggregate load (ours): {(sumTotal > 0 ? (100.0 * sumBusy / sumTotal).ToString("F1") : "n/a")}%");

        long gi = gsIdle1 - gsIdle0, gk = gsKern1 - gsKern0, gu = gsUser1 - gsUser0;
        long gt = gk + gu;
        Console.WriteLine($"aggregate load (GST) : {(gt > 0 ? (100.0 * (gt - gi) / gt).ToString("F1") : "n/a")}%");
        Console.WriteLine("  (these two should be close. if GST is sane and ours is 0, the bug is in our read.)");
    }
}