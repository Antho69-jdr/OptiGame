using System.ComponentModel;
using System.Runtime.InteropServices;
using static OptiGame.Platform.Native.NativeMethods;

namespace OptiGame.Platform.Processes;

/// <summary>
/// Lance un programme SANS droits administrateur depuis OptiGame (élevé), en empruntant le jeton de
/// l'Explorateur Windows (méthode documentée par Microsoft). Ne retombe jamais sur un lancement élevé.
/// </summary>
internal static class UnelevatedLauncher
{
    public static void Launch(string exePath, string commandLine)
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero)
        {
            throw new InvalidOperationException("L'Explorateur Windows n'est pas démarré : relance impossible sans droits administrateur.");
        }

        GetWindowThreadProcessId(shell, out var shellPid);
        var shellProcess = OpenProcess(ProcessQueryInformation, false, shellPid);
        if (shellProcess == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        var shellToken = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(shellProcess, TokenDuplicate, out shellToken))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            const uint access = TokenQuery | TokenAssignPrimary | TokenDuplicate | TokenAdjustDefault | TokenAdjustSessionId;
            if (!DuplicateTokenEx(shellToken, access, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            // CreateProcessWithTokenW peut modifier la ligne de commande : tampon modifiable terminé par \0.
            var buffer = (commandLine + '\0').ToCharArray();
            if (!CreateProcessWithTokenW(primaryToken, 0, exePath, buffer, CreateUnicodeEnvironment, IntPtr.Zero,
                    Path.GetDirectoryName(exePath), ref startup, out var info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
        }
        finally
        {
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
            CloseHandle(shellProcess);
        }
    }
}
