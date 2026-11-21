namespace PdfNet.Tui;

internal static class Program
{
    private static int Main(string[] args) =>
        PdfMergeCommand.Run(args, Console.In, Console.Out, Console.Error, useColor: !Console.IsOutputRedirected);
}
