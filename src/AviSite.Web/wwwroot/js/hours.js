// /admin/hours: keeps the clock-in confirm saying what you picked.
(() => {
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
