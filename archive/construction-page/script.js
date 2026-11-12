const clubs = [
  {
    label: "AVI Robotics",
    mainText: "Ars Vallis Ingenium (AVI) Robotics",
    alt: "Ars Vallis Ingenium (AVI) Robotics logo",
    src: "./Logos/aviDogDefault.png",
  },
  {
    label: "Sequoia Marine Solutions",
    mainText: "Sequoia Marine Solutions (MATE Team)",
    alt: "Sequoia Marine Solutions logo",
    src: "./Logos/smsLogo.png",
  },
];

// Timing
const SWAP_EVERY = 3500;
const FADE_MS = 420;

const logoImg = document.getElementById("logo");
const logoLabel = document.getElementById("logoLabel");
const clubText = document.getElementById("clubText");

let idx = 0;
let timer = null;

const prefersReducedMotion =
  window.matchMedia &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

function preload() {
  clubs.forEach(c => {
    const img = new Image();
    img.src = c.src;
  });
}

function setClub(i) {
  const club = clubs[i];

  logoImg.src = club.src;
  logoImg.alt = club.alt;
  logoLabel.textContent = club.label;
  clubText.textContent = club.mainText;
}

function swapClub() {
  if (prefersReducedMotion) {
    idx = (idx + 1) % clubs.length;
    setClub(idx);
    return;
  }

  logoImg.classList.add("fading");
  clubText.style.opacity = "0";
  clubText.style.transform = "translateY(4px)";
  clubText.style.transition = `opacity ${FADE_MS}ms ease, transform ${FADE_MS}ms ease`;

  setTimeout(() => {
    idx = (idx + 1) % clubs.length;
    setClub(idx);

    requestAnimationFrame(() => {
      logoImg.classList.remove("fading");
      clubText.style.opacity = "1";
      clubText.style.transform = "translateY(0)";
    });
  }, FADE_MS);
}

function start() {
  preload();
  setClub(idx);

  if (timer) clearInterval(timer);
  timer = setInterval(swapClub, SWAP_EVERY);
}

start();