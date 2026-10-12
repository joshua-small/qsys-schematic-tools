using System.IO;
using System.Security.Cryptography;

namespace Designer.Modules.SelectionProbe;

internal static class HostInstallation
{
    internal const string DesignerHash = "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60";
    private const string ModuleCommonHash = "a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee";
    internal static bool IsLab => File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"));
    internal static readonly bool Enabled = CheckEnabled();

    internal static string OutputDirectory => IsLab
        ? Path.Combine(AppContext.BaseDirectory, "selection-probe-output")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QsysSchematicTools", "Designer-10.5", "selection-probe-output");

    private static bool CheckEnabled()
    {
        try
        {
            if (IsLab) return true;
            var marker = Path.Combine(AppContext.BaseDirectory, ".schematic-tools-enabled");
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != DesignerHash) return false;
            return Hash("Q-Sys Designer.dll") == DesignerHash && Hash("ModuleCommon.dll") == ModuleCommonHash;
        }
        catch { return false; }
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, file));
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
