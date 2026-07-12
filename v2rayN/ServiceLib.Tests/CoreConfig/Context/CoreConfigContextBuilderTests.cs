using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Handler.Builder;
using ServiceLib.Helper;
using ServiceLib.Models;
using Xunit;

namespace ServiceLib.Tests.CoreConfig.Context;

public class CoreConfigContextBuilderTests
{
    [Fact]
    public async Task BuildAll_CmccFirstInProxyChain_ShouldCreateLocalFrontProxy()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var cmcc = CoreConfigTestFactory.CreateCmccSocksNode(NewId("cmcc"), "cmcc-front");
        var exit = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, NewId("exit"), "exit-node");
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, NewId("chain"), "chain",
            [cmcc.IndexId, exit.IndexId]);
        await UpsertProfilesAsync(cmcc, exit, chain);

        var result = await CoreConfigContextBuilder.BuildAll(config, chain);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.CombinedValidatorResult.Errors));
        result.FrontProxyResult.Should().NotBeNull();
        result.FrontProxyResult!.Context.RunCoreType.Should().Be(ECoreType.mihomo_cmcc);
        result.FrontProxyResult.Context.Node.ConfigType.Should().Be(EConfigType.CmccSocks);
        result.FrontProxyResult.Context.InboundPortOverride.Should().BeInRange(1, 65535);

        var localSocks = result.MainResult.Context.AllProxiesMap[cmcc.IndexId];
        localSocks.ConfigType.Should().Be(EConfigType.SOCKS);
        localSocks.Address.Should().Be(Global.Loopback);
        localSocks.Port.Should().Be(result.FrontProxyResult.Context.InboundPortOverride);

        var mainConfigResult = new CoreConfigV2rayService(result.MainResult.Context).GenerateClientConfigContent();
        mainConfigResult.Success.Should().BeTrue(mainConfigResult.Msg);
        var mainConfig = JsonUtils.Deserialize<V2rayConfig>(mainConfigResult.Data!.ToString())!;
        var localOutbound = mainConfig.outbounds.Single(x =>
            x.tag.StartsWith("chain-proxy-1-", StringComparison.Ordinal));
        localOutbound.settings.servers!.Single().address.Should().Be(Global.Loopback);
        localOutbound.settings.servers!.Single().port.Should().Be(localSocks.Port);
        mainConfig.outbounds.Single(x => x.tag == Global.ProxyTag)
            .streamSettings.sockopt!.dialerProxy.Should().Be(localOutbound.tag);

        var frontConfigFile = Path.Combine(Path.GetTempPath(), $"cmcc-front-{Guid.NewGuid():N}.yaml");
        try
        {
            var frontConfigResult = await CoreConfigHandler.GenerateClientConfig(
                result.FrontProxyResult.Context, frontConfigFile);
            frontConfigResult.Success.Should().BeTrue(frontConfigResult.Msg);
            var yaml = await File.ReadAllTextAsync(frontConfigFile, TestContext.Current.CancellationToken);
            yaml.Should().Contain($"mixed-port: {localSocks.Port}");
            yaml.Should().Contain("cmcc-auth-method: 0x80");
            yaml.Should().Contain("udp: false");
            yaml.Should().NotContain("external-controller:");
        }
        finally
        {
            File.Delete(frontConfigFile);
        }
    }

    [Fact]
    public async Task BuildAll_CmccFirstInSingboxProxyChain_ShouldCreateDetourViaLocalFrontProxy()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var cmcc = CoreConfigTestFactory.CreateCmccSocksNode(NewId("cmcc"), "cmcc-front");
        var exit = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box, NewId("exit"), "exit-node");
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.sing_box, NewId("chain"), "chain",
            [cmcc.IndexId, exit.IndexId]);
        await UpsertProfilesAsync(cmcc, exit, chain);

        var result = await CoreConfigContextBuilder.BuildAll(config, chain);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.CombinedValidatorResult.Errors));
        result.FrontProxyResult.Should().NotBeNull();
        var localSocks = result.MainResult.Context.AllProxiesMap[cmcc.IndexId];
        localSocks.ConfigType.Should().Be(EConfigType.SOCKS);
        localSocks.Address.Should().Be(Global.Loopback);

        var mainConfigResult = new CoreConfigSingboxService(result.MainResult.Context).GenerateClientConfigContent();
        mainConfigResult.Success.Should().BeTrue(mainConfigResult.Msg);
        var mainConfig = JsonUtils.Deserialize<SingboxConfig>(mainConfigResult.Data!.ToString())!;
        var localOutbound = mainConfig.outbounds.Single(x =>
            x.tag.StartsWith("chain-proxy-1-", StringComparison.Ordinal));
        localOutbound.type.Should().Be("socks");
        localOutbound.server.Should().Be(Global.Loopback);
        localOutbound.server_port.Should().Be(localSocks.Port);
        mainConfig.outbounds.Single(x => x.tag == Global.ProxyTag).detour.Should().Be(localOutbound.tag);
    }

    [Fact]
    public async Task BuildAll_CmccNotFirstInProxyChain_ShouldFailValidation()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var exit = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, NewId("exit"), "exit-node");
        var cmcc = CoreConfigTestFactory.CreateCmccSocksNode(NewId("cmcc"), "cmcc-not-front");
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, NewId("chain"), "chain",
            [exit.IndexId, cmcc.IndexId]);
        await UpsertProfilesAsync(exit, cmcc, chain);

        var result = await CoreConfigContextBuilder.BuildAll(config, chain);

        result.Success.Should().BeFalse();
        result.CombinedValidatorResult.Errors.Should().Contain(x =>
            x.Contains("CMCC SOCKS must be the first node", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveNodeAsync_DirectCycleDependency_ShouldFailWithCycleError()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var groupAId = NewId("group-a");
        var groupBId = NewId("group-b");
        var groupA = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupAId, "group-a", [groupBId]);
        var groupB = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupBId, "group-b", [groupAId]);

        await UpsertProfilesAsync(groupA, groupB);

        var context = CoreConfigTestFactory.CreateContext(config, groupA, ECoreType.Xray);
        context.AllProxiesMap.Clear();

        var (_, validatorResult) = await CoreConfigContextBuilder.ResolveNodeAsync(context, groupA, false);

        validatorResult.Success.Should().BeFalse();
        validatorResult.Errors.Should().Contain(msg => ContainsCycleDependencyMessage(msg));
        context.AllProxiesMap.Should().NotContainKey(groupA.IndexId);
        context.AllProxiesMap.Should().NotContainKey(groupB.IndexId);
    }

    [Fact]
    public async Task ResolveNodeAsync_IndirectCycleDependency_ShouldFailWithCycleError()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var groupAId = NewId("group-a");
        var groupBId = NewId("group-b");
        var groupCId = NewId("group-c");
        var groupA = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupAId, "group-a", [groupBId]);
        var groupB = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupBId, "group-b", [groupCId]);
        var groupC = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupCId, "group-c", [groupAId]);

        await UpsertProfilesAsync(groupA, groupB, groupC);

        var context = CoreConfigTestFactory.CreateContext(config, groupA, ECoreType.Xray);
        context.AllProxiesMap.Clear();

        var (_, validatorResult) = await CoreConfigContextBuilder.ResolveNodeAsync(context, groupA, false);

        validatorResult.Success.Should().BeFalse();
        validatorResult.Errors.Should().Contain(msg => ContainsCycleDependencyMessage(msg));
        context.AllProxiesMap.Should().NotContainKey(groupA.IndexId);
        context.AllProxiesMap.Should().NotContainKey(groupB.IndexId);
        context.AllProxiesMap.Should().NotContainKey(groupC.IndexId);
    }

    [Fact]
    public async Task ResolveNodeAsync_CycleWithValidBranch_ShouldSkipCycleAndKeepValidChild()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var groupAId = NewId("group-a");
        var groupBId = NewId("group-b");
        var leafId = NewId("leaf");
        var groupA = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupAId, "group-a", [groupBId, leafId]);
        var groupB = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, groupBId, "group-b", [groupAId]);
        var leaf = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, leafId, "leaf");

        await UpsertProfilesAsync(groupA, groupB, leaf);

        var context = CoreConfigTestFactory.CreateContext(config, groupA, ECoreType.Xray);
        context.AllProxiesMap.Clear();

        var (_, validatorResult) = await CoreConfigContextBuilder.ResolveNodeAsync(context, groupA, false);

        validatorResult.Success.Should().BeTrue();
        validatorResult.Errors.Should().BeEmpty();
        validatorResult.Warnings.Should().Contain(msg => ContainsCycleDependencyMessage(msg));

        context.AllProxiesMap.Should().ContainKey(leaf.IndexId);
        context.AllProxiesMap.Should().ContainKey(groupA.IndexId);
        context.AllProxiesMap.Should().NotContainKey(groupB.IndexId);
        groupA.GetProtocolExtra().ChildItems.Should().Be(leaf.IndexId);
    }

    private static string NewId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static bool ContainsCycleDependencyMessage(string message)
    {
        return message.Contains("cycle dependency", StringComparison.OrdinalIgnoreCase)
               || message.Contains("循环依赖", StringComparison.Ordinal)
               || message.Contains("循環依賴", StringComparison.Ordinal)
               || message.Contains("циклическую зависимость", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task UpsertProfilesAsync(params ProfileItem[] profiles)
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        SQLiteHelper.Instance.CreateTable<FullConfigTemplateItem>();
        SQLiteHelper.Instance.CreateTable<DNSItem>();
        SQLiteHelper.Instance.CreateTable<RoutingItem>();
        await SQLiteHelper.Instance.ReplaceAsync(new RoutingItem
        {
            Id = "core-config-context-builder-default",
            Remarks = "test-default",
            RuleSet = "[]",
            DomainStrategy = Global.AsIs,
            DomainStrategy4Singbox = string.Empty,
            IsActive = true,
        });
        foreach (var profile in profiles)
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }
    }
}
