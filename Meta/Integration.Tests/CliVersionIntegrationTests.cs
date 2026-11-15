using System.Diagnostics;
using System.Xml.Linq;

namespace Meta.Integration.Tests;

public sealed class CliVersionIntegrationTests
{
    [Theory]
    [InlineData("41.2.3+entry")]
    [InlineData("41.2.3+\u03a9-\"quoted\";path\\suffix")]
    public async Task VersionCommandUsesTheExecutableBuildMetadata(string version)
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(Path.GetTempPath(), "meta-executable-version", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net8.0"),
                    new XElement("OutputType", "Exe"),
                    new XElement("AssemblyName", "meta-version-fixture"),
                    new XElement("InformationalVersion", version),
                    new XElement("IncludeSourceRevisionInInformationalVersion", "false"),
                    new XElement("ImplicitUsings", "enable")),
                new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "Meta.Core"),
                    new XElement("HintPath", Path.Combine(AppContext.BaseDirectory, "Meta.Core.dll")))),
                new XElement("Import", new XAttribute("Project", Path.Combine(root, "Directory.Build.targets")))));
            var projectPath = Path.Combine(fixture, "VersionFixture.csproj");
            await File.WriteAllTextAsync(projectPath, project.ToString());
            await File.WriteAllTextAsync(Path.Combine(fixture, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            await File.WriteAllTextAsync(Path.Combine(fixture, "Program.cs"), """
                using Meta.Core.Presentation;
                using Meta.Core.Presentation.Cli;
                return CliVersion.TryWriteVersion(new ConsolePresenter(), "fixture", args, out var exitCode) ? exitCode : 3;
                """);
            var output = Path.Combine(fixture, "out");
            var build = await RunAsync(fixture, "build", projectPath, "--nologo", "--configuration", "Release", "--output", output,
                "-m:1", "-nr:false", "-p:UseSharedCompilation=false");
            Assert.True(build.ExitCode == 0, build.Output);

            var result = await RunAsync(fixture, Path.Combine(output, "meta-version-fixture.dll"), "--version");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("fixture " + version, result.Output.TrimEnd('\r', '\n'));
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Metadata.Framework.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        return (process.ExitCode, await stdout + await stderr);
    }
}
