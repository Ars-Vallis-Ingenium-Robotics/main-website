---
title: Company · Sequoia Marine Solutions
description: How Sequoia Marine Solutions is organized: a C-suite, seven crews and 51 students from every major.
---

<!--
  Sources: 2026 marketing display (company roster with subsystem and major), the January 2026
  "MATE Subsystem Selection" sheet, 2026 technical documentation, "Marketing Display Content" notes,
  SMS Master Timeline, Team & Subsystem Structure doc.
-->

## hero.kicker
Company

## hero.title
Run like a real firm.

## hero.lede
51 students, a C-suite, seven crews with a lead each, and a purchase-request process that goes through design, operations and finance before anything gets ordered.

## leaders.kicker
2026 leadership

## leaders.title
The people in charge.

## leaders.hint
Tap anyone to read what they did this season.

## crews.kicker
Subsystems

## crews.title
Seven crews, one robot.

## crews.hint
Each crew owned one part of ROSIE or ADAM. Tap a crew for its lead, its members and what it built.

## learned.kicker
What we learned

## learned.title
Communication, creativity, autonomy.

## learned.body
From our 2026 company evaluation: with over fifty people involved, structure came first. Then the Chief Design Officer ran idea-generation workshops, which produced the harpoon manipulators. As the year went on, leads handed more ownership to individual members, and the chassis came together from many people's work: thruster shrouds, camera mounts, manipulators.

## learned.button
Join through AVI

<!--
  ### Name, then role: and major:, then a bullet list of what they did.
  TODO(confirm): bios are drawn from roles and credits in the submission documents. Robert to review.
-->
## leadership

### Robert Voss
role: Chief Executive Officer
major: Computer Science

- Co-founded AVI Robotics and set up SMS as its MATE ROV company.
- Ran the company and its relationships with sponsors, Fresno State and MATE.
- Wrote the software libraries the float team built on.

### Timothy Cromwell
role: Chief Operating Officer
major: Computer Science

- Co-founder of AVI.
- Ran operations: the master timeline, Friday progress checks and purchase requests.
- Shot the ROV part photography used across the marketing display.

### Declan Doss
role: Chief Technical Officer
major: Electrical Engineering

- Co-founder of AVI, and AVI's president for 2026–27.
- Led the technical side across the ROV and the float subsystems.

### Caleb Bahne
role: Chief Design Officer
major: Mechanical Engineering

- Ran the idea-generation workshops that produced the harpoon manipulators.
- Designed the manipulator and float (ADAM) CAD.

### Santiago Flores
role: Chief Planning Officer
major: Electrical Engineering

- Owned planning, safety and the deck crew that runs the robot from the pool side.

### Jaime Jaurigue
role: Director of Operations Finance
major: Electrical Engineering

- Ran the money: estimated-expense reports, purchasing and reimbursements, including getting 13 students to Newfoundland.

### Maria Roceo Sy
role: Director of Marketing
major: Computer Engineering

- Led marketing, including the marketing display.

<!--
  ### Crew name, then id: (for its link), lead:, photo:, members: (comma-separated),
  then a one-paragraph summary and a bullet list of what the crew built.
-->
## crews

### ROV Chassis & Propulsion
id: chassis
lead: Dottie Gunn
photo: rosie-poolside
members: Anapaula Mena, Carlos Vidrio Vasquez, Dana Martinez, Jag Dhillon, Jovani Herrera, Leilah Hunt, Natalie Maciel, Robel Gembre, Tony Garcia

The body of ROSIE: an aluminum T-slot frame around the pressure hull, with eight 3D-printed thruster mounts.

- T-slot rail frame that lets anything be mounted and slid into place
- Eight vectored thrusters in 3D-printed PETG mounts and shrouds
- A removable hull: the enclosure clamps pivot open for access to the electronics
- PVC legs for a stable stance on deck

### ROV Manipulators
id: manipulators
lead: Sam Heinrichs
photo: worlds-rosie-station
members: Alex Ellis, Anthony Nichols, Brianna Vidrio, Ethan Kelstrom, Jovani Mendez, Will Junnila

Everything ROSIE grabs with: a rotating claw and four harpoons.

- A claw with a 360-degree rotating wrist, printed in flexible, grippy filament so it doesn't damage specimens
- Four harpoons that pick up samples and rope on the fly

### ROV Controls & Electronics
id: controls
lead: Dalton Cowin
photo: thruster-bench
members: Clarissa Morgan, Earl Orejola, Haroon Ahmad

The ROV's electronics and software: power, motor control and the code the pilots fly.

- A 48 V / 30 A power system
- Motor control for eight thrusters
- Flight-stick piloting with 44 programmable buttons

### GUI & Communications
id: gui
lead: Ansh Josan
photo: pilot-station
members: Guillermo Prado (pilot), Kyle Pham, Nathaniel Garcia

The pilot's screen and the link between the deck and the robot.

- The pilot GUI with camera views
- Controller mapping for pilot and co-pilot
- Photogrammetry: building 3D models from the ROV's cameras
- The tether: power down to the ROV and two-way Ethernet

### Float Chassis & Buoyancy
id: float
lead: Cal Limdren
photo: adam-water
members: Christian Thao, Ethan Fernandez, Jonathan Camarena, Kellen Davis, Lizbet Robledo, Mario Ortiz

ADAM's body and its buoyancy engine: a pump moves water in and out of a bladder, so the float sinks and rises with no thrusters.

- A peristaltic-pump buoyancy engine
- Removable internals on rails for easy service
- Ballast plates to fine-tune mass
- Cold-tested to 2 °C before competition

### Float Controls & Electronics
id: float-electronics
lead: Matthias Cockrum
photo: worlds-float-electronics
members: Andres Jimenez, Ekin Thao, Jonathan Anguiano, Luis Delgado, Wyatt Grogan

ADAM's brain: custom PCBs, sensors and the code for vertical profiles.

- Three generations of custom PCBs designed in KiCad
- Vertical-profiling and collision-avoidance software
- The surface receiver and float GUI

### Deck Management
id: deck
lead: Santiago Flores
photo: sms-pool-pilot
members: Abraham Cortes, Christopher Torres, Evencio Cortes, Robert Jason Capps

The crew on the pool deck: tether handling, safety and running the mission clock.

- Tether management on deck
- Safety procedures and the job safety analysis

<!-- Support roles outside the seven crews. "- Team: Name, Name" -->
## support
- Marketing: Maria Roceo Sy, Manpreet Kaur
- Finance: Jaime Jaurigue
- 3D printing: Julio Diaz
