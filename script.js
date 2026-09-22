const clubs = [
  {
    label: "AVI Robotics",
    mainText: "Ars Vallis Ingenium Robotics",
    alt: "Ars Vallis Ingenium Robotics logo",
    src: "./Logos/aviDogDefault.png",
  },
  {
    label: "Sequoia Marine Solutions",
    mainText: "MATE ROV competition team supported by AVI Robotics",
    alt: "Sequoia Marine Solutions logo",
    src: "./Logos/smsLogo.png",
  },
];

const SWAP_EVERY = 4200;
const FADE_MS = 420;

const logoImg = document.getElementById("logo");
const logoLabel = document.getElementById("logoLabel");
const clubText = document.getElementById("clubText");
const navToggle = document.querySelector(".nav-toggle");
const siteNav = document.getElementById("site-nav");
const year = document.getElementById("year");

let idx = 0;
let timer = null;

const prefersReducedMotion =
  window.matchMedia &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

function preloadLogos() {
  clubs.forEach((club) => {
    const img = new Image();
    img.src = club.src;
  });
}

function setClub(i) {
  const club = clubs[i];
  if (!logoImg || !logoLabel || !clubText) return;

  logoImg.src = club.src;
  logoImg.alt = club.alt;
  logoLabel.textContent = club.label;
  clubText.textContent = club.mainText;
}

function swapClub() {
  if (!logoImg || !clubText) return;

  if (prefersReducedMotion) {
    idx = (idx + 1) % clubs.length;
    setClub(idx);
    return;
  }

  logoImg.classList.add("fading");
  clubText.style.opacity = "0";
  clubText.style.transform = "translateY(4px)";
  clubText.style.transition = `opacity ${FADE_MS}ms ease, transform ${FADE_MS}ms ease`;

  window.setTimeout(() => {
    idx = (idx + 1) % clubs.length;
    setClub(idx);

    window.requestAnimationFrame(() => {
      logoImg.classList.remove("fading");
      clubText.style.opacity = "1";
      clubText.style.transform = "translateY(0)";
    });
  }, FADE_MS);
}

function startLogoSwap() {
  preloadLogos();
  setClub(idx);

  if (timer) window.clearInterval(timer);
  timer = window.setInterval(swapClub, SWAP_EVERY);
}

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

startLogoSwap();
setupNavigation();
setYear();
