// QR code maker (/tools/qr). Everything runs in the browser: nothing typed here leaves the page.
// Uses lib/qrcode-generator.js (Kazuhiko Arase, MIT) for the code itself; drawing is ours (SVG, then canvas for PNG).
(() => {
  const root = document.querySelector("[data-qr-tool]");
  if (!root || typeof qrcode !== "function") return;
  qrcode.stringToBytes = qrcode.stringToBytesFuncs["UTF-8"];

  const $ = (sel) => root.querySelector(sel);
  const $$ = (sel) => [...root.querySelectorAll(sel)];
  const css = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;

  // Presets. Colors come from the site's own tokens so they match the brand.
  const PRESETS = {
    avi: { fg: css("--ink", "#101011"), eye: css("--accent", "#0c63e6"), logo: "/img/brand/avi-dog-480.webp" },
    sms: { fg: "#06262b", eye: "#1d8f88", logo: "/img/brand/sms-logo.webp" },
    plain: { fg: "#000000", eye: "#000000", logo: null },
  };

  const logoCache = new Map(); // url -> data: URL (so SVG downloads are self-contained)
  let customLogo = null;
  let current = null; // { svg, count, name }

  async function toDataUrl(url) {
    if (!url) return null;
    if (url.startsWith("data:")) return url;
    if (logoCache.has(url)) return logoCache.get(url);
    const blob = await (await fetch(url)).blob();
    const data = await new Promise((ok) => { const r = new FileReader(); r.onload = () => ok(r.result); r.readAsDataURL(blob); });
    logoCache.set(url, data);
    return data;
  }

  function state() {
    const style = ($('[data-qr="style"]:checked') || {}).value || "avi";
    const p = style === "custom"
      ? { fg: $('[data-qr="fg"]').value, eye: $('[data-qr="eye"]').value, logo: customLogo }
      : PRESETS[style];
    return {
      text: $('[data-qr="text"]').value.trim(),
      style,
      shape: ($('[data-qr="shape"]:checked') || {}).value || "rounded",
      useLogo: $('[data-qr="uselogo"]').checked && !!p.logo,
      transparent: $('[data-qr="transparent"]').checked,
      size: +$('[data-qr="size"]').value,
      ...p,
    };
  }

  // Finder patterns (the three big corner squares) are drawn separately so they can be styled.
  const isEye = (r, c, n) => (r < 7 && c < 7) || (r < 7 && c >= n - 7) || (r >= n - 7 && c < 7);

  function buildSvg(s, logoData) {
    const qr = qrcode(0, s.useLogo ? "H" : "M");
    qr.addData(s.text);
    qr.make();
    const n = qr.getModuleCount();
    const m = 4; // quiet zone (modules)
    const W = n + 2 * m;
    // Clear a square in the middle for the logo (about 22% of the width: well inside what level H can lose).
    const hole = s.useLogo ? Math.round(n * 0.22) | 1 : 0;
    const h0 = (n - hole) / 2, h1 = h0 + hole;
    const inHole = (r, c) => hole && r >= h0 - 0.5 && r < h1 - 0.5 && c >= h0 - 0.5 && c < h1 - 0.5;

    const parts = [];
    if (!s.transparent) parts.push(`<rect width="${W}" height="${W}" fill="#ffffff"/>`);
    let path = "";
    for (let r = 0; r < n; r++) {
      for (let c = 0; c < n; c++) {
        if (!qr.isDark(r, c) || isEye(r, c, n) || inHole(r, c)) continue;
        const x = c + m, y = r + m;
        if (s.shape === "dots") parts.push(`<circle cx="${x + 0.5}" cy="${y + 0.5}" r="0.42"/>`);
        else if (s.shape === "rounded") parts.push(`<rect x="${x + 0.06}" y="${y + 0.06}" width="0.88" height="0.88" rx="0.3"/>`);
        else path += `M${x} ${y}h1v1h-1z`;
      }
    }
    const dots = s.shape === "square" ? `<path d="${path}"/>` : parts.splice(s.transparent ? 0 : 1).join("");
    const bg = parts.join("");
    const rr = s.shape === "square" ? 0 : 1; // corner rounding for the eyes
    const eye = (x, y) =>
      `<rect x="${x + 0.5}" y="${y + 0.5}" width="6" height="6" rx="${rr * 1.6}" fill="none" stroke="${s.eye}" stroke-width="1"/>` +
      `<rect x="${x + 2}" y="${y + 2}" width="3" height="3" rx="${rr * 0.8}" fill="${s.eye}"/>`;
    let logo = "";
    if (s.useLogo && logoData) {
      const pad = 0.6, x = m + h0 + pad, size = hole - 2 * pad;
      logo = (s.transparent ? "" : `<rect x="${m + h0}" y="${m + h0}" width="${hole}" height="${hole}" rx="1.2" fill="#ffffff"/>`) +
        `<image href="${logoData}" x="${x}" y="${x}" width="${size}" height="${size}" preserveAspectRatio="xMidYMid meet"/>`;
    }
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${W}" shape-rendering="${s.shape === "square" ? "crispEdges" : "geometricPrecision"}">` +
      `${bg}<g fill="${s.fg}">${dots}</g>${eye(m, m)}${eye(m + n - 7, m)}${eye(m, m + n - 7)}${logo}</svg>`;
    return { svg, count: n };
  }

  function fileName(text, ext) {
    let base = text.replace(/^https?:\/\//, "").replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").slice(0, 40) || "qr";
    return `qr-${base.toLowerCase()}.${ext}`;
  }

  let seq = 0;
  async function render() {
    const s = state();
    const preview = $("[data-qr-preview]");
    const status = $("[data-qr-status]");
    $$("[data-qr-download]").forEach((b) => (b.disabled = !s.text));
    $("[data-qr-custom]").hidden = s.style !== "custom";
    $("[data-qr-size-out]").textContent = s.size;
    if (!s.text) { preview.innerHTML = ""; status.textContent = "Type a link or some text to make a code."; current = null; return; }
    const my = ++seq;
    let logoData = null;
    try { logoData = s.useLogo ? await toDataUrl(s.logo) : null; } catch { logoData = null; }
    if (my !== seq) return; // a newer edit already rendered
    try {
      const out = buildSvg({ ...s, useLogo: s.useLogo && !!logoData }, logoData);
      preview.innerHTML = out.svg;
      preview.classList.toggle("qr-tool__preview--clear", s.transparent);
      current = { ...out, name: s.text, size: s.size };
      status.textContent = `${out.count} × ${out.count} squares${s.useLogo && logoData ? ", with logo" : ""}.`;
    } catch {
      preview.innerHTML = "";
      current = null;
      status.textContent = "That's too much text for one QR code. Try a shorter link.";
    }
  }

  function download(blob, name) {
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  }

  async function savePng() {
    if (!current) return;
    const img = new Image();
    const url = URL.createObjectURL(new Blob([current.svg], { type: "image/svg+xml" }));
    await new Promise((ok, bad) => { img.onload = ok; img.onerror = bad; img.src = url; });
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = current.size;
    const g = canvas.getContext("2d");
    g.imageSmoothingEnabled = false;
    g.drawImage(img, 0, 0, current.size, current.size);
    URL.revokeObjectURL(url);
    canvas.toBlob((b) => download(b, fileName(current.name, "png")), "image/png");
  }

  $$("[data-qr]").forEach((el) => el.addEventListener("input", render));
  $$("[data-qr]").forEach((el) => el.addEventListener("change", render));
  $('[data-qr="logo"]').addEventListener("change", (e) => {
    const f = e.target.files[0];
    if (!f) { customLogo = null; render(); return; }
    const r = new FileReader();
    r.onload = () => { customLogo = r.result; render(); };
    r.readAsDataURL(f);
  });
  root.querySelector('[data-qr-download="png"]').addEventListener("click", savePng);
  root.querySelector('[data-qr-download="svg"]').addEventListener("click", () =>
    current && download(new Blob([current.svg], { type: "image/svg+xml" }), fileName(current.name, "svg")));
  render();
})();
