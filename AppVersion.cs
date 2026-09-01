using System.Reflection;

namespace BackupManager;

public static class AppVersion
{
    private static readonly string Informational =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "dev";

    /// <summary>Semantic version without build metadata, e.g. "1.2.3".</summary>
    public static string Current { get; } =
        Informational.Contains('+') ? Informational[..Informational.IndexOf('+')] : Informational;

    /// <summary>Full informational version including commit hash, e.g. "1.2.3+a1b2c3".</summary>
    public static string Full { get; } = Informational;
}
