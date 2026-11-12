// Media helpers, all progressive enhancements:
//  1. Lightbox: click any photo (figure.photo, not inside a link) to see it large. Esc / click to close.
//  2. 3D models: loads the self-hosted <model-viewer> only on pages that have one.
//  3. Video carousel: arrows cycle which video sits in the big frame.
(() => {
  // ---- 1. lightbox ----
  let box;
  const lightbox = () => {
    if (box) return box;
    box = document.createElement("dialog");
    box.className = "lightbox";
    box.setAttribute("aria-label", "Photo");
    box.innerHTML = '<figure class="lightbox__figure"><img class="lightbox__img" alt="" /><figcaption class="lightbox__cap"></figcaption></figure><button class="lightbox__close" type="button" aria-label="Close">×</button>';
    box.addEventListener("click", (e) => { if (!e.target.closest(".lightbox__img") || e.target.closest(".lightbox__close")) box.close(); });
    document.body.append(box);
    return box;
  };
  document.addEventListener("click", (e) => {
    const img = e.target.closest("figure.photo:not(.no-zoom) .photo__img");
    if (!img || img.closest("a, button, .hero")) return;
    const d = lightbox(), big = d.querySelector(".lightbox__img");
    const srcs = (img.getAttribute("srcset") || "").split(",").map((s) => s.trim().split(" ")[0]).filter(Boolean);
    big.src = srcs.at(-1) || img.currentSrc || img.src;
    big.alt = img.alt;
    const cap = img.closest("figure").querySelector(".photo__caption")?.textContent || img.alt;
    d.querySelector(".lightbox__cap").textContent = cap;
    d.showModal();
  });

  // ---- 2. 3D models ----
  if (document.querySelector("model-viewer")) {
    import("/lib/model-viewer/model-viewer.min.js").catch(() => document.documentElement.classList.add("no-3d"));
    document.addEventListener("click", (e) => {
      const go = e.target.closest("[data-model-reveal]");
      if (!go) return;
      const mv = go.closest(".model")?.querySelector("model-viewer");
      mv?.dismissPoster?.();
      go.closest(".model")?.classList.add("is-live");
    });
  }

  // ---- 3. video carousel ----
  document.querySelectorAll("[data-videos]").forEach((root) => {
    const vids = JSON.parse(root.dataset.videos), frame = root.querySelector("iframe");
    const title = root.querySelector("[data-video-title]"), note = root.querySelector("[data-video-note]");
    const tabs = root.querySelectorAll("[data-video-tab]");
    let i = 0;
    const go = (n) => {
      i = (n + vids.length) % vids.length;
      const v = vids[i];
      frame.src = `https://www.youtube-nocookie.com/embed/${v.id}?rel=0`;
      frame.title = v.title;
      title.textContent = v.title;
      note.textContent = v.note || "";
      tabs.forEach((t, k) => t.setAttribute("aria-current", k === i ? "true" : "false"));
      root.querySelector("[data-video-count]").textContent = `${i + 1} / ${vids.length}`;
    };
    root.querySelector("[data-video-prev]").addEventListener("click", () => go(i - 1));
    root.querySelector("[data-video-next]").addEventListener("click", () => go(i + 1));
    tabs.forEach((t, k) => t.addEventListener("click", (e) => { e.preventDefault(); go(k); }));
    root.classList.add("is-ready");
  });
})();
