using System.IO.Compression;
using AwesomeAssertions;
using AmazTool;
using Xunit;

namespace ServiceLib.Tests.Services;

public class AmazToolUpgradeTests
{
    [Fact]
    public void ArchiveLayout_WithSingleWrapperDirectory_ShouldStripWrapperOnly()
    {
        using var stream = CreateArchive(
            "v2rayN-windows-64-cmcc/v2rayN.exe",
            "v2rayN-windows-64-cmcc/bin/xray/xray.exe");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var root = UpgradeApp.GetArchiveRoot(archive);

        root.Should().Be("v2rayN-windows-64-cmcc");
        UpgradeApp.GetRelativeEntryName(archive.Entries[0].FullName, root).Should().Be("v2rayN.exe");
        UpgradeApp.GetRelativeEntryName(archive.Entries[1].FullName, root).Should().Be("bin/xray/xray.exe");
    }

    [Fact]
    public void ArchiveLayout_WithFilesAtZipRoot_ShouldPreservePaths()
    {
        using var stream = CreateArchive("v2rayN.exe", "bin/xray/xray.exe");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var root = UpgradeApp.GetArchiveRoot(archive);

        root.Should().BeNull();
        UpgradeApp.GetRelativeEntryName(archive.Entries[0].FullName, root).Should().Be("v2rayN.exe");
        UpgradeApp.GetRelativeEntryName(archive.Entries[1].FullName, root).Should().Be("bin/xray/xray.exe");
    }

    [Fact]
    public void ArchiveLayout_WithTraversalPath_ShouldRejectOutput()
    {
        UpgradeApp.GetSafeOutputPath("../outside.exe").Should().BeNull();
    }

    private static MemoryStream CreateArchive(params string[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var entryName in entries)
            {
                using var entryStream = archive.CreateEntry(entryName).Open();
                entryStream.WriteByte(1);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
