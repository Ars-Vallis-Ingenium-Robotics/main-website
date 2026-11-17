// /admin/hours/week: draw blocks of time on a week calendar, adjust them in the list, save them all at once.
// Mouse/pen: press and drag down a day. Touch: tap a day (makes an hour; scrolling still works). Keyboard: the + on each day.
// Every block is a row of real form fields (date, start, end, team, category), so the save is a normal form post.
(() => {
  const week = document.querySelector("[data-week]");
  const form = document.querySelector("[data-week-form]");
  const rows = document.querySelector("[data-week-rows]");
  const tpl = document.querySelector("template[data-week-row]");
  if (!week || !form || !rows || !tpl) return;

  const first = Number(week.dataset.firstHour), last = Number(week.dataset.lastHour);
  const span = (last - first) * 60;
  const SNAP = 15;
  const pad = (n) => String(n).padStart(2, "0");
  const toTime = (min) => `${pad(Math.floor(min / 60) % 24)}:${pad(min % 60)}`;
  const toMin = (t) => { const [h, m] = (t || "0:0").split(":").map(Number); return h * 60 + m; };
  const label = (min) => { const h = Math.floor(min / 60) % 24, m = min % 60; return `${h % 12 || 12}:${pad(m)}${h < 12 ? "a" : "p"}`; };
  const dayName = (iso) => new Date(iso + "T12:00:00").toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" });

  // Minutes since midnight at a pointer's height in a column, snapped to 15.
  const minuteAt = (col, y) => {
    const r = col.getBoundingClientRect();
    const f = Math.min(1, Math.max(0, (y - r.top) / r.height));
    return first * 60 + Math.round((f * span) / SNAP) * SNAP;
  };

  const blocks = []; // { date, start, end, el, row }

  function place(b) {
    const top = (b.start - first * 60) / span, h = (b.end - b.start) / span;
    b.el.style.top = `${Math.max(0, top) * 100}%`;
    b.el.style.height = `${Math.max(0.01, Math.min(1 - top, h)) * 100}%`;
    b.el.querySelector(".wk__time").textContent = `${label(b.start)}–${label(b.end)}`;
    if (b.row) {
      b.row.querySelector("[data-row-start]").value = toTime(b.start);
      b.row.querySelector("[data-row-end]").value = toTime(b.end);
      const d = b.end - b.start;
      b.row.querySelector("[data-row-dur]").textContent = `${Math.floor(d / 60)}:${pad(d % 60)}`;
    }
    flagOverlaps();
  }

  // Red outline where a new block runs into another new block or time already logged that day.
  function flagOverlaps() {
    for (const b of blocks) {
      const col = b.el.parentElement;
      const others = [
        ...blocks.filter((o) => o !== b && o.date === b.date).map((o) => [o.start, o.end]),
        ...[...col.querySelectorAll("[data-week-entry]")].map((e) => [toMin(e.dataset.start), toMin(e.dataset.end) || 24 * 60]),
      ];
      const clash = others.some(([s, e]) => b.start < e && s < b.end);
      b.el.classList.toggle("is-clash", clash);
      b.row?.classList.toggle("is-clash", clash);
    }
  }

  function add(col, start, end) {
    const date = col.dataset.weekCol;
    if (!date) return null; // future day
    const el = document.createElement("div");
    el.className = "wk__block wk__block--new";
    el.innerHTML = '<span>New</span><span class="wk__time"></span>';
    col.append(el);
    const b = { date, start, end: Math.max(end, start + SNAP), el, row: null };
    blocks.push(b);
    place(b);
    return b;
  }

  function addRow(b) {
    const li = tpl.content.firstElementChild.cloneNode(true);
    li.querySelector("[data-row-date]").value = b.date;
    li.querySelector("[data-row-day]").textContent = dayName(b.date);
    b.row = li;
    li.querySelector("[data-row-start]").addEventListener("change", (e) => { b.start = toMin(e.target.value); if (b.end <= b.start) b.end = b.start + 60; place(b); });
    li.querySelector("[data-row-end]").addEventListener("change", (e) => { const v = toMin(e.target.value); b.end = v <= b.start ? v + 24 * 60 : v; place(b); });
    li.querySelector("[data-row-remove]").addEventListener("click", () => remove(b));
    // Rows stay in calendar order.
    const after = [...rows.children].find((r) => {
      const o = blocks.find((x) => x.row === r);
      return o && (o.date > b.date || (o.date === b.date && o.start > b.start));
    });
    rows.insertBefore(li, after || null);
    place(b);
    form.hidden = false;
    updateSave();
  }

  function remove(b) {
    b.el.remove(); b.row?.remove();
    blocks.splice(blocks.indexOf(b), 1);
    flagOverlaps(); updateSave();
    if (!blocks.length) form.hidden = true;
  }

  function updateSave() {
    const total = blocks.reduce((t, b) => t + (b.end - b.start), 0);
    form.querySelector("[data-week-save]").textContent =
      `Save ${blocks.length} ${blocks.length === 1 ? "block" : "blocks"} (${Math.floor(total / 60)}:${pad(total % 60)})`;
  }

  // Drawing
  let drawing = null;
  week.querySelectorAll("[data-week-col]").forEach((col) => {
    col.addEventListener("pointerdown", (e) => {
      if (!col.dataset.weekCol || e.target.closest("[data-week-entry], .wk__block--new") || e.button > 0) return;
      const at = minuteAt(col, e.clientY);
      drawing = { col, from: at, b: null, touch: e.pointerType === "touch", y: e.clientY, id: e.pointerId };
      if (!drawing.touch) { e.preventDefault(); col.setPointerCapture(e.pointerId); drawing.b = add(col, at, at + SNAP); }
    });
    col.addEventListener("pointermove", (e) => {
      if (!drawing || drawing.col !== col || !drawing.b) return;
      const at = minuteAt(col, e.clientY);
      drawing.b.start = Math.min(drawing.from, at);
      drawing.b.end = Math.max(drawing.from + SNAP, at);
      place(drawing.b);
    });
    const finish = (e) => {
      if (!drawing || drawing.col !== col) return;
      let b = drawing.b;
      if (drawing.touch) {
        // A tap (not a scroll) makes an hour starting where you tapped.
        if (Math.abs(e.clientY - drawing.y) < 10) b = add(col, drawing.from, drawing.from + 60);
      } else if (b && b.end - b.start <= SNAP) {
        b.end = b.start + 60; // a click without dragging: an hour
      }
      drawing = null;
      if (b) addRow(b);
    };
    col.addEventListener("pointerup", finish);
    col.addEventListener("pointercancel", () => { if (drawing?.b) remove(drawing.b); drawing = null; });
  });

  // Keyboard / precise: "+" on a day adds two hours after that day's last block (or at 5:30 pm), then focus its start time.
  week.querySelectorAll("[data-week-add]").forEach((btn) => btn.addEventListener("click", () => {
    const date = btn.dataset.weekAdd;
    const col = week.querySelector(`[data-week-col="${date}"]`);
    const ends = [
      ...blocks.filter((b) => b.date === date).map((b) => b.end),
      ...[...col.querySelectorAll("[data-week-entry]")].map((e) => toMin(e.dataset.end)),
    ];
    const start = ends.length ? Math.max(...ends) : 17 * 60 + 30;
    const b = add(col, Math.min(start, 22 * 60), Math.min(start, 22 * 60) + 120);
    if (b) { addRow(b); b.row.querySelector("[data-row-start]").focus(); }
  }));

  form.querySelector("[data-week-clear]").addEventListener("click", () => [...blocks].forEach(remove));
  const warn = document.createElement("p");
  warn.className = "alert"; warn.setAttribute("role", "alert"); warn.hidden = true;
  warn.textContent = "Some blocks overlap (outlined in red). Move or shorten them first.";
  form.querySelector(".hours-form__actions").before(warn);
  form.addEventListener("submit", (e) => {
    const clash = blocks.some((b) => b.el.classList.contains("is-clash"));
    warn.hidden = !clash;
    if (clash) e.preventDefault();
  });
})();
