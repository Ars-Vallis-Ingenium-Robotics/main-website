namespace AviSite.Web.Content;

/// <summary>
/// Facts pulled from the club's own documents (Nov 2026). Each list notes its source so the
/// next officer can update it. Anything uncertain is listed in docs/CONTENT-TODO.md.
/// </summary>
public static class ClubFacts
{
    public sealed record Person(string Name, string Role, string? Group = null);
    public sealed record Milestone(string When, string Title, string Body, string? Photo = null);
    public sealed record Stat(string Value, string Label);

    // Source: AVI Robotics - Meeting Minutes (10/9/26), officer roll call.
    public static readonly IReadOnlyList<Person> Officers =
    [
        new("Declan Doss", "President"),
        new("Dottie Gunn", "Vice President"),
        new("Jaime Jaurique", "Treasurer"),
        new("Ansh Josan", "Secretary"),
        new("Robert Voss", "MATE CEO"),
    ];

    public static readonly IReadOnlyList<Person> Leads =
    [
        new("Caleb Bahne", "Design & Manufacturing Lead"),
        new("Arshiro Vang", "Programming Lead"),
        new("Sebastien Dupere", "Electrical Lead"),
        new("Santiago Flores", "Safety Lead"),
        new("Matthew Sedano", "Marketing Lead"),
    ];

    // Source: 2026 SMS Technical Documentation title page.
    public static readonly IReadOnlyList<Person> Advisors =
    [
        new("Dr. Alaeddin Bani Milhim", "Faculty Advisor"),
        new("Dr. Shuo Wu", "Faculty Advisor"),
        new("Adam Garabedian", "Mentor, 2026 season"),
    ];

    // Source: 2026 SMS Technical Documentation + Engineering Presentation outline.
    public static readonly IReadOnlyList<Person> SmsCSuite2026 =
    [
        new("Robert Voss", "Chief Executive Officer"),
        new("Timothy Cromwell", "Chief Operating Officer"),
        new("Declan Doss", "Chief Technical Officer"),
        new("Caleb Bahne", "Chief Design Officer"),
        new("Santiago Flores", "Chief Planning & Safety Officer, Deck Management Lead"),
    ];

    // Source: 2026 Projects Day write-up + Technical Documentation sponsor section.
    public static readonly IReadOnlyList<string> Sponsors2026 =
    [
        "Precision Civil Engineering", "MESA Fresno State", "McMillan Electric", "Morningstar Institute",
        "Del Monte Electric Co.", "Harris Construction Co.", "Glencass Signs Co.", "National Air Balance Co.",
    ];

    // Source: Push-In Classroom Pitch (Sept 2026) + Technical Documentation.
    public static readonly IReadOnlyList<Stat> Season2026 =
    [
        new("102/100", "Engineering presentation score, an all-time Pioneer-class record"),
        new("4th", "Highest overall score in the Pioneer class at Worlds"),
        new("42", "Students on the 2026 SMS roster"),
        new("2", "Competition robots built in year one: ROSIE and ADAM"),
    ];

    // Sources: New Member Info Packet, IRS letters, photo archive folder dates, Projects Day doc, meeting minutes.
    public static readonly IReadOnlyList<Milestone> History =
    [
        new("Before AVI", "Where it started",
            "AVI's founders first competed in the MATE ROV competition with Engineering Renaissance's Crush Depth team at Clovis Community College. AVI grew out of that experience and that legacy.",
            "showcase-build"),
        new("Sept 2025", "AVI Robotics is founded",
            "Ars Vallis Ingenium, the Valley of Artistic Ingenuity, starts at Fresno State as a club for every major, not just engineering. The leadership team sets up Sequoia Marine Solutions as the club's MATE ROV company."),
        new("Dec 2025", "Officially a nonprofit",
            "The IRS confirms AVI's tax-exempt status, effective September 2, 2025, so sponsor and donor gifts are tax-deductible."),
        new("Jan 2026", "Building the booth, recruiting the crew",
            "Members build and paint the club booth and recruit at Club Rush. By spring, SMS has more than 40 students across seven subsystems.",
            "traditions-booth"),
        new("Mar to Apr 2026", "First water",
            "ROSIE's prototypes go from PVC frames to aluminum T-slot. After weeks of leak hunting, the April 4 water test is a success, followed by a team jump into the pool.",
            "pool-jump"),
        new("Apr 2026", "Regionals in Monterey",
            "SMS competes at the MATE Monterey regional and takes ROSIE and ADAM to the Lyles College of Engineering Projects Day.",
            "monterey-group"),
        new("June 2026", "World Championship, St. John's",
            "SMS travels to Newfoundland for the MATE ROV World Championship and wins the Engineering Presentation award with 102 out of 100 points, an all-time Pioneer-class record, and the fourth-highest overall Pioneer score.",
            "worlds-award-stage"),
        new("Fall 2026", "Year two",
            "AVI documents how the club runs so new officers don't start from scratch, brings robots to MESA Saturday Academy, moves SMS up to the Explorer class for 2027, and starts exploring a Micromouse team.",
            "mesa-outreach"),
    ];

    public const string MeetingTime = "Fridays, 5:30 to 7:30 PM";
    public const string MeetingRoom = "EE 389, Fresno State";
}
