using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DiffForge.App.Dialogs;
using DiffForge.App.Services;
using DiffForge.Core.Compare;
using DiffForge.Core.Reports;

namespace DiffForge.App.Views;

/// <summary>Sesión guardada (.dfsession): especificaciones, reglas y estado de vista.</summary>
public sealed class SessionFile
{
    public string? LeftSpec { get; set; }
    public string? RightSpec { get; set; }
    public string? LeftFormat { get; set; }
    public string? RightFormat { get; set; }
    public string? LeftEncoding { get; set; }
    public string? RightEncoding { get; set; }
    public bool DisableEditing { get; set; }
    public string Description { get; set; } = string.Empty;
    public CompareSettings Settings { get; set; } = new();
    public ViewFilter Filter { get; set; }
    public bool IgnoreUnimportant { get; set; }
    public bool WordWrap { get; set; }
    public bool OverUnder { get; set; }
    public bool VisibleWhitespace { get; set; }
}

public sealed partial class TextCompareView
{
    public const string SessionExtension = ".dfsession";

    private async Task SaveSessionAsync(bool saveAs)
    {
        if (OwnerWindow is not { } owner) return;
        string? path = SessionPath;
        if (saveAs || path is null)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar sesión",
                SuggestedFileName = "comparacion" + SessionExtension,
                DefaultExtension = SessionExtension,
                FileTypeChoices = [new FilePickerFileType("Sesión DiffForge") { Patterns = ["*" + SessionExtension] }],
            });
            path = file?.TryGetLocalPath();
            if (path is null) return;
        }

        var session = new SessionFile
        {
            LeftSpec = Left.IsUntitled ? null : Left.Spec,
            RightSpec = Right.IsUntitled ? null : Right.Spec,
            LeftFormat = Left.FormatOverride,
            RightFormat = Right.FormatOverride,
            LeftEncoding = Left.EncodingOverride,
            RightEncoding = Right.EncodingOverride,
            DisableEditing = DisableEditing,
            Description = Description,
            Settings = Settings,
            Filter = Filter,
            IgnoreUnimportant = RenderState.IgnoreUnimportant,
            WordWrap = WordWrap,
            OverUnder = OverUnder,
            VisibleWhitespace = VisibleWhitespace,
        };
        await File.WriteAllTextAsync(path, AppSettings.Serialize(session));
        SessionPath = path;
        SetMessage($"Sesión guardada: {path}");
        TitleChanged?.Invoke();
    }

    public async Task LoadSessionAsync(string path)
    {
        var session = AppSettings.Deserialize<SessionFile>(await File.ReadAllTextAsync(path))
            ?? throw new InvalidDataException("El archivo de sesión está vacío o dañado.");
        SessionPath = path;
        Settings = session.Settings;
        DisableEditing = session.DisableEditing;
        Description = session.Description;
        Filter = session.Filter;
        RenderState.IgnoreUnimportant = session.IgnoreUnimportant;
        WordWrap = session.WordWrap;
        VisibleWhitespace = session.VisibleWhitespace;
        if (OverUnder != session.OverUnder) { OverUnder = session.OverUnder; ArrangePanes(); }
        Left = new SideDocument { FormatOverride = session.LeftFormat, EncodingOverride = session.LeftEncoding };
        Right = new SideDocument { FormatOverride = session.RightFormat, EncodingOverride = session.RightEncoding };
        await LoadAsync(session.LeftSpec, session.RightSpec);
    }

    private async Task ShowSessionSettingsAsync()
    {
        if (OwnerWindow is not { } owner) return;
        await FlushAsync();
        var dialog = new SessionSettingsDialog(this);
        if (!await dialog.ShowAsync(owner)) return;

        Settings = dialog.Settings;
        DisableEditing = dialog.DisableEditing;
        Description = dialog.Description;
        if (dialog.UpdateDefaults)
        {
            var defaults = Settings.Clone();
            defaults.ManualAlignments.Clear();
            defaults.IsolatedLeft.Clear(); defaults.IsolatedRight.Clear();
            defaults.IgnoredLeft.Clear(); defaults.IgnoredRight.Clear();
            AppSettings.Current.DefaultCompareSettings = defaults;
            AppSettings.Save();
        }

        bool reload = dialog.LeftSpec != (Left.IsUntitled ? "" : Left.Spec)
            || dialog.RightSpec != (Right.IsUntitled ? "" : Right.Spec)
            || dialog.LeftFormat != Left.FormatOverride || dialog.RightFormat != Right.FormatOverride
            || dialog.LeftEncoding != Left.EncodingOverride || dialog.RightEncoding != Right.EncodingOverride;

        if (reload)
        {
            if (!await ConfirmCloseAsync()) return;
            Left = new SideDocument { FormatOverride = dialog.LeftFormat, EncodingOverride = dialog.LeftEncoding };
            Right = new SideDocument { FormatOverride = dialog.RightFormat, EncodingOverride = dialog.RightEncoding };
            await LoadAsync(dialog.LeftSpec, dialog.RightSpec);
        }
        else
        {
            await RecompareAsync(forceRebuild: true);
        }
    }

    private async Task ShowReportAsync()
    {
        if (OwnerWindow is not { } owner || Result is null) return;
        await FlushAsync();
        var dialog = new ReportDialog();
        if (!await dialog.ShowAsync(owner)) return;

        var options = new ReportOptions(dialog.Layout, dialog.Filter, AppSettings.Current.Options.ContextLines, dialog.Html,
            Left.IsUntitled ? Left.DisplayName : Left.Spec, Right.IsUntitled ? Right.DisplayName : Right.Spec);
        string report = TextReport.Generate(Result, options);

        switch (dialog.Output)
        {
            case ReportOutput.Clipboard:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(report);
                SetMessage("Reporte copiado al portapapeles.");
                break;
            case ReportOutput.Browser:
                string temp = Path.Combine(Path.GetTempPath(), $"diffforge-reporte-{DateTime.Now:yyyyMMddHHmmss}.{(options.Html ? "html" : "txt")}");
                await File.WriteAllTextAsync(temp, report);
                Process.Start(new ProcessStartInfo(temp) { UseShellExecute = true });
                break;
            default:
                var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Guardar reporte",
                    SuggestedFileName = dialog.Layout == ReportLayout.Patch ? "cambios.patch" : options.Html ? "reporte.html" : "reporte.txt",
                });
                if (file?.TryGetLocalPath() is { } path)
                {
                    await File.WriteAllTextAsync(path, report);
                    SetMessage($"Reporte guardado: {path}");
                }
                break;
        }
    }

    private async Task ShowInfoAsync()
    {
        if (OwnerWindow is not { } owner || Result is null) return;
        var s = Result.Stats;
        string Side(Services.SideDocument d, string name) =>
            $"{name}: {(d.IsUntitled ? d.DisplayName : d.Spec)}\n" +
            $"   {d.Current.Lines.Count} líneas · {d.SizeBytes:N0} bytes · {d.Current.Encoding.WebName}{(d.Current.HasBom ? " (BOM)" : "")} · {d.Current.LineEndingSummary} · formato {d.Format.Name}";

        string text =
            Side(Left, "Izquierda") + "\n" + Side(Right, "Derecha") + "\n\n" +
            $"Secciones con diferencias importantes: {s.ImportantSections}\n" +
            $"Secciones con diferencias menores: {s.UnimportantSections}\n" +
            $"Líneas iguales: {s.SameLines}\n" +
            $"Líneas cambiadas (importantes): {s.ChangedLines}\n" +
            $"Líneas con diferencias menores: {s.UnimportantLines}\n" +
            $"Solo izquierda: {s.LeftOnlyLines} · Solo derecha: {s.RightOnlyLines}\n" +
            $"Alineación: {Settings.Alignment} · Tiempo de comparación: {s.Elapsed.TotalMilliseconds:0} ms";
        await Dialogs.MessageBox.ShowAsync(owner, "Información de la comparación", text);
    }
}
