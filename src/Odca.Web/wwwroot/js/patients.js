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

function maskCpf(value) {
  const digits = value.replace(/\D/g, '').slice(0, 11);
  let out = digits.slice(0, 3);
  if (digits.length > 3) out += '.' + digits.slice(3, 6);
  if (digits.length > 6) out += '.' + digits.slice(6, 9);
  if (digits.length > 9) out += '-' + digits.slice(9, 11);
  return out;
}

function maskCnpj(value) {
  const digits = value.replace(/\D/g, '').slice(0, 14);
  let out = digits.slice(0, 2);
  if (digits.length > 2) out += '.' + digits.slice(2, 5);
  if (digits.length > 5) out += '.' + digits.slice(5, 8);
  if (digits.length > 8) out += '/' + digits.slice(8, 12);
  if (digits.length > 12) out += '-' + digits.slice(12, 14);
  return out;
}

function bindIdentifierMask(selectName, inputName) {
  const select = document.querySelector(`select[name="${selectName}"]`);
  const input = document.querySelector(`input[name="${inputName}"]`);
  if (!select || !input) return;
  const apply = () => {
    const mask = select.value === 'cpf' ? maskCpf : select.value === 'cnpj' ? maskCnpj : null;
    if (mask) input.value = mask(input.value);
  };
  select.addEventListener('change', apply);
  input.addEventListener('input', apply);
  apply();
}
bindIdentifierMask('IdentifierType', 'IdentifierValue');
bindIdentifierMask('RepresentativeIdentifierType', 'RepresentativeIdentifierValue');
