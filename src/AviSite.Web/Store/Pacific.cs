namespace AviSite.Web.Store;

/// <summary>The club runs on Fresno time. Use this for every date a human sees or a deadline is checked against.</summary>
public static class Pacific
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    public static DateTimeOffset ToPacific(this DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);
}
