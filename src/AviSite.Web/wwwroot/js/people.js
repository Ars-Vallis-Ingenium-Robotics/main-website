// Name pop-ups: any [data-person] link (a person's name) opens a small card with their photo,
// roles and the links they chose to share. Data: /people.json (content/people/*.md + roles).
// Without JS (or with a modifier key) the link goes to /people/<slug> instead.
(() => {
  let data, box;
  const load = () => (data ??= fetch("/people.json", { headers: { Accept: "application/json" } }).then((r) => r.json()));
  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
  const initials = (n) => n.replace(/^Dr\. /, "").split(/\s+/).map((w) => w[0]).slice(0, 2).join("");

  const dialog = () => {
    if (box) return box;
    box = document.createElement("dialog");
    box.className = "sheet person-sheet";
    box.setAttribute("aria-labelledby", "person-sheet-name");
    box.addEventListener("click", (e) => {
      if (e.target === box || e.target.closest("[data-person-close]")) box.close();
    });
    box.addEventListener("close", () => box.returnFocusTo?.focus());
    document.body.append(box);
    return box;
  };

  const render = (p, slug) => {
    const photo = p.photo
      ? `<img class="person-sheet__photo" src="${esc(p.photo)}" alt="" width="120" height="120" />`
      : `<span class="person-sheet__photo person-sheet__photo--initials" aria-hidden="true">${esc(initials(p.name))}</span>`;
    const roles = (p.roles || []).map((r) => `<li>${esc(r)}</li>`).join("");
    const links = (p.links || []).map((l) => {
      const ext = /^https?:/.test(l.href);
      return `<li><a class="profile__link profile__link--${esc(l.kind)}" href="${esc(l.href)}"${ext ? ' target="_blank" rel="noopener me"' : ""}>${esc(l.label)}</a></li>`;
    }).join("");
    return `
      <div class="sheet__head">
        ${photo}
        <div>
          ${p.pronouns ? `<p class="person-sheet__pronouns mono">${esc(p.pronouns)}</p>` : ""}
          <h3 class="sheet__title" id="person-sheet-name">${esc(p.name)}</h3>
          ${p.headline ? `<p class="person-sheet__headline">${esc(p.headline)}</p>` : ""}
          ${p.major ? `<p class="muted">${esc(p.major)}</p>` : ""}
        </div>
        <button class="sheet__close" type="button" data-person-close aria-label="Close">×</button>
      </div>
      ${roles ? `<ul class="person-sheet__roles">${roles}</ul>` : ""}
      ${p.bio ? `<div class="profile__bio">${p.bio}</div>` : ""}
      ${links ? `<ul class="profile__links" role="list">${links}</ul>` : `<p class="person-sheet__empty muted small">No contact info shared yet.</p>`}
      <p class="person-sheet__more"><a class="link-arrow" href="/people/${esc(slug)}">Profile page</a></p>`;
  };

  document.addEventListener("click", async (e) => {
    const a = e.target.closest("a[data-person]");
    if (!a || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
    e.preventDefault();
    const slug = a.dataset.person;
    let p;
    try { p = (await load())[slug]; } catch { /* offline: fall back to the page */ }
    if (!p) { location.href = a.href; return; }
    const d = dialog();
    d.innerHTML = render(p, slug);
    d.returnFocusTo = a;
    if (d.open) d.close();
    d.showModal();
  });
})();
