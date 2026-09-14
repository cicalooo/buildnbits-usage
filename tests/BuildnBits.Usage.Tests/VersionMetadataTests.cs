using BuildnBits.Usage.Core;

namespace BuildnBits.Usage.Tests;

public sealed class VersionMetadataTests
{
    [Fact]
    public void Runtime_client_version_matches_release_version()
    {
        Assert.Equal("1.3.0", AppVersion.Current);
    }

    [Fact]
    public void Runtime_and_packaging_metadata_match_1_3_0()
    {
        var root = FindRoot();
        Assert.Contains("<Version>1.3.0</Version>", File.ReadAllText(Path.Combine(root, "Directory.Build.props")), StringComparison.Ordinal);
        Assert.Contains("<assemblyIdentity version=\"1.3.0.0\"", File.ReadAllText(
            Path.Combine(root, "src", "BuildnBits.Usage.Tray", "app.manifest")), StringComparison.Ordinal);
        Assert.Contains("Version=\"1.3.0.0\"", File.ReadAllText(
            Path.Combine(root, "src", "BuildnBits.Usage.Package", "Package.appxmanifest")), StringComparison.Ordinal);
        Assert.Contains("# BuildnBits.Usage 1.3.0", File.ReadAllText(
            Path.Combine(root, "docs", "release-notes-v1.3.0.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Release_files_do_not_retain_the_previous_version()
    {
        var root = FindRoot();
        var paths = new[]
        {
            Path.Combine(root, "src", "BuildnBits.Usage.Tray", "app.manifest"),
            Path.Combine(root, "src", "BuildnBits.Usage.Package", "Package.appxmanifest"),
            Path.Combine(root, "src", "BuildnBits.Usage.Core", "Providers", "Codex", "CodexUsageClient.cs"),
            Path.Combine(root, "src", "BuildnBits.Usage.Core", "Providers", "Grok", "GrokUsageClient.cs"),
            Path.Combine(root, "publish-portable.ps1")
        };

        foreach (var path in paths)
        {
            Assert.DoesNotContain("1.2.1", File.ReadAllText(path), StringComparison.Ordinal);
        }

        var publisher = File.ReadAllText(Path.Combine(root, "publish-portable.ps1"));
        Assert.Contains("$LASTEXITCODE", publisher, StringComparison.Ordinal);
        Assert.Contains("dotnet publish failed", publisher, StringComparison.OrdinalIgnoreCase);
        var releaseNotesCopy = publisher.IndexOf(
            "Copy-Item (Join-Path $root \"docs\\release-notes-v$version.md\")",
            StringComparison.Ordinal);
        var archive = publisher.IndexOf("Compress-Archive", StringComparison.Ordinal);
        Assert.True(releaseNotesCopy >= 0 && releaseNotesCopy < archive);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BuildnBits.Usage.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("BuildnBits.Usage repository root was not found.");
    }
}
