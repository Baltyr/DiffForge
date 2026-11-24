namespace DiffForge.Core.Formats;

/// <summary>Formatos de archivo incluidos de fábrica. El usuario puede editarlos o agregar otros.</summary>
public static class BuiltInFormats
{
    public const string Comment = "Comment";
    public const string StringElement = "String";
    public const string Keyword = "Keyword";
    public const string Number = "Number";
    public const string Directive = "Directive";
    public const string Tag = "Tag";

    public const string PlainTextName = "Texto plano";

    private const string NumberRegex = @"\b(0[xX][0-9a-fA-F_]+|\d[\d_]*(\.\d+)?([eE][+-]?\d+)?[a-zA-Z]*)\b";

    public static List<FileFormat> Create() =>
    [
        CLike("C#", "*.cs;*.csx;*.cake",
            "abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach get goto if implicit in init int interface internal is lock long namespace new null object operator out override params private protected public readonly record ref return sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where while with yield",
            verbatimStrings: true, preprocessor: true),
        CLike("C/C++", "*.c;*.h;*.cpp;*.hpp;*.cc;*.cxx;*.hh;*.inl",
            "auto bool break case catch char class const constexpr continue default delete do double else enum explicit extern false float for friend goto if inline int long mutable namespace new noexcept nullptr operator private protected public register return short signed sizeof static struct switch template this throw true try typedef typename union unsigned using virtual void volatile while",
            verbatimStrings: false, preprocessor: true),
        CLike("Java / Kotlin", "*.java;*.kt;*.kts;*.groovy;*.scala",
            "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for fun if implements import instanceof int interface long native new null package private protected public return short static super switch synchronized this throw throws transient true false try val var void volatile when while",
            verbatimStrings: false, preprocessor: false),
        CLike("JavaScript / TypeScript", "*.js;*.mjs;*.cjs;*.jsx;*.ts;*.tsx;*.vue",
            "abstract any as async await boolean break case catch class const constructor continue debugger declare default delete do else enum export extends false finally for from function get if implements import in instanceof interface let new null number of private protected public readonly return set static string super switch this throw true try type typeof undefined var void while yield",
            verbatimStrings: false, preprocessor: false, templateStrings: true),
        Sql(),
        Xml(),
        Json(),
        Python(),
        PowerShell(),
        Ini(),
        new FileFormat
        {
            Name = PlainTextName,
            Masks = "*.*",
            Description = "Texto sin gramática. Se usa cuando ningún otro formato coincide.",
            Grammar = [new GrammarItem { Element = Number, Category = GrammarCategory.Basic, Text = NumberRegex, IsRegex = true }],
        },
    ];

    private static FileFormat CLike(string name, string masks, string keywords, bool verbatimStrings, bool preprocessor, bool templateStrings = false)
    {
        var grammar = new List<GrammarItem>
        {
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "//", StopAtEndOfLine = true },
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "/*", EndDelimiter = "*/" },
        };
        if (verbatimStrings)
            grammar.Add(new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "@\"", EndDelimiter = "\"" });
        if (templateStrings)
            grammar.Add(new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "`", EndDelimiter = "`", EscapeCharacter = "\\" });
        grammar.Add(new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"", EndDelimiter = "\"", EscapeCharacter = "\\", StopAtEndOfLine = true });
        grammar.Add(new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "'", EndDelimiter = "'", EscapeCharacter = "\\", StopAtEndOfLine = true });
        if (preprocessor)
            grammar.Add(new() { Element = Directive, Category = GrammarCategory.Basic, Text = @"^\s*#\s*\w+", IsRegex = true });
        grammar.Add(new() { Element = Keyword, Category = GrammarCategory.List, Tokens = [.. keywords.Split(' ')] });
        grammar.Add(new() { Element = Number, Category = GrammarCategory.Basic, Text = NumberRegex, IsRegex = true });

        return new FileFormat { Name = name, Masks = masks, Grammar = grammar, Description = $"Código fuente {name}." };
    }

    private static FileFormat Sql() => new()
    {
        Name = "SQL",
        Masks = "*.sql;*.ddl;*.prc;*.pkb;*.pks",
        Description = "T-SQL / ANSI SQL. Palabras clave sin distinción de mayúsculas.",
        Grammar =
        [
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "--", StopAtEndOfLine = true },
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "/*", EndDelimiter = "*/" },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "N'", EndDelimiter = "'", MatchCase = false },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "'", EndDelimiter = "'" },
            new() { Element = Keyword, Category = GrammarCategory.List, MatchCase = false, Tokens =
            [
                .. ("add all alter and as asc begin between by case cast check column commit constraint convert create cross cursor " +
                    "declare default delete desc distinct drop else end exec execute exists fetch for foreign from full function go grant " +
                    "group having if in index inner insert into is join key left like merge not null on open or order outer primary " +
                    "procedure references return right rollback select set table then top tran transaction trigger truncate union unique " +
                    "update values view when where while with nolock int bigint varchar nvarchar char nchar datetime date bit decimal money").Split(' '),
            ] },
            new() { Element = Number, Category = GrammarCategory.Basic, Text = NumberRegex, IsRegex = true },
        ],
        Elements = [new GrammarElement { Name = Keyword, CaseSensitive = false }],
    };

    private static FileFormat Xml() => new()
    {
        Name = "XML / HTML",
        Masks = "*.xml;*.xsd;*.xsl;*.xslt;*.config;*.csproj;*.props;*.targets;*.resx;*.html;*.htm;*.xaml;*.axaml;*.svg",
        Description = "Marcado. La gramática no es anidada: tags y atributos se reconocen por línea.",
        Grammar =
        [
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "<!--", EndDelimiter = "-->" },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"", EndDelimiter = "\"" },
            new() { Element = Tag, Category = GrammarCategory.Basic, Text = @"</?[\w:.-]+|/?>", IsRegex = true },
        ],
    };

    private static FileFormat Json() => new()
    {
        Name = "JSON",
        Masks = "*.json;*.jsonc;*.json5;*.geojson",
        Grammar =
        [
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "//", StopAtEndOfLine = true },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"", EndDelimiter = "\"", EscapeCharacter = "\\", StopAtEndOfLine = true },
            new() { Element = Keyword, Category = GrammarCategory.List, Tokens = ["true", "false", "null"] },
            new() { Element = Number, Category = GrammarCategory.Basic, Text = @"-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b", IsRegex = true },
        ],
    };

    private static FileFormat Python() => new()
    {
        Name = "Python",
        Masks = "*.py;*.pyw;*.pyi",
        TabStop = 4,
        InsertSpacesInsteadOfTabs = true,
        Grammar =
        [
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"\"\"", EndDelimiter = "\"\"\"" },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "'''", EndDelimiter = "'''" },
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "#", StopAtEndOfLine = true },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"", EndDelimiter = "\"", EscapeCharacter = "\\", StopAtEndOfLine = true },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "'", EndDelimiter = "'", EscapeCharacter = "\\", StopAtEndOfLine = true },
            new() { Element = Keyword, Category = GrammarCategory.List, Tokens =
                [.. "and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield self".Split(' ')] },
            new() { Element = Number, Category = GrammarCategory.Basic, Text = NumberRegex, IsRegex = true },
        ],
    };

    private static FileFormat PowerShell() => new()
    {
        Name = "PowerShell",
        Masks = "*.ps1;*.psm1;*.psd1",
        Grammar =
        [
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "<#", EndDelimiter = "#>" },
            new() { Element = Comment, Category = GrammarCategory.Delimited, StartDelimiter = "#", StopAtEndOfLine = true },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"", EndDelimiter = "\"", EscapeCharacter = "`" },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "'", EndDelimiter = "'" },
            new() { Element = Keyword, Category = GrammarCategory.List, MatchCase = false, Tokens =
                [.. "begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach function if in param process return switch throw trap try until using while -eq -ne -gt -lt -ge -le -like -notlike -match -notmatch -and -or -not".Split(' ')] },
            new() { Element = Number, Category = GrammarCategory.Basic, Text = NumberRegex, IsRegex = true },
        ],
        Elements = [new GrammarElement { Name = Keyword, CaseSensitive = false }],
    };

    private static FileFormat Ini() => new()
    {
        Name = "INI / Config",
        Masks = "*.ini;*.cfg;*.conf;*.properties;*.env;*.editorconfig;*.toml;*.yaml;*.yml",
        LinesAreIndependent = true,
        Grammar =
        [
            new() { Element = Comment, Category = GrammarCategory.Basic, Text = @"^\s*[;#].*$", IsRegex = true },
            new() { Element = Keyword, Category = GrammarCategory.Basic, Text = @"^\s*\[[^\]]*\]", IsRegex = true },
            new() { Element = StringElement, Category = GrammarCategory.Delimited, StartDelimiter = "\"", EndDelimiter = "\"", StopAtEndOfLine = true },
        ],
    };

    public static FileFormat Detect(IReadOnlyList<FileFormat> formats, string? fileName)
    {
        if (!string.IsNullOrEmpty(fileName))
        {
            foreach (var format in formats)
            {
                if (format.Name != PlainTextName && format.Matches(fileName)) return format;
            }
        }
        return formats.FirstOrDefault(f => f.Name == PlainTextName) ?? formats[^1];
    }
}
