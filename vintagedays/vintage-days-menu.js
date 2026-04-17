const PAYMENT_OPTIONS = [
  {
    name: "PayPal",
    className: "paypal",
    href: "https://www.paypal.com/", // replace with your real PayPal link
  },
  // {
  //   name: "Venmo",
  //   className: "venmo",
  //   href: "https://venmo.com/", // replace with your real Venmo link
  // },
  // {
  //   name: "Cash App",
  //   className: "cashapp",
  //   href: "https://cash.app/", // replace with your real Cash App link
  // },
  {
    name: "In-Person",
    className: "inperson",
    href: "#", // no link needed for in-person payment
  }
];

const MENU = {
  tea: {
    title: "Drinks",
    tag: "Boba Tea",
    className: "tea",
    hint: "Cup drinks and add-ons",
    items: [
      { name: "Thai Tea w/ Boba", price: "$5.00", image: "./images/thai-tea.jpg" },
      { name: "Black Tea w/ Boba", price: "$5.00", image: "./images/black-tea.jpg" },
      { name: "Strawberry Milk Tea", price: "$5.00", image: "./images/strawberry-milk-tea.jpg" }
    ]
  },
  food: {
    title: "Santi's Famous Chicken",
    tag: "Food",
    className: "food",
    hint: "Hot food items",
    items: [
      { name: "Platter", price: "$12.00", image: "./images/platter.jpg" },
      { name: "Drumstick", price: "$2.50", image: "./images/drumstick.jpg" },
      { name: "Fries", price: "$5.00", image: "./images/fries.jpg" }
    ]
  },
  combo: {
    title: "Combos",
    tag: "Best Value",
    className: "combo",
    hint: "Featured bundle",
    items: [
      { name: "Platter w/ Drink", desc: "Includes one drink", price: "$15.00", image: "./images/platter-drink.jpg" }
    ]
  },
  extra: {
    title: "Extras",
    tag: "Add On",
    className: "extra",
    hint: "Small upgrades",
    items: [
      { name: "Extra Syrup or Boba", price: "$0.50", image: "./images/extra-boba.jpg" }
    ]
  }
};

const MENU_ORDER = ["tea", "food", "combo", "extra"];
const menuGrid = document.getElementById("menuGrid");
const paymentButtons = document.getElementById("paymentButtons");

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function renderPaymentButtons() {
  paymentButtons.innerHTML = PAYMENT_OPTIONS.map((option) => `
    <a
      class="paymentButton ${escapeHtml(option.className)}"
      href="${escapeHtml(option.href)}"
      target="_blank"
      rel="noopener noreferrer"
    >
      ${escapeHtml(option.name)}
    </a>
  `).join("");
}

function renderItemImage(item) {
  if (item.image) {
    return `
      <div class="itemImage">
        <img src="${escapeHtml(item.image)}" alt="${escapeHtml(item.name)}" loading="lazy">
      </div>
    `;
  }

  return `
    <div class="itemImage">
      <div class="itemImagePlaceholder">Add image</div>
    </div>
  `;
}

function renderItem(item) {
  return `
    <div class="item">
      ${renderItemImage(item)}
      <div class="itemName">
        <div class="itemTitle">${escapeHtml(item.name)}</div>
        ${item.desc ? `<div class="itemDesc">${escapeHtml(item.desc)}</div>` : ""}
      </div>
      <div class="price">${escapeHtml(item.price)}</div>
    </div>
  `;
}

function renderSection(section) {
  return `
    <article class="section ${escapeHtml(section.className)}">
      <div class="sectionHeader">
        <div>
          <h2 class="sectionTitle">${escapeHtml(section.title)}</h2>
          ${section.hint ? `<p class="sectionHint">${escapeHtml(section.hint)}</p>` : ""}
        </div>
        ${section.tag ? `<span class="tag">${escapeHtml(section.tag)}</span>` : ""}
      </div>
      <div class="items">
        ${section.items.map(renderItem).join("")}
      </div>
    </article>
  `;
}

function renderMenu() {
  menuGrid.innerHTML = MENU_ORDER
    .map((key) => MENU[key])
    .filter(Boolean)
    .map(renderSection)
    .join("");
}

renderPaymentButtons();
renderMenu();