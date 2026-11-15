using System.Diagnostics;
using System.Text;
using Meta.Integration;
using Meta.Operations.Domain;
using Meta.Surfaces.CSharp;

namespace Meta.Integration.Tests;

public sealed class CSharpBindingExportIntegrationTests
{
    [Theory]
    [InlineData("xml")]
    [InlineData("csharp")]
    public async Task Export_LoadsSelectedWorkspaceAndWritesSeparateModelOnlyBindings(string surface)
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source");
            var output = Path.Combine(root, "bindings");
            var state = CreateState();
            await WorkspaceSurface.CreateAsync(state, source, surface);
            var original = ReadFiles(source);

            var result = await RunCliAsync("export", "csharp-bindings", "--workspace", source, "--out", output);

            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Equal(2, Directory.GetFiles(output).Length);
            var artifact = Path.Combine(output, "BindingExportFixture.bindings.cs");
            Assert.True(File.Exists(Path.Combine(output, "BindingExportFixture.access.cs")));
            Assert.Equal(
                MetaCSharpBindingWriter.Write(state.Model).Sources[Path.GetFileName(artifact)],
                await File.ReadAllTextAsync(artifact));
            Assert.DoesNotContain("selected-instance-value", await File.ReadAllTextAsync(artifact), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(output, "workspace.meta")));
            AssertFilesEqual(original, ReadFiles(source));

            var mutation = await RunCliAsync(
                "model", "add-property", "Field", "Caption", "--required", "true",
                "--default-value", "new-caption-instance-value", "--workspace", source);
            Assert.True(mutation.ExitCode == 0, mutation.Output);
            var changedOutput = Path.Combine(root, "changed-bindings");
            var regenerated = await RunCliAsync(
                "export", "csharp-bindings", "--workspace", source, "--out", changedOutput);
            Assert.True(regenerated.ExitCode == 0, regenerated.Output);
            Assert.Contains("Caption", await File.ReadAllTextAsync(Path.Combine(changedOutput, Path.GetFileName(artifact))), StringComparison.Ordinal);
            Assert.DoesNotContain("new-caption-instance-value", await File.ReadAllTextAsync(Path.Combine(changedOutput, Path.GetFileName(artifact))), StringComparison.Ordinal);
            Assert.DoesNotContain("Caption", await File.ReadAllTextAsync(artifact), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("source/derived")]
    [InlineData("existing")]
    public async Task Export_RejectsWorkspaceContaminationAndExistingOutput(string destination)
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source");
            await WorkspaceSurface.CreateAsync(CreateState(), source, "csharp");
            var existing = Path.Combine(root, "existing");
            Directory.CreateDirectory(existing);
            var marker = Path.Combine(existing, "keep.txt");
            await File.WriteAllTextAsync(marker, "preserved");
            var original = ReadFiles(source);
            var output = Path.Combine(root, destination.Replace('/', Path.DirectorySeparatorChar));

            var result = await RunCliAsync("export", "csharp-bindings", "--workspace", source, "--out", output);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                destination == "existing" ? "must not already exist" : "outside the source workspace",
                result.Output,
                StringComparison.Ordinal);
            AssertFilesEqual(original, ReadFiles(source));
            Assert.Equal("preserved", await File.ReadAllTextAsync(marker));
            Assert.False(Directory.Exists(Path.Combine(source, "derived")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static InMemoryWorkspace CreateState()
    {
        var model = new GenericModel { Name = "BindingExportFixture" };
        var entity = new GenericEntity { Name = "Field" };
        entity.Properties.Add(new GenericProperty { Name = "Name", IsNullable = false });
        model.Entities.Add(entity);
        var instance = new GenericInstance { ModelName = model.Name };
        var record = new GenericRecord { Id = "arbitrary-field-id" };
        record.Values.Add("Name", "selected-instance-value");
        instance.GetOrCreateEntityRecords(entity.Name).Add(record);
        return new InMemoryWorkspace(model, instance);
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "meta-binding-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, byte[]> ReadFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertFilesEqual(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var file in expected)
        {
            Assert.Equal(file.Value, actual[file.Key]);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunCliAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(CliTestHost.DotNetHost)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        CliTestHost.AddAssemblyArgument(startInfo, "meta");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
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

        return (process.ExitCode, await stdout + Environment.NewLine + await stderr);
    }
}
