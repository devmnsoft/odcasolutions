(() => {
  document.querySelectorAll('[data-password-toggle]').forEach(button => {
    button.addEventListener('click', () => {
      const input = button.closest('.field-with-action')?.querySelector('input');
      if (!input) return;
      const reveal = input.type === 'password';
      input.type = reveal ? 'text' : 'password';
      button.textContent = reveal ? 'Ocultar' : 'Mostrar';
      button.setAttribute('aria-pressed', String(reveal));
    });
  });

  document.querySelectorAll('form[data-processing]').forEach(form => {
    const button = form.querySelector('button[type="submit"]');
    if (button && !button.dataset.originalLabel) {
      button.dataset.originalLabel = button.textContent ?? '';
    }

    form.addEventListener('submit', () => {
      if (!button || !form.checkValidity()) return;
      button.disabled = true;
      button.textContent = button.dataset.processingLabel || 'Processando…';
    });
  });

  addEventListener('pageshow', () => {
    document.querySelectorAll('form[data-processing] button[type="submit"]').forEach(button => {
      button.disabled = false;
      if (button.dataset.originalLabel) {
        button.textContent = button.dataset.originalLabel;
      }
    });
  });

  document.querySelectorAll('[data-dirty-form] form').forEach(form => {
    let dirty = false;
    form.addEventListener('input', () => { dirty = true; });
    form.addEventListener('submit', () => { dirty = false; });
    addEventListener('beforeunload', event => {
      if (!dirty) return;
      event.preventDefault();
      event.returnValue = '';
    });
  });
})();
