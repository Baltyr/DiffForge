using Avalonia.Headless.XUnit;
using DiffForge.App.Commands;
using DiffForge.Core.Compare;

namespace DiffForge.App.Tests;

/// <summary>Ejecuta cada comando del menú sobre una comparación real y verifica que no rompa nada.</summary>
public class CommandSweepTests
{
    // Comandos que abren procesos externos o cierran la ventana: se prueban aparte o no se automatizan.
    private static readonly HashSet<Cmd> Excluded = [Cmd.Exit, Cmd.OpenWithAssociated, Cmd.Explorer];

    public static TheoryData<Cmd> AllCommands()
    {
        var data = new TheoryData<Cmd>();
        foreach (var def in AppCommands.All.Where(d => !d.Unavailable && !Excluded.Contains(d.Id))) data.Add(def.Id);
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(AllCommands))]
    public void Command_RunsWithoutErrors_OnEveryPane(Cmd cmd)
    {
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            using var h = new Harness();
            var pane = side == Side.Left ? h.View.LeftPane : h.View.RightPane;
            pane.Editor.Focus();
            h.Pump();
            h.View.GoToRow(h.View.Result!.Sections[0].Start, pane);
            h.Pump();

            h.Execute(cmd);
            Assert.DoesNotMatch("^(Error|No se pudo)", h.View.StatusMessage);
            h.Execute(cmd); // dos veces: los toggles vuelven a su estado y los saltos encadenan
            Assert.DoesNotMatch("^(Error|No se pudo)", h.View.StatusMessage);

            Assert.Empty(h.Unhandled);
            Assert.NotNull(h.Window.Active);
            if (cmd is not (Cmd.NewComparison or Cmd.CloseTab or Cmd.ClearSession)) Assert.NotNull(h.View.Result);
        }
    }

    [AvaloniaFact]
    public void AllCommands_InSequence_KeepViewConsistent()
    {
        using var h = new Harness();
        foreach (var cmd in AppCommands.All.Where(d => !d.Unavailable && !Excluded.Contains(d.Id)).Select(d => d.Id))
        {
            if (cmd is Cmd.CloseTab or Cmd.ClearSession or Cmd.NewComparison) continue;
            h.Execute(cmd);
            h.Run(h.View.FlushAsync()); // las ediciones directas recomparan con debounce
            Assert.Empty(h.Unhandled);

            var result = h.View.Result!;
            Assert.Equal(result.Rows.Count, h.View.LeftPane.Document.LineCount);
            Assert.Equal(result.Rows.Count, h.View.RightPane.Document.LineCount);
        }
    }

    [AvaloniaFact]
    public void Shortcuts_AreUnique()
    {
        var duplicated = AppCommands.All.Where(c => c.Gesture is not null)
            .GroupBy(c => c.KeyGesture!.ToString())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + ": " + string.Join(", ", g.Select(c => c.Id)))
            .ToList();
        Assert.Empty(duplicated);
    }
}
