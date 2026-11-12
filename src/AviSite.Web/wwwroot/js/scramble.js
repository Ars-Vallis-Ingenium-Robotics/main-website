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
    // Left to right: each letter starts scrambling a little after the one before it and
    // settles a fixed while later, so the new phrase sweeps in from the left.
    const queue = [...Array(len)].map((_, k) => ({ a: from[k] || "", b: target[k] || "", start: k * 2, end: k * 2 + 16 }));
    let frame = 0;
    const tick = () => {
      let out = "", finished = 0;
      for (const q of queue) {
        if (frame >= q.end) { finished++; out += q.b; }
        else if (frame >= q.start) out += q.b === " " ? " " : `<span class="scramble__glyph">${glyphs[Math.floor(Math.random() * glyphs.length)]}</span>`;
        else out += q.a;
      }
      el.innerHTML = out;
      // Slower than one glyph per frame: a new random letter every third frame.
      if (finished === queue.length) { el.textContent = target; done(); } else { frame++; setTimeout(() => requestAnimationFrame(tick), 33); }
    };
    tick();
  });
  const loop = async () => {
    await new Promise((r) => setTimeout(r, 5000));
    i = (i + 1) % phrases.length;
    await to(phrases[i]);
    loop();
  };
  loop();
})();
