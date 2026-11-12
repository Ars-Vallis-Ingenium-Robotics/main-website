// Level-of-detail swap: keep the blurred placeholder until the sharp image has decoded, then fade it in.
(() => {
  const done = (img) => img.classList.add("is-loaded");
  for (const img of document.querySelectorAll("img.photo__img")) {
    if (img.complete) done(img);
    else {
      img.addEventListener("load", () => done(img), { once: true });
      img.addEventListener("error", () => done(img), { once: true });
    }
  }
})();
