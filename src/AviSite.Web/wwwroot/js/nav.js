// Mobile menu (<details>): close on Escape and return focus to the toggle; close after choosing a link.
(() => {
  const menu = document.querySelector(".nav--narrow");
  if (!menu) return;
  const toggle = menu.querySelector("summary");
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && menu.open) { menu.open = false; toggle.focus(); }
  });
  menu.addEventListener("click", (e) => { if (e.target.closest("a")) menu.open = false; });
})();
