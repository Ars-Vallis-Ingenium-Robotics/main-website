namespace AviSite.Web.Options;

/// <summary>Public links and contact details. Bound from the "Site" config section.</summary>
public sealed class SiteOptions
{
    public const string Section = "Site";

    public string Name { get; set; } = "AVI Robotics";
    public string LongName { get; set; } = "Ars Vallis Ingenium Robotics";
    public string? ContactEmail { get; set; }
    public string? DiscordInviteUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? YouTubeUrl { get; set; } = "https://www.youtube.com/@avi-robotics";
    public string? LinktreeUrl { get; set; } = "https://linktr.ee/avi_robotics";
    public string? PortalUrl { get; set; }
    public string SponsorPacketPath { get; set; } = "/Files/aviSponsorship.pdf";
}

/// <summary>Store behaviour. Bound from the "Store" config section.</summary>
public sealed class StoreOptions
{
    public const string Section = "Store";

    /// <summary>Folder for the JSON catalog/orders (draft storage). Relative paths resolve from the content root.</summary>
    public string DataDirectory { get; set; } = "App_Data";

    /// <summary>Master switch. When false the shop shows products but checkout is closed.</summary>
    public bool OrdersOpen { get; set; } = true;

    public string PickupNote { get; set; } =
        "Orders are picked up at a club meeting. We'll email you when shirts arrive.";

    public int MaxQuantityPerLine { get; set; } = 10;
    public int MaxLinesPerOrder { get; set; } = 20;
}

public enum PayPalMode { Fake, Sandbox, Live }

/// <summary>PayPal REST credentials. Bound from the "PayPal" config section. Secrets come from env vars / user-secrets, never appsettings.json.</summary>
public sealed class PayPalOptions
{
    public const string Section = "PayPal";

    public PayPalMode Mode { get; set; } = PayPalMode.Fake;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? WebhookId { get; set; }
    public string Currency { get; set; } = "USD";
    public string BrandName { get; set; } = "AVI Robotics";

    public Uri ApiBase => Mode == PayPalMode.Live
        ? new Uri("https://api-m.paypal.com/")
        : new Uri("https://api-m.sandbox.paypal.com/");
}

/// <summary>Single shared admin login for the orders dashboard. Bound from the "Admin" config section.</summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>Output of <c>dotnet run -- hash-password</c>. Empty disables admin login.</summary>
    public string? PasswordHash { get; set; }
}
