using System.Text;
using DiffForge.Core.Documents;

namespace DiffForge.Core.Tests;

public class TextFileCodecTests
{
    public static TheoryData<byte[]> Samples() => new()
    {
        Encoding.UTF8.GetBytes("a\r\nb\nc\rd"),
        Encoding.UTF8.GetBytes("línea\r\n\r\nfinal\r\n"),
        Encoding.UTF8.GetBytes(""),
        Encoding.UTF8.GetBytes("\n\n"),
        Concat([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes("con BOM\n")),
        Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes("utf16 ñ\r\nx")),
        Concat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes("utf16be\n")),
        new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x0A }, // "café" en Windows-1252 (UTF-8 inválido)
    };

    private static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];

    [Theory]
    [MemberData(nameof(Samples))]
    public void RoundTrip_PreservesBytes(byte[] bytes)
    {
        var file = TextFileCodec.Decode(bytes);
        Assert.Equal(bytes, TextFileCodec.Encode(file));
    }

    [Fact]
    public void Decode_KeepsEndingsPerLine()
    {
        var file = TextFileCodec.Decode(Encoding.UTF8.GetBytes("a\r\nb\nc"));
        Assert.Equal([LineEnding.CrLf, LineEnding.Lf, LineEnding.None], file.Lines.Select(l => l.Ending));
        Assert.Equal("Mixtos", file.LineEndingSummary);
    }

    [Fact]
    public void Decode_FallsBackToAnsi_WhenNotUtf8()
    {
        var file = TextFileCodec.Decode([0x63, 0x61, 0x66, 0xE9]);
        Assert.Equal("café", file.Lines[0].Text);
        Assert.Equal(1252, file.Encoding.CodePage);
    }

    [Fact]
    public void CtrlZ_TruncatesWhenEnabled()
    {
        var file = TextFileCodec.Decode(Encoding.ASCII.GetBytes("ab\u001Acd"), ctrlZIsEof: true);
        Assert.Equal("ab", Assert.Single(file.Lines).Text);
    }
}
