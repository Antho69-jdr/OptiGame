using Microsoft.Extensions.DependencyInjection;
using OptiGame.Core;
using OptiGame.Core.Abstractions;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.Diagnostics.Checks;
using OptiGame.Core.Library;
using OptiGame.Core.Logging;
using OptiGame.Core.Settings;
using OptiGame.Core.Profiles;
using OptiGame.Core.Sessions;
using OptiGame.Core.State;
using OptiGame.Platform.Display;
using OptiGame.Platform.Gpu;
using OptiGame.Platform.Power;
using OptiGame.Platform.Privileged;
using OptiGame.Platform.Processes;
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
        services.AddSingleton(new FileLog(paths.LogsDir));

        services.AddSingleton<IPrivilegedOperations, InProcessPrivilegedOperations>();

        // Accès au système : un même service peut être source de données et accesseur de réglage.
        services.AddSingleton<RegistrySettingAccessor>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<RegistrySettingAccessor>());
        services.AddSingleton<IRegistryReader>(sp => sp.GetRequiredService<RegistrySettingAccessor>());

        services.AddSingleton<PowerSchemeService>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<PowerSchemeService>());
        services.AddSingleton<IPowerSchemeProvider>(sp => sp.GetRequiredService<PowerSchemeService>());
        services.AddSingleton<ISettingAccessor, PowerSettingAccessor>();
        services.AddSingleton<ISettingAccessor, Gpu.NvidiaProfileSettingAccessor>();
        services.AddSingleton<Gpu.NvidiaSmiProvider>();
        services.AddSingleton<INvidiaInfoProvider>(sp => sp.GetRequiredService<Gpu.NvidiaSmiProvider>());

        services.AddSingleton<DisplayService>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<DisplayService>());
        services.AddSingleton<IDisplayInfoProvider>(sp => sp.GetRequiredService<DisplayService>());

        services.AddSingleton<IPowerStatusProvider, PowerStatusProvider>();
        services.AddSingleton<IMemoryInfoProvider, WmiMemoryInfoProvider>();
        services.AddSingleton<IGpuInfoProvider, WmiGpuInfoProvider>();
        services.AddSingleton<IDisplayHdrInfo, Display.DisplayHdrReader>();
        services.AddSingleton<IDeviceGuardProvider, WmiDeviceGuardProvider>();
        services.AddSingleton<IGpuSchedulingProvider, GpuSchedulingProvider>();

        services.AddSingleton<IRunningProgramsProvider, RunningProgramsProvider>();

        services.AddSingleton<ProcessService>();
        services.AddSingleton<ISettingAccessor>(sp => sp.GetRequiredService<ProcessService>());
        services.AddSingleton<IProcessControl>(sp => sp.GetRequiredService<ProcessService>());

        services.AddSingleton(sp => new SettingAccessors(sp.GetServices<ISettingAccessor>()));

        services.AddSingleton(_ => new ProfileStore(new JsonStateStore<ProfilesDocument>(paths.Profiles)));
        services.AddSingleton(_ => new AppSettingsStore(new JsonStateStore<AppSettings>(paths.Settings)));
        services.AddSingleton<IGameLibraryScanner, Library.GameLibraryScanner>();
        services.AddSingleton(_ => new Core.Measurement.CaptureStore(
            new JsonStateStore<Core.Measurement.CapturesDocument>(Path.Combine(paths.CapturesDir, "captures.json")), paths.CapturesDir));
        services.AddSingleton<Measurement.PresentMonRunner>();
        services.AddSingleton<Measurement.AutoCapture>();
        services.AddSingleton<Artwork.IgdbClient>();
        services.AddSingleton<Artwork.ArtworkCache>();
        services.AddSingleton<Library.GameTagCache>();

        services.AddKeyedSingleton(JournalKeys.Fixes, (sp, _) => new ChangeJournal(
            new JsonStateStore<JournalDocument>(paths.FixesJournal), sp.GetRequiredService<SettingAccessors>(), sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedSingleton(JournalKeys.Session, (sp, _) => new ChangeJournal(
            new JsonStateStore<JournalDocument>(paths.SessionJournal), sp.GetRequiredService<SettingAccessors>(), sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton(sp => new GameSessionManager(
            sp.GetRequiredKeyedService<ChangeJournal>(JournalKeys.Session),
            sp.GetRequiredService<ProfileStore>(),
            sp.GetRequiredService<SettingAccessors>(),
            sp.GetRequiredService<IPowerSchemeProvider>(),
            sp.GetRequiredService<IProcessControl>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(_ => new Core.Playtime.PlaytimeStore(new JsonStateStore<Core.Playtime.PlaytimeDocument>(paths.Playtime)));
        services.AddSingleton<Core.Playtime.PlaytimeTracker>();
        services.AddSingleton<GameMonitor>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<Library.SteamPlaytimeReader>();
        services.AddSingleton<Library.SteamLibraryWatcher>();
        services.AddSingleton<Library.GameRequirementsClient>();
        services.AddSingleton<Library.StoreOwnedLibrary>();
        services.AddSingleton<Library.StoreCoverCache>();
        services.AddSingleton<Drivers.NvidiaDriverClient>();
        services.AddSingleton<Drivers.WindowsUpdateDriverSearch>();
        services.AddSingleton<Drivers.DriverDownloader>();
        services.AddSingleton<Drivers.AmdChipsetClient>();
        services.AddSingleton<Updates.AppUpdateClient>();
        services.AddSingleton<Display.FullscreenWatcher>();
        services.AddSingleton<Startup.AutoStartService>();

        // Contrôles du diagnostic, dans l'ordre d'affichage.
        services.AddSingleton<IDiagnosticCheck, DisplayRefreshRateCheck>();
        services.AddSingleton<IDiagnosticCheck, RamSpeedCheck>();
        services.AddSingleton<IDiagnosticCheck, MemoryChannelCheck>();
        services.AddSingleton<IDiagnosticCheck, GpuDriverAgeCheck>();
        services.AddSingleton<IDiagnosticCheck, PowerPlanCheck>();
        services.AddSingleton<IDiagnosticCheck, CpuBoostCheck>();
        services.AddSingleton<IDiagnosticCheck, ResizableBarCheck>();
        services.AddSingleton<IDiagnosticCheck, WindowedGamesCheck>();
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
