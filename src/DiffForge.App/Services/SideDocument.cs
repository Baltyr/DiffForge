using System.Diagnostics;
using System.Text;
using DiffForge.Core.Documents;
using DiffForge.Core.Formats;

namespace DiffForge.App.Services;

/// <summary>Estado de un lado de la comparación: origen, contenido en disco y contenido actual.</summary>
public sealed class SideDocument
{
    public string Spec { get; set; } = string.Empty;
    /// <summary>Título visible cuando no hay archivo (portapapeles, selección).</summary>
    public string? Title { get; set; }
    public bool IsUntitled => string.IsNullOrEmpty(Spec);
    public bool SourceReadOnly { get; set; }
    public bool CanSaveInPlace { get; set; }
    public DateTime? LastWriteTime { get; set; }
    public long SizeBytes { get; set; }

    public TextFile Original { get; set; } = TextFile.Empty;
    public TextFile Current { get; set; } = TextFile.Empty;
    public FileFormat Format { get; set; } = BuiltInFormats.Create()[^1];
    /// <summary>null = formato detectado por máscara.</summary>
    public string? FormatOverride { get; set; }
    /// <summary>null = la codificación la decide el formato/detección.</summary>
    public string? EncodingOverride { get; set; }
    public bool IsModified { get; set; }
    public string? LoadError { get; set; }

    public string DisplayName => Title ?? (IsUntitled ? "(sin título)" : Path.GetFileName(Spec.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Spec);

    public SideDocument CloneShallow() => (SideDocument)MemberwiseClone();
}

/// <summary>Carga y guardado de documentos aplicando el formato (conversión, codificación, límites).</summary>
public static class DocumentService
{
    public static async Task<SideDocument> LoadAsync(string spec, string? formatOverride, string? encodingOverride, CancellationToken ct = default)
    {
        var formats = AppSettings.Current.FileFormats;
        var format = formatOverride is not null
            ? formats.FirstOrDefault(f => f.Name == formatOverride) ?? BuiltInFormats.Detect(formats, spec)
            : BuiltInFormats.Detect(formats, spec);

        var source = await SourceLoader.LoadAsync(spec, ct);
        byte[] bytes = source.Bytes;

        if (format.Conversion == ConversionMethod.ExternalProgram && !string.IsNullOrWhiteSpace(format.LoadCommand))
            bytes = await RunConversionAsync(format.LoadCommand, spec, bytes, ct);

        var encodingName = encodingOverride ?? format.EncodingName;
        var encoding = encodingName is null ? null : TextFileCodec.ResolveEncoding(encodingName);
        var file = TextFileCodec.Decode(bytes, encoding, format.CtrlZIsEndOfFile);
        if (format.CharactersPerLineLimit > 0) file = SplitLongLines(file, format.CharactersPerLineLimit);

        bool conversionBlocksSave = format.Conversion == ConversionMethod.ExternalProgram
            && (format.DisableEditing || string.IsNullOrWhiteSpace(format.SaveCommand));

        return new SideDocument
        {
            Spec = spec,
            SourceReadOnly = source.IsReadOnly || conversionBlocksSave,
            CanSaveInPlace = source.CanSaveInPlace && !conversionBlocksSave,
            LastWriteTime = source.LastWriteTime,
            SizeBytes = source.Bytes.LongLength,
            Original = file,
            Current = file,
            Format = format,
            FormatOverride = formatOverride,
            EncodingOverride = encodingOverride,
        };
    }

    public static SideDocument FromText(string text, string title)
    {
        var lines = TextFileCodec.SplitLines(text);
        var file = new TextFile(lines, new UTF8Encoding(false), false);
        return new SideDocument
        {
            Title = title,
            Original = file,
            Current = file,
            Format = BuiltInFormats.Detect(AppSettings.Current.FileFormats, null),
        };
    }

    /// <summary>Guarda en <paramref name="path"/>, creando backup si corresponde.</summary>
    public static async Task SaveAsync(SideDocument side, string path)
    {
        var format = side.Format;
        var file = side.Current;
        if (format.TrimTrailingWhitespaceOnSave) file = TextTransforms.TrimTrailingWhitespace(file);
        if (format.LeadingSpacesToTabsOnSave) file = TextTransforms.LeadingSpacesToTabs(file, format.TabStop);

        var options = AppSettings.Current.Options;
        if (options.BackupOnSave && File.Exists(path))
        {
            string folder = string.IsNullOrWhiteSpace(options.BackupFolder) ? Path.GetDirectoryName(path)! : options.BackupFolder;
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, Path.GetFileName(path) + options.BackupSuffix), overwrite: true);
        }

        byte[] bytes = TextFileCodec.Encode(file);
        if (format.Conversion == ConversionMethod.ExternalProgram && !string.IsNullOrWhiteSpace(format.SaveCommand))
        {
            string temp = Path.GetTempFileName();
            await File.WriteAllBytesAsync(temp, bytes);
            await RunProcessAsync(format.SaveCommand.Replace("%s", Quote(temp)).Replace("%t", Quote(path)).Replace("%o", Quote(side.Spec)), CancellationToken.None);
            File.Delete(temp);
        }
        else
        {
            await File.WriteAllBytesAsync(path, bytes);
        }

        side.Spec = path;
        side.Title = null;
        side.Current = file;
        side.Original = file;
        side.IsModified = false;
        side.CanSaveInPlace = true;
        side.SourceReadOnly = false;
        side.LastWriteTime = File.GetLastWriteTime(path);
        side.SizeBytes = new FileInfo(path).Length;
    }

    private static TextFile SplitLongLines(TextFile file, int limit)
    {
        var lines = new List<TextLine>(file.Lines.Count);
        foreach (var line in file.Lines)
        {
            string text = line.Text;
            // Los cortes artificiales llevan LineEnding.None: al guardar se concatenan y desaparecen.
            while (text.Length > limit)
            {
                lines.Add(new TextLine(text[..limit], LineEnding.None));
                text = text[limit..];
            }
            lines.Add(line with { Text = text });
        }
        return file.WithLines(lines);
    }

    private static async Task<byte[]> RunConversionAsync(string command, string spec, byte[] bytes, CancellationToken ct)
    {
        string source = File.Exists(spec) ? spec : Path.GetTempFileName();
        if (!File.Exists(spec)) await File.WriteAllBytesAsync(source, bytes, ct);
        string target = Path.GetTempFileName();
        try
        {
            await RunProcessAsync(command.Replace("%s", Quote(source)).Replace("%t", Quote(target)).Replace("%o", Quote(spec)), ct);
            var result = await File.ReadAllBytesAsync(target, ct);
            if (result.Length == 0) throw new InvalidOperationException("La conversión externa generó un archivo vacío.");
            return result;
        }
        finally
        {
            File.Delete(target);
            if (source != spec) File.Delete(source);
        }
    }

    private static async Task RunProcessAsync(string commandLine, CancellationToken ct)
    {
        var (exe, args) = SplitCommand(commandLine);
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar '{exe}'.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"La conversión '{exe}' terminó con código {process.ExitCode}.");
    }

    private static (string Exe, string Args) SplitCommand(string commandLine)
    {
        commandLine = commandLine.Trim();
        if (commandLine.StartsWith('"'))
        {
            int end = commandLine.IndexOf('"', 1);
            return (commandLine[1..end], commandLine[(end + 1)..].Trim());
        }
        int space = commandLine.IndexOf(' ');
        return space < 0 ? (commandLine, string.Empty) : (commandLine[..space], commandLine[(space + 1)..]);
    }

    private static string Quote(string path) => $"\"{path}\"";
}
