namespace ServiceLib.Handler.Fmt;

public class CmccSocksFmt : BaseFmt
{
    public const string DefaultAuthMethod = "0x80";

    public static ProfileItem? Resolve(string str, out string msg)
    {
        msg = ResUI.ConfigurationFormatIncorrect;

        var url = Utils.TryUri(str);
        if (url == null || url.IdnHost.IsNullOrEmpty() || url.Port <= 0)
        {
            return null;
        }

        var query = Utils.ParseQueryString(url.Query);
        var item = new ProfileItem
        {
            ConfigType = EConfigType.CmccSocks,
            CoreType = ECoreType.mihomo_cmcc,
            Address = url.IdnHost,
            Port = url.Port,
            Remarks = url.GetComponents(UriComponents.Fragment, UriFormat.Unescaped),
        };

        var rawUserInfo = Utils.UrlDecode(url.UserInfo);
        if (rawUserInfo.Contains(':'))
        {
            var split = rawUserInfo.Split(':', 2);
            item.Username = split[0];
            item.Password = split[1];
        }
        else
        {
            item.Username = GetQueryDecoded(query, "usr");
            item.Password = GetQueryDecoded(query, "passwd");
        }

        var method = GetQueryValue(query, "method");
        if (method.IsNullOrEmpty())
        {
            method = GetQueryValue(query, "protocol");
        }
        if (method.IsNullOrEmpty())
        {
            method = GetQueryValue(query, "cmcc-auth-method", DefaultAuthMethod);
        }
        method = NormalizeAuthMethod(method);
        if (method.IsNullOrEmpty())
        {
            return null;
        }

        item.SetProtocolExtra(item.GetProtocolExtra() with { CmccAuthMethod = method });
        return item;
    }

    public static string? ToUri(ProfileItem? item)
    {
        if (item == null)
        {
            return null;
        }

        var method = NormalizeAuthMethod(item.GetProtocolExtra().CmccAuthMethod);
        if (method.IsNullOrEmpty())
        {
            return null;
        }

        var userInfo = $"{item.Username}:{item.Password}";
        var query = new Dictionary<string, string> { ["method"] = method };
        var remark = item.Remarks.IsNotEmpty() ? "#" + Utils.UrlEncode(item.Remarks) : string.Empty;
        return ToUri(EConfigType.CmccSocks, item.Address, item.Port, userInfo, query, remark);
    }

    public static string NormalizeAuthMethod(string? value)
    {
        return value?.TrimEx().ToLowerInvariant() switch
        {
            "0x80" or "80" or "128" => "0x80",
            "0x82" or "82" or "130" => "0x82",
            _ => string.Empty,
        };
    }
}
