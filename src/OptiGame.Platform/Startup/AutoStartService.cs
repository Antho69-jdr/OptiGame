using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace OptiGame.Platform.Startup;

/// <summary>
/// Démarrage automatique via une tâche planifiée « à l'ouverture de session, privilèges les plus élevés » :
/// la clé Run ne permet pas de lancer une appli requireAdministrator sans invite UAC.
/// La tâche est importée en XML pour corriger des valeurs par défaut de schtasks inadaptées : arrêt après
/// 72 h, pas de démarrage sur batterie, priorité basse (héritée par les programmes relancés).
/// </summary>
public sealed class AutoStartService
{
    public const string TaskName = "OptiGame";

    public bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"", out _) == 0;

    /// <summary>Programme lancé par la tâche (balise Command de sa définition XML) ; null s'il n'y a pas de tâche.</summary>
    public string? TaskCommand()
    {
        if (RunSchtasks($"/Query /TN \"{TaskName}\" /XML", out var xml) != 0) return null;
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(xml);
            return document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public void Enable(string exePath, string arguments)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"OptiGame-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exePath, arguments, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
            if (RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", out var output) != 0)
            {
                throw new InvalidOperationException($"Création de la tâche planifiée impossible : {output}");
            }
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public void Disable()
    {
        if (IsEnabled() && RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out var output) != 0)
        {
            throw new InvalidOperationException($"Suppression de la tâche planifiée impossible : {output}");
        }
    }

    internal static string BuildTaskXml(string exePath, string arguments, string userId)
    {
        var user = SecurityElement.Escape(userId);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Démarre OptiGame dans la zone de notification à l'ouverture de session.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                  <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int RunSchtasks(string arguments, out string output)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
        process.WaitForExit();
        return process.ExitCode;
    }
}
