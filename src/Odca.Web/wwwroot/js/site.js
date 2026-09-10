(() => {
  const body = document.body;
  const drawer = document.querySelector('.sidebar');
  const openButton = document.querySelector('[data-drawer-open]');
  let returnFocus = null;
  const appContent = document.querySelector('.app-content');

  const closeDrawer = () => {
    if (!body.classList.contains('drawer-open')) return;
    body.classList.remove('drawer-open');
    openButton?.setAttribute('aria-expanded', 'false');
    appContent?.removeAttribute('inert');
    returnFocus?.focus();
  };
  openButton?.addEventListener('click', () => {
    returnFocus = document.activeElement;
    body.classList.add('drawer-open');
    openButton.setAttribute('aria-expanded', 'true');
    appContent?.setAttribute('inert', '');
    drawer?.querySelector('a, button')?.focus();
  });
  document.querySelector('[data-drawer-close]')?.addEventListener('click', closeDrawer);
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') closeDrawer();
    if (event.key !== 'Tab' || !body.classList.contains('drawer-open') || !drawer) return;
    const controls = [...drawer.querySelectorAll('a[href], button:not([disabled])')].filter(control => control.offsetParent !== null);
    if (!controls.length) return;
    const first = controls[0]; const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  });
  document.querySelector('[data-sidebar-collapse]')?.addEventListener('click', event => {
    const collapsed = body.classList.toggle('sidebar-collapsed');
    event.currentTarget.setAttribute('aria-expanded', String(!collapsed));
    event.currentTarget.setAttribute('aria-label', collapsed ? 'Expandir menu' : 'Recolher menu');
    try { localStorage.setItem('odca-sidebar-collapsed', String(collapsed)); } catch { /* Preference is optional. */ }
  });
  try {
    if (localStorage.getItem('odca-sidebar-collapsed') === 'true') {
      body.classList.add('sidebar-collapsed');
      const button = document.querySelector('[data-sidebar-collapse]');
      button?.setAttribute('aria-expanded', 'false'); button?.setAttribute('aria-label', 'Expandir menu');
    }
  } catch { /* Other controls must keep working without storage. */ }
  matchMedia('(min-width: 769px)').addEventListener('change', event => { if (event.matches) closeDrawer(); });
  document.querySelectorAll('[data-password-toggle]').forEach(button => button.addEventListener('click', () => {
    const input = button.closest('.field-with-action')?.querySelector('input');
    if (!input) return;
    const reveal = input.type === 'password'; input.type = reveal ? 'text' : 'password';
    button.textContent = reveal ? 'Ocultar' : 'Mostrar'; button.setAttribute('aria-pressed', String(reveal));
  }));
  document.querySelectorAll('form[data-processing]').forEach(form => form.addEventListener('submit', () => {
    const button = form.querySelector('button[type="submit"]'); if (!button || !form.checkValidity()) return;
    button.dataset.originalLabel ||= button.textContent; button.disabled = true; button.textContent = button.dataset.processingLabel || 'Processando…';
  }));
  addEventListener('pageshow', () => document.querySelectorAll('form[data-processing] button[type="submit"]').forEach(button => { button.disabled = false; if (button.dataset.originalLabel) button.textContent = button.dataset.originalLabel; }));
})();
