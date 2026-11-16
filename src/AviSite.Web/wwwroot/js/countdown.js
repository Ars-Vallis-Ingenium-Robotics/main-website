// /countdown: ticks every card once a second. The server already rendered the right numbers,
// so without JS the page is still correct as of load time.
(() => {
  const cards = [...document.querySelectorAll("[data-countdowns] .countdown[data-start]")].filter((c) => c.querySelector("[data-clock]"));
  if (!cards.length) return;
  const pad = (n) => String(n).padStart(2, "0");
  function tick() {
    const now = Date.now();
    for (const c of cards) {
      const start = Date.parse(c.dataset.start);
      const end = c.dataset.end ? Date.parse(c.dataset.end) : null;
      const started = now >= start;
      const target = started ? (end ?? start) : start;
      let left = Math.max(0, target - now);
      const finished = started && (end === null || now >= end);
      c.classList.toggle("is-now", started && !finished);
      c.classList.toggle("is-soon", !started);
      c.classList.toggle("is-done", finished);
      c.querySelector("[data-state]").textContent = finished ? "Done" : started ? (end ? "Happening now" : "Now") : "";
      c.querySelector("[data-lead]").textContent = finished ? "Finished" : started ? "Ends in" : "Starts in";
      const d = Math.floor(left / 864e5); left -= d * 864e5;
      const h = Math.floor(left / 36e5); left -= h * 36e5;
      const m = Math.floor(left / 6e4); left -= m * 6e4;
      const s = Math.floor(left / 1e3);
      c.querySelector("[data-d]").textContent = d;
      c.querySelector("[data-h]").textContent = pad(h);
      c.querySelector("[data-m]").textContent = pad(m);
      c.querySelector("[data-s]").textContent = pad(s);
    }
  }
  tick();
  setInterval(tick, 1000);
})();
