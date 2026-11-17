using AviSite.Web.Content;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>Who may approve whose profile.</summary>
public static class ProfileRules
{
    /// <summary>Officers, the owner, and anyone who could edit that person on the People page (their leads).
    /// Nobody approves their own, except the owner.</summary>
    public static bool CanReview(Access me, Account target) =>
        me.Account is not null && (me.IsOwner || (target.Email != me.Account.Email && (me.IsOfficer || me.CanEditPerson(target))));

    public static bool IsReviewer(Access me) => me.IsOwner || me.IsOfficer || me.Manages.Count > 0;
}

/// <summary>
/// /admin/profile: members edit their own profile and photo; it goes public after an officer or one of their leads approves.
/// /people/photo/{id}/{size}: the photos (public once approved and shown; otherwise only to the member and their reviewers).
/// </summary>
public static class ProfileEndpoints
{
    private static readonly string[] LinkFields = ["linkedin", "github", "portfolio", "website", "resume", "instagram"];

    public static void MapProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/people/photo/{id}/{size:int}", async (string id, int size, HttpContext http, ProfileStore store, IAccountStore accounts) =>
        {
            if (id.Length != 24 || !id.All(Uri.IsHexDigit) || size is not (160 or 320)) return Results.NotFound();
            if (store.Photo(id, size == 320) is not { } found) return Results.NotFound();
            var live = store.Live().Values.Any(p => p.LivePhoto == id);
            if (!live)
            {
                // Not public (yet): the member themselves, and whoever can approve them.
                if (http.User.Identity?.IsAuthenticated != true) return Results.NotFound();
                var me = await http.AccessAsync(accounts);
                var owner = await accounts.FindAsync(found.Email);
                if (me.Account?.Email != found.Email && (owner is null || !ProfileRules.CanReview(me, owner))) return Results.NotFound();
            }
            // Ids are random and never reused, so a public photo can be cached for good.
            http.Response.Headers.CacheControl = live ? "public, max-age=31536000, immutable" : "private, no-store";
            return Results.File(found.Photo.Data, found.Photo.ContentType);
        });

        var g = app.MapGroup("/admin/profile").RequireAuthorization(AdminEndpoints.QrPolicy);

        // Save my profile. Read by hand (files + many fields), so the antiforgery check is by hand too.
        g.MapPost("/save", async (HttpContext http, IAntiforgery anti, IAccountStore accounts, ProfileStore store) =>
        {
            if (!await anti.IsRequestValidAsync(http)) return Results.BadRequest();
            var me = await http.AccessAsync(accounts);
            if (me.Account is not { } account) return Back("error=account");
            var form = await http.Request.ReadFormAsync();
            string? F(string k, int max) => form[k].ToString().Trim() is { Length: > 0 } v ? v[..Math.Min(v.Length, max)] : null;

            string? error = null;
            string? Url(string k)
            {
                if (F(k, 300) is not { } v) return null;
                if (!v.Contains("://")) v = "https://" + v;
                if (Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.Host.Contains('.')) return u.ToString();
                error ??= "link"; return null;
            }
            var email = F("email", 120);
            if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _)) { error ??= "email"; email = null; }
            var fields = new ProfileFields
            {
                Pronouns = F("pronouns", 30), Major = F("major", 100), Headline = F("headline", 140), Bio = F("bio", 800)?.Replace("\r", ""),
                Email = email, Linkedin = Url("linkedin"), Github = Url("github"), Portfolio = Url("portfolio"),
                Website = Url("website"), Resume = Url("resume"), Instagram = Url("instagram"),
            };
            if (error is not null) return Back("error=" + error);

            // Photo: the page's script sends two ready-made squares (160 and 320). Without script, one file is used for both.
            string? photo = null; var keep = true;
            if (form["remove_photo"] == "true") { keep = false; }
            var small = form.Files["photo_small"]; var large = form.Files["photo_large"] ?? form.Files["photo"];
            if (large is { Length: > 0 })
            {
                if (large.Length > ProfileStore.MaxPhotoBytes || small?.Length > ProfileStore.MaxPhotoBytes) return Back("error=photo-size");
                var big = await Bytes(large); var little = small is { Length: > 0 } ? await Bytes(small) : big;
                var type = ProfileStore.Sniff(big);
                if (type is null || ProfileStore.Sniff(little) != type) return Back("error=photo-type");
                photo = store.AddPhoto(account.Email, little, big, type); keep = false;
            }

            var name = JsonAccountStore.DisplayName(account);
            store.SaveDraft(account.Email, name, People.SlugOf(name), fields, form["public"] == "true", photo, keep);
            var p = store.Get(account.Email)!;
            return Back(p.Pending ? "done=sent" : "done=saved");
        });

        // Approve or send back someone's changes; or hide a public profile.
        g.MapPost("/review", async ([FromForm] string email, [FromForm] string action, [FromForm] string? note,
            HttpContext http, IAccountStore accounts, ProfileStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            var target = await accounts.FindAsync(email);
            if (me.Account is null || target is null || !ProfileRules.CanReview(me, target)) return Results.LocalRedirect("/admin/profile/review?error=denied");
            var ok = action switch
            {
                "approve" => store.Approve(email, me.Account.Email),
                "back" => store.SendBack(email, me.Account.Email, note),
                "hide" => Hide(),
                _ => false,
            };
            bool Hide() { store.Hide(email); return true; }
            return Results.LocalRedirect($"/admin/profile/review?{(ok ? "done=" + action : "error=gone")}");
        });
    }

    private static async Task<byte[]> Bytes(IFormFile f)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static IResult Back(string query) => Results.LocalRedirect("/admin/profile?" + query);
}
