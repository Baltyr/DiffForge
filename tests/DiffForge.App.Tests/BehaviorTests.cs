using System.Text;
using Avalonia.Headless.XUnit;
using DiffForge.App.Commands;
using DiffForge.App.Views;
using DiffForge.Core.Compare;

namespace DiffForge.App.Tests;

public class BehaviorTests
{
    private static string Text(DiffForge.App.Services.SideDocument doc) =>
        string.Concat(doc.Current.Lines.Select(l => l.Text + "\n"));

    [AvaloniaFact]
    public void Load_ShowsAlignedRowsAndSections()
    {
        using var h = new Harness();
        var result = h.View.Result!;
        Assert.True(result.Sections.Count >= 3, $"secciones: {result.Sections.Count}");
        Assert.Equal(result.Rows.Count, h.View.LeftPane.Document.LineCount);
        Assert.Equal(result.Rows.Count, h.View.RightPane.Document.LineCount);
        Assert.Contains("izq.cs", h.View.TabTitle);
    }

    [AvaloniaFact]
    public void CopyAllSectionsToRight_MakesFilesIdentical_AndUndoRestores()
    {
        using var h = new Harness();
        string original = Text(h.View.Right);
        h.View.LeftPane.Editor.Focus();
        h.View.GoToRow(0, h.View.LeftPane);
        h.Execute(Cmd.SelectAll);
        h.Execute(Cmd.CopyToRight);

        Assert.Equal(Text(h.View.Left), Text(h.View.Right));
        Assert.Equal(0, h.View.Result!.Stats.ImportantSections);
        Assert.True(h.View.Right.IsModified);

        h.Execute(Cmd.Undo);
        Assert.Equal(original, Text(h.View.Right));
        h.Execute(Cmd.Redo);
        Assert.Equal(Text(h.View.Left), Text(h.View.Right));
    }

    [AvaloniaFact]
    public void TypingInEditor_RecomparesAndMarksModified()
    {
        using var h = new Harness();
        var pane = h.View.RightPane;
        pane.Editor.Focus();
        var line = pane.Document.GetLineByNumber(1);
        pane.Document.Insert(line.EndOffset, " // extra");
        h.WaitRecompare();

        Assert.StartsWith("*", h.View.TabTitle.Split('↔')[1].Trim());
        Assert.Contains("using System; // extra", Text(h.View.Right));
        Assert.Equal(h.View.Result!.Rows.Count, h.View.LeftPane.Document.LineCount);
    }

    [AvaloniaFact]
    public void Save_RoundTripsBytes_AfterEdit()
    {
        using var h = new Harness();
        var pane = h.View.RightPane;
        pane.Editor.Focus();
        pane.Document.Insert(0, "// top\n");
        h.WaitRecompare();
        h.Run(h.View.SaveSideAsync(Side.Right, saveAs: false));

        string saved = File.ReadAllText(h.RightPath);
        Assert.Equal("// top\r\n" + Harness.RightText, saved);
        Assert.False(h.View.Right.IsModified);
    }

    [AvaloniaFact]
    public void SwapSides_ExchangesDocuments()
    {
        using var h = new Harness();
        h.Execute(Cmd.SwapSides);
        Assert.Equal(h.RightPath, h.View.Left.Spec);
        Assert.Equal(h.LeftPath, h.View.Right.Spec);
    }

    [AvaloniaFact]
    public void FilterDifferences_HidesEqualLines()
    {
        using var h = new Harness();
        h.Execute(Cmd.ShowDifferences);
        var tv = h.View.LeftPane.TextView;
        h.Pump();
        Assert.Equal(ViewFilter.Differences, h.View.Filter);
        h.Execute(Cmd.ShowAll);
        Assert.Equal(ViewFilter.All, h.View.Filter);
    }

    [AvaloniaFact]
    public void NextAndPreviousSection_MoveCaret()
    {
        using var h = new Harness();
        h.View.LeftPane.Editor.Focus();
        h.View.GoToRow(0, h.View.LeftPane);
        h.Pump();
        h.Execute(Cmd.NextSection);
        int first = h.View.CurrentRow;
        h.Execute(Cmd.NextSection);
        int second = h.View.CurrentRow;
        Assert.True(second > first, $"{second} > {first}");
        h.Execute(Cmd.PreviousSection);
        Assert.Equal(first, h.View.CurrentRow);
    }

    [AvaloniaFact]
    public void IgnoreUnimportant_HidesWhitespaceOnlyDifference()
    {
        using var h = new Harness();
        int before = h.View.Result!.Stats.UnimportantSections;
        Assert.True(before > 0, "La línea con espacios al final debe contar como diferencia menor.");
        h.Execute(Cmd.IgnoreUnimportant);
        Assert.True(h.View.RenderState.IgnoreUnimportant);
    }

    [AvaloniaFact]
    public void Bookmarks_ToggleAndJump()
    {
        using var h = new Harness();
        h.View.LeftPane.Editor.Focus();
        h.View.GoToRow(5, h.View.LeftPane);
        h.Pump();
        h.Execute(Cmd.ToggleBookmark3);
        h.View.GoToRow(0, h.View.LeftPane);
        h.Pump();
        h.Execute(Cmd.GoToBookmark3);
        Assert.Equal(5, h.View.CurrentRow);
    }

    [AvaloniaFact]
    public void ReadOnlySide_RejectsCopy()
    {
        using var h = new Harness();
        h.View.RightReadOnlyArg = true;
        string before = Text(h.View.Right);
        h.View.LeftPane.Editor.Focus();
        h.Execute(Cmd.SelectAll);
        h.Execute(Cmd.CopyToRight);
        Assert.Equal(before, Text(h.View.Right));
    }

    [AvaloniaFact]
    public void MissingFile_ShowsErrorWithoutCrashing()
    {
        using var h = new Harness();
        h.Run(h.View.OpenIntoSideAsync(Side.Left, Path.Combine(h.Folder, "no-existe.txt")));
        Assert.NotNull(h.View.Left.LoadError);
        Assert.Empty(h.Unhandled);
    }

    [AvaloniaFact]
    public void IdenticalFiles_ReportNoSections()
    {
        using var h = new Harness(Harness.LeftText, Harness.LeftText);
        Assert.Empty(h.View.Result!.Sections);
        h.Execute(Cmd.NextSection);
        Assert.Empty(h.Unhandled);
    }

    [AvaloniaFact]
    public void EmptyFiles_Work()
    {
        using var h = new Harness("", "");
        Assert.Empty(h.View.Result!.Sections);
        foreach (var cmd in new[] { Cmd.SelectAll, Cmd.CopyToRight, Cmd.DeleteLine, Cmd.InsertLineAfter, Cmd.NextSection, Cmd.Undo })
            h.Execute(cmd);
        Assert.Empty(h.Unhandled);
    }

    [AvaloniaFact]
    public void LargeFiles_LoadAndCompare()
    {
        var sb1 = new StringBuilder();
        var sb2 = new StringBuilder();
        for (int i = 0; i < 20000; i++)
        {
            sb1.Append("línea ").Append(i).Append('\n');
            sb2.Append("línea ").Append(i % 97 == 0 ? -i : i).Append('\n');
        }
        using var h = new Harness(sb1.ToString(), sb2.ToString(), ".txt");
        Assert.True(h.View.Result!.Sections.Count > 100);
        h.Execute(Cmd.NextSection);
        h.Execute(Cmd.ShowDifferences);
        Assert.Empty(h.Unhandled);
    }

    [AvaloniaFact]
    public void Session_LoadsFilesAndViewState()
    {
        using var h = new Harness();
        var session = new SessionFile { LeftSpec = h.LeftPath, RightSpec = h.RightPath, Filter = ViewFilter.Differences, WordWrap = true, OverUnder = true };
        session.Settings.CaseSensitive = false;
        string path = Path.Combine(h.Folder, "s" + TextCompareView.SessionExtension);
        File.WriteAllText(path, DiffForge.App.Services.AppSettings.Serialize(session));

        h.Run(h.Window.OpenSessionFileAsync(path));
        Assert.Equal(h.LeftPath, h.View.Left.Spec);
        Assert.Equal(ViewFilter.Differences, h.View.Filter);
        Assert.True(h.View.WordWrap && h.View.OverUnder);
        Assert.False(h.View.Settings.CaseSensitive);
        Assert.Equal("s", h.View.TabTitle);
    }

    [AvaloniaFact]
    public void ZipMember_OpensReadOnly()
    {
        using var h = new Harness();
        string zip = Path.Combine(h.Folder, "a.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(archive.CreateEntry("src/x.cs").Open()))
            w.Write(Harness.LeftText);
        h.Run(h.View.OpenIntoSideAsync(Side.Right, Path.Combine(zip, "src", "x.cs")));
        Assert.Null(h.View.Right.LoadError);
        Assert.True(h.View.IsSideReadOnly(Side.Right));
        Assert.Empty(h.View.Result!.Sections);
    }

    [AvaloniaFact]
    public void Tabs_NewAndClose()
    {
        using var h = new Harness();
        h.Execute(Cmd.NewComparison);
        Assert.Null(h.View.Result?.Sections.Count > 0 ? h.View.Result : null);
        h.Execute(Cmd.CloseTab);
        Assert.NotNull(h.View.Result);
    }
}
