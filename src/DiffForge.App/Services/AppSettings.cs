using System.Text.Json;
using System.Text.Json.Serialization;
using DiffForge.Core.Compare;
using DiffForge.Core.Formats;

namespace DiffForge.App.Services;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Colores de la vista de archivos. Cada entrada tiene valor para tema claro y oscuro.</summary>
public sealed class ColorScheme
{
    public Dictionary<string, string> Light { get; set; } = DefaultLight();
    public Dictionary<string, string> Dark { get; set; } = DefaultDark();

    public const string ImportantLine = "Línea con diferencia importante";
    public const string ImportantText = "Texto con diferencia importante";
    public const string ImportantTextBack = "Fondo de texto importante";
    public const string UnimportantLine = "Línea con diferencia menor";
    public const string UnimportantText = "Texto con diferencia menor";
    public const string ReplacementText = "Texto reemplazado";
    public const string Filler = "Hueco (línea ausente)";
    public const string CurrentSection = "Sección actual";
    public const string SearchMatch = "Coincidencia de búsqueda";
    public const string EditedMarker = "Marca de línea editada";
    public const string Stripes = "Franjas alternas";

    public static Dictionary<string, string> DefaultLight() => new()
    {
        [ImportantLine] = "#FFF1F0",
        [ImportantText] = "#C8102E",
        [ImportantTextBack] = "#FFD3D0",
        [UnimportantLine] = "#EEF4FF",
        [UnimportantText] = "#2156C9",
        [ReplacementText] = "#7B3FC4",
        [Filler] = "#F2F2F2",
        [CurrentSection] = "#40A0A0A0",
        [SearchMatch] = "#FFE58F",
        [EditedMarker] = "#E59400",
        [Stripes] = "#00000000",
        [BuiltInFormats.Comment] = "#2E7D32",
        [BuiltInFormats.StringElement] = "#A3501A",
        [BuiltInFormats.Keyword] = "#1F4FBF",
        [BuiltInFormats.Number] = "#0B7A75",
        [BuiltInFormats.Directive] = "#6B6B6B",
        [BuiltInFormats.Tag] = "#8A1E78",
    };

    public static Dictionary<string, string> DefaultDark() => new()
    {
        [ImportantLine] = "#3A2023",
        [ImportantText] = "#FF7B7B",
        [ImportantTextBack] = "#5C2A2E",
        [UnimportantLine] = "#1E2A40",
        [UnimportantText] = "#7AA7FF",
        [ReplacementText] = "#C49BFF",
        [Filler] = "#2A2A2A",
        [CurrentSection] = "#50FFFFFF",
        [SearchMatch] = "#6B5B00",
        [EditedMarker] = "#FFB020",
        [Stripes] = "#00000000",
        [BuiltInFormats.Comment] = "#6FBF73",
        [BuiltInFormats.StringElement] = "#E3A06B",
        [BuiltInFormats.Keyword] = "#7FA8FF",
        [BuiltInFormats.Number] = "#5FD1CA",
        [BuiltInFormats.Directive] = "#A0A0A0",
        [BuiltInFormats.Tag] = "#E08AD0",
    };
}

public sealed class ProgramOptions
{
    // Edición de texto
    public bool AutoIndent { get; set; } = true;
    public bool BackspaceUnindents { get; set; } = true;
    public bool InitFindWithCurrentWord { get; set; } = true;
    public bool ShowFilteredLineCounts { get; set; } = true;
    public int ContextLines { get; set; } = 3;

    // Siguiente diferencia
    public bool GoToFirstDifferenceOnLoad { get; set; } = true;
    public bool GoToNextDifferenceAfterCopy { get; set; } = true;
    public bool WrapAroundDifferences { get; set; }
    public bool ShowNoMoreDifferencesMessage { get; set; } = true;

    // Apariencia
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public string EditorFont { get; set; } = "Cascadia Mono, Consolas, Menlo, monospace";
    public double EditorFontSize { get; set; } = 13;
    public ColorScheme Colors { get; set; } = new();
    public bool UseStripes { get; set; }

    // Copias de seguridad
    public bool BackupOnSave { get; set; } = true;
    public string BackupFolder { get; set; } = string.Empty;
    public string BackupSuffix { get; set; } = ".bak";

    // Vista por defecto
    public bool ShowLineNumbers { get; set; } = true;
    public bool ShowThumbnail { get; set; } = true;
    public bool ShowTextDetails { get; set; } = true;
    public bool ShowFileInfo { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool SyntaxHighlighting { get; set; } = true;
}

/// <summary>Configuración global persistida en %APPDATA%\DiffForge\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        IncludeFields = true,
    };

    public ProgramOptions Options { get; set; } = new();
    public List<FileFormat> FileFormats { get; set; } = BuiltInFormats.Create();
    public CompareSettings DefaultCompareSettings { get; set; } = new();
    public List<string> RecentFiles { get; set; } = [];
    public List<string> FindHistory { get; set; } = [];

    /// <summary>Carpeta de configuración; <c>DIFFFORGE_HOME</c> la redirige (tests, modo portable).</summary>
    public static string Folder =>
        Environment.GetEnvironmentVariable("DIFFFORGE_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiffForge");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                if (Current.FileFormats.Count == 0) Current.FileFormats = BuiltInFormats.Create();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Configuración corrupta o ilegible: se parte de valores por defecto.
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"No se pudo guardar la configuración: {ex.Message}");
        }
        Changed?.Invoke();
    }

    public static T DeepClone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json);
}
