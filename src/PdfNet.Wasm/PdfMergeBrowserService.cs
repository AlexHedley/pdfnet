using PdfNet;

namespace PdfNet.Wasm;

public static class PdfMergeBrowserService
{
    public const int MaxFileCount = 100;
    public const long MaxFileSize = 100 * 1024 * 1024;
    public const long MaxTotalSize = 200 * 1024 * 1024;

    public static byte[] Merge(IEnumerable<Stream> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var streams = inputs.ToArray();
        if (streams.Length == 0)
        {
            throw new ArgumentException("At least one PDF file is required.", nameof(inputs));
        }
        if (streams.Length > MaxFileCount)
        {
            throw new ArgumentException($"No more than {MaxFileCount} PDF files can be merged.", nameof(inputs));
        }

        long totalSize = 0;
        foreach (var stream in streams)
        {
            ArgumentNullException.ThrowIfNull(stream, nameof(inputs));
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("PDF input streams must be readable and seekable.", nameof(inputs));
            }
            if (stream.Length > MaxFileSize)
            {
                throw new ArgumentException($"Each PDF must be no larger than {MaxFileSize} bytes.", nameof(inputs));
            }

            totalSize = checked(totalSize + stream.Length);
            if (totalSize > MaxTotalSize)
            {
                throw new ArgumentException($"The total PDF size must not exceed {MaxTotalSize} bytes.", nameof(inputs));
            }
        }

        using var output = new MemoryStream();
        PdfMerger.Merge(streams, output, new PdfMergeOptions { Title = "Merged PDF" });
        return output.ToArray();
    }
}
