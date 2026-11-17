// /admin/qr: when the Team picker changes, swap the team prefix at the start of the code's name
// ("avi-sms-flyer" → "avi-sms-electrical-flyer"), keeping whatever was typed after it. Works without this too:
// the server adds the prefix if it's missing.
(() => {
  document.querySelectorAll("select[data-prefix-for]").forEach((sel) => {
    const input = document.getElementById(sel.dataset.prefixFor);
    if (!input) return;
    let prev = sel.selectedOptions[0]?.dataset.prefix ?? "";
    sel.addEventListener("change", () => {
      const next = sel.selectedOptions[0]?.dataset.prefix ?? "";
      const rest = prev && input.value.startsWith(prev) ? input.value.slice(prev.length) : input.value;
      input.value = next + rest;
      prev = next;
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
  });
})();
