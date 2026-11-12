// Pop-up detail cards. Links with [data-dialog] open the <dialog> they point to (#id).
// Without JS the link still jumps to the dialog, which CSS shows via :target.
(() => {
  document.addEventListener("click", (e) => {
    const open = e.target.closest("a[data-dialog]");
    if (open) {
      const d = document.getElementById(open.getAttribute("href").slice(1));
      if (d && d.showModal) { e.preventDefault(); d.showModal(); d.dataset.opener = open.id || ""; }
      return;
    }
    const close = e.target.closest("[data-dialog-close]");
    if (close) { const d = close.closest("dialog"); if (d && d.open) { e.preventDefault(); d.close(); } return; }
    if (e.target.tagName === "DIALOG" && e.target.open) e.target.close(); // backdrop click
  });
  document.addEventListener("close", (e) => {
    if (e.target.tagName === "DIALOG" && e.target.dataset.opener) document.getElementById(e.target.dataset.opener)?.focus();
  }, true);
})();
