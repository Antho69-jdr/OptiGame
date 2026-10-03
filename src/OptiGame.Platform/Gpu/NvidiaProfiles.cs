using System.Runtime.InteropServices;

namespace OptiGame.Platform.Gpu;

/// <summary>Valeur d'un réglage du pilote NVIDIA pour un jeu : profil appliqué par le pilote et valeur propre à ce profil.</summary>
/// <param name="ProfileName">Profil que le pilote applique à l'exe (null : aucun, le profil global s'applique).</param>
/// <param name="Value">Valeur définie par l'utilisateur dans ce profil ; null = non définie (valeur de NVIDIA ou du profil global).</param>
/// <param name="EffectiveValue">Valeur appliquée au jeu (profil du jeu, sinon profil global) ; null = valeur par défaut du pilote.</param>
public sealed record NvidiaProfileSetting(string? ProfileName, bool ProfileIsPredefined, uint? Value, uint? EffectiveValue);

public sealed class NvidiaApiException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// Profils du pilote NVIDIA (NVAPI « DRS », ceux du Panneau de configuration NVIDIA et de NVIDIA Profile Inspector).
/// Identifiants, structures et codes vérifiés dans les en-têtes officiels (github.com/NVIDIA/nvapi : nvapi.h,
/// nvapi_interface.h, nvapi_lite_common.h, NvApiDriverSettings.h) le 2026-10-03. Les structures sont écrites octet par octet
/// aux positions documentées (NVDRS_SETTING est en #pragma pack(4)) : la taille fait partie de leur numéro de version.
/// Les profils créés par OptiGame s'appellent « OptiGame - &lt;exe&gt; » et sont supprimés quand ils ne contiennent plus rien.
/// </summary>
public static class NvidiaProfiles
{
    public const string CreatedProfilePrefix = "OptiGame - ";

    // ---- Codes de retour (nvapi_lite_common.h) ----
    private const int Ok = 0;
    private const int SettingNotFound = -160;
    private const int ProfileNotFound = -163;
    private const int ExecutableNotFound = -166;

    // ---- Structures (tailles et positions d'après nvapi.h) ----
    private const int UnicodeChars = 2048;
    private const int UnicodeBytes = UnicodeChars * 2;

    private const int SettingSize = 12320;                 // NVDRS_SETTING_V1, pack(4)
    private const uint SettingVersion = SettingSize | (1u << 16);
    private const int SettingIdOffset = 4 + UnicodeBytes;  // 4100
    private const int SettingTypeOffset = SettingIdOffset + 4;
    private const int SettingLocationOffset = SettingIdOffset + 8;
    private const int IsCurrentPredefinedOffset = SettingIdOffset + 12;
    private const int CurrentValueOffset = SettingIdOffset + 20 + 4100; // après le second union : 8220

    private const int ApplicationSize = 20492;             // NVDRS_APPLICATION_V4
    private const uint ApplicationVersion = ApplicationSize | (4u << 16);
    private const int AppNameOffset = 8;

    private const int ProfileSize = 4116;                  // NVDRS_PROFILE_V1
    private const uint ProfileVersion = ProfileSize | (1u << 16);
    private const int ProfileIsPredefinedOffset = 4 + UnicodeBytes + 4;
    private const int ProfileSettingsCountOffset = ProfileIsPredefinedOffset + 8;

    private const int CurrentProfileLocation = 0;          // NVDRS_CURRENT_PROFILE_LOCATION
    private const int DwordType = 0;                       // NVDRS_DWORD_TYPE

    /// <summary>Le pilote NVIDIA (nvapi64.dll) est-il présent ?</summary>
    public static bool IsAvailable => Api.Instance is not null;

    /// <summary>Lecture d'un réglage pour un jeu (sans droits administrateur).</summary>
    public static NvidiaProfileSetting Read(string exePath, uint settingId)
    {
        using var session = Session.Open();
        Check(session.Api.GetCurrentGlobalProfile(session.Handle, out var global), "GetCurrentGlobalProfile");
        var globalValue = GetSetting(session, global, settingId)?.Value; // null : valeur par défaut du pilote
        if (FindProfile(session, exePath) is not { } profile) return new NvidiaProfileSetting(null, false, null, globalValue);
        var (name, predefined, _) = ProfileInfo(session, profile);
        var setting = GetSetting(session, profile, settingId);
        uint? own = setting is { Location: CurrentProfileLocation, IsPredefined: false } ? setting.Value : null;
        return new NvidiaProfileSetting(name, predefined, own, setting?.Value ?? globalValue);
    }

    /// <summary>
    /// Écrit (ou retire, avec null) la valeur propre au profil que le pilote applique au jeu ; crée le profil
    /// « OptiGame - exe » si aucun ne le contient. Demande les droits administrateur : uniquement via IPrivilegedOperations.
    /// </summary>
    public static void Write(string exePath, uint settingId, uint? value)
    {
        using var session = Session.Open();
        var profile = FindProfile(session, exePath);
        if (profile is null)
        {
            if (value is null) return; // rien à retirer
            profile = CreateProfile(session, Path.GetFileName(exePath));
        }

        if (value is { } v)
        {
            var buffer = Marshal.AllocHGlobal(SettingSize);
            try
            {
                Clear(buffer, SettingSize);
                Marshal.WriteInt32(buffer, 0, unchecked((int)SettingVersion));
                Marshal.WriteInt32(buffer, SettingIdOffset, unchecked((int)settingId));
                Marshal.WriteInt32(buffer, SettingTypeOffset, DwordType);
                Marshal.WriteInt32(buffer, CurrentValueOffset, unchecked((int)v));
                Check(session.Api.SetSetting(session.Handle, profile.Value, buffer), "SetSetting");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        else
        {
            var status = session.Api.DeleteProfileSetting(session.Handle, profile.Value, settingId);
            if (status != SettingNotFound) Check(status, "DeleteProfileSetting");
            var (name, _, settings) = ProfileInfo(session, profile.Value);
            if (name.StartsWith(CreatedProfilePrefix, StringComparison.Ordinal) && settings == 0)
            {
                Check(session.Api.DeleteProfile(session.Handle, profile.Value), "DeleteProfile"); // profil d'OptiGame devenu vide
            }
        }
        Check(session.Api.SaveSettings(session.Handle), "SaveSettings");
    }

    private static IntPtr? FindProfile(Session session, string exePath)
    {
        var app = Marshal.AllocHGlobal(ApplicationSize);
        try
        {
            Clear(app, ApplicationSize);
            Marshal.WriteInt32(app, 0, unchecked((int)ApplicationVersion));
            // Chemin complet : NVIDIA renvoie alors le profil que le pilote appliquera vraiment à cet exe.
            var status = session.Api.FindApplicationByName(session.Handle, Unicode(exePath), out var profile, app);
            if (status is ExecutableNotFound or ProfileNotFound) return null;
            Check(status, "FindApplicationByName");
            return profile;
        }
        finally
        {
            Marshal.FreeHGlobal(app);
        }
    }

    private static IntPtr CreateProfile(Session session, string exeName)
    {
        var name = CreatedProfilePrefix + exeName;
        var profile = Marshal.AllocHGlobal(ProfileSize);
        var app = Marshal.AllocHGlobal(ApplicationSize);
        try
        {
            IntPtr handle;
            var status = session.Api.FindProfileByName(session.Handle, Unicode(name), out handle);
            if (status == ProfileNotFound)
            {
                Clear(profile, ProfileSize);
                Marshal.WriteInt32(profile, 0, unchecked((int)ProfileVersion));
                WriteUnicode(profile, 4, name);
                Check(session.Api.CreateProfile(session.Handle, profile, out handle), "CreateProfile");
            }
            else
            {
                Check(status, "FindProfileByName");
            }

            Clear(app, ApplicationSize);
            Marshal.WriteInt32(app, 0, unchecked((int)ApplicationVersion));
            WriteUnicode(app, AppNameOffset, exeName.ToLowerInvariant());
            Check(session.Api.CreateApplication(session.Handle, handle, app), "CreateApplication");
            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(profile);
            Marshal.FreeHGlobal(app);
        }
    }

    private static (string Name, bool Predefined, int Settings) ProfileInfo(Session session, IntPtr profile)
    {
        var buffer = Marshal.AllocHGlobal(ProfileSize);
        try
        {
            Clear(buffer, ProfileSize);
            Marshal.WriteInt32(buffer, 0, unchecked((int)ProfileVersion));
            Check(session.Api.GetProfileInfo(session.Handle, profile, buffer), "GetProfileInfo");
            return (Marshal.PtrToStringUni(buffer + 4) ?? "", Marshal.ReadInt32(buffer, ProfileIsPredefinedOffset) != 0,
                Marshal.ReadInt32(buffer, ProfileSettingsCountOffset));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed record SettingRead(uint Value, int Location, bool IsPredefined);

    private static SettingRead? GetSetting(Session session, IntPtr profile, uint settingId)
    {
        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Clear(buffer, SettingSize);
            Marshal.WriteInt32(buffer, 0, unchecked((int)SettingVersion));
            var status = session.Api.GetSetting(session.Handle, profile, settingId, buffer);
            if (status == SettingNotFound) return null;
            Check(status, "GetSetting");
            if (Marshal.ReadInt32(buffer, SettingTypeOffset) != DwordType) return null;
            return new SettingRead(unchecked((uint)Marshal.ReadInt32(buffer, CurrentValueOffset)),
                Marshal.ReadInt32(buffer, SettingLocationOffset), Marshal.ReadInt32(buffer, IsCurrentPredefinedOffset) != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>NvAPI_UnicodeString : 2048 caractères UTF-16. Tableau de ushort, jamais de char (converti en ANSI par défaut).</summary>
    private static ushort[] Unicode(string text)
    {
        var chars = new ushort[UnicodeChars];
        for (var i = 0; i < Math.Min(text.Length, UnicodeChars - 1); i++) chars[i] = text[i];
        return chars;
    }

    private static void WriteUnicode(IntPtr buffer, int offset, string text) =>
        Marshal.Copy(Array.ConvertAll(Unicode(text), c => unchecked((short)c)), 0, buffer + offset, UnicodeChars);

    private static void Clear(IntPtr buffer, int size) => Marshal.Copy(new byte[size], 0, buffer, size);

    private static void Check(int status, string function)
    {
        if (status != Ok) throw new NvidiaApiException($"NVAPI {function} a échoué (code {status}{(status == -137 ? " : droits administrateur nécessaires" : "")}).", status);
    }

    /// <summary>Session DRS : créée, chargée, détruite à la fin.</summary>
    private sealed class Session : IDisposable
    {
        public required Api Api { get; init; }

        public required IntPtr Handle { get; init; }

        public static Session Open()
        {
            var api = Api.Instance ?? throw new NvidiaApiException("Pilote NVIDIA introuvable (nvapi64.dll).", -2);
            Check(api.CreateSession(out var handle), "CreateSession");
            var session = new Session { Api = api, Handle = handle };
            try
            {
                Check(api.LoadSettings(handle), "LoadSettings");
            }
            catch
            {
                session.Dispose();
                throw;
            }
            return session;
        }

        public void Dispose() => Api.DestroySession(Handle);
    }

    /// <summary>Fonctions NVAPI obtenues par nvapi_QueryInterface (seule fonction exportée par nvapi64.dll).</summary>
    private sealed class Api
    {
        private static readonly Lazy<Api?> Loaded = new(() =>
        {
            try
            {
                return Load();
            }
            catch (Exception ex) when (ex is NvidiaApiException or MarshalDirectiveException or DllNotFoundException or EntryPointNotFoundException)
            {
                return null; // pilote absent ou incomplet : la fonction est simplement indisponible
            }
        });

        public static Api? Instance => Loaded.Value;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterfaceFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CreateSessionFn(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GlobalProfileFn(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int SessionFn(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FindApplicationFn(IntPtr session, [MarshalAs(UnmanagedType.LPArray)] ushort[] name, out IntPtr profile, IntPtr application);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FindProfileFn(IntPtr session, [MarshalAs(UnmanagedType.LPArray)] ushort[] name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CreateProfileFn(IntPtr session, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ProfileStructFn(IntPtr session, IntPtr profile, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ProfileFn(IntPtr session, IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint settingId);

        public required CreateSessionFn CreateSession { get; init; }
        public required GlobalProfileFn GetCurrentGlobalProfile { get; init; }
        public required SessionFn DestroySessionFn { get; init; }
        public required SessionFn LoadSettings { get; init; }
        public required SessionFn SaveSettings { get; init; }
        public required FindApplicationFn FindApplicationByName { get; init; }
        public required FindProfileFn FindProfileByName { get; init; }
        public required CreateProfileFn CreateProfile { get; init; }
        public required ProfileStructFn CreateApplication { get; init; }
        public required ProfileStructFn GetProfileInfo { get; init; }
        public required ProfileFn DeleteProfile { get; init; }
        public required GetSettingFn GetSettingRaw { get; init; }
        public required ProfileStructFn SetSetting { get; init; }
        public required DeleteSettingFn DeleteProfileSetting { get; init; }

        public void DestroySession(IntPtr session) => DestroySessionFn(session);

        public int GetSetting(IntPtr session, IntPtr profile, uint settingId, IntPtr setting) => GetSettingRaw(session, profile, settingId, setting);

        private static Api? Load()
        {
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var library) ||
                !NativeLibrary.TryGetExport(library, "nvapi_QueryInterface", out var queryPointer))
            {
                return null;
            }
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(queryPointer);
            T Get<T>(uint id) where T : Delegate
            {
                var pointer = query(id);
                return pointer == IntPtr.Zero
                    ? throw new NvidiaApiException($"Fonction NVAPI 0x{id:x8} absente de ce pilote.", -3)
                    : Marshal.GetDelegateForFunctionPointer<T>(pointer);
            }

            // Identifiants : nvapi_interface.h.
            if (Get<InitializeFn>(0x0150e828)() != Ok) return null;
            return new Api
            {
                CreateSession = Get<CreateSessionFn>(0x0694d52e),
                GetCurrentGlobalProfile = Get<GlobalProfileFn>(0x617bff9f),
                DestroySessionFn = Get<SessionFn>(0xdad9cff8),
                LoadSettings = Get<SessionFn>(0x375dbd6b),
                SaveSettings = Get<SessionFn>(0xfcbc7e14),
                FindApplicationByName = Get<FindApplicationFn>(0xeee566b2),
                FindProfileByName = Get<FindProfileFn>(0x7e4a9a0b),
                CreateProfile = Get<CreateProfileFn>(0xcc176068),
                CreateApplication = Get<ProfileStructFn>(0x4347a9de),
                GetProfileInfo = Get<ProfileStructFn>(0x61cd6fd6),
                DeleteProfile = Get<ProfileFn>(0x17093206),
                GetSettingRaw = Get<GetSettingFn>(0x73bf8338),
                SetSetting = Get<ProfileStructFn>(0x577dd202),
                DeleteProfileSetting = Get<DeleteSettingFn>(0xe4a26362),
            };
        }
    }
}
