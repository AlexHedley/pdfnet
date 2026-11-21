#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

string[] buildOptions = ["--docs", "--coverage", "--dependency-check", "--app", "--site"];

if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
{
    Console.WriteLine("Usage: dotnet run scripts/GenerateContent.cs -- [--docs] [--coverage] [--dependency-check] [--app] [--site] [--all] [--index-only] [--no-browser] [--no-serve]");
    Console.WriteLine("Combine build flags to select content. No build flags (or --all) builds everything.");
    Console.WriteLine("--docs builds HTML and PDF. The index includes available output, including previous builds.");
    Console.WriteLine("Serve localhost:8080 and open a browser by default. --no-serve only generates output.");
    Console.WriteLine("Only --dependency-check requires Java and DEPENDENCY_CHECK_HOME (CLI 13+).");
    Console.WriteLine("--index-only refreshes navigation for existing output without building content.");
    return 0;
}
if (args.Any(argument => !buildOptions.Contains(argument, StringComparer.Ordinal)
    && argument is not ("--all" or "--index-only" or "--no-browser" or "--no-serve")))
{
    Console.Error.WriteLine("Unknown argument. Use --help for usage.");
    return 1;
}
if (args.Contains("--index-only", StringComparer.Ordinal) &&
    args.Any(argument => argument == "--all" || buildOptions.Contains(argument, StringComparer.Ordinal)))
{
    Console.Error.WriteLine("--index-only cannot be combined with build selections.");
    return 1;
}

var root = SourceDirectory();
var output = Path.Combine(root, "artifacts", "documentation");
var buildAll = args.Contains("--all", StringComparer.Ordinal) ||
    !args.Any(argument => buildOptions.Contains(argument, StringComparer.Ordinal));
bool Build(string option) => !args.Contains("--index-only", StringComparer.Ordinal) &&
    (buildAll || args.Contains(option, StringComparer.Ordinal));

try
{
    Directory.CreateDirectory(output);
    if (!args.Contains("--index-only", StringComparer.Ordinal) ||
        !args.Contains("--no-serve", StringComparer.Ordinal))
    {
        await Run("dotnet", "tool", "restore");
    }
    if (Build("--docs"))
    {
        await Run("dotnet", "docfx", "docs/docfx.json", "--warningsAsErrors");
        await Run("dotnet", "docfx", "pdf", "docs/docfx.json", "--warningsAsErrors");
    }

    var runId = Guid.NewGuid().ToString("N");
    if (Build("--coverage"))
    {
        var results = Path.Combine(root, "artifacts", "test-results", runId);
        await Run("dotnet", "test", "tests/PdfNet.Tests/PdfNet.Tests.csproj",
            "--configuration", "Release", "--collect:XPlat Code Coverage",
            "--results-directory", results);
        await Run("dotnet", "reportgenerator",
            $"-reports:{results}/**/coverage.opencover.xml",
            $"-targetdir:{output}/coverage",
            "-reporttypes:Html;MarkdownSummaryGithub;Cobertura",
            "-assemblyfilters:+PdfNet;+PdfNet.Tui;+PdfNet.Wasm");
    }

    if (Build("--dependency-check"))
    {
        var scannerHome = Environment.GetEnvironmentVariable("DEPENDENCY_CHECK_HOME");
        if (string.IsNullOrWhiteSpace(scannerHome))
        {
            throw new InvalidOperationException(
                "Set DEPENDENCY_CHECK_HOME to the extracted Dependency-Check CLI directory (containing bin and lib).");
        }
        scannerHome = Path.GetFullPath(scannerHome);
        var scannerLibraries = Path.Combine(scannerHome, "lib");
        if (!Directory.Exists(scannerLibraries))
        {
            throw new DirectoryNotFoundException($"Dependency-Check CLI lib directory not found: {scannerLibraries}");
        }
        var scannerJars = Directory.GetFiles(scannerLibraries, "dependency-check-cli-*.jar");
        if (scannerJars.Length != 1)
        {
            throw new InvalidOperationException("Expected exactly one dependency-check-cli-*.jar in the CLI lib directory.");
        }
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        var java = string.IsNullOrWhiteSpace(javaHome)
            ? "java"
            : Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        await Run(java, "-version");

        var packages = Path.Combine(root, "artifacts", "dependency-packages", runId);
        await Run("dotnet", "restore", "src/PdfNet.slnx", "-p:EnableWindowsTargeting=true",
            "--packages", packages);
        var dependencyReport = Path.Combine(output, "dependency-check");
        Directory.CreateDirectory(dependencyReport);
        var scannerArguments = new List<string>
    {
        // Match the official CLI launcher without shell-specific argument handling.
        "--enable-native-access=ALL-UNNAMED", "-XX:+IgnoreUnrecognizedVMOptions",
        "-Dapp.name=dependency-check", $"-Dapp.repo={scannerLibraries}",
        $"-Dapp.home={scannerHome}", $"-Dbasedir={scannerHome}",
        $"-javaagent:{scannerJars[0]}={Path.Combine(scannerHome, "plugins")}",
        "-jar", scannerJars[0],
        "--project", "PdfNet", "--scan", packages, "--format", "HTML", "--out", dependencyReport,
        "--data", Path.Combine(root, "artifacts", "dependency-check-data"),
        "--disableAssembly", "--disableArchive",
    };
        var properties = Environment.GetEnvironmentVariable("DEPENDENCY_CHECK_PROPERTIES");
        if (!string.IsNullOrWhiteSpace(properties))
        {
            properties = Path.GetFullPath(properties);
            if (!File.Exists(properties))
            {
                throw new FileNotFoundException("Dependency-Check properties file not found.", properties);
            }
            scannerArguments.AddRange(["--propertyfile", properties]);
        }
        await Run(java, scannerArguments.ToArray());
        File.Copy(Path.Combine(dependencyReport, "dependency-check-report.html"),
            Path.Combine(dependencyReport, "index.html"), overwrite: true);
    }

    if (Build("--app"))
    {
        await Run("dotnet", "publish", "src/PdfNet.Wasm/PdfNet.Wasm.csproj",
            "--configuration", "Release", "--output", Path.Combine(root, "artifacts", "content-app"));
        CopyDirectory(Path.Combine(root, "artifacts", "content-app", "wwwroot"), Path.Combine(output, "app"));
        var appIndex = Path.Combine(output, "app", "index.html");
        var appHtml = File.ReadAllText(appIndex);
        if (!appHtml.Contains("<base href=\"/\" />", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot configure the published browser app's local base path.");
        }
        File.WriteAllText(appIndex, appHtml.Replace("<base href=\"/\" />", "<base href=\"/app/\" />",
            StringComparison.Ordinal));
    }
    if (Build("--site"))
    {
        CopyDirectory(Path.Combine(root, "site"), Path.Combine(output, "site"));
        var siteIndex = Path.Combine(output, "site", "index.html");
        File.WriteAllText(siteIndex, File.ReadAllText(siteIndex)
            .Replace("https://alexhedley.github.io/pdfnet/docs/", "../docs/", StringComparison.Ordinal)
            .Replace("https://alexhedley.github.io/pdfnet/site/", "../site/", StringComparison.Ordinal)
            .Replace("https://alexhedley.github.io/pdfnet/", "../app/", StringComparison.Ordinal));
    }
    var brandOutput = Path.Combine(output, "assets", "brand");
    Directory.CreateDirectory(brandOutput);
    foreach (var asset in new[] { "pdfnet-logo.svg", "pdfnet-docs-icon.svg" })
    {
        File.Copy(Path.Combine(root, "assets", "brand", asset),
            Path.Combine(brandOutput, asset), overwrite: true);
    }
    var indexHtml = File.ReadAllText(Path.Combine(root, "docs", "generated-index.html"))
        .Replace("../assets/brand/", "assets/brand/", StringComparison.Ordinal);
    indexHtml = Regex.Replace(indexHtml, @"<a href=""([^""]+)"">.*?</a>", match =>
    {
        var target = match.Groups[1].Value;
        var path = Path.Combine(output, target.EndsWith('/') ? target + "index.html" : target);
        return File.Exists(path) ? match.Value : "";
    });
    File.WriteAllText(Path.Combine(output, "index.html"), indexHtml);
    File.WriteAllText(Path.Combine(output, "generated-index.html"), indexHtml);

    Console.WriteLine($"Generated index: {Path.Combine(output, "index.html")}");
    Console.WriteLine("Coverage contains source code. Keep this local; do not deploy this combined output.");
    if (args.Contains("--no-serve", StringComparer.Ordinal))
    {
        return 0;
    }
    Console.WriteLine("Generated content: http://localhost:8080 (Ctrl+C to stop)");
    var serveArguments = new List<string>
    {
        "docfx", "serve", output, "--hostname", "localhost", "--port", "8080",
    };
    if (!args.Contains("--no-browser", StringComparer.Ordinal))
    {
        serveArguments.Add("--open-browser");
    }
    await Run("dotnet", serveArguments.ToArray());
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
    or InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine($"Content generation failed: {exception.Message}");
    return 1;
}

async Task Run(string command, params string[] arguments)
{
    var start = new ProcessStartInfo(command)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }
    Console.WriteLine($"Running {command} {string.Join(" ", arguments)}");
    using var process = Process.Start(start)
        ?? throw new InvalidOperationException($"Could not start {command}.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{command} exited with code {process.ExitCode}.");
    }
}

static string SourceDirectory([CallerFilePath] string source = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, ".."));

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}
