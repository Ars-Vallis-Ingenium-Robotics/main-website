
(() => {
  const btn = document.querySelector('[data-mode-toggle]');
  const key = 'avi-concept-theme';
  const saved = localStorage.getItem(key);
  if (saved) document.documentElement.dataset.theme = saved;
  if (btn) btn.addEventListener('click', () => {
    const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
    document.documentElement.dataset.theme = next;
    localStorage.setItem(key, next);
  });
})();
