// /admin/hours: keeps the "clocked in for…" timer ticking, and keeps the clock-in confirm saying what you picked.
(() => {
  const el = document.querySelector("[data-clock-since]");
  if (el) {
    const since = Number(el.dataset.clockSince) * 1000;
    const show = () => {
      const m = Math.max(0, Math.floor((Date.now() - since) / 60000));
      const h = Math.floor(m / 60), r = m % 60;
      el.textContent = h === 0 ? `${r} min` : r === 0 ? `${h} h` : `${h} h ${r} min`;
    };
    show(); setInterval(show, 15000);
  }
  const form = document.querySelector("[data-clock]");
  if (form) {
    const what = form.querySelector("[data-clock-what]");
    const update = () => {
      const team = form.querySelector("[data-clock-team]");
      const cat = form.querySelector("[data-clock-cat]:checked");
      if (what && team && cat) what.textContent = `${team.selectedOptions[0].textContent.trim()} · ${cat.value}`;
    };
    form.addEventListener("change", update);
    update();
  }
})();
// Report: print button (no inline handlers under our CSP).
document.querySelectorAll("[data-print]").forEach((b) => b.addEventListener("click", () => window.print()));
