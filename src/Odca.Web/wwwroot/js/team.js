(() => {
  const body = document.body;
  const scrim = document.querySelector('.panel-scrim');
  const panels = [...document.querySelectorAll('[data-panel]')];
  let returnFocus = null;
  let activePanel = null;

  const hidePanels = () => {
    panels.forEach(panel => {
      panel.classList.remove('is-open');
      panel.setAttribute('aria-hidden', 'true');
    });
    scrim?.classList.remove('is-open');
    scrim?.setAttribute('hidden', '');
    body.classList.remove('panel-open');
    document.querySelector('.app-content')?.removeAttribute('inert');
    activePanel = null;
  };

  const closePanels = () => {
    hidePanels();
    returnFocus?.focus();
  };

  const openPanel = (name, trigger) => {
    const panel = panels.find(item => item.getAttribute('data-panel') === name);
    if (!panel) return;
    returnFocus = trigger || document.activeElement;
    hidePanels();
    panel.classList.add('is-open');
    panel.setAttribute('aria-hidden', 'false');
    scrim?.classList.add('is-open');
    scrim?.removeAttribute('hidden');
    body.classList.add('panel-open');
    document.querySelector('.app-content')?.setAttribute('inert', '');
    activePanel = panel;
    panel.querySelector('input, select, button, textarea, a')?.focus();
  };

  document.querySelectorAll('[data-open-panel]').forEach(button => {
    button.addEventListener('click', () => openPanel(button.getAttribute('data-open-panel'), button));
  });

  document.querySelectorAll('[data-close-panel]').forEach(control => {
    control.addEventListener('click', closePanels);
  });

  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && activePanel) {
      event.preventDefault();
      closePanels();
      return;
    }

    if (event.key !== 'Tab' || !activePanel) return;
    const controls = [...activePanel.querySelectorAll('a[href], button:not([disabled]), input, select, textarea')]
      .filter(control => control.offsetParent !== null && !control.hasAttribute('disabled'));
    if (!controls.length) return;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  const initiallyOpen = panels.find(panel => panel.classList.contains('is-open'));
  if (initiallyOpen) {
    openPanel(initiallyOpen.getAttribute('data-panel'), null);
  }

  const filterInput = document.querySelector('[data-org-filter]');
  const orgList = document.querySelector('[data-org-list]');
  const empty = document.querySelector('[data-org-empty]');
  if (filterInput && orgList) {
    filterInput.addEventListener('input', () => {
      const query = filterInput.value.trim().toLowerCase();
      let visible = 0;
      orgList.querySelectorAll('[data-org-name]').forEach(card => {
        const match = !query || (card.getAttribute('data-org-name') || '').includes(query);
        card.hidden = !match;
        if (match) visible += 1;
      });
      if (empty) empty.hidden = visible !== 0;
    });
  }
})();
