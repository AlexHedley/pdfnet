#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property Version=0.1.0
#:project PdfNet/PdfNet.csproj

using PdfNet;
using System.Reflection;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]);
    return 0;
}

if (args.Length > 2 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
{
    Console.WriteLine("Usage: dotnet run --file src/PdfNet.FolderMerge.cs -- [directory] [output.pdf]");
    Console.WriteLine("Use --version to show the app version.");
    return args.Length > 2 ? 2 : 0;
}

var inputDirectory = args.Length == 0 ? Directory.GetCurrentDirectory() : args[0];
var outputPath = args.Length == 2 ? args[1] : null;

try
{
    var fileCount = PdfFolderMerger.MergeFolder(inputDirectory, outputPath);
    var destination = outputPath ?? Path.Combine(Path.GetFullPath(inputDirectory), "merged.pdf");
    Console.WriteLine($"Merged {fileCount} PDF(s) to {Path.GetFullPath(destination)}.");
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PdfMergeException)
{
    Console.Error.WriteLine($"Merge failed: {exception.Message}");
    return 1;
}
