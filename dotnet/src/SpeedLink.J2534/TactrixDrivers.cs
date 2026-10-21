using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SpeedLink.J2534;

public sealed record TactrixDriver(string Name, string LibraryPath)
{
    public override string ToString() => Name;
}

public static class TactrixDrivers
{
    /// <summary>Registered 32-bit Openport-compatible entries; no arbitrary DLL browsing.</summary>
    public static IReadOnlyList<TactrixDriver> Discover()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        return DiscoverWindows();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<TactrixDriver> DiscoverWindows()
    {
        var result = new List<TactrixDriver>();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var support = machine.OpenSubKey(@"SOFTWARE\PassThruSupport.04.04");
        if (support is null)
            return result;
        foreach (var keyName in support.GetSubKeyNames())
        {
            using var key = support.OpenSubKey(keyName);
            if (key is null)
                continue;
            var name = key.GetValue("Name") as string ?? keyName;
            var vendor = key.GetValue("Vendor") as string ?? "";
            var path = key.GetValue("FunctionLibrary") as string;
            if (string.IsNullOrWhiteSpace(path) ||
                !(name.Contains("Openport", StringComparison.OrdinalIgnoreCase) || vendor.Contains("Tactrix", StringComparison.OrdinalIgnoreCase)))
                continue;
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            // The 32-bit registry can reference System32; an x64 inspection process
            // needs the actual WOW64 path. A 32-bit reader is redirected by Windows.
            if (Environment.Is64BitProcess)
            {
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System) + Path.DirectorySeparatorChar;
                if (path.StartsWith(system, StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), path[system.Length..]);
            }
            if (File.Exists(path) && !result.Any(item => item.LibraryPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                result.Add(new(name, path));
        }
        return result;
    }
}
