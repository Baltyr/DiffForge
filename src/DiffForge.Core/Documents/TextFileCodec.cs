using System.Text;

namespace DiffForge.Core.Documents;

/// <summary>Lectura y escritura de <see cref="TextFile"/> con round-trip exacto de bytes.</summary>
public static class TextFileCodec
{
    static TextFileCodec() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Codificaciones ofrecidas en la UI para forzar la lectura.</summary>
    public static IReadOnlyList<string> KnownEncodings { get; } =
        ["utf-8", "utf-16", "utf-16BE", "utf-32", "windows-1252", "iso-8859-1", "us-ascii", "ibm850"];

    public static TextFile Decode(byte[] bytes, Encoding? encodingOverride = null, bool ctrlZIsEof = false)
    {
        var (detected, bomLength) = DetectBom(bytes);
        Encoding encoding;
        bool hasBom = bomLength > 0;

        if (encodingOverride is not null)
        {
            encoding = WithoutPreamble(encodingOverride);
            if (detected is not null && detected.CodePage != encodingOverride.CodePage) { bomLength = 0; hasBom = false; }
        }
        else if (detected is not null)
        {
            encoding = detected;
        }
        else
        {
            encoding = IsValidUtf8(bytes) ? new UTF8Encoding(false) : Encoding.GetEncoding(1252);
        }

        string text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        if (ctrlZIsEof && text.IndexOf('\u001A') is var eof and >= 0) text = text[..eof];

        return new TextFile(SplitLines(text), encoding, hasBom);
    }

    public static byte[] Encode(TextFile file)
    {
        var body = WithoutPreamble(file.Encoding).GetBytes(file.ToText());
        if (!file.HasBom) return body;

        var preamble = PreambleFor(file.Encoding);
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    public static IReadOnlyList<TextLine> SplitLines(string content)
    {
        var lines = new List<TextLine>();
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c is not ('\r' or '\n')) continue;

            LineEnding ending;
            if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') { ending = LineEnding.CrLf; }
            else ending = c == '\r' ? LineEnding.Cr : LineEnding.Lf;

            lines.Add(new TextLine(content[start..i], ending));
            if (ending == LineEnding.CrLf) i++;
            start = i + 1;
        }
        if (start < content.Length) lines.Add(new TextLine(content[start..], LineEnding.None));
        return lines;
    }

    public static Encoding ResolveEncoding(string name) => WithoutPreamble(Encoding.GetEncoding(name));

    private static (Encoding? Encoding, int BomLength) DetectBom(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) return (new UTF32Encoding(false, false), 4);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (new UTF8Encoding(false), 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (new UnicodeEncoding(false, false), 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (new UnicodeEncoding(true, false), 2);
        return (null, 0);
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding WithoutPreamble(Encoding e) => e switch
    {
        UTF8Encoding => new UTF8Encoding(false),
        UnicodeEncoding u => new UnicodeEncoding(u.CodePage == 1201, false),
        UTF32Encoding u32 => new UTF32Encoding(u32.CodePage == 12001, false),
        _ => e,
    };

    private static byte[] PreambleFor(Encoding e) => e.CodePage switch
    {
        65001 => [0xEF, 0xBB, 0xBF],
        1200 => [0xFF, 0xFE],
        1201 => [0xFE, 0xFF],
        12000 => [0xFF, 0xFE, 0, 0],
        12001 => [0, 0, 0xFE, 0xFF],
        _ => [],
    };
}
