// Letter scramble between AVI's Latin name and its English translation (hero title).
// Respects reduced motion: then it just shows the Latin.
(() => {
  const el = document.querySelector("[data-scramble]");
  if (!el || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const phrases = el.dataset.scramble.split("|");
  const glyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
  let i = 0;
  const to = (target) => new Promise((done) => {
    const from = el.textContent, len = Math.max(from.length, target.length);
    const queue = [...Array(len)].map((_, k) => ({ a: from[k] || "", b: target[k] || "", start: Math.floor(Math.random() * 18), end: 18 + Math.floor(Math.random() * 22) }));
    let frame = 0;
    const tick = () => {
      let out = "", finished = 0;
      for (const q of queue) {
        if (frame >= q.end) { finished++; out += q.b; }
        else if (frame >= q.start) out += q.b === " " ? " " : `<span class="scramble__glyph">${glyphs[Math.floor(Math.random() * glyphs.length)]}</span>`;
        else out += q.a;
      }
      el.innerHTML = out;
      if (finished === queue.length) { el.textContent = target; done(); } else { frame++; requestAnimationFrame(tick); }
    };
    tick();
  });
  const loop = async () => {
    await new Promise((r) => setTimeout(r, 3200));
    i = (i + 1) % phrases.length;
    await to(phrases[i]);
    loop();
  };
  loop();
})();
