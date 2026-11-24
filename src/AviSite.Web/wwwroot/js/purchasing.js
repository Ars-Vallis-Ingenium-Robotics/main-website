(() => {
  const form = document.querySelector(".purchasing__form");
  if (!form) return;
  const rows = form.querySelector("[data-purchase-lines]");
  const template = form.querySelector("[data-item-template]");
  const add = form.querySelector("[data-add-item]");
  const count = form.querySelector("[data-item-count]");
  if (!rows || !template || !add) return;

  const refresh = () => {
    const items = [...rows.querySelectorAll("[data-item-row]")];
    items.forEach((row, index) => {
      const number = index + 1;
      const legend = row.querySelector("legend");
      const remove = row.querySelector("[data-remove-item]");
      if (legend) legend.textContent = `Item ${number}`;
      if (remove) {
        remove.setAttribute("aria-label", `Remove item ${number}`);
        remove.disabled = items.length <= 1;
      }
    });
    if (count) count.textContent = `${items.length} of 20 item rows`;
    add.disabled = items.length >= 20;
    add.setAttribute("aria-disabled", String(items.length >= 20));
  };

  add.addEventListener("click", () => {
    if (rows.querySelectorAll("[data-item-row]").length >= 20) return;
    rows.append(template.content.cloneNode(true));
    refresh();
    rows.querySelector("[data-item-row]:last-of-type input[name='itemName']")?.focus();
  });
  rows.addEventListener("click", (event) => {
    const button = event.target.closest("[data-remove-item]");
    if (!button || rows.querySelectorAll("[data-item-row]").length <= 1) return;
    button.closest("[data-item-row]").remove();
    refresh();
  });
  refresh();
})();
