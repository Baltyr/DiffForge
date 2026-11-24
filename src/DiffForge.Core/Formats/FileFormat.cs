using System.Text.RegularExpressions;

namespace DiffForge.Core.Formats;

public enum GrammarCategory
{
    /// <summary>Texto o regex simple.</summary>
    Basic,
    /// <summary>Delimitado por inicio/fin (comentarios de bloque, strings). Puede abarcar varias líneas.</summary>
    Delimited,
    /// <summary>Lista de tokens (palabras clave).</summary>
    List,
    /// <summary>Rango de columnas fijo.</summary>
    Columns,
}

public sealed class GrammarItem
{
    public string Element { get; set; } = "Comment";
    public GrammarCategory Category { get; set; }
    public string Text { get; set; } = string.Empty;
    public string StartDelimiter { get; set; } = string.Empty;
    public string EndDelimiter { get; set; } = string.Empty;
    public bool StopAtEndOfLine { get; set; }
    public string EscapeCharacter { get; set; } = string.Empty;
    public List<string> Tokens { get; set; } = [];
    /// <summary>Columna inicial base 1 (categoría Columns).</summary>
    public int StartColumn { get; set; } = 1;
    /// <summary>Columna final base 1 inclusive; 0 = hasta fin de línea.</summary>
    public int EndColumn { get; set; }
    public bool MatchCase { get; set; } = true;
    public bool IsRegex { get; set; }

    public GrammarItem Clone()
    {
        var copy = (GrammarItem)MemberwiseClone();
        copy.Tokens = [.. Tokens];
        return copy;
    }

    public override string ToString() => Category switch
    {
        GrammarCategory.Basic => $"{Element}: {Text}",
        GrammarCategory.Delimited => $"{Element}: {StartDelimiter} … {(StopAtEndOfLine ? "EOL" : EndDelimiter)}",
        GrammarCategory.List => $"{Element}: {Tokens.Count} tokens",
        _ => $"{Element}: columnas {StartColumn}-{(EndColumn == 0 ? "EOL" : EndColumn)}",
    };
}

public sealed class GrammarElement
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Si es false, cambios solo de mayúsculas dentro del elemento no son importantes.</summary>
    public bool CaseSensitive { get; set; } = true;
}

public enum ConversionMethod { None, ExternalProgram }

public sealed class FileFormat
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Máscaras separadas por ';' (ej. <c>*.cs;*.csx</c>).</summary>
    public string Masks { get; set; } = "*.*";
    public string Description { get; set; } = string.Empty;

    public List<GrammarItem> Grammar { get; set; } = [];
    public List<GrammarElement> Elements { get; set; } = [];

    public int TabStop { get; set; } = 4;
    public bool InsertSpacesInsteadOfTabs { get; set; }
    public bool LinesAreIndependent { get; set; }
    public bool ColumnBasedData { get; set; }

    /// <summary>null = detectar automáticamente.</summary>
    public string? EncodingName { get; set; }
    public bool CtrlZIsEndOfFile { get; set; }
    /// <summary>0 = sin límite. Las líneas se cortan al cargar y se reunen al guardar.</summary>
    public int CharactersPerLineLimit { get; set; }
    public bool TrimTrailingWhitespaceOnSave { get; set; }
    public bool LeadingSpacesToTabsOnSave { get; set; }

    public ConversionMethod Conversion { get; set; }
    /// <summary>Comando de carga; %s = origen, %t = destino temporal.</summary>
    public string LoadCommand { get; set; } = string.Empty;
    /// <summary>Comando de guardado; %s = temporal editado, %t = destino, %o = original.</summary>
    public string SaveCommand { get; set; } = string.Empty;
    public bool DisableEditing { get; set; }

    public bool IsElementCaseSensitive(string element) =>
        Elements.FirstOrDefault(e => e.Name == element)?.CaseSensitive ?? true;

    public IEnumerable<string> ElementNames =>
        Grammar.Select(g => g.Element).Concat(Elements.Select(e => e.Name)).Distinct();

    public bool Matches(string fileName)
    {
        string name = Path.GetFileName(fileName);
        foreach (var mask in Masks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pattern = "^" + Regex.Escape(mask).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase)) return true;
        }
        return false;
    }

    public override string ToString() => Name;
}
