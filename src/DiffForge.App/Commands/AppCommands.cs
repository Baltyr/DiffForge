using Avalonia.Input;

namespace DiffForge.App.Commands;

public enum Cmd
{
    // Sesión
    NewComparison, OpenSession, SaveSession, SaveSessionAs, SessionSettings, Locked, ClearSession, SwapSides,
    ReloadFiles, RecompareFiles, Report, Info, CompareFilesUsing, MergeFiles, CompareParentFolders, CloseTab, Exit,
    // Archivo
    OpenFileLeft, OpenFileRight, OpenFile, OpenClipboard, OpenWithAssociated, OpenWithTextEdit, OpenWithTextPatch,
    SaveFile, SaveFileAs, SaveAll, Explorer, CheckIn, CheckOut, UndoCheckOut,
    // Editar
    Undo, Redo, AlignWith, Isolate, AddReplacement, CopyToRight, CopyToLeft, CopyToOtherSide,
    CopyLineToRight, CopyLineToLeft, CopyLineToOtherSide, IncreaseIndent, DecreaseIndent,
    DeleteLine, DeleteToStartOfLine, DeleteToEndOfLine, DeleteWord, DeleteToStartOfWord, DeleteToEndOfWord,
    InsertLineBefore, InsertLineAfter, SelectAll, SelectSection, CompareSelectionToClipboard,
    ConvertTrimTrailing, ConvertLeadingSpacesToTabs, ConvertTabsToSpaces, ConvertEndingsCrLf, ConvertEndingsLf, ConvertEndingsCr,
    // Buscar
    NextDifference, PreviousDifference, NextSection, PreviousSection, NextDifferenceFiles, PreviousDifferenceFiles,
    CopyFileRightAndNext, CopyFileLeftAndNext, NextReplacement, PreviousReplacement, NextEdit, PreviousEdit,
    Find, Replace, FindNext, FindPrevious, GoTo, ClearBookmarks,
    ToggleBookmark0, ToggleBookmark1, ToggleBookmark2, ToggleBookmark3, ToggleBookmark4,
    ToggleBookmark5, ToggleBookmark6, ToggleBookmark7, ToggleBookmark8, ToggleBookmark9,
    GoToBookmark0, GoToBookmark1, GoToBookmark2, GoToBookmark3, GoToBookmark4,
    GoToBookmark5, GoToBookmark6, GoToBookmark7, GoToBookmark8, GoToBookmark9,
    // Ver
    ShowAll, ShowDifferences, ShowSame, ShowContext, ShowNone, IgnoreUnimportant, Ignored,
    VisibleWhitespace, LineNumbers, SyntaxHighlighting, WordWrap, DisplayFont, FontIncrease, FontDecrease, FontReset,
    SideBySide, OverUnder, Webpages, Thumbnail, TextDetails, HexDetails, AlignmentDetails, Ruler, FileInfo, Toolbar,
    // Herramientas / Ayuda
    Options, FileFormats, KeyboardShortcuts, About,
}

public sealed record CommandDef(
    Cmd Id,
    string Menu,
    string Header,
    string? Gesture = null,
    string? Submenu = null,
    bool Toggle = false,
    bool Separator = false,
    string? ToolbarGlyph = null,
    string? ToolbarLabel = null,
    bool Unavailable = false)
{
    public KeyGesture? KeyGesture => Gesture is null ? null : Avalonia.Input.KeyGesture.Parse(Gesture);
}

/// <summary>Tabla única de comandos: genera menús, barra de herramientas y atajos de teclado.</summary>
public static class AppCommands
{
    public const string SessionMenu = "_Sesión";
    public const string FileMenu = "_Archivo";
    public const string EditMenu = "_Editar";
    public const string SearchMenu = "_Buscar";
    public const string ViewMenu = "_Ver";
    public const string ToolsMenu = "_Herramientas";
    public const string HelpMenu = "A_yuda";

    public static IReadOnlyList<string> Menus { get; } = [SessionMenu, FileMenu, EditMenu, SearchMenu, ViewMenu, ToolsMenu, HelpMenu];

    public static IReadOnlyList<CommandDef> All { get; } = Build();

    private static List<CommandDef> Build()
    {
        var list = new List<CommandDef>
        {
            new(Cmd.NewComparison, SessionMenu, "Nueva comparación", "Ctrl+T"),
            new(Cmd.OpenSession, SessionMenu, "Abrir sesión…", "Ctrl+Shift+O"),
            new(Cmd.SaveSession, SessionMenu, "Guardar sesión", "Ctrl+Alt+S"),
            new(Cmd.SaveSessionAs, SessionMenu, "Guardar sesión como…"),
            new(Cmd.SessionSettings, SessionMenu, "Configuración de la sesión…", "Ctrl+Alt+R", Separator: true, ToolbarGlyph: "⚙", ToolbarLabel: "Reglas"),
            new(Cmd.Locked, SessionMenu, "Bloqueada", Toggle: true),
            new(Cmd.ClearSession, SessionMenu, "Limpiar sesión"),
            new(Cmd.SwapSides, SessionMenu, "Intercambiar lados", "Ctrl+I", ToolbarGlyph: "⇄", ToolbarLabel: "Intercambiar"),
            new(Cmd.ReloadFiles, SessionMenu, "Recargar archivos", "Ctrl+Shift+R", ToolbarGlyph: "⟳", ToolbarLabel: "Recargar"),
            new(Cmd.RecompareFiles, SessionMenu, "Recomparar", "F5"),
            new(Cmd.Report, SessionMenu, "Reporte de comparación…", Separator: true),
            new(Cmd.Info, SessionMenu, "Información de la comparación…"),
            new(Cmd.CompareFilesUsing, SessionMenu, "Comparar archivos usando ▸ Text Compare", Separator: true, Unavailable: true),
            new(Cmd.MergeFiles, SessionMenu, "Unir archivos (Text Merge)", Unavailable: true),
            new(Cmd.CompareParentFolders, SessionMenu, "Comparar carpetas padre", Unavailable: true),
            new(Cmd.CloseTab, SessionMenu, "Cerrar pestaña", "Ctrl+W", Separator: true),
            new(Cmd.Exit, SessionMenu, "Salir", "Alt+F4"),

            new(Cmd.OpenFile, FileMenu, "Abrir archivo en el panel activo…", "Ctrl+O", ToolbarGlyph: "📂", ToolbarLabel: "Abrir"),
            new(Cmd.OpenFileLeft, FileMenu, "Abrir archivo izquierdo…"),
            new(Cmd.OpenFileRight, FileMenu, "Abrir archivo derecho…"),
            new(Cmd.OpenClipboard, FileMenu, "Abrir portapapeles en el panel activo"),
            new(Cmd.OpenWithAssociated, FileMenu, "Aplicación asociada", Submenu: "Abrir con", Separator: true),
            new(Cmd.OpenWithTextEdit, FileMenu, "Text Edit", Submenu: "Abrir con", Unavailable: true),
            new(Cmd.OpenWithTextPatch, FileMenu, "Text Patch", Submenu: "Abrir con", Unavailable: true),
            new(Cmd.SaveFile, FileMenu, "Guardar archivo", "Ctrl+S", Separator: true, ToolbarGlyph: "💾", ToolbarLabel: "Guardar"),
            new(Cmd.SaveFileAs, FileMenu, "Guardar archivo como…"),
            new(Cmd.SaveAll, FileMenu, "Guardar ambos", "Ctrl+Shift+S"),
            new(Cmd.Explorer, FileMenu, "Mostrar en el explorador", Separator: true),
            new(Cmd.CheckIn, FileMenu, "Check In (control de versiones)", Unavailable: true),
            new(Cmd.CheckOut, FileMenu, "Check Out", Unavailable: true),
            new(Cmd.UndoCheckOut, FileMenu, "Deshacer Check Out", Unavailable: true),

            new(Cmd.Undo, EditMenu, "Deshacer", "Ctrl+Z"),
            new(Cmd.Redo, EditMenu, "Rehacer", "Ctrl+Y"),
            new(Cmd.AlignWith, EditMenu, "Alinear con…", "Ctrl+Alt+A", Separator: true),
            new(Cmd.Isolate, EditMenu, "Aislar", "Ctrl+Alt+X"),
            new(Cmd.AddReplacement, EditMenu, "Agregar reemplazo…"),
            new(Cmd.CopyToRight, EditMenu, "Copiar a la derecha", "Ctrl+R", Separator: true, ToolbarGlyph: "→", ToolbarLabel: "Copiar →"),
            new(Cmd.CopyToLeft, EditMenu, "Copiar a la izquierda", "Ctrl+L", ToolbarGlyph: "←", ToolbarLabel: "← Copiar"),
            new(Cmd.CopyToOtherSide, EditMenu, "Copiar al otro lado", "Ctrl+Shift+Enter"),
            new(Cmd.CopyLineToRight, EditMenu, "Copiar línea a la derecha", "Alt+Right"),
            new(Cmd.CopyLineToLeft, EditMenu, "Copiar línea a la izquierda", "Alt+Left"),
            new(Cmd.CopyLineToOtherSide, EditMenu, "Copiar línea al otro lado"),
            new(Cmd.IncreaseIndent, EditMenu, "Aumentar sangría", "Ctrl+OemCloseBrackets", Separator: true),
            new(Cmd.DecreaseIndent, EditMenu, "Disminuir sangría", "Ctrl+OemOpenBrackets"),
            new(Cmd.DeleteLine, EditMenu, "Eliminar línea", "Ctrl+D", Separator: true),
            new(Cmd.DeleteToStartOfLine, EditMenu, "Eliminar hasta inicio de línea", "Ctrl+Shift+Back"),
            new(Cmd.DeleteToEndOfLine, EditMenu, "Eliminar hasta fin de línea", "Ctrl+Shift+Delete"),
            new(Cmd.DeleteWord, EditMenu, "Eliminar palabra", "Ctrl+Alt+Delete"),
            new(Cmd.DeleteToStartOfWord, EditMenu, "Eliminar hasta inicio de palabra", "Ctrl+Back"),
            new(Cmd.DeleteToEndOfWord, EditMenu, "Eliminar hasta fin de palabra", "Ctrl+Delete"),
            new(Cmd.InsertLineBefore, EditMenu, "Insertar línea antes", "Ctrl+Alt+Up"),
            new(Cmd.InsertLineAfter, EditMenu, "Insertar línea después", "Ctrl+Alt+Down"),
            new(Cmd.SelectAll, EditMenu, "Seleccionar todo", "Ctrl+A", Separator: true),
            new(Cmd.SelectSection, EditMenu, "Seleccionar sección", "Ctrl+Shift+A"),
            new(Cmd.CompareSelectionToClipboard, EditMenu, "Comparar selección con el portapapeles"),
            new(Cmd.ConvertTrimTrailing, EditMenu, "Quitar espacios al final", Submenu: "Convertir archivo", Separator: true),
            new(Cmd.ConvertLeadingSpacesToTabs, EditMenu, "Espacios iniciales a tabs", Submenu: "Convertir archivo"),
            new(Cmd.ConvertTabsToSpaces, EditMenu, "Tabs a espacios", Submenu: "Convertir archivo"),
            new(Cmd.ConvertEndingsCrLf, EditMenu, "Finales de línea PC (CRLF)", Submenu: "Convertir archivo"),
            new(Cmd.ConvertEndingsLf, EditMenu, "Finales de línea Unix (LF)", Submenu: "Convertir archivo"),
            new(Cmd.ConvertEndingsCr, EditMenu, "Finales de línea Mac (CR)", Submenu: "Convertir archivo"),

            new(Cmd.NextDifference, SearchMenu, "Siguiente diferencia", "Ctrl+Shift+N"),
            new(Cmd.PreviousDifference, SearchMenu, "Diferencia anterior", "Ctrl+Shift+P"),
            new(Cmd.NextSection, SearchMenu, "Siguiente sección con diferencias", "Ctrl+N", ToolbarGlyph: "▼", ToolbarLabel: "Siguiente"),
            new(Cmd.PreviousSection, SearchMenu, "Sección anterior con diferencias", "Ctrl+P", ToolbarGlyph: "▲", ToolbarLabel: "Anterior"),
            new(Cmd.NextDifferenceFiles, SearchMenu, "Siguientes archivos con diferencias", Separator: true, Unavailable: true),
            new(Cmd.PreviousDifferenceFiles, SearchMenu, "Archivos anteriores con diferencias", Unavailable: true),
            new(Cmd.CopyFileRightAndNext, SearchMenu, "Copiar archivo a la derecha y abrir siguiente", Unavailable: true),
            new(Cmd.CopyFileLeftAndNext, SearchMenu, "Copiar archivo a la izquierda y abrir siguiente", Unavailable: true),
            new(Cmd.NextReplacement, SearchMenu, "Siguiente reemplazo", Separator: true),
            new(Cmd.PreviousReplacement, SearchMenu, "Reemplazo anterior"),
            new(Cmd.NextEdit, SearchMenu, "Siguiente edición", "Ctrl+Alt+N"),
            new(Cmd.PreviousEdit, SearchMenu, "Edición anterior", "Ctrl+Alt+P"),
            new(Cmd.Find, SearchMenu, "Buscar…", "Ctrl+F", Separator: true, ToolbarGlyph: "🔍", ToolbarLabel: "Buscar"),
            new(Cmd.Replace, SearchMenu, "Reemplazar…", "Ctrl+H"),
            new(Cmd.FindNext, SearchMenu, "Buscar siguiente", "F3"),
            new(Cmd.FindPrevious, SearchMenu, "Buscar anterior", "Shift+F3"),
            new(Cmd.GoTo, SearchMenu, "Ir a línea…", "Ctrl+G"),
            new(Cmd.ClearBookmarks, SearchMenu, "Quitar marcadores", Separator: true),
        };

        for (int d = 0; d <= 9; d++)
            list.Add(new((Cmd)((int)Cmd.ToggleBookmark0 + d), SearchMenu, $"Marcador {d}", $"Ctrl+Shift+D{d}", Submenu: "Alternar marcador"));
        for (int d = 0; d <= 9; d++)
            list.Add(new((Cmd)((int)Cmd.GoToBookmark0 + d), SearchMenu, $"Marcador {d}", $"Ctrl+D{d}", Submenu: "Ir a marcador"));

        list.AddRange(
        [
            new(Cmd.ShowAll, ViewMenu, "Mostrar todo", Toggle: true, ToolbarGlyph: "≡", ToolbarLabel: "Todo"),
            new(Cmd.ShowDifferences, ViewMenu, "Mostrar diferencias", Toggle: true, ToolbarGlyph: "≠", ToolbarLabel: "Difs"),
            new(Cmd.ShowSame, ViewMenu, "Mostrar iguales", Toggle: true, ToolbarGlyph: "=", ToolbarLabel: "Iguales"),
            new(Cmd.ShowContext, ViewMenu, "Mostrar contexto", Toggle: true, ToolbarGlyph: "⋯", ToolbarLabel: "Contexto"),
            new(Cmd.ShowNone, ViewMenu, "No mostrar nada", Toggle: true),
            new(Cmd.IgnoreUnimportant, ViewMenu, "Ignorar diferencias sin importancia", Toggle: true, Separator: true, ToolbarGlyph: "≈", ToolbarLabel: "Menores"),
            new(Cmd.Ignored, ViewMenu, "Ignorado (selección o sección)", "Ctrl+Alt+G"),
            new(Cmd.VisibleWhitespace, ViewMenu, "Espacios visibles", "Ctrl+Alt+W", Toggle: true, Separator: true, ToolbarGlyph: "¶", ToolbarLabel: "Espacios"),
            new(Cmd.LineNumbers, ViewMenu, "Números de línea", Toggle: true),
            new(Cmd.SyntaxHighlighting, ViewMenu, "Resaltado de sintaxis", Toggle: true),
            new(Cmd.WordWrap, ViewMenu, "Ajuste de línea", Toggle: true, ToolbarGlyph: "↩", ToolbarLabel: "Ajuste"),
            new(Cmd.DisplayFont, ViewMenu, "Fuente…", Separator: true),
            new(Cmd.FontIncrease, ViewMenu, "Aumentar tamaño de fuente", "Ctrl+OemPlus"),
            new(Cmd.FontDecrease, ViewMenu, "Disminuir tamaño de fuente", "Ctrl+OemMinus"),
            new(Cmd.FontReset, ViewMenu, "Restablecer tamaño de fuente", "Ctrl+Alt+D0"),
            new(Cmd.SideBySide, ViewMenu, "Diseño lado a lado", Toggle: true, Separator: true),
            new(Cmd.OverUnder, ViewMenu, "Diseño arriba/abajo", Toggle: true),
            new(Cmd.Webpages, ViewMenu, "Páginas web", Toggle: true, Unavailable: true),
            new(Cmd.Thumbnail, ViewMenu, "Miniatura", Toggle: true, Separator: true),
            new(Cmd.TextDetails, ViewMenu, "Detalle de texto", Toggle: true),
            new(Cmd.HexDetails, ViewMenu, "Detalle hexadecimal", Toggle: true),
            new(Cmd.AlignmentDetails, ViewMenu, "Detalle de alineación", Toggle: true),
            new(Cmd.Ruler, ViewMenu, "Regla", Toggle: true),
            new(Cmd.FileInfo, ViewMenu, "Información de archivo", Toggle: true),
            new(Cmd.Toolbar, ViewMenu, "Barra de herramientas", Toggle: true),

            new(Cmd.Options, ToolsMenu, "Opciones…"),
            new(Cmd.FileFormats, ToolsMenu, "Formatos de archivo…"),

            new(Cmd.KeyboardShortcuts, HelpMenu, "Atajos de teclado", "F1"),
            new(Cmd.About, HelpMenu, "Acerca de DiffForge"),
        ]);
        return list;
    }

    public static CommandDef Get(Cmd id) => All.First(c => c.Id == id);
}
