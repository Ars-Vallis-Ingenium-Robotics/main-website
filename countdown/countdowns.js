/*
  =========================
  SIMPLE PAGE CONFIG
  =========================
*/

// How often the AVI / Sequoia header swaps
const DIVISION_SWAP_INTERVAL_MS = 4500;

// Fade timing for logo/name swap
const HEADER_FADE_DURATION_MS = 500;

// How often countdown numbers update
const COUNTDOWN_TICK_INTERVAL_MS = 1000;

// How often cards fully re-render/re-sort/re-state
const CARD_RERENDER_INTERVAL_MS = 60000;

// When an in-progress event should change to "Ending Soon"
const ENDING_SOON_THRESHOLD_MS = 24 * 60 * 60 * 1000; // 24 hours

// Show completed events at the bottom
const KEEP_COMPLETED_EVENTS_VISIBLE = true;


/*
  =========================
  DIVISION CONFIG
  Define header info for each division here.
  =========================
*/

const divisions = [
  {
    footerLabel: "AVI Robotics",
    heroText: "Ars Vallis Ingenium (AVI) Robotics",
    alt: "Ars Vallis Ingenium Robotics logo",
    src: "../Logos/aviDogDefault.png",
  },
  {
    footerLabel: "Sequoia Marine Solutions",
    heroText: "Sequoia Marine Solutions",
    alt: "Sequoia Marine Solutions logo",
    src: "../Logos/smsLogo.png",
  },
];


/*
  =========================
  EVENT TYPE CONFIG
  Define all type styling here.
  =========================
*/

const EVENT_TYPES = {
  testing: {
    defaultTag: "Testing",
    colors: {
      bgTop: "rgba(34,197,94,.18)",
      bgBottom: "rgba(74,222,128,.10)",
      accent: "rgba(74,222,128,.70)",
      accentSoft: "rgba(34,197,94,.18)",
      tagBg: "rgba(74,222,128,.15)",
      tagBorder: "rgba(74,222,128,.28)",
    },
  },

  competition: {
    defaultTag: "Competition",
    colors: {
      bgTop: "rgba(59,130,246,.18)",
      bgBottom: "rgba(96,165,250,.10)",
      accent: "rgba(96,165,250,.70)",
      accentSoft: "rgba(59,130,246,.18)",
      tagBg: "rgba(96,165,250,.15)",
      tagBorder: "rgba(96,165,250,.28)",
    },
  },

  deadline: {
    defaultTag: "Deadline",
    colors: {
      bgTop: "rgba(245,158,11,.20)",
      bgBottom: "rgba(251,191,36,.10)",
      accent: "rgba(251,191,36,.72)",
      accentSoft: "rgba(245,158,11,.20)",
      tagBg: "rgba(251,191,36,.16)",
      tagBorder: "rgba(251,191,36,.30)",
    },
  },

  worlds: {
    defaultTag: "Worlds",
    colors: {
      bgTop: "rgba(168,85,247,.20)",
      bgBottom: "rgba(192,132,252,.10)",
      accent: "rgba(192,132,252,.72)",
      accentSoft: "rgba(168,85,247,.20)",
      tagBg: "rgba(192,132,252,.16)",
      tagBorder: "rgba(192,132,252,.30)",
    },
  },

  fundraiser: {
    defaultTag: "Fundraiser",
    colors: {
      bgTop: "rgba(236,72,153,.18)",
      bgBottom: "rgba(244,114,182,.10)",
      accent: "rgba(244,114,182,.72)",
      accentSoft: "rgba(236,72,153,.20)",
      tagBg: "rgba(244,114,182,.16)",
      tagBorder: "rgba(244,114,182,.30)",
    },
  },

  default: {
    defaultTag: "Event",
    colors: {
      bgTop: "rgba(125,211,252,.16)",
      bgBottom: "rgba(167,139,250,.08)",
      accent: "rgba(255,255,255,.40)",
      accentSoft: "rgba(255,255,255,.10)",
      tagBg: "rgba(255,255,255,.10)",
      tagBorder: "rgba(255,255,255,.14)",
    },
  },
};


/*
  =========================
  EVENTS
  datetime = start time
  endDatetime = optional end time
  duration = optional manual override
  link/linkLabel = optional hyperlink
  =========================
*/

const events = [
  {
    title: "Panda Express Fundraiser",
    type: "fundraiser",
    timezoneLabel: "Pacific",
    datetime: "2026-04-09T08:00:00-07:00",
    endDatetime: "2026-04-09T17:00:00-07:00",
    displayDate: "Apr 9, 2026 · 8:00 AM - 5:00 PM Pacific",
    location: "Panda Express, University Student Union, Fresno State",
    link: "../Files/placeholderPdf.pdf",
    linkLabel: "Fundraiser Flyer",
  },
  {
    title: "Water Testing",
    type: "testing",
    timezoneLabel: "Pacific",
    datetime: "2026-04-04T11:00:00-07:00",
    endDatetime: "2026-04-04T23:00:00-07:00",
    displayDate: "Apr 4, 2026 · 11:00 AM - 11:00 PM Pacific",
    location: "CEO's House, Fresno, CA",
  },
  {
    title: "Monterey Regional",
    type: "competition",
    timezoneLabel: "Pacific",
    datetime: "2026-04-25T09:00:00-07:00",
    endDatetime: "2026-04-27T17:00:00-07:00",
    displayDate: "Apr 25, 2026 · 9:00 AM Pacific",
    location: "Watsonville, CA",
  },
  {
    title: "Tech Doc Submissions",
    type: "deadline",
    timezoneLabel: "HST",
    datetime: "2026-05-21T03:00:00-10:00",
    displayDate: "May 21, 2026 · 3:00 AM HST",
    location: "Online",
  },
  {
    title: "MATE ROV World Championship",
    type: "worlds",
    timezoneLabel: "Newfoundland local",
    datetime: "2026-06-23T08:00:00-02:30",
    endDatetime: "2026-06-28T18:00:00-02:30",
    displayDate: "Jun 23, 2026 · 8:00 AM local time",
    location: "Newfoundland, Canada",
  },

  /*
  Example fundraiser:
  {
    title: "Spring Fundraiser Ends",
    type: "fundraiser",
    timezoneLabel: "Pacific",
    datetime: "2026-04-20T23:59:00-07:00",
    displayDate: "Apr 20, 2026 · 11:59 PM Pacific",
    location: "Online",
    link: "https://example.com/fundraiser",
    linkLabel: "Open fundraiser",
  }
  */

  /*
  Example manual duration override:
  {
    title: "Team Dinner",
    type: "competition",
    timezoneLabel: "Pacific",
    datetime: "2026-04-24T18:00:00-07:00",
    endDatetime: "2026-04-24T20:30:00-07:00",
    duration: "About 2.5 hours",
    displayDate: "Apr 24, 2026 · 6:00 PM Pacific",
    location: "Watsonville, CA",
  }
  */
];


/*
  =========================
  DOM REFERENCES
  =========================
*/

const heroLogo = document.getElementById("heroLogo");
const heroClubText = document.getElementById("heroClubText");
const heroFooterLabel = document.getElementById("heroFooterLabel");
const countdownGrid = document.getElementById("countdownGrid");

let clubIndex = 0;

const prefersReducedMotion =
  window.matchMedia &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;


/*
  =========================
  HEADER SWAP HELPERS
  =========================
*/

function preloadClubLogos() {
  divisions.forEach((club) => {
    const img = new Image();
    img.src = club.src;
  });
}

function setClub(index) {
  const club = divisions[index];
  heroLogo.src = club.src;
  heroLogo.alt = club.alt;
  heroClubText.textContent = club.heroText;
  heroFooterLabel.textContent = club.footerLabel;
}

function swapClub() {
  if (prefersReducedMotion) {
    clubIndex = (clubIndex + 1) % divisions.length;
    setClub(clubIndex);
    return;
  }

  heroLogo.classList.add("fading");
  heroClubText.classList.add("fading");

  setTimeout(() => {
    clubIndex = (clubIndex + 1) % divisions.length;
    setClub(clubIndex);

    requestAnimationFrame(() => {
      heroLogo.classList.remove("fading");
      heroClubText.classList.remove("fading");
    });
  }, HEADER_FADE_DURATION_MS);
}


/*
  =========================
  GENERIC HELPERS
  =========================
*/

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function formatPlural(value, singular, plural) {
  return value === 1 ? singular : plural;
}


/*
  =========================
  EVENT TIME HELPERS
  =========================
*/

function getEventStartTime(event) {
  return new Date(event.datetime).getTime();
}

function getEventEndTime(event) {
  if (!event.endDatetime) return null;
  const end = new Date(event.endDatetime).getTime();
  return Number.isNaN(end) ? null : end;
}

function getSortTime(event) {
  return getEventStartTime(event);
}


/*
  =========================
  EVENT TYPE / STYLE HELPERS
  =========================
*/

function getEventTypeConfig(type) {
  return EVENT_TYPES[type] || EVENT_TYPES.default;
}

function getEventTag(event) {
  if (event.tag && event.tag.trim()) {
    return event.tag;
  }
  return getEventTypeConfig(event.type).defaultTag;
}

function getEventStyleVars(event) {
  const { colors } = getEventTypeConfig(event.type);

  return [
    `--event-bg-top: ${colors.bgTop}`,
    `--event-bg-bottom: ${colors.bgBottom}`,
    `--event-accent: ${colors.accent}`,
    `--event-accent-soft: ${colors.accentSoft}`,
    `--event-tag-bg: ${colors.tagBg}`,
    `--event-tag-border: ${colors.tagBorder}`,
  ].join("; ");
}


/*
  =========================
  EVENT STATE HELPERS
  States:
  - upcoming
  - live
  - ending-soon
  - complete
  - invalid
  =========================
*/

function getEventState(event, now = Date.now()) {
  const start = getEventStartTime(event);
  const end = getEventEndTime(event);

  if (Number.isNaN(start)) {
    return {
      state: "invalid",
      label: "Invalid Date",
      badgeClass: "complete",
    };
  }

  if (now < start) {
    return {
      state: "upcoming",
      label: "Upcoming",
      badgeClass: "",
    };
  }

  if (!end) {
    return {
      state: "complete",
      label: "Passed",
      badgeClass: "complete",
    };
  }

  if (now >= end) {
    return {
      state: "complete",
      label: "Complete",
      badgeClass: "complete",
    };
  }

  const timeRemainingUntilEnd = end - now;

  if (timeRemainingUntilEnd <= ENDING_SOON_THRESHOLD_MS) {
    return {
      state: "ending-soon",
      label: "Ending Soon",
      badgeClass: "ending",
    };
  }

  return {
    state: "live",
    label: "In Progress",
    badgeClass: "live",
  };
}


/*
  =========================
  DURATION HELPERS
  =========================
*/

function getDurationMs(event) {
  const start = getEventStartTime(event);
  const end = getEventEndTime(event);

  if (!end || Number.isNaN(start) || end <= start) {
    return null;
  }

  return end - start;
}

function formatDurationFromMs(ms) {
  if (!ms || ms <= 0) return null;

  const totalMinutes = Math.round(ms / 60000);

  const days = Math.floor(totalMinutes / (60 * 24));
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
  const minutes = totalMinutes % 60;

  const parts = [];

  if (days > 0) parts.push(`${days} ${days === 1 ? "day" : "days"}`);
  if (hours > 0) parts.push(`${hours} ${hours === 1 ? "hour" : "hours"}`);
  if (minutes > 0) parts.push(`${minutes} ${minutes === 1 ? "minute" : "minutes"}`);

  if (parts.length === 0) return "Less than 1 minute";

  return parts.join(", ");
}

function getDurationText(event) {
  if (event.duration && String(event.duration).trim()) {
    return String(event.duration).trim();
  }

  const durationMs = getDurationMs(event);
  if (!durationMs) return null;

  return formatDurationFromMs(durationMs);
}


/*
  =========================
  EVENT SORTING
  =========================
*/

function sortEventsByTime(inputEvents) {
  const now = Date.now();

  return [...inputEvents].sort((a, b) => {
    const timeA = getSortTime(a);
    const timeB = getSortTime(b);

    const stateA = getEventState(a, now).state;
    const stateB = getEventState(b, now).state;

    const isCompleteA = stateA === "complete";
    const isCompleteB = stateB === "complete";

    if (KEEP_COMPLETED_EVENTS_VISIBLE && isCompleteA !== isCompleteB) {
      return isCompleteA ? 1 : -1;
    }

    return timeA - timeB;
  });
}


/*
  =========================
  SMALL RENDER HELPERS
  =========================
*/

function renderEventDuration(event) {
  const durationText = getDurationText(event);
  if (!durationText) return "";

  return `
    <p class="eventDuration">
      Duration: ${escapeHtml(durationText)}
    </p>
  `;
}

function renderEventLink(event) {
  if (!event.link) return "";

  const label = event.linkLabel || "Open link";

  return `
    <div class="eventLinkRow">
      <a
        class="eventLink"
        href="${escapeHtml(event.link)}"
        target="_blank"
        rel="noopener noreferrer"
      >
        ${escapeHtml(label)}
        <span aria-hidden="true">↗</span>
      </a>
    </div>
  `;
}

function renderStateBadge(event, now) {
  const { state, label, badgeClass } = getEventState(event, now);

  if (state === "upcoming") return "";

  return `
    <div class="stateBadge ${badgeClass}">
      <span class="stateDot" aria-hidden="true"></span>
      ${escapeHtml(label)}
    </div>
  `;
}


/*
  =========================
  MAIN CARD RENDER
  =========================
*/

function renderEvents() {
  const sortedEvents = sortEventsByTime(events);
  const now = Date.now();

  const nextUpcomingIndex = sortedEvents.findIndex(
    (event) => getEventState(event, now).state === "upcoming"
  );

  countdownGrid.innerHTML = sortedEvents
    .map((event, index) => {
      const eventTag = getEventTag(event);
      const styleVars = getEventStyleVars(event);
      const eventState = getEventState(event, now).state;
      const isNextUp = index === nextUpcomingIndex && eventState === "upcoming";
      const nextUpBadge = isNextUp ? `<div class="nextUpBadge">Next Up</div>` : "";

      return `
        <article
          class="countdownItem state-${eventState} ${isNextUp ? "is-next-up" : ""}"
          data-target="${escapeHtml(event.datetime)}"
          data-end-target="${escapeHtml(event.endDatetime || "")}"
          style="${styleVars}"
        >
          ${nextUpBadge}
          ${renderStateBadge(event, now)}

          <div class="eventTop">
            <span class="eventTag">${escapeHtml(eventTag)}</span>
            <span class="eventTimezone">${escapeHtml(event.timezoneLabel)}</span>
          </div>

          <h2 class="eventTitle">${escapeHtml(event.title)}</h2>
          <p class="eventMeta">${escapeHtml(event.displayDate)}</p>
          <p class="eventLocation">${escapeHtml(event.location)}</p>

          ${renderEventDuration(event)}
          ${renderEventLink(event)}

          <div class="countdown" aria-label="Countdown timing for ${escapeHtml(event.title)}">
            <div class="timeBox">
              <span class="timeValue" data-unit="days">0</span>
              <span class="timeLabel">Days</span>
            </div>
            <div class="timeBox">
              <span class="timeValue" data-unit="hours">0</span>
              <span class="timeLabel">Hours</span>
            </div>
            <div class="timeBox">
              <span class="timeValue" data-unit="minutes">0</span>
              <span class="timeLabel">Minutes</span>
            </div>
            <div class="timeBox">
              <span class="timeValue" data-unit="seconds">0</span>
              <span class="timeLabel">Seconds</span>
            </div>
          </div>

          <p class="status" data-status>Loading…</p>
        </article>
      `;
    })
    .join("");
}


/*
  =========================
  COUNTDOWN DISPLAY HELPERS
  =========================
*/

function setCountdownValues(card, diffMs) {
  const daysEl = card.querySelector('[data-unit="days"]');
  const hoursEl = card.querySelector('[data-unit="hours"]');
  const minutesEl = card.querySelector('[data-unit="minutes"]');
  const secondsEl = card.querySelector('[data-unit="seconds"]');

  const totalSeconds = Math.max(0, Math.floor(diffMs / 1000));
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  daysEl.textContent = String(days);
  hoursEl.textContent = String(hours).padStart(2, "0");
  minutesEl.textContent = String(minutes).padStart(2, "0");
  secondsEl.textContent = String(seconds).padStart(2, "0");

  return { days, hours, minutes, seconds };
}


/*
  =========================
  PER-CARD LIVE UPDATE
  =========================
*/

function updateCountdown(card) {
  const start = new Date(card.dataset.target).getTime();
  const end = card.dataset.endTarget ? new Date(card.dataset.endTarget).getTime() : null;
  const now = Date.now();

  const statusEl = card.querySelector("[data-status]");

  if (Number.isNaN(start)) {
    statusEl.textContent = "Invalid date";
    return;
  }

  card.classList.remove("state-upcoming", "state-live", "state-ending-soon", "state-complete");

  // Before start
  if (now < start) {
    const diffUntilStart = start - now;
    const { days, hours, minutes } = setCountdownValues(card, diffUntilStart);

    card.classList.add("state-upcoming");

    statusEl.textContent =
      `${days} ${formatPlural(days, "day", "days")}, ` +
      `${hours} ${formatPlural(hours, "hour", "hours")}, ` +
      `${minutes} ${formatPlural(minutes, "minute", "minutes")} until start`;
    return;
  }

  // In progress
  if (end && !Number.isNaN(end) && now < end) {
    const diffUntilEnd = end - now;
    const { days, hours, minutes } = setCountdownValues(card, diffUntilEnd);

    if (diffUntilEnd <= ENDING_SOON_THRESHOLD_MS) {
      card.classList.add("state-ending-soon");
      statusEl.textContent =
        `${days} ${formatPlural(days, "day", "days")}, ` +
        `${hours} ${formatPlural(hours, "hour", "hours")}, ` +
        `${minutes} ${formatPlural(minutes, "minute", "minutes")} until it ends`;
    } else {
      card.classList.add("state-live");
      statusEl.textContent =
        `${days} ${formatPlural(days, "day", "days")}, ` +
        `${hours} ${formatPlural(hours, "hour", "hours")}, ` +
        `${minutes} ${formatPlural(minutes, "minute", "minutes")} remaining in progress`;
    }

    return;
  }

  // Complete / passed
  setCountdownValues(card, 0);
  card.classList.add("state-complete");
  statusEl.textContent = end ? "This event has concluded." : "This deadline has passed.";
}

function tick() {
  const cards = document.querySelectorAll(".countdownItem");
  cards.forEach(updateCountdown);
}


/*
  =========================
  PAGE STARTUP
  =========================
*/

function rerenderForStateChanges() {
  renderEvents();
  tick();
}

function startCountdownPage() {
  preloadClubLogos();
  setClub(clubIndex);

  renderEvents();
  tick();

  setInterval(tick, COUNTDOWN_TICK_INTERVAL_MS);
  setInterval(rerenderForStateChanges, CARD_RERENDER_INTERVAL_MS);
  setInterval(swapClub, DIVISION_SWAP_INTERVAL_MS);
}

startCountdownPage();