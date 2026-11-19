// /admin/branding: the preview follows the color and font pickers before saving.
(() => {
  for (const card of document.querySelectorAll(".branding-admin__card")) {
    const set = (name, value) => card.style.setProperty(name, value);
    for (const input of card.querySelectorAll("[data-brand-color]")) {
      input.addEventListener("input", () => set(input.dataset.brandColor, input.value));
    }
    for (const select of card.querySelectorAll("[data-brand-font]")) {
      select.addEventListener("change", () => {
        const o = select.selectedOptions[0];
        if (!o) return;
        if (select.dataset.brandFont === "heading") {
          set("--brand-heading-font", o.dataset.stack);
          set("--brand-heading-weight", o.dataset.weight);
          set("--brand-heading-stretch", o.dataset.stretch);
        } else {
          set("--brand-body-font", o.dataset.stack);
        }
      });
    }
  }
})();
