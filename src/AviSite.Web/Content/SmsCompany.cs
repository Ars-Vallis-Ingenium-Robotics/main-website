namespace AviSite.Web.Content;

/// <summary>
/// Sequoia Marine Solutions 2026: leadership bios and the crews (subsystems) with their members.
/// Sources: 2026 marketing display (company roster with subsystem and major), the January 2026
/// "MATE Subsystem Selection" sheet (which crew each software/electrical member joined), 2026 technical
/// documentation, "Marketing Display Content" notes, SMS Master Timeline, Team & Subsystem Structure doc.
/// </summary>
public static class SmsCompany
{
    public sealed record Leader(string Name, string Role, string Major, IReadOnlyList<string> Did);

    public sealed record Crew(string Id, string Name, string Lead, string Summary, IReadOnlyList<string> Built,
        IReadOnlyList<string> Members, string? Photo = null);

    // TODO(confirm): bios are drawn from roles and credits in the submission documents. Robert to review.
    public static readonly IReadOnlyList<Leader> Leadership =
    [
        new("Robert Voss", "Chief Executive Officer", "Computer Science",
            ["Co-founded AVI Robotics and set up SMS as its MATE ROV company.", "Ran the company and its relationships with sponsors, Fresno State and MATE.", "Wrote the software libraries the float team built on."]),
        new("Timothy Cromwell", "Chief Operating Officer", "Computer Science",
            ["Co-founder of AVI.", "Ran operations: the master timeline, Friday progress checks and purchase requests.", "Shot the ROV part photography used across the marketing display."]),
        new("Declan Doss", "Chief Technical Officer", "Electrical Engineering",
            ["Co-founder of AVI, and AVI's president for 2026–27.", "Led the technical side across the ROV and the float subsystems."]),
        new("Caleb Bahne", "Chief Design Officer", "Mechanical Engineering",
            ["Ran the idea-generation workshops that produced the harpoon manipulators.", "Designed the manipulator and float (ADAM) CAD."]),
        new("Santiago Flores", "Chief Planning Officer", "Electrical Engineering",
            ["Owned planning, safety and the deck crew that runs the robot from the pool side."]),
        new("Jaime Jaurigue", "Director of Operations Finance", "Electrical Engineering",
            ["Ran the money: estimated-expense reports, purchasing and reimbursements, including getting 13 students to Newfoundland."]),
        new("Maria Roceo Sy", "Director of Marketing", "Computer Engineering",
            ["Led marketing, including the marketing display."]),
    ];

    public static readonly IReadOnlyList<Crew> Crews =
    [
        new("chassis", "ROV Chassis & Propulsion", "Dottie Gunn",
            "The body of ROSIE: an aluminum T-slot frame around the pressure hull, with eight 3D-printed thruster mounts.",
            ["T-slot rail frame that lets anything be mounted and slid into place", "Eight vectored thrusters in 3D-printed PETG mounts and shrouds", "A removable hull: the enclosure clamps pivot open for access to the electronics", "PVC legs for a stable stance on deck"],
            ["Anapaula Mena", "Carlos Vidrio Vasquez", "Dana Martinez", "Jag Dhillon", "Jovani Herrera", "Leilah Hunt", "Natalie Maciel", "Robel Gembre", "Tony Garcia"],
            "rosie-poolside"),
        new("manipulators", "ROV Manipulators", "Sam Heinrichs",
            "Everything ROSIE grabs with: a rotating claw and four harpoons.",
            ["A claw with a 360-degree rotating wrist, printed in flexible, grippy filament so it doesn't damage specimens", "Four harpoons that pick up samples and rope on the fly"],
            ["Alex Ellis", "Anthony Nichols", "Brianna Vidrio", "Ethan Kelstrom", "Jovani Mendez", "Will Junnila"],
            "worlds-rosie-station"),
        new("controls", "ROV Controls & Electronics", "Dalton Cowin",
            "The ROV's electronics and software: power, motor control and the code the pilots fly.",
            ["A 48 V / 30 A power system", "Motor control for eight thrusters", "Flight-stick piloting with 44 programmable buttons"],
            ["Clarissa Morgan", "Earl Orejola", "Haroon Ahmad"],
            "thruster-bench"),
        new("gui", "GUI & Communications", "Ansh Josan",
            "The pilot's screen and the link between the deck and the robot.",
            ["The pilot GUI with camera views", "Controller mapping for pilot and co-pilot", "Photogrammetry: building 3D models from the ROV's cameras", "The tether: power down to the ROV and two-way Ethernet"],
            ["Guillermo Prado (pilot)", "Kyle Pham", "Nathaniel Garcia"],
            "pilot-station"),
        new("float", "Float Chassis & Buoyancy", "Cal Limdren",
            "ADAM's body and its buoyancy engine: a pump moves water in and out of a bladder, so the float sinks and rises with no thrusters.",
            ["A peristaltic-pump buoyancy engine", "Removable internals on rails for easy service", "Ballast plates to fine-tune mass", "Cold-tested to 2 °C before competition"],
            ["Christian Thao", "Ethan Fernandez", "Jonathan Camarena", "Kellen Davis", "Lizbet Robledo", "Mario Ortiz"],
            "adam-water"),
        new("float-electronics", "Float Controls & Electronics", "Matthias Cockrum",
            "ADAM's brain: custom PCBs, sensors and the code for vertical profiles.",
            ["Three generations of custom PCBs designed in KiCad", "Vertical-profiling and collision-avoidance software", "The surface receiver and float GUI"],
            ["Andres Jimenez", "Ekin Thao", "Jonathan Anguiano", "Luis Delgado", "Wyatt Grogan"],
            "worlds-float-electronics"),
        new("deck", "Deck Management", "Santiago Flores",
            "The crew on the pool deck: tether handling, safety and running the mission clock.",
            ["Tether management on deck", "Safety procedures and the job safety analysis"],
            ["Abraham Cortes", "Christopher Torres", "Evencio Cortes", "Robert Jason Capps"],
            "sms-pool-pilot"),
    ];

    /// <summary>Support roles outside the seven crews.</summary>
    public static readonly IReadOnlyList<(string Team, IReadOnlyList<string> Members)> Support =
    [
        ("Marketing", ["Maria Roceo Sy", "Manpreet Kaur"]),
        ("Finance", ["Jaime Jaurigue"]),
        ("3D printing", ["Julio Diaz"]),
    ];

    public static string Slug(string name) =>
        string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
