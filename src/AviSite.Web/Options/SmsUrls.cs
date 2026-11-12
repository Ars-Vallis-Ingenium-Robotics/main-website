using Microsoft.Extensions.Options;

namespace AviSite.Web.Options;

/// <summary>Builds links that work whether the SMS pages are viewed at /sms/... or on sms.avirobotics.org.</summary>
public sealed class SmsUrls(IHttpContextAccessor http, IOptions<SmsSiteOptions> options)
{
    public bool OnSmsHost => http.HttpContext.OnSmsHost();

    /// <summary>Link to an SMS page, e.g. Sms("/rosie").</summary>
    public string Sms(string path = "/") =>
        OnSmsHost ? (path == "" ? "/" : path) : SmsHostRouting.Prefix + (path == "/" ? "" : path);

    /// <summary>Link to an AVI page from SMS pages.</summary>
    public string Avi(string path = "/") =>
        OnSmsHost ? options.Value.AviBaseUrl.TrimEnd('/') + path : path;
}
