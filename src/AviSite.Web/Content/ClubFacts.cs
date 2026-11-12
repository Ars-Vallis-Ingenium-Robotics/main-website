namespace AviSite.Web.Content;

/// <summary>
/// Facts pulled from the club's own documents (Nov 2026). Each list notes its source so the
/// next officer can update it. Anything uncertain is listed in docs/CONTENT-TODO.md.
/// </summary>
public static class ClubFacts
{
    public sealed record Person(string Name, string Role, string? Group = null)
    {
        /// <summary>File stem under wwwroot/img/people (built by tools/images/build_people.py).</summary>
        public string Slug => string.Join('-', Name.Replace("Dr. ", "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <param name="More">Optional detail paragraphs shown when the timeline entry is expanded.</param>
    public sealed record Milestone(string When, string Title, string Body, string? Photo = null, IReadOnlyList<string>? More = null, string? MorePhoto = null);
    public sealed record OrgNode(Person Person, IReadOnlyList<OrgNode>? Reports = null);
    public sealed record Stat(string Value, string Label);

    // Source: AVI Robotics - Meeting Minutes (10/9/26), officer roll call.
    public static readonly IReadOnlyList<Person> Officers =
    [
        new("Declan Doss", "President"),
        new("Dottie Gunn", "Vice President"),
        new("Jaime Jaurigue", "Treasurer"),
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
    ];

    // Source: Robert Voss (Nov 2026). Adam renewed for 2027; Dalton joins for 2026–27.
    public static readonly IReadOnlyList<Person> Mentors =
    [
        new("Adam Garabedian", "Mentor · 2026 and 2027 seasons"),
        new("Dalton Cowan", "Mentor · 2027 season"),
    ];

    // Source: Robert Voss (Nov 2026).
    public static readonly IReadOnlyList<Person> Founders =
    [
        new("Robert Voss", "Co-founder"),
        new("Declan Doss", "Co-founder"),
        new("Timothy Cromwell", "Co-founder"),
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

    // Sources: "Team & Subsystem Structure" (Sept 2025), Engineering Presentation outline,
    // 2026 Technical Documentation lead labels. TODO(confirm) with Robert.
    public static readonly OrgNode SmsOrg2026 =
        new(new("Robert Voss", "Chief Executive Officer"),
        [
            new(new("Timothy Cromwell", "Chief Operating Officer"),
            [
                new(new("Jaime Jaurigue", "Chief Financial Officer")),
                new(new("Maria Roceo Sy", "Chief Marketing Officer")),
                new(new("Santiago Flores", "Chief Planning & Safety Officer · Deck Management")),
            ]),
            new(new("Declan Doss", "Chief Technical Officer")),
            new(new("Caleb Bahne", "Chief Design Officer")),
        ]);

    /// <summary>Subsystem leads, who report to the CTO and CDO.</summary>
    public static readonly IReadOnlyList<Person> SmsLeads2026 =
    [
        new("Dottie Gunn", "Mechanical Lead", "ROV Chassis & Propulsion"),
        new("Sam Heinrichs", "Mechanical Lead", "ROV Manipulators"),
        new("Ansh Josan", "Electrical Lead", "ROV Controls & Electronics · GUI & Comms"),
        new("Dalton Cowan", "Programming Lead", "ROV software"),
        new("Cal Limdren", "Mechanical Lead", "Float Chassis & Buoyancy"),
        new("Matthias Cockrum", "Electrical Lead", "Float Controls & Electronics"),
    ];

    public sealed record Sponsor(string Name, string Logo, int Tier, int W, int H);

    // Source: "final sponsorship shirt back.png" (2026 Worlds shirt). Tier = size on the shirt:
    // 1 headline, 2 major, 3 partner, 4 supporter. Logos: tools/images/build_sponsors.py.
    public static readonly IReadOnlyList<Sponsor> Sponsors2026 =
    [
        new("Precision Civil Engineering", "precision", 1, 900, 260),
        new("Fresno State", "fresno-state", 2, 852, 360),
        new("MESA", "mesa", 2, 735, 360),
        new("IEEE", "ieee", 3, 831, 240),
        new("Schmidt Ocean Coalition", "schmidt-ocean", 3, 900, 187),
        new("McMillan Electric", "mcmillan", 3, 777, 239),
        new("Lyles College of Engineering", "lyles", 3, 378, 311),
        new("MATE ROV Competition", "mate", 3, 638, 305),
        new("Morning Star Institute", "morning-star", 3, 860, 263),
        new("Longroad Energy", "longroad", 3, 798, 177),
        new("Harris Construction", "harris", 4, 393, 247),
        new("Sigler Wholesale Distributors", "sigler", 4, 465, 189),
        new("Aqua Sports", "aqua-sports", 4, 404, 255),
        new("AC Electrical Construction", "ac-electrical", 4, 279, 279), // TODO(confirm) exact name
        new("Del Monte Electric Co.", "del-monte", 4, 793, 230),
        new("Roll-Em-Up Taquitos", "roll-em-up", 4, 275, 288),
        new("National Air Balance Co.", "national-air", 4, 900, 71),
        new("Glencass Signs Co.", "glencass", 4, 807, 84),
        new("Fowler Packaging", "fowler", 4, 748, 85),
    ];

    // Source: Push-In Classroom Pitch (Sept 2026) + Technical Documentation.
    public static readonly IReadOnlyList<Stat> Season2026 =
    [
        new("102/100", "Engineering presentation score, an all-time Pioneer-class record"),
        new("4th", "Highest overall score in the Pioneer class at Worlds"),
        new("51", "Students on the 2026 SMS roster"),
        new("2", "Competition robots built in year one: ROSIE and ADAM"),
    ];

    // Sources: New Member Info Packet, IRS letters, photo archive folder dates, Projects Day doc, meeting minutes.
    public static readonly IReadOnlyList<Milestone> History =
    [
        new("Before AVI", "Where it started",
            "AVI's founders first competed in the MATE ROV competition with Engineering Renaissance's Crush Depth team at Clovis Community College. AVI grew out of that experience and that legacy.",
            More: ["Robert Voss, Declan Doss and Timothy Cromwell brought that experience to Fresno State and founded AVI together."]),
        new("Sept 2025", "AVI Robotics is founded",
            "Ars Vallis Ingenium, the Valley of Artistic Ingenuity, starts at Fresno State as a club for every major, not just engineering. The leadership team sets up Sequoia Marine Solutions as the club's MATE ROV company.",
            "dec-meeting",
            More: ["The founders wrote the first team and subsystem structure that September: a C-suite, seven subsystems and a lead for each one."]),
        new("Dec 2025", "Officially a nonprofit",
            "The IRS confirms AVI's tax-exempt status, effective September 2, 2025, so sponsor and donor gifts are tax-deductible.",
            More: ["Becoming a 501(c)(3) in our first semester meant companies could sponsor us properly. Look at the back of our Worlds shirt to see how that went."]),
        new("Jan 2026", "Building the booth, recruiting the crew",
            "Members build and paint the club booth and recruit at Club Rush. By spring, 51 students are on the SMS roster across seven subsystems.",
            "jan-meeting",
            More: ["At the January 30 general meeting we thanked our first sponsors in front of the whole club."]),
        new("Feb 2026", "First MESA volunteering day",
            "AVI volunteers at a MESA event at Fresno State, the first of many. Outreach has been part of the club ever since.",
            "mesa-feb",
            More: ["We went back in April and again this fall, and we now bring robots to MESA Saturday Academy."]),
        new("Mar to Apr 2026", "First water",
            "ROSIE's prototypes go from PVC frames to aluminum T-slot. After weeks of leak hunting, the April 4 water test is a success, followed by a team jump into the pool.",
            "pool-jump",
            More: ["Three water tests in late March found leak after leak. On April 4, ROSIE stayed dry."], MorePhoto: "pool-proto"),
        new("Apr 2026", "Regionals in Monterey",
            "SMS competes at the MATE Monterey regional and takes ROSIE and ADAM to the Lyles College of Engineering Projects Day.",
            "monterey-group",
            More: ["At Projects Day on April 29, we showed the Lyles College of Engineering what a first-year club had built."], MorePhoto: "projects-day-rosie"),
        new("June 4, 2026", "Pool day with special guests",
            "Three weeks before Worlds, SMS ran ROSIE and ADAM at the Fresno State pool. Fresno State's president, the dean of the Lyles College of Engineering and the owner of Precision Civil Engineering came out to watch.",
            "pool-guests",
            More: ["Guests watched ROSIE dive from the deck and saw the team lie flat on the concrete to lower ADAM into the water."], MorePhoto: "pool-adam-deploy"),
        new("June 2026", "World Championship, St. John's",
            "SMS travels to Newfoundland for the MATE ROV World Championship and wins the Engineering Presentation award with 102 out of 100 points, an all-time Pioneer-class record, and the fourth-highest overall Pioneer score.",
            "worlds-award-stage",
            More: ["Thirteen students and two robots made the trip. Day three was the float deployment and the wave tank."], MorePhoto: "worlds-team-rosie"),
        new("Fall 2026", "Year two",
            "A new officer board, workshops every Friday, and robots at MESA Saturday Academy. SMS moves up to the Explorer class for 2027 and a Micromouse team starts forming.",
            "mesa-outreach",
            More: ["We wrote down how the club runs so new officers don't start from scratch.", "We also built a booth for the Phantom Bash."], MorePhoto: "phantom-booth"),
    ];

    public const string MeetingTime = "Fridays, 5:30 to 7:30 PM";
    public const string MeetingRoom = "EE 389, Fresno State";
}
