using OptiGame.Core.Changes;
using OptiGame.Core.Diagnostics;
using OptiGame.Core.State;

namespace OptiGame.Core.Tests.Diagnostics;

public sealed class OneClickOptimizationTests
{
    private static DiagnosticFix Fix(string id, bool advanced = false) => new(new ReversibleChange
    {
        Id = id,
        Title = id,
        What = "",
        Why = "",
        Writes = [],
    }, advanced);

    private static DiagnosticResult Result(string id, DiagnosticStatus status, params DiagnosticFix[] fixes) => new()
    {
        CheckId = id,
        Title = id,
        Status = status,
        Summary = "",
        Fixes = fixes,
    };

    private static ChangeRecord Applied(string id) => new(id, id, "", false, DateTimeOffset.UnixEpoch);

    private static readonly IReadOnlyList<DiagnosticResult> Results =
    [
        Result("game-mode", DiagnosticStatus.NeedsAttention, Fix("fix.windows.game-mode")),
        Result("boost", DiagnosticStatus.NeedsAttention, Fix("fix.power.boost"), Fix("fix.power.maxstate")), // complémentaires
        Result("memory-integrity", DiagnosticStatus.NeedsAttention, Fix("fix.security.memory-integrity", advanced: true)),
        Result("driver-age", DiagnosticStatus.NeedsAttention), // à faire soi-même (page Pilotes)
        Result("hags", DiagnosticStatus.Info, Fix("fix.gpu.hags")),
        Result("refresh", DiagnosticStatus.Ok),
    ];

    [Fact]
    public void The_button_applies_only_recommended_fixes()
    {
        var overview = OneClickOptimization.Overview(Results, []);

        Assert.Equal(["fix.windows.game-mode", "fix.power.boost", "fix.power.maxstate"], overview.Recommended.Select(c => c.Id));
        Assert.Equal(["memory-integrity", "driver-age"], overview.ManualActions.Select(r => r.CheckId)); // l'option avancée reste manuelle
        Assert.Equal(["hags"], overview.Optional.Select(r => r.CheckId));
        Assert.Equal((1, 4, 1), (overview.OkCount, overview.AttentionCount, overview.InfoCount));
        Assert.False(overview.IsOptimized);
    }

    [Fact]
    public void Already_applied_fixes_are_not_proposed_again_and_only_diagnostic_fixes_are_undone()
    {
        var active = new[] { Applied("fix.windows.game-mode"), Applied("game.framecap.6bca4cb1"), Applied("fix.gpu.hags") };
        var overview = OneClickOptimization.Overview(Results, active);

        Assert.Equal(["fix.power.boost", "fix.power.maxstate"], overview.Recommended.Select(c => c.Id));
        Assert.Empty(overview.Optional); // HAGS déjà appliqué
        Assert.Equal(["fix.windows.game-mode", "fix.gpu.hags"], overview.Applied.Select(c => c.Id)); // jamais le plafond du jeu
    }

    [Fact]
    public void A_pc_without_anything_to_fix_is_optimized()
    {
        var overview = OneClickOptimization.Overview([Result("refresh", DiagnosticStatus.Ok), Result("hags", DiagnosticStatus.Info, Fix("fix.gpu.hags"))], []);
        Assert.True(overview.IsOptimized);
        Assert.Equal(DiagnosticVerdict.Ready, overview.Verdict);
    }

    [Fact]
    public void A_check_that_could_not_run_is_counted_and_prevents_the_ready_verdict()
    {
        // Avant : un contrôle en erreur était rangé dans « À savoir » et la page affichait « Votre PC est prêt ».
        var overview = OneClickOptimization.Overview([Result("refresh", DiagnosticStatus.Ok), Result("vbs", DiagnosticStatus.Error)], []);
        Assert.Equal(["vbs"], overview.Unverified.Select(r => r.CheckId));
        Assert.Equal(DiagnosticVerdict.Incomplete, overview.Verdict);
        Assert.Equal(0, overview.InfoCount);
    }

    [Fact]
    public void The_verdict_puts_what_the_button_can_do_first()
    {
        Assert.Equal(DiagnosticVerdict.Recommended, OneClickOptimization.Overview(Results, []).Verdict);
        var manualOnly = OneClickOptimization.Overview([Result("driver-age", DiagnosticStatus.NeedsAttention), Result("vbs", DiagnosticStatus.Error)], []);
        Assert.Equal(DiagnosticVerdict.ManualActions, manualOnly.Verdict);
    }
}
