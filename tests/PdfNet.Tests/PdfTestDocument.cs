using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfNet.Tests;

internal static partial class PdfTestDocument
{
    public static byte[] Create(int pageCount, double firstPageWidth = 100, double pageHeight = 792, string? subject = null)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Concat(Enumerable.Range(0, pageCount).Select(index => $" {index + 3} 0 R"))} ] /Count {pageCount} >>",
        };

        for (var index = 0; index < pageCount; index++)
        {
            var width = (firstPageWidth + index).ToString("0.###", CultureInfo.InvariantCulture);
            var height = pageHeight.ToString("0.###", CultureInfo.InvariantCulture);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << >> >>");
        }
        var infoObjectNumber = subject is null ? 0 : objects.Count + 1;
        if (subject is not null)
        {
            var escapedSubject = subject.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);
            objects.Add($"<< /Subject ({escapedSubject}) >>");
        }

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>(objects.Count);
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = builder.Length;
        builder.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:D10} 00000 n \n");
        }
        var infoEntry = infoObjectNumber == 0 ? string.Empty : $" /Info {infoObjectNumber} 0 R";
        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{infoEntry} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    public static int GetPageCount(byte[] pdf) =>
        PageTypeRegex().Matches(Encoding.Latin1.GetString(pdf)).Count;

    public static double[] GetPageWidths(byte[] pdf) =>
        MediaBoxRegex().Matches(Encoding.Latin1.GetString(pdf))
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

    public static double[] GetPageHeights(byte[] pdf) =>
        MediaBoxRegex().Matches(Encoding.Latin1.GetString(pdf))
            .Select(match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToArray();

    public static bool ContainsText(byte[] pdf, string text)
    {
        var contents = Encoding.Latin1.GetString(pdf);
        var hexString = Convert.ToHexString(Encoding.UTF8.GetBytes(text));
        return contents.Contains($"({text})", StringComparison.Ordinal) ||
            contents.Contains($"<{hexString}>", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"/Type\s*/Page\b")]
    private static partial Regex PageTypeRegex();

    [GeneratedRegex(@"/MediaBox\s*\[\s*[-+]?\d*\.?\d+\s+[-+]?\d*\.?\d+\s+([-+]?\d*\.?\d+)\s+([-+]?\d*\.?\d+)\s*\]")]
    private static partial Regex MediaBoxRegex();
}
