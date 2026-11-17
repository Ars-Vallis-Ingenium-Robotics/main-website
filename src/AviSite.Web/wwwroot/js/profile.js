// /admin/profile: pick a photo, drag it into the circle, zoom; on save, send two square WebPs (320 and 160 px).
// Without this script the chosen file is sent as-is (the server checks it's a JPEG/PNG/WebP under 2 MB).
(() => {
  const form = document.querySelector("[data-profile-form]");
  if (!form) return;
  const pick = form.querySelector("[data-photo-pick]");
  const canvas = form.querySelector("[data-photo-canvas]");
  const zoom = form.querySelector("[data-photo-zoom]");
  const zoomWrap = form.querySelector("[data-photo-zoom-wrap]");
  const current = form.querySelector("[data-photo-current]");
  const hint = form.querySelector("[data-photo-hint]");
  const small = form.querySelector("[data-photo-small]");
  const large = form.querySelector("[data-photo-large]");
  const ctx = canvas.getContext("2d");
  const S = canvas.width;
  let img = null, z = 1, cx = 0.5, cy = 0.5; // zoom, and the image point at the circle's centre (0–1)

  // Largest square that fits, shrunk by the zoom, centred on (cx, cy) and kept inside the image.
  function crop() {
    const side = Math.min(img.width, img.height) / z;
    const x = Math.min(Math.max(cx * img.width - side / 2, 0), img.width - side);
    const y = Math.min(Math.max(cy * img.height - side / 2, 0), img.height - side);
    cx = (x + side / 2) / img.width; cy = (y + side / 2) / img.height;
    return { x, y, side };
  }
  function draw(target, size) {
    const c = crop(), g = target.getContext("2d");
    g.imageSmoothingQuality = "high";
    g.clearRect(0, 0, size, size);
    g.drawImage(img, c.x, c.y, c.side, c.side, 0, 0, size, size);
  }

  pick.addEventListener("change", () => {
    const file = pick.files[0];
    if (!file) return;
    if (!/^image\/(jpeg|png|webp)$/.test(file.type)) { hint.textContent = "That isn't a JPEG, PNG or WebP."; pick.value = ""; return; }
    const url = URL.createObjectURL(file);
    const i = new Image();
    i.onload = () => {
      img = i; z = 1; cx = cy = 0.5; zoom.value = "1";
      canvas.hidden = false; zoomWrap.hidden = false; if (current) current.hidden = true;
      hint.textContent = "Drag to move it in the circle, zoom to fit. Saved when you press Save.";
      draw(canvas, S);
      URL.revokeObjectURL(url);
    };
    i.onerror = () => { hint.textContent = "Couldn't open that image."; pick.value = ""; };
    i.src = url;
  });

  zoom.addEventListener("input", () => { z = Number(zoom.value); if (img) draw(canvas, S); });

  let drag = null;
  canvas.addEventListener("pointerdown", (e) => { if (!img) return; drag = { x: e.clientX, y: e.clientY, cx, cy }; canvas.setPointerCapture(e.pointerId); });
  canvas.addEventListener("pointermove", (e) => {
    if (!drag) return;
    const side = Math.min(img.width, img.height) / z, px = canvas.getBoundingClientRect().width;
    cx = drag.cx - ((e.clientX - drag.x) / px) * side / img.width;
    cy = drag.cy - ((e.clientY - drag.y) / px) * side / img.height;
    draw(canvas, S);
  });
  const end = () => { drag = null; };
  canvas.addEventListener("pointerup", end); canvas.addEventListener("pointercancel", end);
  // Keyboard: arrows move, +/- zoom.
  canvas.tabIndex = 0;
  canvas.addEventListener("keydown", (e) => {
    if (!img) return;
    const step = 0.02 / z, k = e.key;
    if (k === "ArrowLeft") cx -= step; else if (k === "ArrowRight") cx += step;
    else if (k === "ArrowUp") cy -= step; else if (k === "ArrowDown") cy += step;
    else if (k === "+" || k === "=") z = Math.min(3, z + 0.1); else if (k === "-") z = Math.max(1, z - 0.1);
    else return;
    e.preventDefault(); zoom.value = String(z); draw(canvas, S);
  });

  const blob = (size) => new Promise((resolve) => {
    const c = document.createElement("canvas"); c.width = c.height = size;
    draw(c, size);
    c.toBlob(resolve, "image/webp", 0.86);
  });

  let ready = false;
  form.addEventListener("submit", async (e) => {
    if (!img || ready) return;
    e.preventDefault();
    const [b320, b160] = await Promise.all([blob(320), blob(160)]);
    if (!b320 || b320.type !== "image/webp") { ready = true; form.requestSubmit(); return; } // no WebP encoder: send the original
    const put = (input, b, n) => { const dt = new DataTransfer(); dt.items.add(new File([b], n, { type: "image/webp" })); input.files = dt.files; };
    put(large, b320, "photo-320.webp"); put(small, b160, "photo-160.webp");
    pick.removeAttribute("name"); // the original stays on this computer
    ready = true;
    form.requestSubmit();
  });
})();
