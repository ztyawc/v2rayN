using AwesomeAssertions;
using System.IO.Compression;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.Services;

public class CmccUpdateServiceTests
{
    [Fact]
    public async Task CheckUpdateCore_LiveRelease_ShouldDownloadVerifiedWindowsArchive()
    {
        if (Environment.GetEnvironmentVariable("V2RAYN_CMCC_UPDATE_LIVE") != "1")
        {
            return;
        }

        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        await CertPemManager.Instance.Init(config);
        string? downloadedFile = null;
        var messages = new List<string>();
        var service = new UpdateService(config, (success, message) =>
        {
            messages.Add(message);
            if (success && File.Exists(message))
            {
                downloadedFile = message;
            }
            return Task.CompletedTask;
        });

        try
        {
            await service.CheckUpdateCore(ECoreType.mihomo_cmcc, true);

            downloadedFile.Should().NotBeNullOrWhiteSpace(string.Join(Environment.NewLine, messages));
            File.Exists(downloadedFile).Should().BeTrue();
            using var archive = ZipFile.OpenRead(downloadedFile!);
            archive.Entries.Should().Contain(x => x.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (downloadedFile.IsNotEmpty())
            {
                File.Delete(downloadedFile);
            }
        }
    }
}
