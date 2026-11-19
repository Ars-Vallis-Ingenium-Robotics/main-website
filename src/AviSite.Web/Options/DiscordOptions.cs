namespace AviSite.Web.Options;

/// <summary>Discord Interactions and account-linking settings. Secrets are environment/user-secrets only.</summary>
public sealed class DiscordOptions
{
    public const string Section = "Discord";

    /// <summary>Discord application ID (also the OAuth client ID).</summary>
    public string? ApplicationId { get; set; }
    /// <summary>Ed25519 public key from the Discord Developer Portal; public, 32 bytes as 64 hex characters.</summary>
    public string? PublicKey { get; set; }
    /// <summary>OAuth client secret. Never put this in appsettings or logs.</summary>
    public string? ClientSecret { get; set; }
    /// <summary>Exact callback registered in the Discord Developer Portal.</summary>
    public string? RedirectUri { get; set; }
    /// <summary>Canonical site URL used to build account-link instructions; no trailing slash required.</summary>
    public string? PublicBaseUrl { get; set; }
    /// <summary>Bot token: only for posting to channels, registering commands and reading app status. Environment only.</summary>
    public string? BotToken { get; set; }

    public bool InteractionsConfigured => IsSnowflake(ApplicationId) && IsHex(PublicKey, 64);

    public bool LinkingConfigured => InteractionsConfigured
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && IsSafeUrl(RedirectUri)
        && IsSafeUrl(PublicBaseUrl);

    /// <summary>Owner tools that call Discord's API (status, command registration, channel posts).</summary>
    public bool BotConfigured => InteractionsConfigured && !string.IsNullOrWhiteSpace(BotToken);

    public static bool IsSnowflake(string? value) =>
        value is { Length: >= 17 and <= 20 } && value.All(char.IsAsciiDigit);

    private static bool IsHex(string? value, int length) =>
        value is { Length: var n } && n == length && value.All(Uri.IsHexDigit);

    private static bool IsSafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
