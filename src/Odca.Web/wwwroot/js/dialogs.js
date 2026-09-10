(() => {
  const dialog = document.querySelector('[data-confirm-dialog]');
  if (!(dialog instanceof HTMLDialogElement)) return;

  const titleTarget = dialog.querySelector('[data-confirm-title-target]');
  const bodyTarget = dialog.querySelector('[data-confirm-body-target]');
  const actionTarget = dialog.querySelector('[data-confirm-action-target]');
  let pendingFormId = null;
  let returnFocus = null;

  document.querySelectorAll('[data-confirm-open]').forEach(button => {
    button.addEventListener('click', () => {
      pendingFormId = button.getAttribute('data-confirm-form');
      returnFocus = button;
      if (titleTarget) titleTarget.textContent = button.getAttribute('data-confirm-title') || 'Confirmar ação';
      if (bodyTarget) bodyTarget.textContent = button.getAttribute('data-confirm-body') || '';
      if (actionTarget) actionTarget.textContent = button.getAttribute('data-confirm-action') || 'Confirmar';
      dialog.showModal();
      actionTarget?.focus();
    });
  });

  dialog.addEventListener('close', () => {
    if (dialog.returnValue === 'confirm' && pendingFormId) {
      const form = document.getElementById(pendingFormId);
      if (form) {
        if (typeof form.requestSubmit === 'function') {
          form.requestSubmit();
        } else {
          form.submit();
        }
      }
    }
    pendingFormId = null;
    returnFocus?.focus();
  });

  dialog.addEventListener('cancel', () => {
    dialog.returnValue = 'cancel';
  });
})();
