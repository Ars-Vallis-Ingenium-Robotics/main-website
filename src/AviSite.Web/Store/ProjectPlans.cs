using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

public sealed record ProjectPlan(string Id, string TeamId, string Title, string Description, bool Archived, string Actor, string UpdatedUtc);
public sealed record ProjectTask(string Id, string ProjectId, string Title, string OwnerEmail, string Status, DateOnly StartDate, DateOnly DueDate, int Progress, string Description);
public sealed record ProjectEvent(long Id, string? TaskId, string Action, string Actor, string AtUtc);

/// <summary>Tenant-scoped project plans stored in dedicated idempotent tables.</summary>
public sealed class ProjectPlans(SiteDb db)
{
    public static void Initialize(SiteDb db)
    {
        using var c = db.Open();
        SiteDb.Exec(c, """
            CREATE TABLE IF NOT EXISTS project_plans (id TEXT PRIMARY KEY, team_id TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL, archived INTEGER NOT NULL DEFAULT 0, actor TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS project_plans_team ON project_plans(team_id, archived);
            CREATE TABLE IF NOT EXISTS project_tasks (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES project_plans(id) ON DELETE CASCADE, title TEXT NOT NULL, owner_email TEXT NOT NULL, status TEXT NOT NULL, start_date TEXT NOT NULL, due_date TEXT NOT NULL, progress INTEGER NOT NULL CHECK(progress BETWEEN 0 AND 100), description TEXT NOT NULL, actor TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS project_tasks_project ON project_tasks(project_id, start_date);
            CREATE TABLE IF NOT EXISTS project_dependencies (task_id TEXT NOT NULL REFERENCES project_tasks(id) ON DELETE CASCADE, depends_on TEXT NOT NULL REFERENCES project_tasks(id) ON DELETE CASCADE, PRIMARY KEY(task_id, depends_on), CHECK(task_id <> depends_on));
            CREATE TABLE IF NOT EXISTS project_events (id INTEGER PRIMARY KEY, project_id TEXT NOT NULL, task_id TEXT, action TEXT NOT NULL, actor TEXT NOT NULL, at_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS project_events_project ON project_events(project_id, id);
            """);
    }

    public IReadOnlyList<ProjectPlan> List(IEnumerable<string> teams, bool includeArchived = false)
    {
        var ids = teams.Distinct().ToArray(); if (ids.Length == 0) return [];
        using var c = db.Open(); using var cmd = SiteDb.Cmd(c, $"SELECT id,team_id,title,description,archived,actor,updated_utc FROM project_plans WHERE team_id IN ({string.Join(',', ids.Select((_,i) => "$t"+i))}) {(includeArchived ? "" : "AND archived=0")} ORDER BY updated_utc DESC");
        for (var i=0;i<ids.Length;i++) cmd.Parameters.AddWithValue("$t"+i, ids[i]);
        using var r=cmd.ExecuteReader(); var rows=new List<ProjectPlan>(); while(r.Read()) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4)!=0,r.GetString(5),r.GetString(6))); return rows;
    }
    public ProjectPlan? Get(string id)
    { using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT id,team_id,title,description,archived,actor,updated_utc FROM project_plans WHERE id=$id",null,("$id",id)); using var r=cmd.ExecuteReader(); return r.Read()?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4)!=0,r.GetString(5),r.GetString(6)):null; }
    public IReadOnlyList<ProjectTask> Tasks(string projectId)
    { using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT id,project_id,title,owner_email,status,start_date,due_date,progress,description FROM project_tasks WHERE project_id=$p ORDER BY start_date,due_date",null,("$p",projectId)); using var r=cmd.ExecuteReader(); var rows=new List<ProjectTask>(); while(r.Read()) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),DateOnly.ParseExact(r.GetString(5),"yyyy-MM-dd"),DateOnly.ParseExact(r.GetString(6),"yyyy-MM-dd"),r.GetInt32(7),r.GetString(8))); return rows; }
    public IReadOnlyList<string> Dependencies(string taskId)
    { using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT depends_on FROM project_dependencies WHERE task_id=$id ORDER BY depends_on",null,("$id",taskId)); using var r=cmd.ExecuteReader(); var rows=new List<string>(); while(r.Read()) rows.Add(r.GetString(0)); return rows; }
    public IReadOnlyList<ProjectEvent> Events(string projectId)
    { using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT id,task_id,action,actor,at_utc FROM project_events WHERE project_id=$p ORDER BY id DESC LIMIT 100",null,("$p",projectId)); using var r=cmd.ExecuteReader(); var rows=new List<ProjectEvent>(); while(r.Read()) rows.Add(new(r.GetInt64(0),r.IsDBNull(1)?null:r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4))); return rows; }
    public string Save(string? id,string team,string title,string description,string actor)
    { using var c=db.Open(); var key=string.IsNullOrWhiteSpace(id)?Guid.NewGuid().ToString("N"):id; var now=DateTimeOffset.UtcNow.ToString("O"); SiteDb.Exec(c,"INSERT INTO project_plans(id,team_id,title,description,actor,updated_utc) VALUES($id,$team,$title,$description,$actor,$now) ON CONFLICT(id) DO UPDATE SET title=excluded.title,description=excluded.description,actor=excluded.actor,updated_utc=excluded.updated_utc",null,("$id",key),("$team",team),("$title",title),("$description",description),("$actor",actor),("$now",now)); Event(c,key,null,id is null?"Created":"Updated",actor,now); return key; }
    public string? SaveTask(string project,string? id,string title,string owner,string status,string start,string due,int progress,string description,string actor,string?[] dependencies)
    { if(!DateOnly.TryParseExact(start,"yyyy-MM-dd",out var s)||!DateOnly.TryParseExact(due,"yyyy-MM-dd",out var d)||d<s||progress is <0 or >100||status is not("Planned" or "In progress" or "Blocked" or "Done")) return null;
      using var c=db.Open(); using var tx=c.BeginTransaction(); var key=string.IsNullOrWhiteSpace(id)?Guid.NewGuid().ToString("N"):id;
      SiteDb.Exec(c,"INSERT INTO project_tasks(id,project_id,title,owner_email,status,start_date,due_date,progress,description,actor,updated_utc) VALUES($id,$p,$title,$owner,$status,$start,$due,$progress,$desc,$actor,$now) ON CONFLICT(id) DO UPDATE SET title=excluded.title,owner_email=excluded.owner_email,status=excluded.status,start_date=excluded.start_date,due_date=excluded.due_date,progress=excluded.progress,description=excluded.description,actor=excluded.actor,updated_utc=excluded.updated_utc WHERE project_id=$p",tx,("$id",key),("$p",project),("$title",title),("$owner",owner),("$status",status),("$start",s.ToString("yyyy-MM-dd")),("$due",d.ToString("yyyy-MM-dd")),("$progress",progress),("$desc",description),("$actor",actor),("$now",DateTimeOffset.UtcNow.ToString("O")));
      using(var check=SiteDb.Cmd(c,"SELECT project_id FROM project_tasks WHERE id=$id",tx,("$id",key))) if(check.ExecuteScalar() is string taskProject&&taskProject!=project) { tx.Rollback(); return null; }
      SiteDb.Exec(c,"DELETE FROM project_dependencies WHERE task_id=$id",tx,("$id",key)); foreach(var dep in dependencies.Where(x=>!string.IsNullOrWhiteSpace(x))) { if(dep==key||!Exists(c,tx,project,dep!)) { tx.Rollback(); return null; } SiteDb.Exec(c,"INSERT INTO project_dependencies(task_id,depends_on) VALUES($id,$dep)",tx,("$id",key),("$dep",dep)); if(HasCycle(c,tx,project)) { tx.Rollback(); return null; } }
      Event(c,project,key,"TaskSaved",actor,DateTimeOffset.UtcNow.ToString("O"),tx); tx.Commit(); return key; }
    public void Archive(string id,string actor) { using var c=db.Open(); var now=DateTimeOffset.UtcNow.ToString("O"); SiteDb.Exec(c,"UPDATE project_plans SET archived=1,actor=$a,updated_utc=$u WHERE id=$id",null,("$a",actor),("$u",now),("$id",id)); Event(c,id,null,"Archived",actor,now); }
    public static bool HasCycle(IReadOnlyDictionary<string,IReadOnlyCollection<string>> links) { var visiting=new HashSet<string>(); var done=new HashSet<string>(); bool Visit(string n){if(visiting.Contains(n))return true;if(!done.Add(n))return false;visiting.Add(n);if(links.TryGetValue(n,out var deps)&&deps.Any(Visit))return true;visiting.Remove(n);return false;} return links.Keys.Any(Visit); }
    private static bool HasCycle(SqliteConnection c,SqliteTransaction tx,string project) { var mutable=new Dictionary<string,List<string>>(); using var cmd=SiteDb.Cmd(c,"SELECT d.task_id,d.depends_on FROM project_dependencies d JOIN project_tasks t ON t.id=d.task_id WHERE t.project_id=$p",tx,("$p",project)); using var r=cmd.ExecuteReader(); while(r.Read()){var k=r.GetString(0); if(!mutable.TryGetValue(k,out var dependencies))mutable[k]=dependencies=[]; dependencies.Add(r.GetString(1));} return HasCycle(mutable.ToDictionary(pair=>pair.Key,pair=>(IReadOnlyCollection<string>)pair.Value)); }
    private static bool Exists(SqliteConnection c,SqliteTransaction tx,string project,string id) { using var cmd=SiteDb.Cmd(c,"SELECT 1 FROM project_tasks WHERE id=$id AND project_id=$p",tx,("$id",id),("$p",project)); return cmd.ExecuteScalar() is not null; }
    private static void Event(SqliteConnection c,string project,string? task,string action,string actor,string now,SqliteTransaction? tx=null)=>SiteDb.Exec(c,"INSERT INTO project_events(project_id,task_id,action,actor,at_utc) VALUES($p,$t,$a,$actor,$now)",tx,("$p",project),("$t",task),("$a",action),("$actor",actor),("$now",now));
}

public sealed class ProjectPlanModuleInitializer(SiteDb db) : IHostedService
{ public Task StartAsync(CancellationToken cancellationToken) { ProjectPlans.Initialize(db); return Task.CompletedTask; } public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask; }
