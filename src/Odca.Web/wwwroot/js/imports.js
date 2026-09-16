const workspace = document.querySelector('[data-import-review]');
const button = workspace?.querySelector('[data-save-review]');
const message = workspace?.querySelector('[data-review-message]');

button?.addEventListener('click', async () => {
  const decisions = [...workspace.querySelectorAll('[data-suggestion-id]')].map(card => {
    const selected = card.querySelector('input[type="radio"]:checked');
    return selected ? { suggestionId: card.dataset.suggestionId, status: selected.value, value: card.querySelector('[data-reviewed-value]').value } : null;
  }).filter(Boolean);
  if (!decisions.length) { message.textContent = 'Escolha ao menos uma decisão para salvar.'; return; }
  button.disabled = true; message.textContent = 'Salvando revisão…';
  try {
    const csrf = workspace.querySelector('input[name="__RequestVerificationToken"]').value;
    const response = await fetch(workspace.dataset.saveUrl, { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf }, body: JSON.stringify({ idempotencyKey: crypto.randomUUID(), contractVersion: Number(workspace.dataset.contractVersion), decisions }) });
    const result = await response.json();
    if (!response.ok) throw new Error(result.detail || result.title || 'Não foi possível salvar.');
    message.textContent = 'Revisão salva. Recarregue para conferir o estado persistido.';
  } catch (error) { message.textContent = error.message; } finally { button.disabled = false; }
});
