using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

/// <summary>What a member fills in about themselves. Every field is optional; blank ones never show.</summary>
public sealed record ProfileFields
{
    public string? Pronouns { get; init; }
    public string? Major { get; init; }
    public string? Headline { get; init; }
    /// <summary>Plain text (paragraphs split on blank lines). Never Markdown or HTML: it's typed by members.</summary>
    public string? Bio { get; init; }
    public string? Email { get; init; }
    public string? Linkedin { get; init; }
    public string? Github { get; init; }
    public string? Portfolio { get; init; }
    public string? Website { get; init; }
    public string? Resume { get; init; }
    public string? Instagram { get; init; }
}

/// <summary>
/// A member's profile. They edit the draft; an officer or one of their leads approves it, which copies the draft
/// to live. Only live + <see cref="Public"/> profiles show on the public site.
/// </summary>
public sealed class ProfileRow
{
    public string Email { get; set; } = "";
    /// <summary>first-last, from the account name: the same key the roster and content/people use.</summary>
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public ProfileFields Draft { get; set; } = new();
    public string? DraftPhoto { get; set; }
    public ProfileFields? Live { get; set; }
    public string? LivePhoto { get; set; }
    public bool Public { get; set; }
    public bool Pending { get; set; }
    /// <summary>Why the last change was sent back, for the member to read.</summary>
    public string? Note { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedUtc { get; set; }

    public bool ShowsPublicly => Public && Live is not null;
}

public sealed record ProfilePhoto(byte[] Data, string ContentType);

public sealed class ProfileStore(SiteDb db, TimeProvider clock)
{
    public const int MaxPhotoBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private const string Cols = "email, slug, name, draft, draft_photo, live, live_photo, public, pending, note, updated_utc, reviewed_by, reviewed_utc";

    // The public pages ask for profiles a lot (every avatar); keep the live ones in memory and drop the copy on any change.
    private volatile Dictionary<string, ProfileRow>? _live;

    private DateTimeOffset Now => clock.GetUtcNow();

    private static ProfileRow Read(SqliteDataReader r) => new()
    {
        Email = r.GetString(0), Slug = r.GetString(1), Name = r.GetString(2),
        Draft = JsonSerializer.Deserialize<ProfileFields>(r.GetString(3), Json) ?? new(),
        DraftPhoto = SiteDb.StrOrNull(r, 4),
        Live = r.IsDBNull(5) ? null : JsonSerializer.Deserialize<ProfileFields>(r.GetString(5), Json),
        LivePhoto = SiteDb.StrOrNull(r, 6),
        Public = r.GetInt64(7) == 1, Pending = r.GetInt64(8) == 1, Note = SiteDb.StrOrNull(r, 9),
        UpdatedUtc = SiteDb.Time(r.GetString(10)), ReviewedBy = SiteDb.StrOrNull(r, 11), ReviewedUtc = SiteDb.TimeOrNull(r, 12),
    };

    private static List<ProfileRow> Query(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = SiteDb.Cmd(c, sql, null, args);
        using var r = cmd.ExecuteReader();
        var list = new List<ProfileRow>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public ProfileRow? Get(string email)
    {
        using var c = db.Open();
        return Query(c, $"SELECT {Cols} FROM profiles WHERE email = $e", ("$e", email)).FirstOrDefault();
    }

    /// <summary>Waiting for approval, oldest first. The page filters to the people the viewer may approve.</summary>
    public List<ProfileRow> Pending()
    {
        using var c = db.Open();
        return Query(c, $"SELECT {Cols} FROM profiles WHERE pending = 1 ORDER BY updated_utc");
    }

    /// <summary>Approved, public profiles by slug (what the people pages show).</summary>
    public IReadOnlyDictionary<string, ProfileRow> Live()
    {
        if (_live is { } cached) return cached;
        using var c = db.Open();
        var map = new Dictionary<string, ProfileRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Query(c, $"SELECT {Cols} FROM profiles WHERE public = 1 AND live IS NOT NULL ORDER BY updated_utc"))
            map.TryAdd(p.Slug, p); // two accounts with the same name: the first one keeps the page
        return _live = map;
    }

    /// <summary>Save what the member typed. It waits for approval unless it matches what's already live.</summary>
    public ProfileRow SaveDraft(string email, string name, string slug, ProfileFields draft, bool isPublic, string? photo, bool keepPhoto)
    {
        var p = Get(email) ?? new ProfileRow { Email = email };
        p.Name = name; p.Slug = slug; p.Draft = draft; p.Public = isPublic;
        if (!keepPhoto) p.DraftPhoto = photo;
        p.Pending = !SameAsLive(p);
        if (p.Pending) p.Note = null;
        p.UpdatedUtc = Now;
        Write(p);
        return p;
    }

    public bool Approve(string email, string by)
    {
        var p = Get(email);
        if (p is null || !p.Pending) return false;
        p.Live = p.Draft; p.LivePhoto = p.DraftPhoto; p.Pending = false; p.Note = null;
        p.ReviewedBy = by; p.ReviewedUtc = Now;
        Write(p);
        return true;
    }

    public bool SendBack(string email, string by, string? note)
    {
        var p = Get(email);
        if (p is null || !p.Pending) return false;
        p.Pending = false; p.Note = string.IsNullOrWhiteSpace(note) ? "Not approved." : note.Trim()[..Math.Min(note.Trim().Length, 300)];
        p.ReviewedBy = by; p.ReviewedUtc = Now;
        Write(p);
        return true;
    }

    /// <summary>Take a profile off the public site now (no approval needed to hide).</summary>
    public void Hide(string email)
    {
        using var c = db.Open();
        SiteDb.Exec(c, "UPDATE profiles SET public = 0 WHERE email = $e", null, ("$e", email));
        _live = null;
    }

    private static bool SameAsLive(ProfileRow p) => p.Live is not null && p.Live == p.Draft && p.LivePhoto == p.DraftPhoto;

    private void Write(ProfileRow p)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        SiteDb.Exec(c, $"""
            INSERT INTO profiles ({Cols}) VALUES ($e, $s, $n, $d, $dp, $l, $lp, $pub, $pen, $note, $u, $rb, $ru)
            ON CONFLICT(email) DO UPDATE SET slug = $s, name = $n, draft = $d, draft_photo = $dp, live = $l, live_photo = $lp,
                public = $pub, pending = $pen, note = $note, updated_utc = $u, reviewed_by = $rb, reviewed_utc = $ru
            """, tx,
            ("$e", p.Email), ("$s", p.Slug), ("$n", p.Name), ("$d", JsonSerializer.Serialize(p.Draft, Json)), ("$dp", p.DraftPhoto),
            ("$l", p.Live is null ? null : JsonSerializer.Serialize(p.Live, Json)), ("$lp", p.LivePhoto),
            ("$pub", p.Public ? 1 : 0), ("$pen", p.Pending ? 1 : 0), ("$note", p.Note), ("$u", SiteDb.Iso(p.UpdatedUtc)),
            ("$rb", p.ReviewedBy), ("$ru", p.ReviewedUtc is { } t ? SiteDb.Iso(t) : null));
        // Photos nobody points at any more go.
        SiteDb.Exec(c, "DELETE FROM profile_photos WHERE email = $e AND id NOT IN (COALESCE($dp, ''), COALESCE($lp, ''))", tx,
            ("$e", p.Email), ("$dp", p.DraftPhoto), ("$lp", p.LivePhoto));
        tx.Commit();
        _live = null;
    }

    // ---------- Photos (stored in the database; two sizes, both square) ----------

    /// <summary>Stores an uploaded photo; returns its id (random, so URLs can be cached forever).</summary>
    public string AddPhoto(string email, byte[] small, byte[] large, string contentType)
    {
        var id = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
        using var c = db.Open();
        SiteDb.Exec(c, "INSERT INTO profile_photos (id, email, small, large, type, created_utc) VALUES ($id, $e, $s, $l, $t, $c)", null,
            ("$id", id), ("$e", email), ("$s", small), ("$l", large), ("$t", contentType), ("$c", SiteDb.Iso(Now)));
        return id;
    }

    public (ProfilePhoto Photo, string Email)? Photo(string id, bool large)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, $"SELECT {(large ? "large" : "small")}, type, email FROM profile_photos WHERE id = $id", null, ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? (new ProfilePhoto((byte[])r[0], r.GetString(1)), r.GetString(2)) : null;
    }

    /// <summary>The image type from the file's first bytes; null for anything that isn't a plain WebP, JPEG or PNG.</summary>
    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length > 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        return null;
    }
}
