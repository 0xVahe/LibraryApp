using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LibraryApp.Benchmark;

/// <summary>
/// The machine the run was made on: timings of different machines cannot be compared, so the report records it. The
/// machine and user names are left out (personal data).
/// </summary>
public sealed record MachineInfo(
    string Os,
    string Architecture,
    string Cpu,
    int LogicalCores,
    double TotalMemoryGb,
    double? FreeMemoryGb,
    TimeSpan Uptime,
    string Runtime,
    bool ServerGc)
{
    public static MachineInfo Read() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        ReadCpuName(),
        Environment.ProcessorCount,
        Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024 / 1024, 1),
        ReadFreeMemoryGb(),
        TimeSpan.FromMilliseconds(Environment.TickCount64),
        RuntimeInformation.FrameworkDescription,
        System.Runtime.GCSettings.IsServerGC);

    private static string ReadCpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) is string name
                    ? name.Trim()
                    : "unknown";

            if (OperatingSystem.IsLinux())
                return File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim() ?? "unknown";

            if (OperatingSystem.IsMacOS())
                return Run("sysctl", "-n machdep.cpu.brand_string") ?? "unknown";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The CPU name is information only.
        }

        return "unknown";
    }

    private static double? ReadFreeMemoryGb()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                return GlobalMemoryStatusEx(ref status) ? Math.Round(status.AvailablePhysical / 1024d / 1024 / 1024, 1) : null;
            }

            if (OperatingSystem.IsLinux())
            {
                var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemAvailable:", StringComparison.Ordinal));
                return line is null ? null : Math.Round(long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) / 1024d / 1024, 1);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Free memory is information only.
        }

        return null;
    }

    internal static string? Run(string file, string arguments, string? workingDirectory = null)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            });
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;   // the program is not installed
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
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
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

/// <summary>The code that was measured: commit, tag and whether the working tree had changes.</summary>
public sealed record RepositoryInfo(string Root, string Commit, string ShortCommit, string Branch, string? Tag, bool Dirty)
{
    public static RepositoryInfo Read()
    {
        var root = FindRoot();
        string? Git(string arguments) => MachineInfo.Run("git", arguments, root);

        var commit = Git("rev-parse HEAD") ?? "unknown";
        return new RepositoryInfo(
            root,
            commit,
            commit.Length >= 7 ? commit[..7] : commit,
            Git("rev-parse --abbrev-ref HEAD") ?? "unknown",
            Git("describe --tags --exact-match HEAD"),
            !string.IsNullOrEmpty(Git("status --porcelain")));
    }

    /// <summary>The folder with <c>LibraryApp.sln</c>, searched upwards from the working directory.</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LibraryApp.sln")))
                return directory.FullName;
        }

        throw new BenchmarkException("run the benchmark from inside the repository (LibraryApp.sln was not found above the current folder).");
    }
}
