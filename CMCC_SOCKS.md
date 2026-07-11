# CMCC SOCKS support

This fork adds the private SOCKS5 variant described in the accompanying protocol analysis to v2rayN 7.23.3.

## Supported scope

- Windows 10 x64, WPF build.
- Authentication methods `0x80` and `0x82`.
- TCP proxying through the maintained [`ztyawc/mihomo`](https://github.com/ztyawc/mihomo) fork.
- System proxy, Mihomo mixed inbound, TUN startup, sharing/import, and per-node delay/speed tests.
- UDP remains disabled in v2rayN. The Mihomo fork contains experimental UDP support, but it is not part of this release's compatibility promise.

Use **Servers → CMCC SOCKS**, then enter the server, port, username, password, and authentication method. The core selector is intentionally locked: these nodes can only use `mihomo_cmcc`.

Canonical share links use:

```text
cmcc://username:password@server:port?method=0x80#remark
```

Provider-style links using `usr`, `passwd`, and `protocol` query parameters are also accepted. A provider link without a method defaults to `0x80` and should be checked before connecting.

## Core delivery and updates

The Windows release workflow builds only the x64 WPF package. It overlays the official v2rayN core bundle with the latest `mihomo-windows-amd64-v1-cmcc.zip`, verifies GitHub's SHA-256 asset digest, checks the `-cmcc.<commit>` version marker, and stores the core under `bin/mihomo_cmcc`.

The application updater treats the CMCC core as a separate core type. It compares the 12-character source commit embedded in the local binary with the `cmcc-alpha-<commit>` release tag, downloads the matching Windows asset, verifies SHA-256, then installs it without replacing official Mihomo.

Never commit real access credentials. Live tests read them only from environment variables:

```powershell
$env:V2RAYN_CMCC_TEST_CORE = "C:\path\to\mihomo.exe"
$env:MIHOMO_CMCC_TEST_ADDR = "server:port"
$env:MIHOMO_CMCC_TEST_USERNAME = "username"
$env:MIHOMO_CMCC_TEST_PASSWORD = "password"
$env:MIHOMO_CMCC_TEST_METHOD = "0x80"
dotnet test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj --filter "GenerateClientCmccConfig_LiveCore_ShouldProxyHttp"
```

## Long-term upstream maintenance

Keep the v2rayN fork's `main` branch releasable and retain `upstream` as `https://github.com/2dust/v2rayN.git`. The weekly sync workflow merges `upstream/master` on a temporary branch, runs ServiceLib tests and the Windows WPF build, and opens a pull request only after both pass. Merge that PR after reviewing conflicts around enums, the add-server window, core management, and update handling.

The Mihomo fork independently follows `MetaCubeX/mihomo:Alpha` using its existing tested sync workflow. Do not copy the CMCC implementation into every new Mihomo snapshot; keep its protocol commits on top of upstream history.

For a manual v2rayN sync:

```bash
git fetch upstream master --tags
git switch main
git merge --no-edit upstream/master
dotnet test ./v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Release
dotnet build ./v2rayN/v2rayN/v2rayN.csproj -c Release -p:EnableWindowsTargeting=true
```

## Implementation alternatives

| Approach | Advantages | Disadvantages | Decision |
| --- | --- | --- | --- |
| Native v2rayN node + small Mihomo fork | Best user experience; reuses Mihomo routing/TUN; protocol changes stay isolated; both upstreams remain mergeable | Requires maintaining two small patch sets and a custom-core updater | **Selected** |
| v2rayN built-in .NET adapter | No proxy-core fork; one application repository | Must implement and maintain a SOCKS server, relaying, lifecycle, tests, and performance behavior inside v2rayN | Not selected |
| Independent sidecar adapter | Strong isolation and can serve other clients | Adds another binary, release pipeline, process lifecycle, ports, and cross-platform packaging | Not selected |
| Custom Mihomo YAML only | Smallest v2rayN code change | Manual configuration, no native node/share workflow, easy to install the wrong core | Useful only as a fallback |
| Large permanent Mihomo fork | Direct native protocol integration | High merge burden if history is flattened or unrelated features accumulate | Explicitly avoided |
