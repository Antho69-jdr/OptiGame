using System.Reflection;

namespace OptiGame.App;

/// <summary>Version de l'appli (Directory.Build.props), sans le « +commit » que le SDK ajoute à la version informative.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?").Split('+')[0];
}
