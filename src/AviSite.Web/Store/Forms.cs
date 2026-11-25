using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

public enum FormStatus { Draft, Published, Archived }
public enum FormQuestionType { ShortText, LongText, SingleChoice, MultipleChoice, Date, Number }

public sealed record FormQuestion(Guid Id, string Label, FormQuestionType Type, bool Required, string[] Options)
{
    public string FieldName => $"q-{Id:N}";
}

public sealed record FormScope(string Organization, string Team, long OrganizationCreated, long TeamCreated)
{
    // IDs can be reused after deleting a group. Its creation time prevents a new group inheriting old data.
    public static FormScope? From(GroupTree tree, string? teamId)
    {
        if (tree.Find(teamId) is not { } team) return null;
        var root = team;
        var visited = new HashSet<string>();
        while (root.Parent is { } parent)
        {
            if (!visited.Add(root.Id) || tree.Find(parent) is not { } ancestor) return null;
            root = ancestor;
        }
        return new(root.Id, team.Id, root.AddedUtc.UtcTicks, team.AddedUtc.UtcTicks);
    }

    public static FormScope? ManagedBy(Access access, string? teamId) =>
        access.Account is not null && access.CanManageGroup(teamId) ? From(access.Tree, teamId) : null;
}

public sealed record FormDefinition(string Id, FormScope Scope, string Title, string Description, FormStatus Status,
    long Revision, FormQuestion[] Questions);
public sealed record FormSummary(string Id, string Team, string Title, FormStatus Status);
public sealed record FormAnswer(Guid QuestionId, string Label, string[] Values);
public sealed record FormSubmission(long Id, string SubmittedUtc, FormAnswer[] Answers);

public sealed class FormsStore
{
    public const int MaxQuestions = 20;
    public const int MaxOptions = 12;
    public const int LongTextLimit = 2000;
    public const int ShortTextLimit = 240;
    public const int PageSize = 25;
    public const int SubmissionPermitLimit = 20;

    private readonly SiteDb _db;
    private readonly TimeProvider _clock;
    private readonly IDataProtector _links;
    private readonly ITimeLimitedDataProtector _receipts;
    private const string Columns = "id, organization_id, team_id, organization_created, team_created, title, description, status, revision, questions_json, share_protected";

    public FormsStore(SiteDb db, TimeProvider clock, IDataProtectionProvider protection)
    {
        _db = db;
        _clock = clock;
        _links = protection.CreateProtector("AviSite.Forms.ShareToken.v1");
        _receipts = protection.CreateProtector("AviSite.Forms.SubmissionReceipt.v1").ToTimeLimitedDataProtector();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        SiteDb.Exec(c, """
            CREATE TABLE IF NOT EXISTS avi_forms (
                id TEXT PRIMARY KEY NOT NULL,
                organization_id TEXT NOT NULL,
                team_id TEXT NOT NULL,
                organization_created INTEGER NOT NULL,
                team_created INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                status INTEGER NOT NULL CHECK(status IN (0, 1, 2)),
                revision INTEGER NOT NULL DEFAULT 1,
                questions_json TEXT NOT NULL DEFAULT '[]',
                share_hash TEXT UNIQUE,
                share_protected TEXT,
                CHECK ((status = 1 AND share_hash IS NOT NULL AND share_protected IS NOT NULL)
                    OR (status <> 1 AND share_hash IS NULL AND share_protected IS NULL))
            );
            CREATE INDEX IF NOT EXISTS avi_forms_scope ON avi_forms(team_id, organization_id);
            CREATE TABLE IF NOT EXISTS avi_form_submissions (
                id INTEGER NOT NULL,
                form_id TEXT NOT NULL REFERENCES avi_forms(id),
                submitted_utc TEXT NOT NULL,
                answers_json TEXT NOT NULL,
                PRIMARY KEY(form_id,id)
            ) WITHOUT ROWID;
            """, tx);
        tx.Commit();
    }

    public string Create(FormScope scope, string title, string description)
    {
        using var c = _db.Open();
        var id = Guid.NewGuid().ToString("N");
        SiteDb.Exec(c, """
            INSERT INTO avi_forms(id, organization_id, team_id, organization_created, team_created, created_utc, title, description, status)
            VALUES($id,$org,$team,$orgCreated,$teamCreated,$now,$title,$description,0)
            """, null, ("$id", id), ("$now", SiteDb.Iso(_clock.GetUtcNow())), ("$org", scope.Organization), ("$team", scope.Team), ("$orgCreated", scope.OrganizationCreated),
            ("$teamCreated", scope.TeamCreated), ("$title", title), ("$description", description));
        return id;
    }

    public IReadOnlyList<FormSummary> ListManaged(Access access)
    {
        var scopes = access.Tree.All.Select(g => FormScope.ManagedBy(access, g.Id)).OfType<FormScope>();
        using var c = _db.Open();
        using var cmd = SiteDb.Cmd(c, """
            SELECT f.id, f.team_id, f.title, f.status
            FROM json_each($scopes) s JOIN avi_forms f
              ON f.team_id = json_extract(s.value, '$.Team')
                 AND f.organization_id = json_extract(s.value, '$.Organization')
                 AND f.team_created = json_extract(s.value, '$.TeamCreated')
                 AND f.organization_created = json_extract(s.value, '$.OrganizationCreated')
            ORDER BY f.created_utc DESC, f.id DESC
            """, null, ("$scopes", JsonSerializer.Serialize(scopes)));
        using var reader = cmd.ExecuteReader();
        var list = new List<FormSummary>();
        while (reader.Read()) list.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), (FormStatus)reader.GetInt32(3)));
        return list;
    }

    public FormDefinition? FindManaged(string id, Access access, out string? shareToken, bool includeShareToken = false)
    {
        shareToken = null;
        using var c = _db.Open();
        // Columns is a fixed projection; request values always use parameters.
        using var cmd = SiteDb.Cmd(c, $"SELECT {Columns} FROM avi_forms WHERE id = $id", null, ("$id", id));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || ReadScope(reader) != FormScope.ManagedBy(access, reader.GetString(2))) return null;
        if (includeShareToken && !reader.IsDBNull(10))
        {
            try { shareToken = _links.Unprotect(reader.GetString(10)); }
            catch (CryptographicException) { /* Lost keys must not leak the ciphertext or break response access. */ }
        }
        return ReadDefinition(reader);
    }

    public FormDefinition? FindPublished(string? token, GroupTree tree)
    {
        if (!IsShareToken(token)) return null;
        using var c = _db.Open();
        using var cmd = SiteDb.Cmd(c, $"SELECT {Columns} FROM avi_forms WHERE share_hash = $hash AND status = 1",
            null, ("$hash", Hash(token)));
        using var reader = cmd.ExecuteReader();
        return reader.Read() && ReadScope(reader) == FormScope.From(tree, reader.GetString(2)) ? ReadDefinition(reader) : null;
    }

    public bool Save(FormDefinition current, string title, string description, FormQuestion[] questions, bool publish)
    {
        using var c = _db.Open();
        var token = publish ? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) : null;
        using var cmd = SiteDb.Cmd(c, """
            UPDATE avi_forms SET title=$title, description=$description, questions_json=$questions, revision=revision+1,
                status=CASE WHEN $publish THEN 1 ELSE status END,
                share_hash=COALESCE($hash,share_hash), share_protected=COALESCE($protected,share_protected)
            WHERE id=$id AND revision=$revision AND status<>2 AND (NOT $publish OR status=0)
            """, null, ("$title", title), ("$description", description), ("$questions", JsonSerializer.Serialize(questions)),
            ("$publish", publish), ("$hash", token is null ? null : Hash(token)), ("$protected", token is null ? null : _links.Protect(token)),
            ("$id", current.Id), ("$revision", current.Revision));
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool Archive(FormDefinition current)
    {
        using var c = _db.Open();
        using var cmd = SiteDb.Cmd(c, """
            UPDATE avi_forms SET status=2, revision=revision+1, share_hash=NULL, share_protected=NULL
            WHERE id=$id AND revision=$revision AND status<>2
            """, null, ("$id", current.Id), ("$revision", current.Revision));
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool Submit(FormDefinition current, FormAnswer[] answers)
    {
        using var c = _db.Open();
        // No answers are inserted against a stale or archived form, even if it changed after validation.
        using var cmd = SiteDb.Cmd(c, """
            INSERT INTO avi_form_submissions(id,form_id,submitted_utc,answers_json)
            SELECT COALESCE((SELECT MAX(id) FROM avi_form_submissions WHERE form_id=$id),0)+1,id,$now,$answers
            FROM avi_forms WHERE id=$id AND revision=$revision AND status=1
            """, null, ("$now", SiteDb.Iso(_clock.GetUtcNow())), ("$answers", JsonSerializer.Serialize(answers)),
            ("$id", current.Id), ("$revision", current.Revision));
        return cmd.ExecuteNonQuery() == 1;
    }

    public string IssueReceipt(string token) => _receipts.Protect(Hash(token), TimeSpan.FromMinutes(10));

    public bool VerifyReceipt(string? token, string? receipt)
    {
        if (!IsShareToken(token) || receipt is not { Length: > 0 and <= 512 }) return false;
        try { return _receipts.Unprotect(receipt) == Hash(token); }
        catch (CryptographicException) { return false; }
    }

    public IReadOnlyList<FormSubmission> Submissions(FormDefinition authorized, long before, int limit)
    {
        using var c = _db.Open();
        using var cmd = SiteDb.Cmd(c, """
            SELECT id, submitted_utc, answers_json FROM avi_form_submissions
            WHERE form_id=$form AND id<$before ORDER BY id DESC LIMIT $limit
            """, null, ("$form", authorized.Id), ("$before", before), ("$limit", limit));
        using var reader = cmd.ExecuteReader();
        var list = new List<FormSubmission>();
        while (reader.Read()) list.Add(new(reader.GetInt64(0), reader.GetString(1), JsonSerializer.Deserialize<FormAnswer[]>(reader.GetString(2))!));
        return list;
    }

    private static bool IsShareToken([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? token) =>
        token is { Length: 64 } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    private static FormScope ReadScope(SqliteDataReader r) => new(r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4));
    private static FormDefinition ReadDefinition(SqliteDataReader r) => new(r.GetString(0), ReadScope(r), r.GetString(5),
        r.GetString(6), (FormStatus)r.GetInt32(7), r.GetInt64(8), JsonSerializer.Deserialize<FormQuestion[]>(r.GetString(9))!);
}
