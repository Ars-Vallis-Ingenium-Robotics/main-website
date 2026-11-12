// Timeline chapters: a block with moments "morphs" into its own sub-timeline.
// Uses the View Transitions API where available (the clicked block and the chapter header share a
// view-transition-name), otherwise it just swaps. Without JS, #sub-… links + CSS :target do the same job.
(() => {
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const swap = (fn) => (document.startViewTransition && !reduce ? document.startViewTransition(fn) : (fn(), null));

  const show = (wrap, sub, from) => {
    const head = sub.querySelector(".subtl__head");
    if (from) from.style.viewTransitionName = "tl-chapter";
    swap(() => {
      if (from) from.style.viewTransitionName = "";
      wrap.querySelectorAll(".subtl.is-open").forEach((s) => s.classList.remove("is-open"));
      wrap.classList.add("is-sub");
      sub.classList.add("is-open");
      head.style.viewTransitionName = "tl-chapter";
      history.replaceState(null, "", "#" + sub.id);
      wrap.scrollIntoView({ block: "start", behavior: "instant" });
    })?.finished.finally(() => { head.style.viewTransitionName = ""; });
    sub.querySelector(".subtl__back")?.focus({ preventScroll: true });
  };

  const hide = (wrap, sub, backTo) => {
    const head = sub.querySelector(".subtl__head");
    head.style.viewTransitionName = "tl-chapter";
    swap(() => {
      head.style.viewTransitionName = "";
      sub.classList.remove("is-open");
      wrap.classList.remove("is-sub");
      if (backTo) backTo.style.viewTransitionName = "tl-chapter";
      history.replaceState(null, "", location.pathname + location.search);
      backTo?.scrollIntoView({ block: "center", behavior: "instant" });
    })?.finished.finally(() => { if (backTo) backTo.style.viewTransitionName = ""; });
    backTo?.querySelector(".timeline__open")?.focus({ preventScroll: true });
  };

  document.addEventListener("click", (e) => {
    const open = e.target.closest("[data-sub]");
    if (open) {
      const wrap = open.closest("[data-timeline]"), sub = document.getElementById(open.dataset.sub);
      if (!wrap || !sub) return;
      e.preventDefault();
      show(wrap, sub, open.closest(".timeline__item") || open.closest(".subtl"));
      return;
    }
    const back = e.target.closest("[data-sub-close]");
    if (back) {
      const wrap = back.closest("[data-timeline]"), sub = back.closest(".subtl");
      e.preventDefault();
      hide(wrap, sub, document.getElementById(back.getAttribute("href").slice(1)));
    }
  });

  document.addEventListener("keydown", (e) => {
    if (e.key !== "Escape" || document.querySelector("dialog[open]")) return;
    const sub = document.querySelector(".subtl.is-open");
    sub?.querySelector("[data-sub-close]")?.click();
  });

  // Deep link straight into a chapter (#history-sub-2).
  const target = location.hash ? document.getElementById(location.hash.slice(1)) : null;
  if (target?.classList.contains("subtl")) {
    const wrap = target.closest("[data-timeline]");
    wrap.classList.add("is-sub"); target.classList.add("is-open");
  }
})();
