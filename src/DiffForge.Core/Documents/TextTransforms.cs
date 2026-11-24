using System.Text;

namespace DiffForge.Core.Documents;

/// <summary>Conversiones de archivo completo (menú Editar ▸ Convertir archivo).</summary>
public static class TextTransforms
{
    public static TextFile TrimTrailingWhitespace(TextFile file) =>
        file.WithLines(file.Lines.Select(l => l with { Text = l.Text.TrimEnd(' ', '\t') }).ToArray());

    public static TextFile TabsToSpaces(TextFile file, int tabStop) =>
        file.WithLines(file.Lines.Select(l => l with { Text = ExpandTabs(l.Text, tabStop) }).ToArray());

    public static TextFile LeadingSpacesToTabs(TextFile file, int tabStop) =>
        file.WithLines(file.Lines.Select(l => l with { Text = LeadingToTabs(l.Text, tabStop) }).ToArray());

    public static TextFile ConvertLineEndings(TextFile file, LineEnding ending) =>
        file.WithLines(file.Lines.Select((l, i) =>
            l.Ending == LineEnding.None && i == file.Lines.Count - 1 ? l : l with { Ending = ending }).ToArray());

    public static string ExpandTabs(string text, int tabStop)
    {
        if (!text.Contains('\t')) return text;
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (c == '\t') sb.Append(' ', tabStop - sb.Length % tabStop);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string LeadingToTabs(string text, int tabStop)
    {
        int i = 0, column = 0;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            column = text[i] == '\t' ? (column / tabStop + 1) * tabStop : column + 1;
            i++;
        }
        if (i == 0) return text;
        return new string('\t', column / tabStop) + new string(' ', column % tabStop) + text[i..];
    }
}
