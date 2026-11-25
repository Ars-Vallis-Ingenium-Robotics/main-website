using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

public sealed record MatrixAlternative(string Name, string Notes);
public sealed record MatrixCriterion(string Name, decimal Weight, string Notes);
public sealed record MatrixScore(int Alternative, int Criterion, int? Score, string Rationale);
public sealed record MatrixDraft(string Id, string OrganizationId, string TeamId, string Title,
    IReadOnlyList<MatrixAlternative> Alternatives, IReadOnlyList<MatrixCriterion> Criteria, IReadOnlyList<MatrixScore> Scores);
public sealed record MatrixResult(string Alternative, decimal? WeightedScore, int? Rank, bool MissingScores);
public sealed record DecisionMatrix(string Id, string OrganizationId, string TeamId, string Title, bool Archived,
    string UpdatedBy, DateTimeOffset UpdatedUtc, MatrixDraft State, IReadOnlyList<MatrixResult> Results);
public sealed record MatrixAudit(string Action, string Actor, DateTimeOffset AtUtc);

public static class DecisionMatrixRules
{
    public const int MinAlternatives = 2, MaxAlternatives = 20;
    public const int MinCriteria = 1, MaxCriteria = 20;
    public const decimal MinWeight = 0.001m, MaxWeight = 10000m;

    public static bool ValidDimensions(int alternatives, int criteria) =>
        alternatives is >= MinAlternatives and <= MaxAlternatives && criteria is >= MinCriteria and <= MaxCriteria;

    public static IReadOnlyList<MatrixResult> Calculate(MatrixDraft draft)
    {
        Validate(draft);
        var total = draft.Criteria.Sum(c => c.Weight);
        var results = draft.Alternatives.Select((a, i) =>
        {
            var scores = draft.Scores.Where(s => s.Alternative == i).ToArray();
            var missing = scores.Length != draft.Criteria.Count || scores.Any(s => s.Score is null);
            decimal? value = missing ? null : scores.Sum(s => (decimal)s.Score!.Value * draft.Criteria[s.Criterion].Weight) / total;
            return (Name: a.Name, Value: value, Missing: missing);
        }).ToArray();
        var ranked = results.Select((x,i)=>(x,i)).Where(x => x.x.Value.HasValue).OrderByDescending(x => x.x.Value).ThenBy(x => x.x.Name, StringComparer.Ordinal).ThenBy(x => x.i).ToArray();
        return results.Select(x => new MatrixResult(x.Name, x.Value, x.Value is null ? null : Array.FindIndex(ranked, r => r.x.Value == x.Value) + 1, x.Missing)).ToArray();
    }

    public static void Validate(MatrixDraft d)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Length > 160 || d.OrganizationId.Length is < 1 or > 100 || d.TeamId.Length is < 1 or > 100) throw new ArgumentException("Title and valid scope are required.");
        if (!ValidDimensions(d.Alternatives.Count, d.Criteria.Count)) throw new ArgumentException("Use 2–20 alternatives and 1–20 criteria.");
        if (d.Alternatives.Any(a => a is null || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 120 || (a.Notes?.Length ?? 0) > 1000) || d.Criteria.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 120 || c.Weight < MinWeight || c.Weight > MaxWeight || (c.Notes?.Length ?? 0) > 1000) || d.Criteria.Sum(c => c.Weight) > MaxWeight * MaxCriteria) throw new ArgumentException("Names, notes, and weights within the per-criterion and total limits are required.");
        if (d.Alternatives.Select(a => a.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.Alternatives.Count
            || d.Criteria.Select(c => c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.Criteria.Count)
            throw new ArgumentException("Alternative and criterion names must be unique.");
        if (d.Scores.Count != d.Alternatives.Count * d.Criteria.Count || d.Scores.Any(s => s is null || s.Alternative < 0 || s.Alternative >= d.Alternatives.Count || s.Criterion < 0 || s.Criterion >= d.Criteria.Count || s.Score is < 0 or > 100 || s.Rationale is null || s.Rationale.Replace("\r\n", "\n").Length > 1000) || d.Scores.Select(s => (s.Alternative, s.Criterion)).Distinct().Count() != d.Scores.Count) throw new ArgumentException("Each alternative/criterion pair must have exactly one score (0–100) or an explicit blank, with valid rationale.");
    }
}

/// <summary>Organization-scoped matrices. Tables are created idempotently without changing SiteDb migrations.</summary>
public sealed class DecisionMatrixStore(SiteDb db, TimeProvider clock)
{
    public DecisionMatrixStore EnsureSchema() { using var c = db.Open(); SiteDb.Exec(c, """
        CREATE TABLE IF NOT EXISTS decision_matrices(id TEXT PRIMARY KEY, organization_id TEXT NOT NULL, team_id TEXT NOT NULL, title TEXT NOT NULL, archived INTEGER NOT NULL DEFAULT 0, state_json TEXT NOT NULL, updated_by TEXT NOT NULL, updated_utc TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS decision_matrices_scope ON decision_matrices(organization_id, team_id, archived);
        CREATE TABLE IF NOT EXISTS decision_matrix_audit(id INTEGER PRIMARY KEY, matrix_id TEXT NOT NULL, organization_id TEXT NOT NULL, team_id TEXT NOT NULL, action TEXT NOT NULL, actor TEXT NOT NULL, at_utc TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS decision_matrix_audit_scope ON decision_matrix_audit(organization_id, team_id, matrix_id, id);
        """); return this; }

    public IReadOnlyList<DecisionMatrix> List(string org, IEnumerable<string> teams, bool includeArchived = false)
    {
        var ids = teams.Distinct().ToArray(); if (ids.Length == 0) return [];
        using var c = db.Open(); using var cmd = SiteDb.Cmd(c, $"SELECT id,organization_id,team_id,title,archived,state_json,updated_by,updated_utc FROM decision_matrices WHERE organization_id=$org AND team_id IN ({string.Join(',', ids.Select((_,i)=>"$t"+i))}) AND ($arch=1 OR archived=0) ORDER BY updated_utc DESC", null, new[] { (Name:"$org", Value:(object?)org), (Name:"$arch", Value:(object?)(includeArchived ? 1 : 0)) }.Concat(ids.Select((x,i)=>(Name:"$t"+i,Value:(object?)x))).ToArray());
        var rows = new List<DecisionMatrix>(); using var r = cmd.ExecuteReader(); while (r.Read())
        {
            try { var d = JsonSerializer.Deserialize<MatrixDraft>(r.GetString(5))!; rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4)!=0,r.GetString(6),SiteDb.Time(r.GetString(7)),d,DecisionMatrixRules.Calculate(d))); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or InvalidOperationException or OverflowException or FormatException) { /* Skip corrupt module rows rather than failing the whole page. */ }
        }
        return rows;
    }

    public DecisionMatrix? Get(string id, string org, string team)
    { using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT title,archived,state_json,updated_by,updated_utc FROM decision_matrices WHERE id=$id AND organization_id=$org AND team_id=$team",null,("$id",id),("$org",org),("$team",team)); using var r=cmd.ExecuteReader(); if(!r.Read()) return null; try { var d=JsonSerializer.Deserialize<MatrixDraft>(r.GetString(2)); if(d is null)return null; return new(id,org,team,r.GetString(0),r.GetInt64(1)!=0,r.GetString(3),SiteDb.Time(r.GetString(4)),d,DecisionMatrixRules.Calculate(d)); } catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or InvalidOperationException or OverflowException or FormatException) { return null; } }
    private static string NextTimestamp(DateTimeOffset current, string? previous)
    {
        var now = current.ToUniversalTime();
        if (previous is not null && DateTimeOffset.TryParseExact(previous, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) && now <= parsed) now = parsed.AddMilliseconds(1);
        return SiteDb.Iso(now);
    }
    public bool Save(MatrixDraft draft, string actor, bool editing, string? expectedUpdatedUtc = null)
    {
        DecisionMatrixRules.Validate(draft);
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(draft.Id) || draft.Id.Length > 100
            || (editing && string.IsNullOrWhiteSpace(expectedUpdatedUtc)))
            throw new ArgumentException("Invalid actor or matrix id.");
        draft = draft with
        {
            Title = draft.Title.Trim(),
            Alternatives = draft.Alternatives.Select(a => a with { Name = a.Name.Trim() }).ToArray(),
            Criteria = draft.Criteria.Select(c => c with { Name = c.Name.Trim() }).ToArray(),
            Scores = draft.Scores.Select(s => s with { Rationale = s.Rationale.Trim() }).ToArray()
        };
        var now = NextTimestamp(clock.GetUtcNow(), expectedUpdatedUtc);

        using var c = db.Open();
        using var tx = c.BeginTransaction();
        using var write = SiteDb.Cmd(c, editing ? """
            UPDATE decision_matrices SET title=$title,state_json=$state,updated_by=$actor,updated_utc=$now
            WHERE id=$id AND organization_id=$org AND team_id=$team AND archived=0 AND updated_utc=$expected
            """ : """
            INSERT INTO decision_matrices(id,organization_id,team_id,title,state_json,updated_by,updated_utc)
            VALUES($id,$org,$team,$title,$state,$actor,$now) ON CONFLICT(id) DO NOTHING
            """, tx, ("$id", draft.Id), ("$org", draft.OrganizationId), ("$team", draft.TeamId),
            ("$title", draft.Title), ("$state", JsonSerializer.Serialize(draft)), ("$actor", actor), ("$now", now));
        if (editing) write.Parameters.AddWithValue("$expected", expectedUpdatedUtc!);
        if (write.ExecuteNonQuery() != 1) return false;
        Audit(c, tx, draft, editing ? "Edited" : "Created", actor, now);
        tx.Commit();
        return true;
    }
    public bool Archive(string id,string org,string team,string actor,bool archived)
    { using var c=db.Open(); using var tx=c.BeginTransaction(); var draft=Read(c,tx,id,org,team); if(draft is null)return false; using var current=SiteDb.Cmd(c,"SELECT updated_utc FROM decision_matrices WHERE id=$id AND organization_id=$org AND team_id=$team",tx,("$id",id),("$org",org),("$team",team)); var now=NextTimestamp(clock.GetUtcNow(),current.ExecuteScalar() as string); using var update=SiteDb.Cmd(c,"UPDATE decision_matrices SET archived=$a,updated_by=$actor,updated_utc=$now WHERE id=$id AND organization_id=$org AND team_id=$team AND archived<>$a",tx,("$a",archived?1:0),("$actor",actor),("$now",now),("$id",id),("$org",org),("$team",team)); if(update.ExecuteNonQuery()!=1)return false; Audit(c,tx,draft,archived?"Archived":"Restored",actor,now); tx.Commit(); return true; }
    public IReadOnlyList<MatrixAudit> AuditFor(string id,string org,string team)
    { using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT action,actor,at_utc FROM decision_matrix_audit WHERE matrix_id=$id AND organization_id=$org AND team_id=$team ORDER BY id",null,("$id",id),("$org",org),("$team",team)); var a=new List<MatrixAudit>(); using var r=cmd.ExecuteReader(); while(r.Read())a.Add(new(r.GetString(0),r.GetString(1),SiteDb.Time(r.GetString(2)))); return a; }
    private static MatrixDraft? Read(SqliteConnection c,SqliteTransaction tx,string id,string org,string team){using var cmd=SiteDb.Cmd(c,"SELECT state_json FROM decision_matrices WHERE id=$id AND organization_id=$org AND team_id=$team",tx,("$id",id),("$org",org),("$team",team)); if(cmd.ExecuteScalar() is not string s)return null; try { var draft=JsonSerializer.Deserialize<MatrixDraft>(s); if(draft is null)return null; DecisionMatrixRules.Validate(draft); return draft; } catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or InvalidOperationException or OverflowException or FormatException) { return null; }}
    private static void Audit(SqliteConnection c,SqliteTransaction tx,MatrixDraft d,string action,string actor,string now)=>SiteDb.Exec(c,"INSERT INTO decision_matrix_audit(matrix_id,organization_id,team_id,action,actor,at_utc) VALUES($id,$org,$team,$action,$actor,$now)",tx,("$id",d.Id),("$org",d.OrganizationId),("$team",d.TeamId),("$action",action),("$actor",actor),("$now",now));
}
