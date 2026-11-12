namespace AviSite.Web.Content;

/// <summary>
/// Facts pulled from the club's own documents (Nov 2026). Each list notes its source so the
/// next officer can update it. Anything uncertain is listed in docs/CONTENT-TODO.md.
/// </summary>
public static class ClubFacts
{
    public sealed record Person(string Name, string Role, string? Group = null, string? Email = null)
    {
        /// <summary>File stem under wwwroot/img/people (built by tools/images/build_people.py).</summary>
        public string Slug => string.Join('-', Name.Replace("Dr. ", "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>One thing that happened inside a timeline block (shown when the block is expanded).</summary>
    public sealed record Moment(string Date, string Title, string? Body = null, string? Photo = null, string? Caption = null);

    /// <param name="Moments">A curated view of what happened inside this block of time.</param>
    public sealed record Milestone(string When, string Title, string Body, string? Photo = null, IReadOnlyList<Moment>? Moments = null);
    public sealed record Stat(string Value, string Label);

    // Source: AVI Robotics - Meeting Minutes (10/9/26), officer roll call. Role addresses: Robert (Nov 2026).
    public static readonly IReadOnlyList<Person> Officers =
    [
        new("Declan Doss", "President", Email: "president@avirobotics.org"),
        new("Dottie Gunn", "Vice President", Email: "vicepresident@avirobotics.org"),
        new("Jaime Jaurigue", "Treasurer", Email: "treasurer@avirobotics.org"),
        new("Ansh Josan", "Secretary", Email: "secretary@avirobotics.org"),
        new("Robert Voss", "MATE CEO", Email: "ceo@sms.avirobotics.org"),
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
        new("Dalton Cowin", "Mentor · 2027 season"),
    ];

    // Source: Robert Voss (Nov 2026).
    public static readonly IReadOnlyList<Person> Founders =
    [
        new("Robert Voss", "Co-founder", Email: "robert.a.voss@avirobotics.org"),
        new("Declan Doss", "Co-founder", Email: "declan.doss@avirobotics.org"),
        new("Timothy Cromwell", "Co-founder", Email: "timothy.cromwell@avirobotics.org"),
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
        new("A-C Electric Company", "ac-electrical", 4, 279, 279),
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
    // Sources: SMS Master Timeline, Team & Subsystem Structure doc, IRS letters, BCP rules doc,
    // sponsorship packet, photo archive folder dates, Robert (Nov 2026).
    public static readonly IReadOnlyList<Milestone> History =
    [
        new("Before AVI", "Where it started",
            "AVI's founders first competed in MATE ROV with Engineering Renaissance's Crush Depth team at Clovis Community College. Robert Voss, Declan Doss and Timothy Cromwell brought that experience to Fresno State."),
        new("Fall 2025", "AVI Robotics is founded",
            "Ars Vallis Ingenium, the Valley of Artistic Ingenuity, starts at Fresno State as a club for every major. The fall goes into training: technical skills, documentation habits, marketing and teamwork.",
            "fall-2025-group",
            [
                new("Sept 2, 2025", "Officially a nonprofit (effective date)", "The IRS later confirmed AVI's 501(c)(3) status back to this date."),
                new("Sept 2025", "The company takes shape", "The founders write the first team and subsystem structure for Sequoia Marine Solutions, AVI's MATE ROV company: a C-suite, subsystems and a lead for each.", "fall-2025-leads", "The first team leads."),
                new("Oct 10 – Nov 7, 2025", "The first Blank Check Project", "Teams get four weeks to design and build a themed, interactive display with moving parts, electronics and code. (Blank check, because we're broke.)"),
                new("Dec 2025", "IRS letter arrives", "Tax-exempt status confirmed, so sponsor and donor gifts are tax-deductible.", "dec-meeting", "The last general meeting of the semester."),
            ]),
        new("Jan – Feb 2026", "Building the booth, recruiting the crew",
            "Members build, paint and install the club booth, recruit at Club Rush, and SMS starts its build season with 51 students on the roster.",
            "booth-installed",
            [
                new("Jan 10 – 17", "Booth building and painting", "Two build days and a painting day produce the Ars Vallis Ingenium booth."),
                new("Jan 19", "Booth installed", null, "booth-installed"),
                new("Jan 30", "Thanking our first sponsors", "At the general meeting we put our first sponsors on the big screen.", "jan-meeting"),
                new("Feb 7", "First MESA volunteering day", "The first of many MESA events.", "mesa-feb"),
                new("Feb 2026", "Parts ordered, prototypes built", "ROV and float electronics are ordered, the tether is finished, and the first chassis and manipulator prototypes come together."),
            ]),
        new("Mar – Apr 2026", "First water",
            "The robot gets wet for the first time. Weeks of leak hunting end with a successful water test on April 4. In between, we volunteer at two runs and MESA.",
            "first-water",
            [
                new("Mar 14", "Pi Day Run", "Tabling and volunteering at the Pi Day Run.", "pi-day-demo"),
                new("Mar 21", "First water test", "The PVC prototype goes in the pool for the first time, and someone has to go in after it.", "first-water-night"),
                new("Mar 28", "UHC Foundation Fun Run", "Showing an early ROV to runners and families at the UHC Foundation's 2026 Fun Run.", "uhc-crowd"),
                new("Apr 4", "It stays dry", "The water test that finally works.", "pool-proto"),
                new("Apr 11", "MESA volunteering", null, "mesa-apr"),
            ]),
        new("Apr 2026", "Volunteering at the Monterey regional",
            "We didn't compete at the MATE Monterey regional. We went to help run it: judging and helping the organizers keep the day moving.",
            "monterey-group",
            [
                new("Apr 25", "MATE Monterey regional", "AVI members volunteer as judges and event helpers.", "monterey-volunteers"),
                new("Apr 29", "Lyles College Projects Day", "ROSIE and ADAM on display for the College of Engineering.", "projects-day-rosie"),
            ]),
        new("May – June 2026", "Final prep and a pool day with guests",
            "Pilots are chosen, the technical documentation goes in, and on June 4 we run both robots at the Fresno State pool in front of special guests: Fresno State's president, the dean of the Lyles College of Engineering and the owner of Precision Civil Engineering.",
            "pool-day-crowd",
            [
                new("May 2 – 3", "Pilot selection and flight sticks", "Two water tests pick the pilots and tune the controls."),
                new("May 21", "Technical documentation submitted"),
                new("June 4", "Pool day with special guests", "Guests watched ROSIE dive and the team lower ADAM into the water.", "pool-guests", "Robert Voss with water polo coach Ed Dunkel and Ashley Stiles."),
                new("June 4", "Deploying ADAM", null, "pool-adam-deploy"),
            ]),
        new("June 2026", "World Championship, St. John's",
            "Thirteen students and two robots fly to Newfoundland. SMS wins the Engineering Presentation award with 102 out of 100 points, an all-time Pioneer-class record, and posts the fourth-highest overall Pioneer score.",
            "worlds-award-stage",
            [
                new("June 22", "Arrival and check-in", null, "worlds-checkin"),
                new("June 23 – 24", "Safety inspections and the marketing display", null, "worlds-display"),
                new("June 25 – 27", "Competition days", "Float deployment, the wave tank and the engineering presentation.", "worlds-rosie-feed"),
                new("June 27", "102 out of 100", "The Engineering Presentation award.", "worlds-award-screen"),
                new("June 27", "Three California teams, one stage", "Sequoia Marine Solutions, Clovis Community College's Crush Depth and Cal Poly Pomona's Pomona Subsea Initiative together on stage when Pomona took second place in the Pioneer class.", "worlds-stage-friends"),
                new("June 2026", "The team in St. John's", null, "worlds-backdrop"),
            ]),
        new("Fall 2026", "Year two",
            "A new officer board, workshops every Friday, a second Blank Check Project, more outreach, and our first trophy on campus: we won the club booth decorating contest. SMS moves up to the Explorer class for 2027 and a Micromouse team starts forming.",
            "fall-group-2026",
            [
                new("Sept 2", "Traditions Day", "Recruiting with ROSIE on the table.", "traditions-booth"),
                new("Oct 3", "MESA Saturday Academy", null, "mesa-outreach"),
                new("Oct 9", "Club photo", "About fifty of us in front of the Lyles College of Engineering.", "fall-group-2026"),
                new("Oct 16 – Nov 13", "Blank Check Project, round two", "New members learn by building in teams, with a technical lead for each group.", "bcp-build"),
                new("Fall 2026", "We won the booth decorating contest", "Our Ars Vallis Ingenium booth took first place in the club booth decorating contest.", "phantom-booth"),
                new("Oct 30", "Phantom Bash", "Costumes, a decorated booth and a Blank Check Project work night.", "phantom-costumes"),
                new("Nov 7", "MESA, with Sundog", null, "mesa-nov"),
            ]),
    ];

    // Source: "Sequoia Marine Solutions Master Timeline" (2026 build timeline), photo archive, Robert.
    public static readonly IReadOnlyList<Milestone> SmsSeason2026 =
    [
        new("Fall 2025", "Company formed",
            "An open forum settles the structure: a C-suite and subsystems, each with a lead. Members try every subsystem before choosing.",
            "fall-2025-group",
            [
                new("Sept 2025", "Structure written", "CEO, COO and CTO at the top; financing, marketing and deck management; ROV and float subsystems underneath.", "fall-2025-leads"),
                new("Fall 2025", "Research and idea workshops", "A study of past MATE teams and the Chief Design Officer's idea-generation workshops, which produced the harpoon manipulators."),
            ]),
        new("Jan – Feb 2026", "Design and prototypes",
            "Every subsystem works to a dated master timeline with reports due on Mondays and progress checks every Friday.",
            "pvc-frame",
            [
                new("Jan 16", "Spring semester starts"),
                new("Jan 26", "Subsystem timelines due"),
                new("Feb 2 – 7", "Electronics selected and ordered", "For both the ROV and the float, after an estimated-expenses report."),
                new("Feb 16", "Tether finished"),
                new("Feb 23", "First prototypes due", "Chassis and propulsion, manipulators, ROV electronics and the competition props."),
            ]),
        new("Mar – Apr 2026", "Water testing",
            "Leak hunting with sponges inside the pressure hull, a one-week saltwater corrosion test on the T-slot rail, then a successful April 4 water test and qualification practice.",
            "first-water",
            [
                new("Mar 9", "ROV GUI finished"),
                new("Mar 16", "Chassis, manipulator and float hardware finished"),
                new("Mar 21", "First water test", null, "first-water-night"),
                new("Apr 4", "Successful water test", null, "pool-proto"),
                new("Apr 10 – 24", "Qualification practice", "Practice runs for the qualification video."),
            ]),
        new("Apr 25", "Monterey regional, as volunteers",
            "SMS didn't compete in Monterey. Members volunteered as judges and helped the organizers run the event.",
            "monterey-group",
            [ new("Apr 25", "Judging and helping out", null, "monterey-volunteers") ]),
        new("May 2026", "Pilots, GUI and documentation",
            "Pilots trained on ThrustMaster flight sticks with 44 programmable buttons. The full GUI came together and the technical documentation was submitted.",
            "worlds-rosie-station",
            [
                new("May 2 – 3", "Pilot selection and flight-stick testing", "Candidate pilots fly ROSIE in a backyard pool.", "rosie-backyard"),
                new("May 4", "Tether counter and float GUI finished"),
                new("May 11", "Full GUI finished"),
                new("May 21", "Technical documentation submitted"),
            ]),
        new("June 4", "Pool test with guests",
            "A full run of ROSIE and ADAM at the Fresno State pool, with Fresno State's president, the Lyles College dean and Precision Civil Engineering's owner watching from the deck.",
            "pool-adam-deploy",
            [
                new("June 4", "ROSIE dives", null, "pool-rosie-dive"),
                new("June 4", "Under the surface", null, "rosie-deep"),
                new("June 4", "Guests under the tent", null, "pool-day-crowd"),
                new("June 4", "ADAM goes in", "Lowering the float into the water by hand.", "adam-water"),
            ]),
        new("June 17", "Joint showcase with Crush Depth",
            "One last run before Worlds, alongside Crush Depth, the Clovis Community College team where AVI's founders started.",
            "rosie-in-pool",
            [
                new("June 17", "ROSIE in the water", null, "sms-underwater"),
                new("June 17", "On deck", null, "rosie-poolside"),
                new("June 17", "Both teams", "SMS and Crush Depth together at the Fresno State pool.", "showcase-group"),
            ]),
        new("June 22 – 28", "World Championship, St. John's",
            "Thirteen students and two robots fly from LAX to St. John's, Newfoundland.",
            "worlds-team-rosie",
            [
                new("June 22", "Travel and check-in", null, "worlds-checkin"),
                new("June 23 – 24", "Safety inspections", null, "worlds-display"),
                new("June 25 – 27", "Competition days", null, "worlds-wavetank"),
                new("June 27", "Engineering Presentation award, 102/100", null, "worlds-award-stage"),
                new("June 27", "Three California teams, one stage", "Sequoia Marine Solutions, Clovis Community College's Crush Depth and Cal Poly Pomona's Pomona Subsea Initiative together on stage when Pomona took second place in the Pioneer class.", "worlds-stage-friends"),
                new("June 28", "Home to Fresno", null, "worlds-backdrop"),
            ]),
    ];

    /// <summary>The MATE ROV Competition (our competition).</summary>
    public const string MateUrl = "https://materovcompetition.org/";
    /// <summary>Crush Depth has no site of its own yet, so link its home: Clovis Community College engineering. Source: Robert (Nov 2026).</summary>
    public const string CrushDepthUrl = "https://cloviscollege.edu/academics/guided-pathways/stem-computer-science/engineering.html";

    public const string MeetingTime = "Fridays, 5:30 to 7:30 PM";
    public const string MeetingRoom = "EE 389, Fresno State";
}
