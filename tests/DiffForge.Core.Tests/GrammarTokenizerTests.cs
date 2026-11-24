using DiffForge.Core.Formats;

namespace DiffForge.Core.Tests;

public class GrammarTokenizerTests
{
    private static readonly FileFormat CSharp = BuiltInFormats.Create().First(f => f.Name == "C#");

    [Fact]
    public void LineComment_RunsToEndOfLine()
    {
        int open = -1;
        var spans = new GrammarTokenizer(CSharp).TokenizeLine("x = 1; // nota", ref open);
        var comment = Assert.Single(spans, s => s.Element == BuiltInFormats.Comment);
        Assert.Equal((7, 7), (comment.Start, comment.Length));
    }

    [Fact]
    public void BlockComment_SpansLines()
    {
        var spans = new GrammarTokenizer(CSharp).TokenizeAll(["a /* uno", "dos", "tres */ b"]);
        Assert.Equal(BuiltInFormats.Comment, Assert.Single(spans[1]).Element);
        var last = spans[2].First();
        Assert.Equal((0, 7), (last.Start, last.Length));
    }

    [Fact]
    public void String_RespectsEscapedQuote()
    {
        int open = -1;
        var spans = new GrammarTokenizer(CSharp).TokenizeLine("s = \"a\\\"b\";", ref open);
        var str = Assert.Single(spans, s => s.Element == BuiltInFormats.StringElement);
        Assert.Equal("\"a\\\"b\"", "s = \"a\\\"b\";".Substring(str.Start, str.Length));
    }

    [Fact]
    public void Keyword_RequiresWordBoundary()
    {
        int open = -1;
        var spans = new GrammarTokenizer(CSharp).TokenizeLine("int interior;", ref open);
        Assert.Single(spans, s => s.Element == BuiltInFormats.Keyword);
    }

    [Fact]
    public void Detect_UsesMasks()
    {
        var formats = BuiltInFormats.Create();
        Assert.Equal("SQL", BuiltInFormats.Detect(formats, @"C:\x\Script.SQL").Name);
        Assert.Equal(BuiltInFormats.PlainTextName, BuiltInFormats.Detect(formats, "notas.txt").Name);
    }
}
