const navToggle = document.querySelector(".nav-toggle");
const siteNav = document.getElementById("site-nav");
const year = document.getElementById("year");

function setupNavigation() {
  if (!navToggle || !siteNav) return;

  navToggle.addEventListener("click", () => {
    const isOpen = siteNav.classList.toggle("is-open");
    navToggle.setAttribute("aria-expanded", String(isOpen));
  });

  siteNav.addEventListener("click", (event) => {
    if (event.target instanceof HTMLAnchorElement) {
      siteNav.classList.remove("is-open");
      navToggle.setAttribute("aria-expanded", "false");
    }
  });
}

function setYear() {
  if (year) year.textContent = String(new Date().getFullYear());
}

setupNavigation();
setYear();
