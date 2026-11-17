// /admin/hours: keeps the "clocked in for…" timer ticking, and keeps the clock-in confirm saying what you picked.
(() => {
  const el = document.querySelector("[data-clock-since]");
  if (el) {
    const since = Number(el.dataset.clockSince) * 1000;
    // h:mm:ss, every second.
    const show = () => {
      const t = Math.max(0, Math.floor((Date.now() - since) / 1000));
      const h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), sec = t % 60;
      el.textContent = `${h}:${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
    };
    show(); setInterval(show, 1000);
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
// "Not now" closes the confirm without reloading (it's a plain link without script).
document.querySelectorAll("[data-clock-cancel]").forEach((a) => a.addEventListener("click", (e) => {
  const d = a.closest("details");
  if (d) { e.preventDefault(); d.open = false; d.querySelector("summary")?.focus(); }
}));
// Report: print button (no inline handlers under our CSP).
document.querySelectorAll("[data-print]").forEach((b) => b.addEventListener("click", () => window.print()));
