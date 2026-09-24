const toggle = document.querySelector('[data-representative-toggle]');
const fields = document.querySelector('[data-representative-fields]');
if (toggle && fields) {
  const update = () => { fields.hidden = !toggle.checked; };
  toggle.addEventListener('change', update);
  update();
}
document.querySelectorAll('[data-submit-once]').forEach(form => form.addEventListener('submit', () => {
  if (!form.checkValidity()) return;
  form.querySelectorAll('button[type="submit"]').forEach(button => { button.disabled = true; button.setAttribute('aria-busy', 'true'); });
}));
