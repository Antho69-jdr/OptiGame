using System.Xml.Linq;
using OptiGame.Platform.Startup;

namespace OptiGame.Platform.Tests;

/// <summary>Vérifie le XML de la tâche planifiée, sans la créer.</summary>
public sealed class AutoStartServiceTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static XDocument Task() => XDocument.Parse(
        AutoStartService.BuildTaskXml(@"C:\Program Files\OptiGame & Co\OptiGame.exe", "--minimized", @"PC\antho"));

    [Fact]
    public void Task_runs_elevated_at_logon_of_the_current_user()
    {
        var task = Task();

        Assert.Equal("HighestAvailable", task.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("InteractiveToken", task.Descendants(Ns + "LogonType").Single().Value);
        Assert.Equal(@"PC\antho", task.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
    }

    [Fact]
    public void Task_overrides_unsuitable_schtasks_defaults()
    {
        var task = Task();

        Assert.Equal("PT0S", task.Descendants(Ns + "ExecutionTimeLimit").Single().Value);        // pas d'arrêt après 72 h
        Assert.Equal("false", task.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value); // démarre sur batterie
        Assert.Equal("false", task.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.Equal("5", task.Descendants(Ns + "Priority").Single().Value);                       // priorité normale
    }

    [Fact]
    public void Paths_are_xml_escaped()
    {
        var exec = Task().Descendants(Ns + "Exec").Single();

        Assert.Equal(@"C:\Program Files\OptiGame & Co\OptiGame.exe", exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--minimized", exec.Element(Ns + "Arguments")!.Value);
    }
}
