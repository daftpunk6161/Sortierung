using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// M2 Danger-Level (High): ConvertOnly verschiebt verifizierte Quell-Dateien nach
/// erfolgreicher Konvertierung in <c>_TRASH_CONVERTED</c>. Vor dem Start muss daher
/// eine harte DangerConfirm mit getipptem Bestaetigungs-Token eingeholt werden
/// (analog Move). Lehnt der Nutzer ab oder verweigert die Tipp-Bestaetigung, darf
/// weder ConvertOnly noch DryRun=false gesetzt werden, und es darf kein Run starten.
/// </summary>
public class ConvertOnlyConfirmTests
{
    [Fact]
    public void ConvertOnly_AbortsWhenUserDeclinesDangerConfirmation()
    {
        var dialog = new StubDialogService { DangerConfirmResult = false };
        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");

        // Vorbedingung: ConvertOnly ist initial false, DryRun spiegelt Defaults wider.
        var dryRunBefore = vm.DryRun;

        Assert.True(vm.ConvertOnlyCommand.CanExecute(null),
            "ConvertOnlyCommand muss bei vorhandenem Root und Idle-State ausfuehrbar sein");

        vm.ConvertOnlyCommand.Execute(null);

        Assert.False(vm.ConvertOnly,
            "Bei abgelehnter DangerConfirm-Bestaetigung darf ConvertOnly nicht aktiviert werden");
        Assert.Equal(dryRunBefore, vm.DryRun);
        Assert.Single(dialog.DangerConfirmCalls);
        Assert.Empty(dialog.ConfirmCalls);
    }

    [Fact]
    public void ConvertOnly_ProceedsWhenUserConfirmsDanger()
    {
        var dialog = new StubDialogService { DangerConfirmResult = true };
        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");

        vm.ConvertOnlyCommand.Execute(null);

        Assert.True(vm.ConvertOnly,
            "Nach bestaetigter DangerConfirm muss ConvertOnly aktiviert sein");
        Assert.False(vm.DryRun,
            "Nach bestaetigter DangerConfirm muss DryRun deaktiviert sein, damit der reale Lauf startet");
        Assert.Single(dialog.DangerConfirmCalls);
    }

    [Fact]
    public void ConvertOnly_DoesNotFallbackToPlainConfirm_WhenDangerConfirmDeclined()
    {
        // Schutz gegen still-Regression: einmal DangerConfirm = false darf nicht
        // durch eine Plain-Confirm umgangen werden.
        var dialog = new StubDialogService
        {
            DangerConfirmResult = false,
            ConfirmResult = true, // wuerde plain-Confirm durchwinken, falls aufgerufen
        };
        var vm = new MainViewModel(new StubThemeService(), dialog);
        vm.Roots.Add(@"C:\TestRoot");

        vm.ConvertOnlyCommand.Execute(null);

        Assert.False(vm.ConvertOnly);
        Assert.Empty(dialog.ConfirmCalls);
    }
}
