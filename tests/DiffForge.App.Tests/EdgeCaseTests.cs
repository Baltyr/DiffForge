using System.Text;
using Avalonia.Headless.XUnit;
using DiffForge.App.Commands;
using DiffForge.App.Services;

namespace DiffForge.App.Tests;

/// <summary>Archivos raros + todos los comandos en secuencia: huecos al inicio/fin, sin salto final, CR, tabs, líneas enormes.</summary>
public class EdgeCaseTests
{
    public static TheoryData<string, string, string> Fixtures() => new()
    {
        { "solo-izq-al-inicio", "nueva\na\nb\n", "a\nb\n" },
        { "solo-der-al-final", "a\nb", "a\nb\nc\nd" },
        { "todo-distinto", "x\ny\nz\n", "1\n2\n3\n4\n5\n" },
        { "un-lado-vacio", "", "a\nb\nc\n" },
        { "solo-cr", "a\rb\rc\r", "a\rB\rc\r" },
        { "tabs", "\tif (x)\n\t\treturn;\n", "    if (x)\n        return;\n" },
        { "linea-enorme", new string('a', 20000) + "\n", new string('a', 10000) + "b" + new string('a', 9999) + "\n" },
        { "unicode", "ñandú 🦤\nçé\n", "ñandu 🦤\nçé\n" },
        { "sin-salto-vs-con-salto", "a\nb", "a\nb\n" },
    };

    private static readonly HashSet<Cmd> Skip = [Cmd.Exit, Cmd.OpenWithAssociated, Cmd.Explorer, Cmd.CloseTab, Cmd.ClearSession, Cmd.NewComparison];

    [AvaloniaTheory]
    [MemberData(nameof(Fixtures))]
    public void AllCommands_OnEdgeCaseFiles(string name, string left, string right)
    {
        using var h = new Harness(left, right, ".txt");
        foreach (bool counts in new[] { true, false })
        {
            AppSettings.Current.Options.ShowFilteredLineCounts = counts;
            foreach (var cmd in AppCommands.All.Where(d => !d.Unavailable && !Skip.Contains(d.Id)).Select(d => d.Id))
            {
                h.Execute(cmd);
                h.Run(h.View.FlushAsync());
                Assert.True(h.Unhandled.Count == 0, $"{name}/{cmd}: {h.Unhandled.FirstOrDefault()}");
                Assert.False(h.View.StatusMessage.StartsWith("Error"), $"{name}/{cmd}: {h.View.StatusMessage}");
                var rows = h.View.Result!.Rows.Count;
                Assert.Equal(Math.Max(rows, 1), h.View.LeftPane.Document.LineCount);
                Assert.Equal(Math.Max(rows, 1), h.View.RightPane.Document.LineCount);
            }
        }
        AppSettings.Current.Options.ShowFilteredLineCounts = true;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Fixtures))]
    public void UnchangedSave_KeepsBytes(string name, string left, string right)
    {
        using var h = new Harness(left, right, ".txt");
        // Editar y deshacer deja el documento igual: guardar no debe alterar ningún byte.
        var pane = h.View.RightPane;
        int row = Enumerable.Range(0, h.View.Result!.Rows.Count).FirstOrDefault(r => pane.LineIndexOfRow(r) >= 0);
        int offset = pane.Document.GetLineByNumber(row + 1).Offset;
        pane.Document.Insert(offset, "x");
        h.WaitRecompare();
        pane.Document.Remove(pane.Document.GetLineByNumber(row + 1).Offset, 1);
        h.WaitRecompare();
        h.Run(h.View.SaveSideAsync(DiffForge.Core.Compare.Side.Right, saveAs: false));
        Assert.True(Encoding.UTF8.GetBytes(right).SequenceEqual(File.ReadAllBytes(h.RightPath)), name);
    }

    [AvaloniaFact]
    public void DarkTheme_AppliesWithoutErrors()
    {
        using var h = new Harness();
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        h.Pump();
        h.Execute(Cmd.RecompareFiles);
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        Assert.Empty(h.Unhandled);
    }
}
