(() => {
  const body = document.body;
  const drawer = document.querySelector('.sidebar');
  const openButton = document.querySelector('[data-drawer-open]');
  const appContent = document.querySelector('.app-content');
  const desktopQuery = matchMedia('(min-width: 769px)');
  let returnFocus = null;

  const closeDrawer = () => {
    if (!body.classList.contains('drawer-open')) return;
    body.classList.remove('drawer-open');
    openButton?.setAttribute('aria-expanded', 'false');
    appContent?.removeAttribute('inert');
    returnFocus?.focus();
  };

  const syncCollapsedForViewport = () => {
    if (desktopQuery.matches) return;
    // Mobile drawer must keep visible labels even if desktop collapse preference is set.
    body.classList.remove('sidebar-collapsed');
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
    const controls = [...drawer.querySelectorAll('a[href], button:not([disabled])')]
      .filter(control => control.offsetParent !== null);
    if (!controls.length) return;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    }
    if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  document.querySelector('[data-sidebar-collapse]')?.addEventListener('click', event => {
    if (!desktopQuery.matches) return;
    const collapsed = body.classList.toggle('sidebar-collapsed');
    event.currentTarget.setAttribute('aria-expanded', String(!collapsed));
    event.currentTarget.setAttribute('aria-label', collapsed ? 'Expandir menu' : 'Recolher menu');
    try {
      localStorage.setItem('odca-sidebar-collapsed', String(collapsed));
    } catch {
      /* Preference is optional. */
    }
  });

  try {
    if (desktopQuery.matches && localStorage.getItem('odca-sidebar-collapsed') === 'true') {
      body.classList.add('sidebar-collapsed');
      const button = document.querySelector('[data-sidebar-collapse]');
      button?.setAttribute('aria-expanded', 'false');
      button?.setAttribute('aria-label', 'Expandir menu');
    }
  } catch {
    /* Other controls must keep working without storage. */
  }

  desktopQuery.addEventListener('change', event => {
    if (event.matches) {
      closeDrawer();
      try {
        if (localStorage.getItem('odca-sidebar-collapsed') === 'true') {
          body.classList.add('sidebar-collapsed');
        }
      } catch {
        /* ignore */
      }
    } else {
      syncCollapsedForViewport();
    }
  });

  syncCollapsedForViewport();
})();
