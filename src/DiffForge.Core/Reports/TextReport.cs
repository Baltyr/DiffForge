using System.Net;
using System.Text;
using DiffForge.Core.Compare;

namespace DiffForge.Core.Reports;

public enum ReportLayout { SideBySide, Interleaved, Patch }
public enum ReportFilter { All, Differences, Context }

public sealed record ReportOptions(
    ReportLayout Layout,
    ReportFilter Filter,
    int ContextLines,
    bool Html,
    string LeftTitle,
    string RightTitle);

/// <summary>Reportes de Text Compare: lado a lado, intercalado o parche unificado; HTML o texto.</summary>
public static class TextReport
{
    public static string Generate(ComparisonResult result, ReportOptions options) => options.Layout switch
    {
        ReportLayout.Patch => UnifiedPatch(result, options),
        _ when options.Html => Html(result, options),
        _ => PlainText(result, options),
    };

    private static bool[] VisibleRows(ComparisonResult result, ReportFilter filter, int context)
    {
        var rows = result.Rows;
        var visible = new bool[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            bool diff = rows[i].IsDifference;
            visible[i] = filter == ReportFilter.All || diff;
            if (filter == ReportFilter.Context && diff)
            {
                for (int k = Math.Max(0, i - context); k <= Math.Min(rows.Count - 1, i + context); k++) visible[k] = true;
            }
        }
        return visible;
    }

    private static string Html(ComparisonResult result, ReportOptions o)
    {
        var visible = VisibleRows(result, o.Filter, o.ContextLines);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Reporte DiffForge</title><style>")
          .Append("body{font-family:system-ui,sans-serif;margin:16px}table{border-collapse:collapse;width:100%;font:13px Consolas,monospace}")
          .Append("td{padding:0 6px;white-space:pre;vertical-align:top}.n{color:#888;text-align:right;user-select:none}")
          .Append(".imp{background:#fde8e8}.unimp{background:#e8f0fd}.gap{background:#f2f2f2}.i{color:#c00;background:#f9c9c9}")
          .Append(".u{color:#1d4ed8}.r{color:#7c3aed}.skip td{color:#888;background:#fafafa;font-style:italic}</style></head><body>");
        sb.Append($"<h2>Reporte de comparación de texto</h2><p>{Enc(o.LeftTitle)}<br>{Enc(o.RightTitle)}</p>");
        var s = result.Stats;
        sb.Append($"<p>{s.ImportantSections} sección(es) con diferencias importantes · {s.SameLines} líneas iguales · " +
                  $"{s.ChangedLines} cambiadas · {s.LeftOnlyLines} solo izquierda · {s.RightOnlyLines} solo derecha</p><table>");

        bool skipping = false;
        for (int i = 0; i < result.Rows.Count; i++)
        {
            if (!visible[i])
            {
                if (!skipping) sb.Append("<tr class=\"skip\"><td colspan=\"4\">…</td></tr>");
                skipping = true;
                continue;
            }
            skipping = false;
            var row = result.Rows[i];
            var intra = row.Kind == RowKind.Same ? null : result.CompareRow(row);
            string cls = row.Kind == RowKind.Same ? "" : row.Important ? "imp" : "unimp";

            string leftCell = row.Left >= 0 ? Highlight(result.Left[row.Left].Text, intra?.Left, row.Important) : "";
            string rightCell = row.Right >= 0 ? Highlight(result.Right[row.Right].Text, intra?.Right, row.Important) : "";
            string leftNo = row.Left >= 0 ? (row.Left + 1).ToString() : "";
            string rightNo = row.Right >= 0 ? (row.Right + 1).ToString() : "";

            if (o.Layout == ReportLayout.SideBySide)
            {
                sb.Append($"<tr><td class=\"n\">{leftNo}</td><td class=\"{(row.Left < 0 ? "gap" : cls)}\">{leftCell}</td>")
                  .Append($"<td class=\"n\">{rightNo}</td><td class=\"{(row.Right < 0 ? "gap" : cls)}\">{rightCell}</td></tr>");
            }
            else
            {
                if (row.Left >= 0) sb.Append($"<tr><td class=\"n\">{leftNo}</td><td class=\"n\">&lt;</td><td colspan=\"2\" class=\"{cls}\">{leftCell}</td></tr>");
                if (row.Right >= 0 && row.Kind != RowKind.Same)
                    sb.Append($"<tr><td class=\"n\">{rightNo}</td><td class=\"n\">&gt;</td><td colspan=\"2\" class=\"{cls}\">{rightCell}</td></tr>");
            }
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string Highlight(string text, IReadOnlyList<DiffSpan>? spans, bool rowImportant)
    {
        if (spans is null || spans.Count == 0) return Enc(text);
        var sb = new StringBuilder();
        int pos = 0;
        foreach (var span in spans)
        {
            sb.Append(Enc(text[pos..span.Start]));
            string cls = span.Kind == SpanKind.Replacement ? "r" : span.Kind == SpanKind.Important && rowImportant ? "i" : "u";
            sb.Append($"<span class=\"{cls}\">{Enc(text.Substring(span.Start, span.Length))}</span>");
            pos = span.Start + span.Length;
        }
        sb.Append(Enc(text[pos..]));
        return sb.ToString();
    }

    private static string PlainText(ComparisonResult result, ReportOptions o)
    {
        var visible = VisibleRows(result, o.Filter, o.ContextLines);
        var sb = new StringBuilder();
        sb.AppendLine($"Izquierda: {o.LeftTitle}").AppendLine($"Derecha:   {o.RightTitle}").AppendLine();

        int width = Math.Clamp(result.Left.Length == 0 ? 40 : result.Left.Max(l => l.Text.Length), 20, 80);
        bool skipping = false;
        for (int i = 0; i < result.Rows.Count; i++)
        {
            if (!visible[i])
            {
                if (!skipping) sb.AppendLine("…");
                skipping = true;
                continue;
            }
            skipping = false;
            var row = result.Rows[i];
            char marker = row.Kind == RowKind.Same ? ' ' : !row.Important ? '~' : row.Kind switch
            {
                RowKind.LeftOnly => '<',
                RowKind.RightOnly => '>',
                _ => '|',
            };
            string l = row.Left >= 0 ? result.Left[row.Left].Text : "";
            string r = row.Right >= 0 ? result.Right[row.Right].Text : "";

            if (o.Layout == ReportLayout.SideBySide)
            {
                string ln = row.Left >= 0 ? (row.Left + 1).ToString() : "";
                string rn = row.Right >= 0 ? (row.Right + 1).ToString() : "";
                string lt = l.Length > width ? l[..width] : l.PadRight(width);
                sb.AppendLine($"{ln,6} {lt} {marker} {rn,6} {r}");
            }
            else
            {
                if (row.Kind == RowKind.Same) { sb.AppendLine($"  {l}"); continue; }
                if (row.Left >= 0) sb.AppendLine($"< {l}");
                if (row.Right >= 0) sb.AppendLine($"> {r}");
            }
        }
        return sb.ToString();
    }

    /// <summary>Parche en formato unificado (compatible con git apply / patch).</summary>
    public static string UnifiedPatch(ComparisonResult result, ReportOptions o)
    {
        int ctx = Math.Max(0, o.ContextLines);
        var rows = result.Rows;
        var sb = new StringBuilder();
        sb.Append("--- ").AppendLine(o.LeftTitle).Append("+++ ").AppendLine(o.RightTitle);

        int i = 0;
        while (i < rows.Count)
        {
            if (!rows[i].IsDifference) { i++; continue; }

            int start = Math.Max(0, i - ctx);
            int end = i;
            while (end < rows.Count)
            {
                if (rows[end].IsDifference) { end++; continue; }
                int run = 0;
                while (end + run < rows.Count && !rows[end + run].IsDifference) run++;
                if (end + run >= rows.Count || run > ctx * 2) { end = Math.Min(rows.Count, end + ctx); break; }
                end += run;
            }

            int leftStart = -1, rightStart = -1, leftCount = 0, rightCount = 0;
            var body = new StringBuilder();
            for (int k = start; k < end; k++)
            {
                var row = rows[k];
                if (row.Left >= 0) { if (leftStart < 0) leftStart = row.Left; leftCount++; }
                if (row.Right >= 0) { if (rightStart < 0) rightStart = row.Right; rightCount++; }
            }
            int p = start;
            while (p < end)
            {
                if (rows[p].Kind == RowKind.Same) { body.Append(' ').AppendLine(result.Left[rows[p].Left].Text); p++; continue; }
                int q = p;
                while (q < end && rows[q].Kind != RowKind.Same) q++;
                for (int k = p; k < q; k++) if (rows[k].Left >= 0) body.Append('-').AppendLine(result.Left[rows[k].Left].Text);
                for (int k = p; k < q; k++) if (rows[k].Right >= 0) body.Append('+').AppendLine(result.Right[rows[k].Right].Text);
                p = q;
            }

            sb.Append($"@@ -{Math.Max(leftStart, 0) + (leftCount > 0 ? 1 : 0)},{leftCount} +{Math.Max(rightStart, 0) + (rightCount > 0 ? 1 : 0)},{rightCount} @@").AppendLine();
            sb.Append(body);
            i = end;
        }
        return sb.ToString();
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
