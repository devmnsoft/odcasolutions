const panel = document.querySelector('.obligation-detail-panel');
const title = document.querySelector('#detail-title');
const copy = document.querySelector('#detail-copy');
const history = document.querySelector('#detail-history');

const eventLabels = {
  created: 'Obrigação criada', start: 'Andamento iniciado', fulfill: 'Cumprimento registrado',
  reschedule: 'Prazo alterado', reassign: 'Responsável alterado', cancel: 'Obrigação cancelada', reopen: 'Obrigação reaberta'
};

function describe(details) {
  try {
    const value = JSON.parse(details);
    const parts = [];
    if (value.reason) parts.push(`Motivo: ${value.reason}`);
    if (value.note) parts.push(`Observação: ${value.note}`);
    if (value.previousDueDate && value.dueDate) parts.push(`Prazo: ${value.previousDueDate} → ${value.dueDate}`);
    if (value.previousOwner && value.owner) parts.push('Responsável anterior e novo registrados na auditoria.');
    if (value.evidenceDocumentVersionId) parts.push('Evidência documental vinculada.');
    return parts.join(' ');
  } catch {
    return '';
  }
}

for (const button of document.querySelectorAll('.obligation-detail')) {
  button.addEventListener('click', async () => {
    title.textContent = button.dataset.title;
    copy.textContent = `Contrato: ${button.dataset.contract}. Responsável: ${button.dataset.owner}. Prazo: ${button.dataset.due}.`;
    history.replaceChildren(Object.assign(document.createElement('p'), { textContent: 'Carregando histórico…' }));
    panel?.focus();
    try {
      const response = await fetch(button.dataset.historyUrl, { headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error('history_request_failed');
      const result = await response.json();
      if (result.events.length === 0) {
        history.replaceChildren(Object.assign(document.createElement('p'), { textContent: 'Nenhuma alteração registrada.' }));
        return;
      }
      const list = document.createElement('ol');
      list.className = 'obligation-history';
      for (const event of result.events) {
        const item = document.createElement('li');
        const heading = document.createElement('strong');
        heading.textContent = eventLabels[event.eventType] ?? 'Alteração registrada';
        const metadata = document.createElement('small');
        metadata.textContent = `${event.actor} · ${new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(event.occurredAt))}`;
        item.append(heading, metadata);
        const detail = describe(event.details);
        if (detail) item.append(Object.assign(document.createElement('p'), { textContent: detail }));
        list.append(item);
      }
      history.replaceChildren(list);
    } catch {
      history.replaceChildren(Object.assign(document.createElement('p'), { textContent: 'Não foi possível carregar o histórico. Tente novamente.' }));
    }
  });
}
