using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.State;
using OptiGame.Platform.Display;
using OptiGame.Platform.Gpu;
using OptiGame.Platform.Power;
using OptiGame.Platform.Privileged;
using OptiGame.Platform.Registry;
using OptiGame.Platform.Wmi;

namespace OptiGame.Platform;

public static class JournalKeys
{
    /// <summary>Corrections durables du diagnostic (annulables).</summary>
    public const string Fixes = "fixes";

    /// <summary>Changements temporaires d'une session de jeu.</summary>
    public const string Session = "session";
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOptiGamePlatform(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IPrivilegedOperations, InProcessPrivilegedOperations>();

        // Accès au système : un même service peut être source de données et accesseur de réglage.
        services.AddSingleton<RegistrySettingAccessor>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<RegistrySettingAccessor>());
        services.AddSingleton<IRegistryReader>(sp => sp.GetRequiredService<RegistrySettingAccessor>());

        services.AddSingleton<PowerSchemeService>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<PowerSchemeService>());
        services.AddSingleton<IPowerSchemeProvider>(sp => sp.GetRequiredService<PowerSchemeService>());

        services.AddSingleton<DisplayService>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<DisplayService>());
        services.AddSingleton<IDisplayInfoProvider>(sp => sp.GetRequiredService<DisplayService>());

        services.AddSingleton<IPowerStatusProvider, PowerStatusProvider>();
        services.AddSingleton<IMemoryInfoProvider, WmiMemoryInfoProvider>();
        services.AddSingleton<IGpuInfoProvider, WmiGpuInfoProvider>();
        services.AddSingleton<IDeviceGuardProvider, WmiDeviceGuardProvider>();
        services.AddSingleton<IGpuSchedulingProvider, GpuSchedulingProvider>();

        services.AddSingleton(sp => new SettingAccessors(sp.GetServices<ISettingAccessor>()));

        services.AddKeyedSingleton(JournalKeys.Fixes, (sp, _) => new ChangeJournal(
            new JsonStateStore<JournalDocument>(paths.FixesJournal), sp.GetRequiredService<SettingAccessors>(), sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedSingleton(JournalKeys.Session, (sp, _) => new ChangeJournal(
            new JsonStateStore<JournalDocument>(paths.SessionJournal), sp.GetRequiredService<SettingAccessors>(), sp.GetRequiredService<TimeProvider>()));

        // Contrôles du diagnostic, dans l'ordre d'affichage.
        services.AddSingleton<IDiagnosticCheck, DisplayRefreshRateCheck>();
        services.AddSingleton<IDiagnosticCheck, RamSpeedCheck>();
        services.AddSingleton<IDiagnosticCheck, MemoryChannelCheck>();
        services.AddSingleton<IDiagnosticCheck, GpuDriverAgeCheck>();
        services.AddSingleton<IDiagnosticCheck, PowerPlanCheck>();
        services.AddSingleton<IDiagnosticCheck, PowerSourceCheck>();
        services.AddSingleton<IDiagnosticCheck, GameModeCheck>();
        services.AddSingleton<IDiagnosticCheck, HagsCheck>();
        services.AddSingleton<IDiagnosticCheck, BackgroundRecordingCheck>();
        services.AddSingleton<IDiagnosticCheck, MemoryIntegrityCheck>();
        services.AddSingleton<IDiagnosticCheck, GpuPreferenceCheck>();
        services.AddSingleton<DiagnosticRunner>();

        return services;
    }
}
