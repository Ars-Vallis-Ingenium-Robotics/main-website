// QR code maker (/tools/qr). Everything runs in the browser: nothing typed here leaves the page.
// lib/qrcode-generator.js (Kazuhiko Arase, MIT) works out which squares are dark; everything you see is drawn here as SVG,
// then rasterized on a canvas for PNG/JPG.
(() => {
  const root = document.querySelector("[data-qr-tool]");
  if (!root || typeof qrcode !== "function") return;
  qrcode.stringToBytes = qrcode.stringToBytesFuncs["UTF-8"];

  const $ = (s) => root.querySelector(s);
  const $$ = (s) => [...root.querySelectorAll(s)];
  const field = (k) => root.querySelector(`[data-qr="${k}"]`);
  const radio = (k) => (root.querySelector(`[data-qr="${k}"]:checked`) || {}).value;
  const val = (k) => (field(k) ? (field(k).type === "checkbox" ? field(k).checked : field(k).value) : "");
  const setRadio = (k, v) => { const el = root.querySelector(`[data-qr="${k}"][value="${v}"]`); if (el) el.checked = true; };
  const setVal = (k, v) => { const el = field(k); if (el) el.value = v; };

  // ---------- Looks ----------
  const LOOKS = {
    avi:     { dot: "rounded", fill: "solid", fg: "#101011", fg2: "#0c63e6", angle: 45, eyeframe: "rounded", eyeball: "rounded", eye: "#0c63e6", eye2: "#101011", bgmode: "solid", bg: "#ffffff", bg2: "#e8f0fe", radius: 0, logo: "avi", plate: "square", frame: "none", fcolor: "#101011", ftcolor: "#ffffff" },
    sms:     { dot: "dots", fill: "solid", fg: "#06262b", fg2: "#1d8f88", angle: 45, eyeframe: "circle", eyeball: "circle", eye: "#1d8f88", eye2: "#06262b", bgmode: "solid", bg: "#ffffff", bg2: "#e4f6f4", radius: 0, logo: "sms", plate: "circle", frame: "none", fcolor: "#06262b", ftcolor: "#ffffff" },
    classic: { dot: "square", fill: "solid", fg: "#000000", fg2: "#000000", angle: 45, eyeframe: "square", eyeball: "square", eye: "#000000", eye2: "#000000", bgmode: "solid", bg: "#ffffff", bg2: "#ffffff", radius: 0, logo: "none", plate: "square", frame: "none", fcolor: "#000000", ftcolor: "#ffffff" },
    ocean:   { dot: "fluid", fill: "linear", fg: "#0b3d91", fg2: "#138a83", angle: 135, eyeframe: "rounded", eyeball: "circle", eye: "#0b3d91", eye2: "#138a83", bgmode: "solid", bg: "#e4f6f4", bg2: "#ffffff", radius: 3, logo: "none", plate: "circle", frame: "below", fcolor: "#0b3d91", ftcolor: "#ffffff" },
    sunset:  { dot: "classy", fill: "linear", fg: "#b0124f", fg2: "#c24a00", angle: 45, eyeframe: "leaf", eyeball: "leaf", eye: "#b0124f", eye2: "#c24a00", bgmode: "linear", bg: "#fff4d6", bg2: "#fde8ec", radius: 3, logo: "none", plate: "square", frame: "none", fcolor: "#b0124f", ftcolor: "#ffffff" },
    forest:  { dot: "vertical", fill: "radial", fg: "#2e7d32", fg2: "#123d17", angle: 45, eyeframe: "rounded", eyeball: "rounded", eye: "#1b5e20", eye2: "#2e7d32", bgmode: "solid", bg: "#f1f8e9", bg2: "#ffffff", radius: 2, logo: "none", plate: "square", frame: "badge", fcolor: "#1b5e20", ftcolor: "#ffffff" },
    mono:    { dot: "tiny", fill: "solid", fg: "#101011", fg2: "#101011", angle: 45, eyeframe: "circle", eyeball: "diamond", eye: "#101011", eye2: "#101011", bgmode: "solid", bg: "#f4f1ea", bg2: "#ffffff", radius: 2, logo: "none", plate: "circle", frame: "none", fcolor: "#101011", ftcolor: "#f4f1ea" },
  };
  const LOOK_KEYS = Object.keys(LOOKS.avi);
  const RADIOS = new Set(["dot", "eyeframe", "eyeball", "logo", "frame"]);
  const STORE = "avi-qr-look";
  const LOGOS = { avi: "/img/brand/avi-dog-480.webp", sms: "/img/brand/sms-logo.webp" };

  function applyLook(name) {
    const look = LOOKS[name] || LOOKS.avi;
    for (const k of LOOK_KEYS) (RADIOS.has(k) ? setRadio : setVal)(k, look[k]);
    $$("[data-qr-theme]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.qrTheme === name)));
  }
  function saveLook() {
    const look = {}; for (const k of LOOK_KEYS) look[k] = RADIOS.has(k) ? radio(k) : val(k);
    look.ftext = val("ftext"); look.lsize = val("lsize"); look.margin = val("margin"); look.ecl = val("ecl"); look.size = val("size");
    try { localStorage.setItem(STORE, JSON.stringify(look)); } catch { /* private mode: fine */ }
  }
  function loadLook() {
    try {
      const look = JSON.parse(localStorage.getItem(STORE) || "null");
      if (!look) return false;
      for (const [k, v] of Object.entries(look)) if (v !== undefined && v !== null && k !== "logo" || (k === "logo" && v !== "upload")) (RADIOS.has(k) ? setRadio : setVal)(k, v);
      return true;
    } catch { return false; }
  }

  // ---------- What the code says ----------
  const esc = (s) => String(s).replace(/([\\;,:"])/g, "\\$1");
  function payload() {
    const kind = radio("kind");
    switch (kind) {
      case "text": return val("text");
      case "wifi": return val("ssid") ? `WIFI:T:${val("wsec")};S:${esc(val("ssid"))};${val("wsec") === "nopass" ? "" : `P:${esc(val("wpass"))};`}${val("whidden") ? "H:true;" : ""};` : "";
      case "email": {
        if (!val("email")) return "";
        const q = [val("esub") && "subject=" + encodeURIComponent(val("esub")), val("ebody") && "body=" + encodeURIComponent(val("ebody"))].filter(Boolean).join("&");
        return `mailto:${val("email")}${q ? "?" + q : ""}`;
      }
      case "tel": return val("tel") ? "tel:" + val("tel").replace(/[^\d+]/g, "") : "";
      case "sms": return val("smsto") ? `SMSTO:${val("smsto").replace(/[^\d+]/g, "")}:${val("smsbody")}` : "";
      case "vcard": {
        if (!val("vname")) return "";
        const name = val("vname").trim(), parts = name.split(/\s+/), last = parts.length > 1 ? parts.pop() : "";
        return ["BEGIN:VCARD", "VERSION:3.0", `N:${esc(last)};${esc(parts.join(" "))}`, `FN:${esc(name)}`,
          val("vorg") && `ORG:${esc(val("vorg"))}`, val("vtitle") && `TITLE:${esc(val("vtitle"))}`, val("vtel") && `TEL:${val("vtel")}`,
          val("vemail") && `EMAIL:${val("vemail")}`, val("vurl") && `URL:${val("vurl")}`, "END:VCARD"].filter(Boolean).join("\n");
      }
      default: return val("url").trim();
    }
  }

  // ---------- Drawing helpers ----------
  const f = (n) => +n.toFixed(3);
  // Rectangle with its own radius per corner (tl, tr, br, bl).
  function rr(x, y, w, h, tl, tr, br, bl) {
    return `M${f(x + tl)} ${f(y)}H${f(x + w - tr)}${tr ? `A${tr} ${tr} 0 0 1 ${f(x + w)} ${f(y + tr)}` : ""}V${f(y + h - br)}${br ? `A${br} ${br} 0 0 1 ${f(x + w - br)} ${f(y + h)}` : ""}H${f(x + bl)}${bl ? `A${bl} ${bl} 0 0 1 ${f(x)} ${f(y + h - bl)}` : ""}V${f(y + tl)}${tl ? `A${tl} ${tl} 0 0 1 ${f(x + tl)} ${f(y)}` : ""}Z`;
  }
  const circle = (cx, cy, r) => `M${f(cx - r)} ${f(cy)}a${r} ${r} 0 1 0 ${f(2 * r)} 0a${r} ${r} 0 1 0 ${f(-2 * r)} 0Z`;
  const diamond = (cx, cy, r) => `M${f(cx)} ${f(cy - r)}L${f(cx + r)} ${f(cy)}L${f(cx)} ${f(cy + r)}L${f(cx - r)} ${f(cy)}Z`;
  const sparkle = (cx, cy, r) => { const k = r * 0.28; return `M${f(cx)} ${f(cy - r)}L${f(cx + k)} ${f(cy - k)}L${f(cx + r)} ${f(cy)}L${f(cx + k)} ${f(cy + k)}L${f(cx)} ${f(cy + r)}L${f(cx - k)} ${f(cy + k)}L${f(cx - r)} ${f(cy)}L${f(cx - k)} ${f(cy - k)}Z`; };

  function eyeFrame(shape, x, y) {
    switch (shape) {
      case "rounded": return rr(x, y, 7, 7, 2.3, 2.3, 2.3, 2.3) + rr(x + 1, y + 1, 5, 5, 1.4, 1.4, 1.4, 1.4);
      case "circle": return circle(x + 3.5, y + 3.5, 3.5) + circle(x + 3.5, y + 3.5, 2.5);
      case "leaf": return rr(x, y, 7, 7, 3, 0, 3, 0) + rr(x + 1, y + 1, 5, 5, 2, 0, 2, 0);
      default: return rr(x, y, 7, 7, 0, 0, 0, 0) + rr(x + 1, y + 1, 5, 5, 0, 0, 0, 0);
    }
  }
  function eyeBall(shape, x, y) {
    const cx = x + 3.5, cy = y + 3.5;
    switch (shape) {
      case "rounded": return rr(x + 2, y + 2, 3, 3, 0.9, 0.9, 0.9, 0.9);
      case "circle": return circle(cx, cy, 1.5);
      case "diamond": return diamond(cx, cy, 1.9);
      case "leaf": return rr(x + 2, y + 2, 3, 3, 1.3, 0, 1.3, 0);
      default: return rr(x + 2, y + 2, 3, 3, 0, 0, 0, 0);
    }
  }

  function gradient(id, mode, c1, c2, angle, x, y, size) {
    if (mode === "radial")
      return `<radialGradient id="${id}" gradientUnits="userSpaceOnUse" cx="${f(x + size / 2)}" cy="${f(y + size / 2)}" r="${f(size * 0.72)}"><stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></radialGradient>`;
    const a = (angle * Math.PI) / 180, cx = x + size / 2, cy = y + size / 2, r = size / 2;
    return `<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${f(cx - Math.cos(a) * r)}" y1="${f(cy - Math.sin(a) * r)}" x2="${f(cx + Math.cos(a) * r)}" y2="${f(cy + Math.sin(a) * r)}"><stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></linearGradient>`;
  }

  // ---------- Build the SVG ----------
  function build(s, logoData) {
    const ecl = s.ecl === "auto" ? (logoData ? "H" : "M") : s.ecl;
    const qr = qrcode(0, ecl);
    qr.addData(s.text);
    qr.make();
    const n = qr.getModuleCount(), m = s.margin, W = n + 2 * m;
    const isEye = (r, c) => (r < 7 && c < 7) || (r < 7 && c >= n - 7) || (r >= n - 7 && c < 7);

    // The middle is cleared for the logo.
    let hole = 0, h0 = 0, h1 = 0;
    if (logoData) { hole = Math.max(3, Math.round((n * s.lsize) / 100)) | 1; h0 = (n - hole) / 2; h1 = h0 + hole; }
    const inHole = (r, c) => {
      if (!hole) return false;
      if (s.plate === "circle") { const d = Math.hypot(r + 0.5 - n / 2, c + 0.5 - n / 2); return d <= hole / 2 + 0.6; }
      return r >= h0 - 0.5 && r < h1 + 0.5 && c >= h0 - 0.5 && c < h1 + 0.5;
    };
    const dark = (r, c) => r >= 0 && c >= 0 && r < n && c < n && qr.isDark(r, c) && !isEye(r, c) && !inHole(r, c);

    let d = "";
    const R = 0.5;
    if (s.dot === "vertical" || s.dot === "horizontal") {
      const v = s.dot === "vertical";
      for (let a = 0; a < n; a++) {
        let b = 0;
        while (b < n) {
          if (!(v ? dark(b, a) : dark(a, b))) { b++; continue; }
          let e = b; while (e < n && (v ? dark(e, a) : dark(a, e))) e++;
          const len = e - b;
          d += v ? rr(a + m + 0.1, b + m + 0.05, 0.8, len - 0.1, 0.4, 0.4, 0.4, 0.4) : rr(b + m + 0.05, a + m + 0.1, len - 0.1, 0.8, 0.4, 0.4, 0.4, 0.4);
          b = e;
        }
      }
    } else {
      for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) {
        if (!dark(r, c)) continue;
        const x = c + m, y = r + m;
        const up = dark(r - 1, c), dn = dark(r + 1, c), lf = dark(r, c - 1), rt = dark(r, c + 1);
        switch (s.dot) {
          case "rounded": d += rr(x + 0.06, y + 0.06, 0.88, 0.88, 0.3, 0.3, 0.3, 0.3); break;
          case "dots": d += circle(x + 0.5, y + 0.5, 0.45); break;
          case "tiny": d += rr(x + 0.18, y + 0.18, 0.64, 0.64, 0, 0, 0, 0); break;
          case "diamond": d += diamond(x + 0.5, y + 0.5, 0.55); break;
          case "star": d += sparkle(x + 0.5, y + 0.5, 0.6); break;
          case "fluid": d += rr(x, y, 1, 1, !up && !lf ? R : 0, !up && !rt ? R : 0, !dn && !rt ? R : 0, !dn && !lf ? R : 0); break;
          case "classy": d += rr(x, y, 1, 1, !up && !lf ? R : 0, 0, !dn && !rt ? R : 0, 0); break;
          default: d += `M${x} ${y}h1v1h-1z`;
        }
      }
    }

    const defs = [];
    let fill = s.fg;
    if (s.fill !== "solid") { defs.push(gradient("qrfg", s.fill, s.fg, s.fg2, +s.angle, m, m, n)); fill = "url(#qrfg)"; }

    // Frame around the code (adds space).
    const framed = s.frame !== "none";
    const pad = framed ? 1.4 : 0, band = framed ? 6 : 0;
    const TW = W + 2 * pad, TH = W + 2 * pad + (s.frame === "badge" ? band * 0.6 : band);
    const ox = pad, oy = pad + (s.frame === "above" ? band : 0); // where the code sits

    const parts = [];
    if (framed && s.frame !== "badge") parts.push(`<rect width="${TW}" height="${TH}" rx="${Math.max(1.5, +s.radius)}" fill="${s.fcolor}"/>`);
    if (s.bgmode !== "none") {
      let bgFill = s.bg;
      if (s.bgmode === "linear") { defs.push(gradient("qrbg", "linear", s.bg, s.bg2, 135, ox, oy, W)); bgFill = "url(#qrbg)"; }
      parts.push(`<rect x="${ox}" y="${oy}" width="${W}" height="${W}" rx="${+s.radius}" fill="${bgFill}"/>`);
    }
    if (s.frame === "badge") parts.push(`<rect x="${ox - 0.7}" y="${oy - 0.7}" width="${W + 1.4}" height="${W + 1.4}" rx="${+s.radius + 1.2}" fill="none" stroke="${s.fcolor}" stroke-width="0.7"/>`);

    parts.push(`<g transform="translate(${ox} ${oy})">`);
    parts.push(`<path fill="${fill}" d="${d}"/>`);
    for (const [ex, ey] of [[m, m], [m + n - 7, m], [m, m + n - 7]]) {
      parts.push(`<path fill="${s.eye}" fill-rule="evenodd" d="${eyeFrame(s.eyeframe, ex, ey)}"/>`);
      parts.push(`<path fill="${s.eye2}" d="${eyeBall(s.eyeball, ex, ey)}"/>`);
    }
    if (logoData) {
      const plateFill = s.bgmode === "none" ? "#ffffff" : s.bg;
      const cx = m + n / 2, size = hole - 1.2;
      if (s.plate === "square") parts.push(`<rect x="${f(m + h0)}" y="${f(m + h0)}" width="${hole}" height="${hole}" rx="${f(hole * 0.18)}" fill="${plateFill}"/>`);
      if (s.plate === "circle") parts.push(`<circle cx="${f(cx)}" cy="${f(cx)}" r="${f(hole / 2 + 0.3)}" fill="${plateFill}"/>`);
      const inset = s.plate === "circle" ? size * 0.12 : 0;
      parts.push(`<image href="${logoData}" x="${f(cx - size / 2 + inset)}" y="${f(cx - size / 2 + inset)}" width="${f(size - 2 * inset)}" height="${f(size - 2 * inset)}" preserveAspectRatio="xMidYMid meet"/>`);
    }
    parts.push(`</g>`);

    if (framed && s.ftext) {
      const label = s.ftext.replace(/[<>&"]/g, (ch) => ({ "<": "&lt;", ">": "&gt;", "&": "&amp;", '"': "&quot;" }[ch]));
      const font = `font-family="Archivo, 'Helvetica Neue', Arial, sans-serif" font-weight="800" text-anchor="middle" letter-spacing="0.08em"`;
      if (s.frame === "badge") {
        const pw = Math.min(TW - 4, Math.max(10, label.length * 1.35 + 4)), ph = 3.6, py = oy + W + 0.7 - ph / 2;
        parts.push(`<rect x="${f(TW / 2 - pw / 2)}" y="${f(py)}" width="${f(pw)}" height="${ph}" rx="${ph / 2}" fill="${s.fcolor}"/>`);
        parts.push(`<text x="${f(TW / 2)}" y="${f(py + ph / 2 + 0.75)}" font-size="2.1" fill="${s.ftcolor}" ${font}>${label}</text>`);
      } else {
        const by = s.frame === "above" ? pad / 2 : oy + W;
        const fs = Math.min(3.2, (TW - 4) / Math.max(4, label.length * 0.72));
        parts.push(`<text x="${f(TW / 2)}" y="${f(by + band / 2 + fs * 0.36)}" font-size="${f(fs)}" fill="${s.ftcolor}" ${font}>${label}</text>`);
      }
    }

    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${f(TW)} ${f(TH)}" width="${f(TW * 10)}" height="${f(TH * 10)}" shape-rendering="${s.dot === "square" || s.dot === "tiny" ? "crispEdges" : "geometricPrecision"}">` +
      (defs.length ? `<defs>${defs.join("")}</defs>` : "") + parts.join("") + `</svg>`;
    return { svg, n, ecl, ratio: TH / TW, version: qr.getTypeNumber ? qr.getTypeNumber() : null };
  }

  // ---------- Scannability checks ----------
  function lum(hex) {
    const v = hex.replace("#", ""); const c = [0, 2, 4].map((i) => parseInt(v.slice(i, i + 2), 16) / 255).map((x) => (x <= 0.03928 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4));
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
  }
  const contrast = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
  function warnings(s, hasLogo, ecl) {
    const w = [];
    const darks = [s.fg, s.eye, s.eye2, ...(s.fill !== "solid" ? [s.fg2] : [])];
    const lights = s.bgmode === "none" ? ["#ffffff"] : [s.bg, ...(s.bgmode === "linear" ? [s.bg2] : [])];
    const worst = Math.min(...darks.flatMap((d) => lights.map((l) => contrast(d, l))));
    if (worst < 3) w.push(`Low contrast (${worst.toFixed(1)}:1). Phones may struggle; aim for 4:1 or more.`);
    if (darks.some((d) => lights.some((l) => lum(d) > lum(l)))) w.push("Light dots on a darker background don't scan on some phones. Keep the dots darker than the background.");
    if (s.bgmode === "none") w.push("Transparent background: put it on something light when you use it.");
    if (hasLogo && ecl !== "H" && ecl !== "Q") w.push("With a logo, set error correction to High so the code survives the missing dots.");
    if (+s.margin < 2) w.push("A quiet zone under 2 dots can stop some scanners. Leave space around it when printing.");
    if (["star", "diamond", "tiny"].includes(s.dot) && hasLogo) w.push("Small dot shapes plus a logo are harder to read. Test it with a couple of phones.");
    return w;
  }

  // ---------- State and rendering ----------
  const logoCache = new Map();
  let uploaded = null, current = null, seq = 0;

  async function dataUrl(url) {
    if (!url) return null;
    if (logoCache.has(url)) return logoCache.get(url);
    const blob = await (await fetch(url)).blob();
    const out = await new Promise((ok) => { const r = new FileReader(); r.onload = () => ok(r.result); r.readAsDataURL(blob); });
    logoCache.set(url, out);
    return out;
  }

  function state() {
    const s = { text: payload() };
    for (const k of LOOK_KEYS) s[k] = RADIOS.has(k) ? radio(k) : val(k);
    s.ftext = val("ftext"); s.lsize = +val("lsize"); s.margin = +val("margin"); s.ecl = val("ecl"); s.size = +val("size");
    return s;
  }

  function showWhen(s) {
    const on = { gradient: s.fill !== "solid", linear: s.fill === "linear", bg: s.bgmode !== "none", bggradient: s.bgmode === "linear",
      upload: s.logo === "upload", haslogo: s.logo !== "none", frame: s.frame !== "none" };
    $$("[data-when]").forEach((el) => (el.hidden = !on[el.dataset.when]));
    const kind = radio("kind");
    $$(".qr-kind").forEach((el) => (el.hidden = el.dataset.kind !== kind));
    for (const k of ["angle", "radius", "lsize", "size", "margin"]) { const o = root.querySelector(`[data-out="${k}"]`); if (o) o.textContent = val(k); }
  }

  async function render() {
    const s = state();
    showWhen(s);
    const preview = $("[data-qr-preview]"), status = $("[data-qr-status]"), warn = $("[data-qr-warn]");
    const ready = !!s.text;
    $$("[data-qr-download], [data-qr-copy]").forEach((b) => (b.disabled = !ready));
    if (!ready) { preview.innerHTML = ""; warn.innerHTML = ""; status.textContent = "Fill in what the code should open."; current = null; return; }
    const my = ++seq;
    let logo = null;
    try { logo = s.logo === "upload" ? uploaded : s.logo !== "none" ? await dataUrl(LOGOS[s.logo]) : null; } catch { logo = null; }
    if (my !== seq) return;
    try {
      const out = build(s, logo);
      preview.innerHTML = out.svg;
      preview.style.setProperty("--ratio", out.ratio);
      preview.classList.toggle("qr-tool__preview--clear", s.bgmode === "none");
      current = { ...out, name: s.text, size: s.size };
      status.textContent = `${out.n} × ${out.n} squares · error correction ${out.ecl}${logo ? " · with logo" : ""}`;
      warn.innerHTML = warnings(s, !!logo, out.ecl).map((t) => `<li>${t}</li>`).join("");
    } catch {
      preview.innerHTML = ""; current = null; warn.innerHTML = "";
      status.textContent = "That's too much for one QR code. Shorten it, or lower the error correction.";
    }
    saveLook();
  }

  // ---------- Export ----------
  function fileName(ext) {
    const base = (current?.name || "qr").replace(/^[a-z]+:\/*/i, "").replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").slice(0, 40).toLowerCase() || "qr";
    return `qr-${base}.${ext}`;
  }
  function save(blob, name) {
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob); a.download = name;
    document.body.appendChild(a); a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  }
  async function raster(type) {
    const img = new Image();
    const url = URL.createObjectURL(new Blob([current.svg], { type: "image/svg+xml" }));
    await new Promise((ok, bad) => { img.onload = ok; img.onerror = bad; img.src = url; });
    const canvas = document.createElement("canvas");
    canvas.width = current.size; canvas.height = Math.round(current.size * current.ratio);
    const g = canvas.getContext("2d");
    if (type === "image/jpeg") { g.fillStyle = "#ffffff"; g.fillRect(0, 0, canvas.width, canvas.height); }
    g.drawImage(img, 0, 0, canvas.width, canvas.height);
    URL.revokeObjectURL(url);
    return new Promise((ok) => canvas.toBlob(ok, type, 0.95));
  }
  root.querySelector('[data-qr-download="svg"]').addEventListener("click", () => current && save(new Blob([current.svg], { type: "image/svg+xml" }), fileName("svg")));
  root.querySelector('[data-qr-download="png"]').addEventListener("click", async () => current && save(await raster("image/png"), fileName("png")));
  root.querySelector('[data-qr-download="jpg"]').addEventListener("click", async () => current && save(await raster("image/jpeg"), fileName("jpg")));
  root.querySelector("[data-qr-copy]").addEventListener("click", async () => {
    if (!current) return;
    const status = $("[data-qr-status]");
    try { await navigator.clipboard.write([new ClipboardItem({ "image/png": raster("image/png") })]); status.textContent = "Copied. Paste it anywhere."; }
    catch { status.textContent = "Your browser won't copy images here. Use the PNG download instead."; }
  });

  // ---------- Wiring ----------
  root.addEventListener("input", (e) => { if (e.target.matches("[data-qr]")) { if (LOOK_KEYS.includes(e.target.dataset.qr)) $$("[data-qr-theme]").forEach((b) => b.setAttribute("aria-pressed", "false")); render(); } });
  root.addEventListener("change", (e) => { if (e.target.matches("[data-qr]")) render(); });
  $$("[data-qr-theme]").forEach((b) => b.addEventListener("click", () => { applyLook(b.dataset.qrTheme); render(); }));
  $$("[data-qr-bg]").forEach((b) => b.addEventListener("click", () => { setVal("bg", b.dataset.qrBg); if (val("bgmode") === "none") setVal("bgmode", "solid"); render(); }));
  $("[data-qr-reset]").addEventListener("click", () => { applyLook("avi"); setVal("ftext", "SCAN ME"); setVal("lsize", 22); setVal("margin", 4); setVal("ecl", "auto"); render(); });
  $("[data-qr-file]").addEventListener("change", (e) => {
    const file = e.target.files[0];
    if (!file) { uploaded = null; render(); return; }
    const r = new FileReader(); r.onload = () => { uploaded = r.result; setRadio("logo", "upload"); render(); }; r.readAsDataURL(file);
  });

  // Start: a ?style= in the link wins, then whatever this browser used last, then the AVI look.
  const start = document.currentScript?.dataset.startTheme || "avi";
  const fromLink = new URLSearchParams(location.search).has("style");
  if (fromLink || !loadLook()) applyLook(start);
  if (radio("logo") === "upload") setRadio("logo", "none");
  render();
})();
