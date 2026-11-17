document.documentElement.classList.add("js");
// Theme: the device's setting, unless this visitor pressed the toggle (then "dark" or "light" is remembered here only).
// Runs in <head> before the page paints, so dark mode never flashes white first.
// SMS pages keep their own light design for now.
(() => {
  const sms = location.hostname.startsWith("sms.") || location.pathname === "/sms" || location.pathname.startsWith("/sms/");
  let pick = null;
  try { pick = localStorage.getItem("avi-theme"); } catch { /* private mode: follow the device */ }
  if (sms) document.documentElement.dataset.theme = "light";
  else if (pick === "dark" || pick === "light") document.documentElement.dataset.theme = pick;
})();
