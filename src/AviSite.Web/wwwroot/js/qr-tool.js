// QR code maker (/tools/qr). Everything runs in the browser: nothing typed here leaves the page.
// lib/qrcode-generator.js (Kazuhiko Arase, MIT) works out which squares are dark; everything you see is drawn here as SVG,
// then rasterized on a canvas for PNG/JPG. "Many codes" draws a batch with the same look and zips it or prints a sheet.
(() => {
  const root = document.querySelector("[data-qr-tool]");
  if (!root || typeof qrcode !== "function") return;
  qrcode.stringToBytes = qrcode.stringToBytesFuncs["UTF-8"];
  const startTheme = document.currentScript?.dataset.startTheme || "avi";

  const $ = (s) => root.querySelector(s);
  const $$ = (s) => [...root.querySelectorAll(s)];
  const page = document.querySelector(".qr-section") || document;
  const field = (k) => page.querySelector(`[data-qr="${k}"]:not([type="radio"])`);
  const radio = (k) => (page.querySelector(`[data-qr="${k}"]:checked`) || {}).value;
  const val = (k) => { const el = field(k); return el ? (el.type === "checkbox" ? el.checked : el.value) : ""; };
  const setRadio = (k, v) => { const el = page.querySelector(`[data-qr="${k}"][value="${v}"]`); if (el) el.checked = true; };
  const setVal = (k, v) => { const el = field(k); if (el) el.value = v; };

  // ---------- Looks ----------
  const LOOKS = {
    avi:     { dot: "fluid", fill: "linear", fg: "#101011", fg2: "#0c63e6", angle: 135, eyeframe: "leaf", eyeball: "rounded", eye: "#0c63e6", eye2: "#101011", bgmode: "solid", bg: "#ffffff", bg2: "#e8f0fe", radius: 3, logo: "avi", plate: "none", lsize: 24, frame: "below", ftext: "SCAN ME", fcolor: "#0c63e6", ftcolor: "#ffffff" },
    sms:     { dot: "dots", fill: "radial", fg: "#1d8f88", fg2: "#06262b", angle: 45, eyeframe: "circle", eyeball: "circle", eye: "#06262b", eye2: "#1d8f88", bgmode: "solid", bg: "#f7f1e6", bg2: "#e4f6f4", radius: 4, logo: "sms", plate: "none", lsize: 28, frame: "badge", ftext: "DIVE IN", fcolor: "#06262b", ftcolor: "#f7f1e6" },
    classic: { dot: "square", fill: "solid", fg: "#000000", fg2: "#000000", angle: 45, eyeframe: "square", eyeball: "square", eye: "#000000", eye2: "#000000", bgmode: "solid", bg: "#ffffff", bg2: "#ffffff", radius: 0, logo: "none", plate: "none", lsize: 22, frame: "none", ftext: "SCAN ME", fcolor: "#000000", ftcolor: "#ffffff" },
    ocean:   { dot: "fluid", fill: "linear", fg: "#0b3d91", fg2: "#138a83", angle: 135, eyeframe: "rounded", eyeball: "circle", eye: "#0b3d91", eye2: "#138a83", bgmode: "solid", bg: "#e4f6f4", bg2: "#ffffff", radius: 3, logo: "none", plate: "none", lsize: 22, frame: "below", ftext: "SCAN ME", fcolor: "#0b3d91", ftcolor: "#ffffff" },
    sunset:  { dot: "classy", fill: "linear", fg: "#b0124f", fg2: "#c24a00", angle: 45, eyeframe: "leaf", eyeball: "leaf", eye: "#b0124f", eye2: "#c24a00", bgmode: "linear", bg: "#fff4d6", bg2: "#fde8ec", radius: 3, logo: "none", plate: "none", lsize: 22, frame: "none", ftext: "SCAN ME", fcolor: "#b0124f", ftcolor: "#ffffff" },
    forest:  { dot: "vertical", fill: "radial", fg: "#2e7d32", fg2: "#123d17", angle: 45, eyeframe: "rounded", eyeball: "rounded", eye: "#1b5e20", eye2: "#2e7d32", bgmode: "solid", bg: "#f1f8e9", bg2: "#ffffff", radius: 2, logo: "none", plate: "none", lsize: 22, frame: "badge", ftext: "SCAN ME", fcolor: "#1b5e20", ftcolor: "#ffffff" },
    mono:    { dot: "tiny", fill: "solid", fg: "#101011", fg2: "#101011", angle: 45, eyeframe: "circle", eyeball: "diamond", eye: "#101011", eye2: "#101011", bgmode: "solid", bg: "#f4f1ea", bg2: "#ffffff", radius: 2, logo: "none", plate: "none", lsize: 22, frame: "none", ftext: "SCAN ME", fcolor: "#101011", ftcolor: "#f4f1ea" },
  };
  const LOOK_KEYS = Object.keys(LOOKS.avi);
  const EXTRA_KEYS = ["margin", "ecl", "size"];
  const RADIOS = new Set(["dot", "eyeframe", "eyeball", "logo", "frame"]);
  const STORE = "avi-qr-look";
  const LOGOS = { avi: "/img/brand/avi-dog-480.webp", sms: "/img/brand/sms-mark.webp", smsink: "/img/brand/sms-mark-ink.webp" };

  function applyLook(name) {
    const look = LOOKS[name] || LOOKS.avi;
    for (const k of LOOK_KEYS) (RADIOS.has(k) ? setRadio : setVal)(k, look[k]);
    pressTheme(name);
  }
  const pressTheme = (name) => $$("[data-qr-theme]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.qrTheme === name)));
  function readLook() { const l = {}; for (const k of [...LOOK_KEYS, ...EXTRA_KEYS]) l[k] = RADIOS.has(k) ? radio(k) : val(k); return l; }
  function saveLook() { try { localStorage.setItem(STORE, JSON.stringify({ ...readLook(), theme: currentTheme })); } catch { /* fine */ } }
  function loadLook() {
    try {
      const l = JSON.parse(localStorage.getItem(STORE) || "null");
      if (!l) return false;
      for (const [k, v] of Object.entries(l)) {
        if (k === "theme" || v === undefined || v === null || (k === "logo" && v === "upload")) continue;
        (RADIOS.has(k) ? setRadio : setVal)(k, v);
      }
      currentTheme = l.theme || null; pressTheme(currentTheme);
      return true;
    } catch { return false; }
  }

  // ---------- What a single code says ----------
  const esc = (s) => String(s).replace(/([\\;,:"])/g, "\\$1");
  function payload() {
    switch (val("kind")) {
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

  // ---------- A batch ----------
  const MAX_BULK = 500;
  function bulkItems() {
    const items = [];
    if (radio("bulkmode") === "number") {
      const a = Math.max(0, parseInt(val("nfrom"), 10) || 0), b = Math.min(a + MAX_BULK - 1, parseInt(val("nto"), 10) || 0);
      const link = val("nlink").trim(), label = val("nlabel").trim();
      if (!link) return items;
      for (let n = a; n <= b; n++) items.push({ label: (label || link).replaceAll("{n}", n), text: link.replaceAll("{n}", n) });
      return items;
    }
    for (const raw of val("bulktext").split(/\r?\n/)) {
      const line = raw.trim();
      if (!line) continue;
      let label, text;
      if (line.includes("\t")) { const i = line.indexOf("\t"); label = line.slice(0, i).trim(); text = line.slice(i + 1).trim(); }
      else {
        const m = line.match(/(https?:\/\/\S+|mailto:\S+|tel:\S+|www\.\S+)/i);
        if (m) { text = m[1]; label = line.slice(0, m.index).replace(/[,;|\s]+$/, "").trim(); }
        else { text = line; label = ""; }
      }
      if (!text) continue;
      items.push({ label: label || text.replace(/^https?:\/\//, ""), text });
      if (items.length >= MAX_BULK) break;
    }
    return items;
  }

  // ---------- Drawing helpers ----------
  const f = (n) => +n.toFixed(3);
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
  const xml = (s) => String(s).replace(/[<>&"]/g, (ch) => ({ "<": "&lt;", ">": "&gt;", "&": "&amp;", '"': "&quot;" }[ch]));

  // ---------- Build one SVG ----------
  let uid = 0;
  function build(s, logoData) {
    const ecl = s.ecl === "auto" ? (logoData ? "H" : "M") : s.ecl;
    const qr = qrcode(0, ecl);
    qr.addData(s.text);
    qr.make();
    const n = qr.getModuleCount(), m = +s.margin, W = n + 2 * m, id = "q" + (++uid);
    const isEye = (r, c) => (r < 7 && c < 7) || (r < 7 && c >= n - 7) || (r >= n - 7 && c < 7);

    let hole = 0, h0 = 0, h1 = 0;
    if (logoData) { hole = Math.max(3, Math.round((n * s.lsize) / 100)) | 1; h0 = (n - hole) / 2; h1 = h0 + hole; }
    const inHole = (r, c) => {
      if (!hole) return false;
      if (s.plate === "circle") return Math.hypot(r + 0.5 - n / 2, c + 0.5 - n / 2) <= hole / 2 + 0.6;
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
    if (s.fill !== "solid") { defs.push(gradient(id + "fg", s.fill, s.fg, s.fg2, +s.angle, m, m, n)); fill = `url(#${id}fg)`; }

    const framed = s.frame !== "none" && !!s.ftext;
    const pad = framed ? 1.4 : 0, band = framed ? 6 : 0;
    const TW = W + 2 * pad, TH = W + 2 * pad + (s.frame === "badge" ? band * 0.6 : band);
    const ox = pad, oy = pad + (framed && s.frame === "above" ? band : 0);

    const parts = [];
    if (framed && s.frame !== "badge") parts.push(`<rect width="${f(TW)}" height="${f(TH)}" rx="${Math.max(1.5, +s.radius)}" fill="${s.fcolor}"/>`);
    if (s.bgmode !== "none") {
      let bgFill = s.bg;
      if (s.bgmode === "linear") { defs.push(gradient(id + "bg", "linear", s.bg, s.bg2, 135, ox, oy, W)); bgFill = `url(#${id}bg)`; }
      parts.push(`<rect x="${ox}" y="${oy}" width="${W}" height="${W}" rx="${+s.radius}" fill="${bgFill}"/>`);
    }
    if (framed && s.frame === "badge") parts.push(`<rect x="${f(ox - 0.7)}" y="${f(oy - 0.7)}" width="${f(W + 1.4)}" height="${f(W + 1.4)}" rx="${+s.radius + 1.2}" fill="none" stroke="${s.fcolor}" stroke-width="0.7"/>`);

    parts.push(`<g transform="translate(${ox} ${oy})">`, `<path fill="${fill}" d="${d}"/>`);
    for (const [ex, ey] of [[m, m], [m + n - 7, m], [m, m + n - 7]]) {
      parts.push(`<path fill="${s.eye}" fill-rule="evenodd" d="${eyeFrame(s.eyeframe, ex, ey)}"/>`, `<path fill="${s.eye2}" d="${eyeBall(s.eyeball, ex, ey)}"/>`);
    }
    if (logoData) {
      const plateFill = s.bgmode === "none" ? "#ffffff" : s.bg;
      const cx = m + n / 2, size = hole - (s.plate === "none" ? 0.6 : 1.2);
      if (s.plate === "square") parts.push(`<rect x="${f(m + h0)}" y="${f(m + h0)}" width="${hole}" height="${hole}" rx="${f(hole * 0.18)}" fill="${plateFill}"/>`);
      if (s.plate === "circle") parts.push(`<circle cx="${f(cx)}" cy="${f(cx)}" r="${f(hole / 2 + 0.3)}" fill="${plateFill}"/>`);
      const inset = s.plate === "circle" ? size * 0.12 : 0;
      parts.push(`<image href="${logoData}" x="${f(cx - size / 2 + inset)}" y="${f(cx - size / 2 + inset)}" width="${f(size - 2 * inset)}" height="${f(size - 2 * inset)}" preserveAspectRatio="xMidYMid meet"/>`);
    }
    parts.push(`</g>`);

    if (framed) {
      const label = xml(s.ftext);
      const font = `font-family="Archivo, 'Helvetica Neue', Arial, sans-serif" font-weight="800" text-anchor="middle" letter-spacing="0.06em"`;
      if (s.frame === "badge") {
        const pw = Math.min(TW - 4, Math.max(10, s.ftext.length * 1.35 + 4)), ph = 3.6, py = oy + W + 0.7 - ph / 2;
        const fs = Math.min(2.1, (pw - 2) / Math.max(4, s.ftext.length * 0.72));
        parts.push(`<rect x="${f(TW / 2 - pw / 2)}" y="${f(py)}" width="${f(pw)}" height="${ph}" rx="${ph / 2}" fill="${s.fcolor}"/>`);
        parts.push(`<text x="${f(TW / 2)}" y="${f(py + ph / 2 + fs * 0.36)}" font-size="${f(fs)}" fill="${s.ftcolor}" ${font}>${label}</text>`);
      } else {
        const by = s.frame === "above" ? pad / 2 : oy + W;
        const fs = Math.min(3.2, (TW - 4) / Math.max(4, s.ftext.length * 0.72));
        parts.push(`<text x="${f(TW / 2)}" y="${f(by + band / 2 + fs * 0.36)}" font-size="${f(fs)}" fill="${s.ftcolor}" ${font}>${label}</text>`);
      }
    }

    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${f(TW)} ${f(TH)}" width="${f(TW * 10)}" height="${f(TH * 10)}" shape-rendering="${s.dot === "square" || s.dot === "tiny" ? "crispEdges" : "geometricPrecision"}">` +
      (defs.length ? `<defs>${defs.join("")}</defs>` : "") + parts.join("") + `</svg>`;
    return { svg, n, ecl, ratio: TH / TW };
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
    if (darks.some((d) => lights.some((l) => lum(d) > lum(l)))) w.push("Light dots on a darker background don't scan on some phones. Keep the dots darker.");
    if (s.bgmode === "none") w.push("Transparent background: put it on something light.");
    if (hasLogo && ecl !== "H" && ecl !== "Q") w.push("With a logo, set error correction to High.");
    if (+s.margin < 2) w.push("A quiet zone under 2 dots can stop some scanners.");
    return w;
  }

  // ---------- State ----------
  const logoCache = new Map();
  let uploaded = null, current = null, seq = 0, currentTheme = startTheme, mode = "one";

  async function dataUrl(url) {
    if (!url) return null;
    if (logoCache.has(url)) return logoCache.get(url);
    const blob = await (await fetch(url)).blob();
    const out = await new Promise((ok) => { const r = new FileReader(); r.onload = () => ok(r.result); r.readAsDataURL(blob); });
    logoCache.set(url, out);
    return out;
  }
  const look = () => { const l = readLook(); l.lsize = +l.lsize; l.size = +l.size; return l; };
  async function logoFor(l) {
    try { return l.logo === "upload" ? uploaded : l.logo !== "none" ? await dataUrl(LOGOS[l.logo]) : null; } catch { return null; }
  }

  function showWhen(l) {
    const on = { gradient: l.fill !== "solid", linear: l.fill === "linear", bg: l.bgmode !== "none", bggradient: l.bgmode === "linear",
      upload: l.logo === "upload", haslogo: l.logo !== "none", frame: l.frame !== "none", bulkmode: mode === "bulk" };
    page.querySelectorAll("[data-when]").forEach((el) => (el.hidden = !on[el.dataset.when]));
    $$(".qr-kind").forEach((el) => (el.hidden = el.dataset.kind !== val("kind")));
    $$("[data-bulk]").forEach((el) => (el.hidden = el.dataset.bulk !== radio("bulkmode")));
    for (const k of ["angle", "radius", "lsize", "size", "margin"]) { const o = page.querySelector(`[data-out="${k}"]`); if (o) o.textContent = val(k); }
  }

  // ---------- Render: one ----------
  async function renderOne(l, logo) {
    const preview = page.querySelector("[data-qr-preview]"), status = page.querySelector("[data-qr-status]"), warn = page.querySelector("[data-qr-warn]");
    const text = payload();
    page.querySelectorAll('[data-qr-pane="one"] [data-qr-download], [data-qr-copy]').forEach((b) => (b.disabled = !text));
    if (!text) { preview.innerHTML = ""; warn.innerHTML = ""; status.textContent = "Fill in what the code should open."; current = null; return; }
    try {
      const out = build({ ...l, text }, logo);
      preview.innerHTML = out.svg;
      preview.style.setProperty("--ratio", out.ratio);
      preview.classList.toggle("qr-tool__preview--clear", l.bgmode === "none");
      current = { ...out, name: text, size: l.size };
      status.textContent = `${out.n} × ${out.n} squares · error correction ${out.ecl}${logo ? " · with logo" : ""}`;
      warn.innerHTML = warnings(l, !!logo, out.ecl).map((t) => `<li>${t}</li>`).join("");
    } catch {
      preview.innerHTML = ""; current = null; warn.innerHTML = "";
      status.textContent = "That's too much for one QR code. Shorten it, or lower the error correction.";
    }
  }

  // ---------- Render: bulk ----------
  let batch = [];
  function bulkLook(l, item) {
    if (!val("bulklabels")) return { ...l, text: item.text };
    return { ...l, text: item.text, frame: l.frame === "none" ? "below" : l.frame, ftext: item.label.slice(0, 40) };
  }
  async function renderBulk(l, logo) {
    const items = bulkItems();
    const grid = page.querySelector("[data-qr-bulkgrid]"), count = page.querySelector("[data-qr-bulkcount]"), warn = page.querySelector("[data-qr-bulkwarn]");
    page.querySelectorAll("[data-qr-zip], [data-qr-print]").forEach((b) => (b.disabled = !items.length));
    batch = [];
    if (!items.length) { grid.innerHTML = ""; warn.innerHTML = ""; count.textContent = "Add some lines to make a batch."; return; }
    let bad = 0;
    for (const it of items) {
      try { batch.push({ ...it, ...build(bulkLook(l, it), logo) }); } catch { bad++; }
    }
    const shown = batch.slice(0, 24);
    grid.innerHTML = shown.map((b) => `<li><div class="qr-bulk__code">${b.svg}</div>${val("bulklabels") ? "" : `<span>${xml(b.label)}</span>`}</li>`).join("");
    count.textContent = `${batch.length} code${batch.length === 1 ? "" : "s"}${batch.length > shown.length ? `, showing the first ${shown.length}` : ""}.`;
    const w = warnings(l, !!logo, batch[0]?.ecl || "M");
    if (bad) w.unshift(`${bad} line${bad === 1 ? " was" : "s were"} too long for a QR code and left out.`);
    warn.innerHTML = w.map((t) => `<li>${t}</li>`).join("");
  }

  async function render() {
    const l = look();
    showWhen(l);
    const my = ++seq;
    const logo = await logoFor(l);
    if (my !== seq) return;
    if (mode === "bulk") await renderBulk(l, logo); else await renderOne(l, logo);
    saveLook();
  }

  // Each look button shows that look on a sample code.
  async function drawThumbs() {
    for (const b of $$("[data-qr-thumb]")) {
      const name = b.dataset.qrThumb, l = { ...LOOKS[name], margin: 2, ecl: "auto", size: 300, frame: "none" };
      const logo = l.logo !== "none" ? await dataUrl(LOGOS[l.logo]).catch(() => null) : null;
      try { b.innerHTML = build({ ...l, text: "https://avirobotics.org" }, logo).svg; } catch { /* skip */ }
    }
  }

  // ---------- Export ----------
  const slug = (t) => (t || "qr").replace(/^[a-z]+:\/*/i, "").replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").slice(0, 40).toLowerCase() || "qr";
  function save(blob, name) {
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob); a.download = name;
    document.body.appendChild(a); a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1500);
  }
  async function raster(svg, ratio, size, type) {
    const img = new Image();
    const url = URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" }));
    await new Promise((ok, bad) => { img.onload = ok; img.onerror = bad; img.src = url; });
    const canvas = document.createElement("canvas");
    canvas.width = size; canvas.height = Math.round(size * ratio);
    const g = canvas.getContext("2d");
    if (type === "image/jpeg") { g.fillStyle = "#ffffff"; g.fillRect(0, 0, canvas.width, canvas.height); }
    g.drawImage(img, 0, 0, canvas.width, canvas.height);
    URL.revokeObjectURL(url);
    return new Promise((ok) => canvas.toBlob(ok, type, 0.95));
  }

  // Minimal ZIP writer ("stored", no compression: PNGs are already compressed).
  const CRC = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
  const crc32 = (u8) => { let c = 0xffffffff; for (let i = 0; i < u8.length; i++) c = CRC[(c ^ u8[i]) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  function zip(files) {
    const enc = new TextEncoder(), chunks = [], central = [];
    let offset = 0;
    const now = new Date(), dosTime = (now.getHours() << 11) | (now.getMinutes() << 5) | (now.getSeconds() >> 1), dosDate = ((now.getFullYear() - 1980) << 9) | ((now.getMonth() + 1) << 5) | now.getDate();
    for (const { name, data } of files) {
      const nm = enc.encode(name), crc = crc32(data);
      const h = new DataView(new ArrayBuffer(30));
      h.setUint32(0, 0x04034b50, true); h.setUint16(4, 20, true); h.setUint16(6, 0x0800, true); h.setUint16(8, 0, true);
      h.setUint16(10, dosTime, true); h.setUint16(12, dosDate, true); h.setUint32(14, crc, true); h.setUint32(18, data.length, true); h.setUint32(22, data.length, true);
      h.setUint16(26, nm.length, true); h.setUint16(28, 0, true);
      chunks.push(new Uint8Array(h.buffer), nm, data);
      const c = new DataView(new ArrayBuffer(46));
      c.setUint32(0, 0x02014b50, true); c.setUint16(4, 20, true); c.setUint16(6, 20, true); c.setUint16(8, 0x0800, true); c.setUint16(10, 0, true);
      c.setUint16(12, dosTime, true); c.setUint16(14, dosDate, true); c.setUint32(16, crc, true); c.setUint32(20, data.length, true); c.setUint32(24, data.length, true);
      c.setUint16(28, nm.length, true); c.setUint32(42, offset, true);
      central.push(new Uint8Array(c.buffer), nm);
      offset += 30 + nm.length + data.length;
    }
    const size = central.reduce((s, a) => s + a.length, 0);
    const e = new DataView(new ArrayBuffer(22));
    e.setUint32(0, 0x06054b50, true); e.setUint16(8, files.length, true); e.setUint16(10, files.length, true); e.setUint32(12, size, true); e.setUint32(16, offset, true);
    return new Blob([...chunks, ...central, new Uint8Array(e.buffer)], { type: "application/zip" });
  }

  page.querySelector('[data-qr-download="svg"]').addEventListener("click", () => current && save(new Blob([current.svg], { type: "image/svg+xml" }), `qr-${slug(current.name)}.svg`));
  page.querySelector('[data-qr-download="png"]').addEventListener("click", async () => current && save(await raster(current.svg, current.ratio, current.size, "image/png"), `qr-${slug(current.name)}.png`));
  page.querySelector('[data-qr-download="jpg"]').addEventListener("click", async () => current && save(await raster(current.svg, current.ratio, current.size, "image/jpeg"), `qr-${slug(current.name)}.jpg`));
  page.querySelector("[data-qr-copy]").addEventListener("click", async () => {
    if (!current) return;
    const status = page.querySelector("[data-qr-status]");
    try { await navigator.clipboard.write([new ClipboardItem({ "image/png": raster(current.svg, current.ratio, current.size, "image/png") })]); status.textContent = "Copied. Paste it anywhere."; }
    catch { status.textContent = "Your browser won't copy images here. Use Download PNG instead."; }
  });
  page.querySelectorAll("[data-qr-zip]").forEach((btn) => btn.addEventListener("click", async () => {
    if (!batch.length) return;
    const kind = btn.dataset.qrZip, count = page.querySelector("[data-qr-bulkcount]"), size = +val("size"), used = new Map(), files = [];
    const label = btn.textContent;
    btn.disabled = true;
    for (let i = 0; i < batch.length; i++) {
      const b = batch[i];
      let base = `${String(i + 1).padStart(String(batch.length).length, "0")}-${slug(b.label)}`;
      used.set(base, (used.get(base) || 0) + 1); if (used.get(base) > 1) base += "-" + used.get(base);
      const data = kind === "svg" ? new TextEncoder().encode(b.svg) : new Uint8Array(await (await raster(b.svg, b.ratio, size, "image/png")).arrayBuffer());
      files.push({ name: `${base}.${kind}`, data });
      btn.textContent = `Making ${i + 1} of ${batch.length}…`;
    }
    save(zip(files), `qr-codes-${files.length}.zip`);
    btn.textContent = label; btn.disabled = false;
    count.textContent = `${files.length} codes zipped.`;
  }));
  page.querySelector("[data-qr-print]").addEventListener("click", () => {
    if (!batch.length) return;
    const per = +val("perpage"), cols = { 6: 2, 12: 3, 20: 4, 30: 5 }[per] || 3;
    const sheet = document.querySelector("[data-qr-printsheet]");
    let html = "";
    for (let i = 0; i < batch.length; i += per) {
      html += `<div class="qr-print__page" style="--cols:${cols}">` + batch.slice(i, i + per).map((b) => `<figure>${b.svg}${val("bulklabels") ? "" : `<figcaption>${xml(b.label)}</figcaption>`}</figure>`).join("") + `</div>`;
    }
    sheet.innerHTML = html;
    document.body.classList.add("is-printing-qr");
    const done = () => { document.body.classList.remove("is-printing-qr"); window.removeEventListener("afterprint", done); };
    window.addEventListener("afterprint", done);
    setTimeout(() => window.print(), 50);
  });

  // ---------- Wiring ----------
  // Keep Enter from submitting/reloading the tool form, without an inline CSP-blocked handler.
  root.querySelector("form").addEventListener("submit", (e) => e.preventDefault());
  function setMode(m) {
    mode = m;
    page.querySelectorAll("[data-qr-mode]").forEach((b) => b.setAttribute("aria-selected", String(b.dataset.qrMode === m)));
    page.querySelectorAll("[data-qr-pane]").forEach((p) => (p.hidden = p.dataset.qrPane !== m));
    render();
  }
  page.querySelectorAll("[data-qr-mode]").forEach((b) => b.addEventListener("click", () => setMode(b.dataset.qrMode)));
  $$("[data-qr-tab]").forEach((t) => t.addEventListener("click", () => {
    $$("[data-qr-tab]").forEach((x) => x.setAttribute("aria-selected", String(x === t)));
    $$("[data-qr-panel]").forEach((p) => (p.hidden = p.dataset.qrPanel !== t.dataset.qrTab));
  }));
  root.addEventListener("input", (e) => {
    if (!e.target.matches("[data-qr]")) return;
    if (LOOK_KEYS.includes(e.target.dataset.qr)) { currentTheme = null; pressTheme(null); }
    render();
  });
  root.addEventListener("change", (e) => { if (e.target.matches("[data-qr]")) render(); });
  page.querySelector('[data-qr="perpage"]').addEventListener("change", saveLook);
  $$("[data-qr-theme]").forEach((b) => b.addEventListener("click", () => { currentTheme = b.dataset.qrTheme; applyLook(currentTheme); render(); }));
  $$("[data-qr-bg]").forEach((b) => b.addEventListener("click", () => { setVal("bg", b.dataset.qrBg); if (val("bgmode") === "none") setVal("bgmode", "solid"); currentTheme = null; pressTheme(null); render(); }));
  $("[data-qr-reset]").addEventListener("click", () => { currentTheme = "avi"; applyLook("avi"); setVal("margin", 4); setVal("ecl", "auto"); setVal("size", 1200); render(); });
  $("[data-qr-file]").addEventListener("change", (e) => {
    const file = e.target.files[0];
    if (!file) { uploaded = null; render(); return; }
    const r = new FileReader(); r.onload = () => { uploaded = r.result; setRadio("logo", "upload"); render(); }; r.readAsDataURL(file);
  });

  // Start: ?style= in the link wins, then this browser's last look, then AVI. A #bulk=… link opens "Many codes" filled in.
  const fromLink = new URLSearchParams(location.search).has("style");
  if (fromLink || !loadLook()) { currentTheme = startTheme; applyLook(startTheme); }
  if (radio("logo") === "upload") setRadio("logo", "none");
  const hash = new URLSearchParams(location.hash.slice(1));
  if (hash.get("bulk")) { setVal("bulktext", hash.get("bulk")); setRadio("bulkmode", "list"); mode = "bulk"; }
  setMode(mode);
  drawThumbs();
})();
