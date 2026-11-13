using AviSite.Web.Content;

namespace AviSite.Web.Endpoints;

/// <summary>
/// /people.json: everyone named on the site, for the name pop-ups (people.js).
/// Only what members chose to share (content/people/*.md) plus roles the site already shows.
/// </summary>
public static class PeopleEndpoints
{
    public static void MapPeopleEndpoints(this WebApplication app)
    {
        app.MapGet("/people.json", (People people, Copy copy) =>
        {
            var all = people.Directory().ToDictionary(kv => kv.Key, kv =>
            {
                var p = people.Profile(kv.Key);
                var photo = people.PhotoSlug(kv.Key);
                return new
                {
                    name = p?.Name ?? kv.Value.Name,
                    roles = kv.Value.Roles,
                    photo = photo is null ? null : $"/img/people/{photo}-320.webp",
                    pronouns = p?.Pronouns,
                    major = p?.Major,
                    headline = p?.Headline is { } h ? copy.Plain(h) : null,
                    links = p?.Links.Select(l => new { kind = l.Kind, label = l.Label, href = l.Href }),
                    bio = p is { Bio.Length: > 0 } ? copy.Blocks(p.Bio).Value : null,
                };
            });
            return Results.Json(all);
        });
    }
}
