// Mobile menu (<details>): close on Escape and return focus to the toggle; close after choosing a link.
(() => {
  const menu = document.querySelector(".nav--narrow");
  if (!menu) return;
  const toggle = menu.querySelector("summary");
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && menu.open) { menu.open = false; toggle.focus(); }
  });
  menu.addEventListener("click", (e) => { if (e.target.closest("a")) menu.open = false; });
})();

// Header clock ("Clocked in · 1:02"): keep the minutes current.
(() => {
  const els = document.querySelectorAll("[data-clock-since][data-clock-short]");
  if (!els.length) return;
  const tick = () => els.forEach((el) => {
    const m = Math.max(0, Math.floor((Date.now() - Number(el.dataset.clockSince) * 1000) / 60000));
    el.textContent = `${Math.floor(m / 60)}:${String(m % 60).padStart(2, "0")}`;
  });
  tick(); setInterval(tick, 20000);
})();

// Theme toggle (header): flips between dark and light and remembers it in this browser.
(() => {
  const root = document.documentElement;
  const isDark = () => root.dataset.theme ? root.dataset.theme === "dark" : matchMedia("(prefers-color-scheme: dark)").matches;
  const buttons = document.querySelectorAll("[data-theme-toggle]");
  const sync = () => buttons.forEach((b) => {
    b.setAttribute("aria-pressed", String(isDark()));
    b.setAttribute("title", isDark() ? "Switch to light mode" : "Switch to dark mode");
  });
  buttons.forEach((b) => b.addEventListener("click", () => {
    const next = isDark() ? "light" : "dark";
    root.dataset.theme = next;
    try { localStorage.setItem("avi-theme", next); } catch { /* fine: applies until reload */ }
    sync();
  }));
  matchMedia("(prefers-color-scheme: dark)").addEventListener?.("change", sync);
  sync();
})();
