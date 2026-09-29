using System.Management;
using OptiGame.Platform.Processes;

namespace OptiGame.Platform.Tests;

/// <summary>
/// Régression : une requête « SELECT ProcessID, ProcessName FROM … » faisait lever « Paramètre non valide » à
/// WqlEventQuery, ce qui désactivait silencieusement toute la détection des jeux.
/// </summary>
public sealed class GameMonitorQueryTests
{
    [Theory]
    [InlineData(GameMonitor.StartQuery, "Win32_ProcessStartTrace")]
    [InlineData(GameMonitor.StopQuery, "Win32_ProcessStopTrace")]
    public void Event_queries_are_accepted_by_WqlEventQuery(string query, string eventClass)
    {
        var parsed = new WqlEventQuery(query);

        Assert.Equal(eventClass, parsed.EventClassName);
    }
}
