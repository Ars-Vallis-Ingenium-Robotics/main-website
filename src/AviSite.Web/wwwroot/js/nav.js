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

// Every running clock on the page (header and the hours page), h:mm:ss, every second.
(() => {
  const els = document.querySelectorAll("[data-clock-since]");
  if (!els.length) return;
  const pad = (n) => String(n).padStart(2, "0");
  const tick = () => els.forEach((el) => {
    const t = Math.max(0, Math.floor((Date.now() - Number(el.dataset.clockSince) * 1000) / 1000));
    el.textContent = `${Math.floor(t / 3600)}:${pad(Math.floor((t % 3600) / 60))}:${pad(t % 60)}`;
  });
  tick(); setInterval(tick, 1000);
})();

// Header quick clock-in: tap once to arm, again (same spot) to clock in. Disarms after a few seconds,
// on Escape, or on a tap anywhere else, so a stray tap later can't clock you in.
(() => {
  document.querySelectorAll("[data-quick-clock]").forEach((d) => {
    let timer;
    d.addEventListener("toggle", () => {
      clearTimeout(timer);
      if (d.open) timer = setTimeout(() => { d.open = false; }, 5000);
    });
  });
  const closeAll = (except) => document.querySelectorAll("[data-quick-clock][open]").forEach((d) => { if (d !== except) d.open = false; });
  document.addEventListener("click", (e) => closeAll(e.target.closest("[data-quick-clock]")));
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeAll(null); });
})();

// Folding team trees (admin): remember what was folded, per browser; "Expand all" / "Collapse all".
(() => {
  const folds = document.querySelectorAll("details[data-fold]");
  if (!folds.length) return;
  const key = (d) => "avi-fold:" + d.dataset.fold;
  folds.forEach((d) => {
    try { if (localStorage.getItem(key(d)) === "0") d.open = false; } catch { /* storage off: everything open */ }
    d.addEventListener("toggle", () => { try { d.open ? localStorage.removeItem(key(d)) : localStorage.setItem(key(d), "0"); } catch { /* fine */ } });
  });
  document.querySelectorAll("[data-fold-all]").forEach((b) => b.addEventListener("click", () => {
    const open = b.dataset.open === "true";
    document.querySelectorAll(`details[data-fold^="${b.dataset.foldAll}:"]`).forEach((d) => { d.open = open; });
  }));
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
