(() => {
  const dialog = document.querySelector('[data-confirm-dialog]');
  if (dialog instanceof HTMLDialogElement) {
    const titleTarget = dialog.querySelector('[data-confirm-title-target]');
    const bodyTarget = dialog.querySelector('[data-confirm-body-target]');
    const actionTarget = dialog.querySelector('[data-confirm-action-target]');
    let pendingForm = null;
    let returnFocus = null;

    document.addEventListener('click', (e) => {
      const button = e.target.closest('[data-confirm-open]');
      if (!button) return;
      e.preventDefault();
      const formId = button.getAttribute('data-confirm-form');
      pendingForm = formId ? document.getElementById(formId) : button.closest('form');
      returnFocus = button;
      if (titleTarget) titleTarget.textContent = button.getAttribute('data-confirm-title') || 'Confirmar ação';
      if (bodyTarget) bodyTarget.textContent = button.getAttribute('data-confirm-body') || '';
      if (actionTarget) actionTarget.textContent = button.getAttribute('data-confirm-action') || 'Confirmar';
      dialog.showModal();
      actionTarget?.focus();
    });

    dialog.addEventListener('close', () => {
      if (dialog.returnValue === 'confirm' && pendingForm) {
        if (typeof pendingForm.requestSubmit === 'function') {
          pendingForm.requestSubmit();
        } else {
          pendingForm.submit();
        }
      }
      pendingForm = null;
      returnFocus?.focus();
    });

    dialog.addEventListener('cancel', () => {
      dialog.returnValue = 'cancel';
    });
  }

  // Toast notifications auto-dismiss and keyboard support
  document.querySelectorAll('[data-toast-autodismiss]').forEach(toast => {
    const duration = parseInt(toast.getAttribute('data-toast-autodismiss') || '8000', 10);
    setTimeout(() => {
      if (toast && toast.parentNode) {
        toast.classList.add('toast-fadeout');
        setTimeout(() => toast.remove(), 300);
      }
    }, duration);
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      const openDialog = document.querySelector('dialog[open]');
      if (!openDialog) {
        document.querySelectorAll('.toast').forEach(t => t.remove());
      }
    }
  });
})();
