using AviSite.Web.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>
/// The site's SQLite database: one file, <c>site.db</c>, in the data directory (next to the JSON files).
/// No server to run; back it up by copying the data folder (SQLite's online backup is used by <see cref="BackupTo"/>).
///
/// Schema changes are numbered steps in <see cref="Migrations"/>: append a new step, never edit an old one.
/// On start the app takes a SQLite write lock, then applies all pending steps in one transaction so parallel starters cannot race.
/// </summary>
public sealed class SiteDb
{
    private readonly string _connectionString;
    public string Path { get; }

    public SiteDb(IOptions<StoreOptions> options, IWebHostEnvironment env)
    {
        var dir = System.IO.Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "site.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Cache = SqliteCacheMode.Shared, Pooling = true }.ToString();
        using var c = Open();
        Exec(c, "PRAGMA journal_mode = WAL;"); // readers never block the writer; survives crashes
        Migrate(c);
    }

    /// <summary>A fresh connection with foreign keys on and a 5 s wait if another write is in progress. Dispose it.</summary>
    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        Exec(c, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return c;
    }

    public static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static SqliteCommand Cmd(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Copies the whole database to <paramref name="file"/> while the site keeps running.</summary>
    public void BackupTo(string file)
    {
        using var src = Open();
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString());
        dst.Open();
        src.BackupDatabase(dst);
    }

    // ---------- Schema ----------

    private static readonly string[] Migrations =
    [
        // 1: dynamic QR codes (moved from dynamic-qr.json). Logos live in their own table so a scan never touches them.
        """
        CREATE TABLE qr_codes (
            slug TEXT PRIMARY KEY,
            target TEXT NOT NULL,
            campaign TEXT NOT NULL,
            note TEXT,
            active INTEGER NOT NULL DEFAULT 1,
            team TEXT,
            created_by TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            scans INTEGER NOT NULL DEFAULT 0,
            last_scan_utc TEXT,
            weekday TEXT NOT NULL DEFAULT '[0,0,0,0,0,0,0]',
            hour TEXT NOT NULL DEFAULT '[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]',
            devices TEXT NOT NULL DEFAULT '{}',
            look TEXT,
            look_updated_utc TEXT,
            look_by TEXT,
            deleted_utc TEXT,
            deleted_by TEXT
        );
        CREATE TABLE qr_daily (slug TEXT NOT NULL REFERENCES qr_codes(slug), day TEXT NOT NULL, scans INTEGER NOT NULL, PRIMARY KEY (slug, day));
        CREATE TABLE qr_logos (slug TEXT PRIMARY KEY REFERENCES qr_codes(slug), data TEXT NOT NULL);
        """,
        // 2: hours. A running session has no end yet; one running session per person.
        """
        CREATE TABLE hours (
            id INTEGER PRIMARY KEY,
            email TEXT NOT NULL,
            team TEXT NOT NULL,
            category TEXT NOT NULL,
            start_utc TEXT NOT NULL,
            end_utc TEXT,
            note TEXT,
            source TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            updated_by TEXT
        );
        CREATE UNIQUE INDEX hours_one_running ON hours(email) WHERE end_utc IS NULL;
        CREATE INDEX hours_by_email ON hours(email, start_utc);
        CREATE INDEX hours_by_team ON hours(team, start_utc);
        CREATE TABLE hours_categories (org TEXT NOT NULL, name TEXT NOT NULL, sort INTEGER NOT NULL, PRIMARY KEY (org, name));
        CREATE TABLE hours_settings (org TEXT PRIMARY KEY, leaderboard INTEGER NOT NULL DEFAULT 0, season_month INTEGER NOT NULL DEFAULT 8, season_day INTEGER NOT NULL DEFAULT 1);
        """,
        // 3: member profiles (draft → approved live copy) and their photos.
        """
        CREATE TABLE profiles (
            email TEXT PRIMARY KEY,
            slug TEXT NOT NULL,
            name TEXT NOT NULL,
            draft TEXT NOT NULL,
            draft_photo TEXT,
            live TEXT,
            live_photo TEXT,
            public INTEGER NOT NULL DEFAULT 0,
            pending INTEGER NOT NULL DEFAULT 0,
            note TEXT,
            updated_utc TEXT NOT NULL,
            reviewed_by TEXT,
            reviewed_utc TEXT
        );
        CREATE INDEX profiles_by_slug ON profiles(slug);
        CREATE TABLE profile_photos (id TEXT PRIMARY KEY, email TEXT NOT NULL, small BLOB NOT NULL, large BLOB NOT NULL, type TEXT NOT NULL, created_utc TEXT NOT NULL);
        """,
        // 4: cross-instance unique Discord account links.
        """
        CREATE TABLE discord_links (
            email TEXT PRIMARY KEY,
            discord_user_id TEXT NOT NULL UNIQUE
        );
        """,
        // 5: Discord bot management. One state row (pause), allowed servers with their per-server settings,
        // and an anonymous activity log for the owner's status page (no Discord user IDs, no message content).
        """
        CREATE TABLE discord_bot (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            paused INTEGER NOT NULL DEFAULT 0,
            changed_by TEXT,
            changed_utc TEXT
        );
        INSERT INTO discord_bot (id, paused) VALUES (1, 0);
        CREATE TABLE discord_guilds (
            guild_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            team TEXT NOT NULL,
            command_channels TEXT NOT NULL DEFAULT '',
            updates_channel TEXT,
            weekly_summary INTEGER NOT NULL DEFAULT 0,
            weekly_leaderboard INTEGER NOT NULL DEFAULT 0,
            panel_channel TEXT,
            meeting_day INTEGER,
            meeting_time TEXT,
            panel_message_id TEXT,
            last_summary TEXT,
            last_panel TEXT,
            added_by TEXT NOT NULL,
            added_utc TEXT NOT NULL,
            updated_by TEXT,
            updated_utc TEXT
        );
        CREATE TABLE discord_events (
            id INTEGER PRIMARY KEY,
            at_utc TEXT NOT NULL,
            guild_id TEXT,
            kind TEXT NOT NULL,
            outcome TEXT NOT NULL
        );
        CREATE INDEX discord_events_by_time ON discord_events(at_utc);
        """,
    ];

    private static void Migrate(SqliteConnection c)
    {
        // Acquire SQLite's writer lock before reading schema_version. Otherwise concurrent instances can
        // both decide to create the same migration objects and one of them fails during startup.
        using var tx = c.BeginTransaction(deferred: false);
        Exec(c, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)", tx);
        using (var cmd = Cmd(c, "SELECT COUNT(*) FROM schema_version", tx))
            if ((long)cmd.ExecuteScalar()! == 0) Exec(c, "INSERT INTO schema_version VALUES (0)", tx);
        long version;
        using (var cmd = Cmd(c, "SELECT version FROM schema_version", tx)) version = (long)cmd.ExecuteScalar()!;
        for (var v = (int)version; v < Migrations.Length; v++)
        {
            Exec(c, Migrations[v], tx);
            Exec(c, "UPDATE schema_version SET version = $v", tx, ("$v", v + 1));
        }
        tx.Commit();
    }

    // ---------- Value helpers (dates as ISO 8601 UTC text, which sorts correctly) ----------

    public static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
    public static string? Iso(DateTimeOffset? t) => t is { } v ? Iso(v) : null;
    public static DateTimeOffset Time(string s) => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
    public static DateTimeOffset? TimeOrNull(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : Time(r.GetString(i));
    public static string? StrOrNull(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
