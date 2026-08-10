using AwesomeAssertions;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig.Clash;

public class CoreConfigCmccServiceTests
{
    [Theory]
    [InlineData("0x80")]
    [InlineData("0x82")]
    public async Task GenerateClientCmccConfig_ShouldCreateTcpOnlyMihomoConfig(string method)
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = new ProfileItem
        {
            ConfigType = EConfigType.CmccSocks,
            CoreType = ECoreType.mihomo_cmcc,
            Remarks = "CMCC test",
            Address = "192.0.2.10",
            Port = 10800,
            Username = "1234567890123456789",
            Password = "secret",
        };
        node.SetProtocolExtra(new ProtocolExtraItem { CmccAuthMethod = method });
        var fileName = Path.Combine(Path.GetTempPath(), $"cmcc-{Guid.NewGuid():N}.yaml");

        try
        {
            var result = await new CoreConfigClashService(config, false)
                .GenerateClientCmccConfig(node, fileName, 2080);

            result.Success.Should().BeTrue(result.Msg);
            var content = await File.ReadAllTextAsync(fileName, TestContext.Current.CancellationToken);
            content.Should().Contain("cmcc-auth-method: " + method);
            content.Should().Contain("type: socks5");
            content.Should().Contain("udp: false");
            content.Should().Contain("mixed-port: 2080");
            content.Should().Contain("MATCH,PROXY");
        }
        finally
        {
            File.Delete(fileName);
        }
    }

    [Fact]
    public async Task GenerateClientCmccConfig_LiveCore_ShouldProxyHttp()
    {
        var core = Environment.GetEnvironmentVariable("V2RAYN_CMCC_TEST_CORE");
        var server = Environment.GetEnvironmentVariable("MIHOMO_CMCC_TEST_ADDR");
        var username = Environment.GetEnvironmentVariable("MIHOMO_CMCC_TEST_USERNAME");
        var password = Environment.GetEnvironmentVariable("MIHOMO_CMCC_TEST_PASSWORD");
        var method = Environment.GetEnvironmentVariable("MIHOMO_CMCC_TEST_METHOD");
        if (new[] { core, server, username, password, method }.Any(x => x.IsNullOrEmpty()))
        {
            return;
        }

        var endpoint = server!.Split(':', 2);
        endpoint.Should().HaveCount(2);
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = new ProfileItem
        {
            ConfigType = EConfigType.CmccSocks,
            CoreType = ECoreType.mihomo_cmcc,
            Remarks = "CMCC live test",
            Address = endpoint[0],
            Port = endpoint[1].ToInt(),
            Username = username!,
            Password = password!,
        };
        node.SetProtocolExtra(new ProtocolExtraItem { CmccAuthMethod = method });
        var port = GetFreePort();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"v2rayn-cmcc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var fileName = Path.Combine(tempDirectory, "config.yaml");
        Process? process = null;

        try
        {
            var result = await new CoreConfigClashService(config, false)
                .GenerateClientCmccConfig(node, fileName, port);
            result.Success.Should().BeTrue(result.Msg);
            process = Process.Start(new ProcessStartInfo
            {
                FileName = core!,
                Arguments = $"-f \"{fileName}\" -d \"{tempDirectory}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            process.Should().NotBeNull();
            await WaitForPort(port, TestContext.Current.CancellationToken);

            using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{port}") };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync("http://cp.cloudflare.com/generate_204",
                TestContext.Current.CancellationToken);
            ((int)response.StatusCode).Should().BeInRange(200, 399);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            process?.Dispose();
            Directory.Delete(tempDirectory, true);
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForPort(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, timeout.Token);
            }
        }
    }
}
