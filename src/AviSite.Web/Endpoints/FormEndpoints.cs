using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace AviSite.Web.Endpoints;

public static class FormEndpoints
{
    public const string ManagerPolicy = "FormsManagerAccount";
    private const string SubmitPolicy = "forms-submit";
    private const string AntiforgeryField = "__RequestVerificationToken"; // matches the application's AntiforgeryToken component
    private const long BodyLimit = 400 * 1024; // 20 long answers, including percent-encoded UTF-8
    private const int OptionLimit = 100;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Program.cs: services.AddFormsModule(); before builder.Build().
    // Program.cs: app.MapFormEndpoints(); alongside other Map calls, with the existing auth,
    // antiforgery, rate-limiter and Razor components pipeline. No SiteDb migration or global CSS change.
    // Before rollout: keep Data Protection keys persistent and redact /forms/* URLs (including
    // redirect locations) at the reverse proxy, in telemetry and in application request logging.
    public static IServiceCollection AddFormsModule(this IServiceCollection services)
    {
        services.AddSingleton<FormsStore>();
        services.AddTransient<IStartupFilter, FormPrivacyHeaders>();
        services.AddAuthorizationBuilder().AddPolicy(ManagerPolicy, p => p.AddAuthenticationSchemes(AdminEndpoints.Scheme)
            .RequireAuthenticatedUser().RequireClaim(AccountAuth.EmailClaim));
        services.AddRateLimiter(options => options.AddPolicy(SubmitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = FormsStore.SubmissionPermitLimit, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 })));
        return services;
    }

    public static void MapFormEndpoints(this WebApplication app)
    {
        app.Services.GetRequiredService<FormsStore>(); // apply the idempotent module schema before serving traffic
        var admin = app.MapGroup("/admin/forms").RequireAuthorization(ManagerPolicy);
        admin.MapPost("/create", Create);
        admin.MapPost("/{id:guid}/save", Save);
        admin.MapPost("/{id:guid}/archive", Archive);
        admin.MapGet("/{id:guid}/responses.csv", Export);
        app.MapPost("/forms/{token}/submit", Submit).AllowAnonymous().RequireRateLimiting(SubmitPolicy);
    }

    public static async Task<Access> ManagerAccess(HttpContext http, IAccountStore accounts)
    {
        var ct = http.RequestAborted;
        var email = http.User.FindFirstValue(AccountAuth.EmailClaim);
        if (http.User.Identity?.IsAuthenticated != true || email is null || await accounts.FindAsync(email, ct) is not { } before)
            return Access.For(null, new GroupTree([]));
        var tree = await accounts.GroupsAsync(ct);
        var after = await accounts.FindAsync(email, ct);
        // Bracket the tree snapshot: permission changes update the stamp, so a new membership
        // cannot be combined with the tree of a deleted and recreated group.
        return Access.For(after?.Stamp == before.Stamp ? before : null, tree);
    }

    private static async Task<IResult> Create(HttpContext http, IAccountStore accounts, FormsStore store, IAntiforgery antiforgery)
    {
        var access = await ManagerAccess(http, accounts);
        if (!access.LeadsAnything || access.Account is null) return Unavailable();
        var (form, error) = await ReadPost(http, antiforgery);
        if (error is not null) return error;
        if (FormScope.ManagedBy(access, One(form!, "team")) is not { } scope) return Unavailable();
        if (!TryDetails(form!, out var title, out var description)) return Invalid("Enter a title and keep the description to 1,000 characters.");
        return SeeOther($"/admin/forms/{store.Create(scope, title, description)}");
    }

    private static async Task<IResult> Save(Guid id, HttpContext http, IAccountStore accounts, FormsStore store, IAntiforgery antiforgery)
    {
        var current = store.FindManaged(id.ToString("N"), await ManagerAccess(http, accounts), out _);
        if (current is null) return Unavailable();
        var (form, error) = await ReadPost(http, antiforgery);
        if (error is not null) return error;
        if (!MatchesRevision(form!, current) || current.Status == FormStatus.Archived) return Changed();
        var intent = One(form!, "intent");
        if (intent is not ("save" or "publish")) return Invalid("Choose Save or Publish.");
        if (!TryDetails(form!, out var title, out var description)) return Invalid("Enter a title and keep the description to 1,000 characters.");

        var known = new HashSet<string>(["title", "description", "revision", "intent", AntiforgeryField], StringComparer.Ordinal);
        foreach (var key in current.Questions.Select(q => q.FieldName).Append("new"))
            foreach (var suffix in new[] { "label", "type", "options", "required", "remove" })
                if (key != "new" || suffix != "remove") known.Add($"{key}-{suffix}");
        if (form!.Keys.Any(key => !known.Contains(key))) return Invalid("This edit contains an unknown question field.");

        var questions = new List<FormQuestion>();
        foreach (var q in current.Questions)
        {
            var remove = $"{q.FieldName}-remove";
            if (form.ContainsKey(remove))
            {
                if (One(form, remove) != "true") return Invalid("Invalid remove selection.");
                continue;
            }
            if (!TryQuestion(form, q.FieldName, q.Id, out var updated)) return Invalid(QuestionHelp);
            questions.Add(updated!);
        }
        if (form.Keys.Any(key => key.StartsWith("new-", StringComparison.Ordinal)) && (One(form, "new-label") != ""
            || One(form, "new-options") != "" || One(form, "new-type") != "ShortText" || form.ContainsKey("new-required")))
        {
            if (!TryQuestion(form, "new", Guid.NewGuid(), out var added)) return Invalid(QuestionHelp);
            questions.Add(added!);
        }
        var publish = intent == "publish";
        if (questions.Count > FormsStore.MaxQuestions) return Invalid($"A form can have at most {FormsStore.MaxQuestions} questions.");
        if ((publish || current.Status == FormStatus.Published) && questions.Count == 0) return Invalid("Add at least one question before publishing.");
        return store.Save(current, title, description, questions.ToArray(), publish)
            ? SeeOther($"/admin/forms/{current.Id}?saved=1") : Changed();
    }

    private static async Task<IResult> Archive(Guid id, HttpContext http, IAccountStore accounts, FormsStore store, IAntiforgery antiforgery)
    {
        var current = store.FindManaged(id.ToString("N"), await ManagerAccess(http, accounts), out _);
        if (current is null) return Unavailable();
        var (form, error) = await ReadPost(http, antiforgery);
        if (error is not null) return error;
        return MatchesRevision(form!, current) && store.Archive(current) ? SeeOther($"/admin/forms/{current.Id}?saved=1") : Changed();
    }

    private static async Task<IResult> Submit(string token, HttpContext http, IAccountStore accounts, FormsStore store, IAntiforgery antiforgery)
    {
        var current = store.FindPublished(token, await accounts.GroupsAsync(http.RequestAborted));
        if (current is null) return Unavailable();
        var (form, error) = await ReadPost(http, antiforgery);
        if (error is not null) return error;
        if (!MatchesRevision(form!, current)) return Changed();
        var known = current.Questions.Select(q => q.FieldName).ToHashSet(StringComparer.Ordinal);
        if (form!.Keys.Any(key => key is not ("revision" or AntiforgeryField) && !known.Contains(key)))
            return Invalid("This submission contains an unknown question. Go back and reload the form.");

        var answers = new List<FormAnswer>();
        for (var i = 0; i < current.Questions.Length; i++)
        {
            var q = current.Questions[i];
            var posted = form[q.FieldName];
            if (posted.Count == 0 || (q.Type is not FormQuestionType.MultipleChoice && posted.Count == 1 && posted[0] == ""))
            {
                if (q.Required) return Invalid($"Question {i + 1} is required. Go back and answer it.");
                continue;
            }
            if (q.Type == FormQuestionType.MultipleChoice ? posted.Count > q.Options.Length : posted.Count != 1)
                return Invalid($"Question {i + 1} has an invalid number of answers.");
            var parsed = new List<string>();
            foreach (var raw in posted)
            {
                if (!TryAnswer(q, raw, out var value) || parsed.Contains(value, StringComparer.Ordinal))
                    return Invalid($"Question {i + 1} has an invalid answer. Go back and check its format, choices or length.");
                parsed.Add(value);
            }
            if (parsed[0].Length == 0)
            {
                if (q.Required) return Invalid($"Question {i + 1} is required. Go back and answer it.");
                continue;
            }
            answers.Add(new(q.Id, q.Label, parsed.ToArray()));
        }
        return store.Submit(current, answers.ToArray())
            ? SeeOther($"/forms/{token}?receipt={store.IssueReceipt(token)}") : Changed();
    }

    private static async Task<IResult> Export(Guid id, HttpContext http, IAccountStore accounts, FormsStore store)
    {
        var current = store.FindManaged(id.ToString("N"), await ManagerAccess(http, accounts), out _);
        if (current is null) return Unavailable();
        http.Response.Headers.ContentDisposition = $"attachment; filename=avi-form-{current.Id}.csv";
        return Results.Stream(async body =>
        {
            var ct = http.RequestAborted;
            await using var writer = new StreamWriter(body, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
            await writer.WriteLineAsync("submission_id,submitted_utc,question_id,question,answer".AsMemory(), ct);
            var before = long.MaxValue;
            while (!ct.IsCancellationRequested)
            {
                var page = store.Submissions(current, before, FormsStore.PageSize);
                foreach (var submission in page)
                {
                    var prefix = $"{submission.Id.ToString(Invariant)},{CsvCell(submission.SubmittedUtc)},";
                    if (submission.Answers.Length == 0) await writer.WriteLineAsync((prefix + ",,").AsMemory(), ct);
                    foreach (var answer in submission.Answers)
                        foreach (var choice in answer.Values)
                            await writer.WriteLineAsync(($"{prefix}{answer.QuestionId:N},{CsvCell(answer.Label)},{CsvCell(choice)}").AsMemory(), ct);
                }
                if (page.Count < FormsStore.PageSize) break;
                before = page[^1].Id;
            }
        }, "text/csv; charset=utf-8");
    }

    private const string QuestionHelp = "Give each question a label and a valid type. Choice questions need 2 to 12 distinct options, one per line (up to 100 characters each).";

    private static bool TryQuestion(IFormCollection form, string prefix, Guid id, out FormQuestion? question)
    {
        question = null;
        var rawType = One(form, $"{prefix}-type");
        if (!Enum.TryParse<FormQuestionType>(rawType, out var type) || rawType != type.ToString() || !Enum.IsDefined(type)
            || !TryText(One(form, $"{prefix}-label"), 160, false, out var label) || label.Length == 0
            || One(form, $"{prefix}-options") is not { Length: <= 1500 } rawOptions) return false;
        var required = One(form, $"{prefix}-required");
        if (form.ContainsKey($"{prefix}-required") && required != "true") return false;
        var options = new List<string>();
        if (type is FormQuestionType.SingleChoice or FormQuestionType.MultipleChoice)
        {
            foreach (var line in rawOptions.ReplaceLineEndings("\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!TryText(line, OptionLimit, false, out var option) || options.Contains(option, StringComparer.OrdinalIgnoreCase)) return false;
                options.Add(option);
            }
            if (options.Count is < 2 or > FormsStore.MaxOptions) return false;
        }
        else if (!string.IsNullOrWhiteSpace(rawOptions)) return false;
        question = new(id, label, type, required == "true", options.ToArray());
        return true;
    }

    private static bool TryAnswer(FormQuestion q, string? raw, out string value)
    {
        value = raw ?? "";
        return q.Type switch
        {
            FormQuestionType.ShortText => TryText(raw, FormsStore.ShortTextLimit, false, out value),
            FormQuestionType.LongText => TryText(raw, FormsStore.LongTextLimit, true, out value),
            FormQuestionType.SingleChoice or FormQuestionType.MultipleChoice => value.Length <= OptionLimit && q.Options.Contains(value, StringComparer.Ordinal),
            FormQuestionType.Date => value.Length == 10 && DateOnly.TryParseExact(value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out _),
            FormQuestionType.Number => value.Length <= 64 && !value.Any(c => c is not (>= '0' and <= '9' or '+' or '-' or '.' or 'e' or 'E'))
                && double.TryParse(value, NumberStyles.Float, Invariant, out var number) && double.IsFinite(number),
            _ => false
        };
    }

    private static bool TryDetails(IFormCollection form, out string title, out string description)
    {
        var titleOk = TryText(One(form, "title"), 120, false, out title);
        return TryText(One(form, "description"), 1000, true, out description) && titleOk && title.Length > 0;
    }

    private static bool TryText(string? raw, int limit, bool multiline, out string value)
    {
        value = "";
        if (multiline) raw = raw?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (raw is null || raw.Length > limit || raw.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t'))) return false;
        value = raw.Trim();
        return true;
    }

    private static async Task<(IFormCollection?, IResult?)> ReadPost(HttpContext http, IAntiforgery antiforgery)
    {
        // Only accept URL-encoded bodies; never parse multipart or accept files. Bound the body before antiforgery reads it.
        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            return (null, Results.Text("This form does not accept files or other upload formats.", statusCode: 415));
        if (http.Request.ContentLength > BodyLimit) return (null, Results.Text("Form is too large.", statusCode: 413));
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size && (size.MaxRequestBodySize is null || size.MaxRequestBodySize > BodyLimit))
            size.MaxRequestBodySize = BodyLimit;
        try
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            return await antiforgery.IsRequestValidAsync(http) ? (form, null) : (null, Invalid("Form verification failed. Reload the page and try again."));
        }
        catch (BadHttpRequestException ex) { return (null, Results.Text("Invalid form body.", statusCode: ex.StatusCode)); }
        catch (InvalidDataException) { return (null, Invalid("Invalid form body.")); }
    }

    private static string? One(IFormCollection form, string key) => form[key] is { Count: 1 } values ? values[0] : null;
    private static bool MatchesRevision(IFormCollection form, FormDefinition current) => One(form, "revision") == current.Revision.ToString(Invariant);
    private static IResult Invalid(string message) => Results.Text(message, statusCode: 400);
    private static IResult Unavailable() => Results.Text("Form unavailable.", statusCode: 404);
    private static IResult Changed() => Results.Text("This form has changed or closed. Go back and reload before trying again.", statusCode: 409);
    private static IResult SeeOther(string path) => new FormRedirect(path);

    private static string CsvCell(string value)
    {
        var first = 0;
        while (first < value.Length && (char.IsWhiteSpace(value[first]) || char.IsControl(value[first]) || char.GetUnicodeCategory(value[first]) == UnicodeCategory.Format)) first++;
        if ((value.Length > 0 && value[0] is '\t' or '\r' or '\n') || (first < value.Length && "=+-@＝＋－＠".Contains(value[first]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed record FormRedirect(string Path) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.StatusCode = StatusCodes.Status303SeeOther;
            http.Response.Headers.Location = Path;
            return Task.CompletedTask;
        }
    }

    // Register before shared security middleware so this OnStarting callback runs after its defaults.
    private sealed class FormPrivacyHeaders : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, proceed) =>
            {
                if (http.Request.Path.StartsWithSegments("/forms") || http.Request.Path.StartsWithSegments("/admin/forms"))
                    http.Response.OnStarting(() =>
                    {
                        http.Response.Headers.CacheControl = "no-store";
                        http.Response.Headers["Referrer-Policy"] = "no-referrer";
                        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                        return Task.CompletedTask;
                    });
                await proceed();
            });
            next(app);
        };
    }
}
