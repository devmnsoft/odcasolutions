(() => {
  const form = document.querySelector('[data-contract-form]');
  if (!form) return;

  const indefinite = form.querySelector('[data-indefinite-toggle]');
  const endDate = form.querySelector('[data-end-date]');
  if (!(indefinite instanceof HTMLInputElement) || !(endDate instanceof HTMLInputElement)) return;

  const sync = () => {
    endDate.disabled = indefinite.checked;
    endDate.required = !indefinite.checked;
    if (indefinite.checked) {
      endDate.value = '';
    }
  };

  indefinite.addEventListener('change', sync);
  sync();
})();
