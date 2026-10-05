import { formatCurrency, isPartialDecimal, parseDecimal } from "./studio-decimal.js";

const studio = document.querySelector("[data-studio]");
if (studio) {
  const content = JSON.parse(document.querySelector("#studio-content").textContent);
  const fields = JSON.parse(document.querySelector("#studio-fields").textContent);
  const initialValues = JSON.parse(document.querySelector("#studio-values").textContent);
  const defId = def => String(def?.id ?? def?.Id ?? def?.fieldId ?? def?.FieldId ?? "");
  const nodeFieldId = node => String(node?.fieldId ?? node?.FieldId ?? "");
  const normalizeFieldType = type => {
    if (typeof type === "number") {
      const map = ["ShortText", "LongText", "Date", "Number", "Currency", "BrazilianDocument", "Choice"];
      return map[type] || "ShortText";
    }
    const s = String(type || "").toLowerCase();
    if (s === "longtext" || s === "textarea") return "LongText";
    if (s === "date") return "Date";
    if (s === "number") return "Number";
    if (s === "currency") return "Currency";
    if (s === "braziliandocument" || s === "cpf_cnpj" || s === "document") return "BrazilianDocument";
    if (s === "choice" || s === "select") return "Choice";
    return "ShortText";
  };

  const originLabels = {
    organization: "Organização/clínica",
    patient: "Paciente",
    contractor: "Contratante/responsável financeiro",
    representative: "Representante",
    manual: "Entrada manual"
  };
  const fieldOrigin = definition => String(definition?.origin ?? definition?.Origin ?? "manual").toLowerCase();
  const values = new Map(
    initialValues
      .filter(val => Boolean(val.fieldId ?? val.FieldId))
      .map(value => {
        const id = value.fieldId ?? value.FieldId;
        return [id, { fieldId: id, value: value.value ?? value.Value ?? "", confirmed: Boolean(value.confirmed ?? value.Confirmed), source: value.source ?? value.Source ?? "manual" }];
      })
  );
  const paper = studio.querySelector("[data-document]");
  const panel = studio.querySelector("[data-fields]");
  const state = studio.querySelector("[data-save-state]");
  const savedAt = studio.querySelector("[data-saved-at]");
  let version = Number(studio.dataset.version), localRevision = 0, acknowledgedRevision = 0;
  let timer = 0, activeRequest = null, conflict = false, selectedText = null;
  let commentParentId = null;

  const markDirty = () => {
    localRevision += 1; state.textContent = "Alterações não salvas"; state.dataset.state = "dirty";
    clearTimeout(timer); timer = setTimeout(save, 900);
  };
  const renderNode = (node, parent) => {
    if (node.type === "text") {
      const span = document.createElement("span"); span.textContent = node.text || ""; span.contentEditable = "true"; span.spellcheck = true;
      for (const mark of node.marks || []) span.classList.add(`mark-${mark}`);
      span.addEventListener("focus", () => { selectedText = { node, span }; }); span.addEventListener("input", () => { node.text = span.textContent; markDirty(); }); parent.append(span); return;
    }
    if (node.type === "field") {
      const fId = nodeFieldId(node);
      const definition = fields.find(item => defId(item) === fId);
      const value = values.get(fId);
      const label = definition?.label ?? definition?.Label ?? fId;
      const button = document.createElement("button"); button.type = "button"; button.className = `smart-field ${value?.confirmed ? "confirmed" : "pending"}`;
      button.dataset.fieldId = fId; button.setAttribute("aria-label", `${label}: ${value?.confirmed ? "confirmado" : "pendente"}`);
      const smartHelp = String(definition?.help ?? definition?.Help ?? "");
      if (smartHelp) button.title = smartHelp;
      const shown = normalizeFieldType(definition?.type ?? definition?.Type) === "Currency" && value?.value ? formatCurrency(value.value) : value?.value;
      button.textContent = shown ? shown : `⚠ ${label}`;
      button.addEventListener("click", () => document.querySelector(`[data-field-editor="${CSS.escape(fId)}"]`)?.focus());
      parent.append(button); return;
    }
    const tags = { document: "div", paragraph: "p", heading: `h${node.level || 2}`, bulletList: "ul", orderedList: "ol", listItem: "li", table: "table", tableRow: "tr", tableCell: "td", pageBreak: "hr" };
    const element = document.createElement(tags[node.type] || "div"); if (node.type === "pageBreak") element.className = "page-break";
    if (node.alignment) element.style.textAlign = node.alignment; for (const child of node.content || []) renderNode(child, element); parent.append(element);
  };
  const refresh = () => { paper.replaceChildren(); renderNode(content, paper); renderFields(); };
  const requirementActive = definition => {
    const required = Boolean(definition.required ?? definition.Required);
    if (!required) return false;
    const controller = definition.requiredWhenFieldId ?? definition.RequiredWhenFieldId;
    if (!controller) return true;
    const anyOf = definition.requiredWhenAnyOf ?? definition.RequiredWhenAnyOf ?? [];
    const current = String(values.get(controller)?.value ?? "").trim();
    return anyOf.map(item => String(item)).includes(current);
  };
  const assignValue = (value, next) => {
    const previous = value.value ?? "";
    const previousSource = value.source;
    if (previousSource && previousSource !== "manual" && next !== previous) {
      value.source = "manual";
      value.confirmed = false;
    }
    value.value = next;
    value.draft = null;
  };
  const numericDrafts = () => [...panel.querySelectorAll("[data-numeric='true']")].map(input => {
    const id = input.dataset.fieldEditor;
    const definition = fields.find(item => defId(item) === id);
    return { input, id, definition, label: definition?.label ?? definition?.Label ?? id, currency: input.dataset.currency === "true" };
  });
  const renderFields = () => {
    panel.replaceChildren(); let pending = 0;
    for (const definition of fields) {
      const id = defId(definition);
      if (!id) continue;
      const value = values.get(id) || { fieldId: id, value: "", confirmed: false, source: fieldOrigin(definition) };
      values.set(id, value);
      const isRequired = requirementActive(definition);
      if (!value.confirmed && isRequired) pending++;

      const box = document.createElement("div"); box.className = "field-editor";
      const label = document.createElement("label");
      const fieldLabelText = definition.label ?? definition.Label ?? id;
      label.textContent = fieldLabelText + (isRequired ? " *" : "");

      const fieldType = normalizeFieldType(definition.type ?? definition.Type);
      let input;
      if (fieldType === "LongText") {
        input = document.createElement("textarea"); input.rows = 3; input.value = value.draft ?? value.value ?? "";
      } else if (fieldType === "Choice") {
        input = document.createElement("select");
        const defaultOption = document.createElement("option"); defaultOption.value = ""; defaultOption.textContent = "— Selecione uma opção —"; input.append(defaultOption);
        const choices = definition.choices ?? definition.Choices ?? [];
        for (const choice of choices) {
          const opt = document.createElement("option"); opt.value = choice; opt.textContent = choice;
          if (choice === value.value) opt.selected = true;
          input.append(opt);
        }
        if (!input.value && value.value) input.value = value.value;
      } else {
        input = document.createElement("input");
        if (fieldType === "Date") { input.type = "date"; input.value = value.value || ""; }
        else if (fieldType === "Number" || fieldType === "Currency") {
          input.type = "text"; input.inputMode = "decimal"; input.autocomplete = "off"; input.dataset.numeric = "true";
          input.dataset.currency = fieldType === "Currency" ? "true" : "false";
          input.placeholder = fieldType === "Currency" ? "0,00" : "0";
          input.value = value.draft ?? (fieldType === "Currency" && value.value ? formatCurrency(value.value) : (value.value || ""));
        } else if (fieldType === "BrazilianDocument") { input.type = "text"; input.maxLength = 18; input.placeholder = "000.000.000-00 ou 00.000.000/0000-00"; input.value = value.value || ""; }
        else { input.type = "text"; input.value = value.value || ""; }
      }
      input.dataset.fieldEditor = id; input.className = "field-input"; input.id = `field-${id}`;
      label.htmlFor = input.id;
      const helpText = String(definition.help ?? definition.Help ?? "").trim();
      const helpEl = document.createElement("small"); helpEl.className = "field-help"; helpEl.hidden = !helpText; helpEl.textContent = helpText;
      if (helpText) { helpEl.id = `field-help-${id}`; input.setAttribute("aria-describedby", helpEl.id); }
      const configuredOrigin = fieldOrigin(definition);
      const source = document.createElement("small");
      source.textContent = `Origem no modelo: ${originLabels[configuredOrigin] || configuredOrigin}. Origem do valor: ${originLabels[value.source] || value.source || "manual"}.`;
      const fieldError = document.createElement("small"); fieldError.className = "field-error"; fieldError.hidden = !value.error; fieldError.textContent = value.error || "";
      const confirm = document.createElement("button"); confirm.type = "button"; confirm.className = "button button-small button-link"; confirm.textContent = value.confirmed ? "Confirmado" : "Confirmar valor";
      const commitText = confirmValue => {
        const previous = value.value ?? "";
        const next = input.value;
        assignValue(value, next);
        if (next !== previous && !confirmValue) value.confirmed = false;
        value.confirmed = confirmValue && Boolean(String(value.value).trim());
        if (!confirmValue) value.confirmed = false;
        value.error = null;
        values.set(id, value);
        markDirty();
        return true;
      };
      const commitNumeric = confirmValue => {
        const typed = input.value;
        const currency = fieldType === "Currency";
        if (!String(typed).trim()) {
          assignValue(value, "");
          value.confirmed = false;
          value.error = null;
          values.set(id, value);
          markDirty();
          return true;
        }
        if (isPartialDecimal(typed)) {
          value.draft = typed;
          value.confirmed = false;
          value.error = "Valor incompleto. Complete ou apague o campo. O valor anterior não foi gravado.";
          fieldError.hidden = false; fieldError.textContent = value.error;
          if (localRevision === acknowledgedRevision) localRevision += 1;
          state.textContent = `${fieldLabelText}: ${value.error}`; state.dataset.state = "failed";
          return false;
        }
        const parsed = parseDecimal(typed, currency);
        if (!parsed.ok) {
          value.draft = typed;
          value.confirmed = false;
          value.error = parsed.error;
          fieldError.hidden = false; fieldError.textContent = value.error;
          if (localRevision === acknowledgedRevision) localRevision += 1;
          state.textContent = `${fieldLabelText}: ${value.error}`; state.dataset.state = "failed";
          return false;
        }
        assignValue(value, currency ? parsed.canonical : parsed.canonical);
        value.confirmed = confirmValue && Boolean(value.value);
        if (!confirmValue) value.confirmed = false;
        value.error = null;
        values.set(id, value);
        markDirty();
        return true;
      };
      if (fieldType === "Currency" || fieldType === "Number") {
        input.addEventListener("input", () => {
          value.draft = input.value;
          value.error = null;
          fieldError.hidden = true;
          state.textContent = "Digitação numérica em andamento"; state.dataset.state = "dirty";
          if (localRevision === acknowledgedRevision) localRevision += 1;
          clearTimeout(timer);
        });
        input.addEventListener("blur", () => { if (commitNumeric(false)) refresh(); });
      } else if (input.tagName === "SELECT") {
        input.addEventListener("change", () => { if (commitText(false)) refresh(); });
      } else {
        input.addEventListener("input", () => commitText(false));
      }
      confirm.addEventListener("click", () => {
        const ok = fieldType === "Currency" || fieldType === "Number" ? commitNumeric(true) : commitText(true);
        if (ok) refresh();
      });
      label.append(input); box.append(label, source, helpEl, fieldError, confirm); panel.append(box);
    }
    studio.querySelector("[data-pending-count]").textContent = String(pending);
  };
  const rejectNumericDrafts = () => {
    for (const item of numericDrafts()) {
      const typed = item.input.value;
      const current = values.get(item.id);
      const fieldError = item.input.closest(".field-editor")?.querySelector(".field-error");
      const show = message => {
        if (current) { current.draft = typed; current.error = message; current.confirmed = false; }
        if (fieldError) { fieldError.hidden = false; fieldError.textContent = message; }
        return `${item.label}: ${message}`;
      };
      if (!String(typed).trim()) continue;
      if (isPartialDecimal(typed)) return show("Valor incompleto. Complete ou apague o campo. O valor anterior não foi gravado.");
      const parsed = parseDecimal(typed, item.currency);
      if (!parsed.ok) return show(`${parsed.error} O valor anterior não foi gravado.`);
    }
    return null;
  };
  const save = async force => {
    if (conflict || activeRequest) return;
    if (!force && numericDrafts().some(item => item.input === document.activeElement)) {
      state.textContent = "Digitação numérica em andamento"; state.dataset.state = "dirty"; return;
    }
    const blocked = rejectNumericDrafts();
    if (blocked) {
      state.textContent = blocked; state.dataset.state = "failed";
      if (localRevision === acknowledgedRevision) localRevision += 1;
      return;
    }
    for (const item of numericDrafts()) {
      const current = values.get(item.id);
      if (!current) continue;
      const typed = item.input.value;
      if (!String(typed).trim()) assignValue(current, "");
      else {
        const parsed = parseDecimal(typed, item.currency);
        if (!parsed.ok) { state.textContent = `${item.label}: ${parsed.error}`; state.dataset.state = "failed"; return; }
        assignValue(current, parsed.canonical);
      }
      current.error = null;
    }
    if (localRevision === acknowledgedRevision) return;
    const sentRevision = localRevision; state.textContent = "Salvando…"; state.dataset.state = "saving";
    const requestId = crypto.randomUUID(); const token = studio.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const cleanValues = [...values.values()].filter(v => Boolean(v.fieldId)).map(v => ({ fieldId: v.fieldId, value: v.value ?? "", confirmed: Boolean(v.confirmed), source: v.source || "manual" }));
    activeRequest = fetch(studio.dataset.saveUrl, { method: "PUT", headers: { "Content-Type": "application/json", ...(token ? { "RequestVerificationToken": token } : {}) }, body: JSON.stringify({ content, fields, values: cleanValues, expectedVersion: version, clientRevision: requestId }) });
    try {
      const response = await activeRequest;
      if (response.status === 409) { conflict = true; state.textContent = "Conflito"; state.dataset.state = "conflict"; studio.querySelector("[data-conflict]").hidden = false; return; }
      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        state.textContent = problem.detail || problem.title || "Falha ao salvar"; state.dataset.state = "failed"; return;
      }
      const result = await response.json(); version = result.version; studio.dataset.version = String(version); acknowledgedRevision = Math.max(acknowledgedRevision, sentRevision);
      if (localRevision === sentRevision) { state.textContent = "Salvo"; state.dataset.state = "saved"; savedAt.textContent = ` ${new Date(result.savedAt).toLocaleString()}`; refresh(); }
      else { state.textContent = "Alterações não salvas"; state.dataset.state = "dirty"; timer = setTimeout(() => save(false), 150); }
    }
    catch { state.textContent = "Falha de comunicação. Nada foi confirmado como salvo."; state.dataset.state = "failed"; }
    finally { activeRequest = null; }
  };
  for (const button of studio.querySelectorAll("[data-add]")) button.addEventListener("click", () => {
    const type=button.dataset.add; const node=type==="pageBreak"?{type}:{type, ...(type==="heading"?{level:2}:{}), content:[{type:"text",text:"Novo bloco"}]}; content.content.push(node); markDirty(); refresh();
  });
  for (const button of studio.querySelectorAll("[data-command]")) button.addEventListener("click", () => {
    if(!selectedText)return; const mark=button.dataset.command; selectedText.node.marks=selectedText.node.marks||[]; const index=selectedText.node.marks.indexOf(mark); if(index>=0)selectedText.node.marks.splice(index,1);else selectedText.node.marks.push(mark); markDirty(); refresh();
  });
  studio.querySelector("[data-save]").addEventListener("click",()=>save(true));
  studio.querySelector("[data-retry]").addEventListener("click",()=>{conflict=false;save();});
  studio.querySelector("[data-zoom]").addEventListener("change",event=>paper.style.zoom=`${event.target.value}%`);
  studio.querySelector("[data-reading]").addEventListener("click",event=>{const reading=studio.classList.toggle("reading-mode");event.currentTarget.setAttribute("aria-pressed",String(reading));});
  const openTab = async name => {
    for(const section of studio.querySelectorAll("[data-panel]"))section.hidden=section.dataset.panel!==name;
    for(const tab of studio.querySelectorAll("[data-tab]"))tab.setAttribute("aria-selected",String(tab.dataset.tab===name));
    if(name==="comments")await loadComments();if(name==="checklist")await loadChecklist();if(name==="history")await loadVersions();
  };
  for(const tab of studio.querySelectorAll("[data-tab]"))tab.addEventListener("click",()=>openTab(tab.dataset.tab));
  const request = async (url, options={}) => {const token=studio.querySelector('input[name="__RequestVerificationToken"]')?.value;const response=await fetch(url,{...options,headers:{...(options.body?{"Content-Type":"application/json"}:{}),...(token?{"RequestVerificationToken":token}:{}),...options.headers}});if(!response.ok)throw new Error((await response.json().catch(()=>({}))).title||"Operação não confirmada.");return response.status===204?null:response.json();};
  const loadVersions=async()=>{const versions=await request(studio.dataset.versionsUrl);const history=studio.querySelector("[data-history]");history.replaceChildren(...versions.map(item=>{const p=document.createElement("p");p.textContent=`Versão ${item.number} · ${item.author} · ${new Date(item.createdAt).toLocaleString()}`;return p;}));return versions;};
  const loadComments=async()=>{const items=await request(`${studio.dataset.commentsUrl}?resolved=${studio.querySelector("[data-resolved]").checked}`);const target=studio.querySelector("[data-comments]");studio.querySelector("[data-comment-count]").textContent=String(items.filter(item=>!item.resolved).length);target.replaceChildren(...items.map(item=>{const article=document.createElement("article");article.className=`comment${item.parentId?" reply":""}`;article.id=`comment-${item.id}`;const heading=document.createElement("strong");heading.textContent=`${item.author} · ${item.reference}`;const metadata=document.createElement("small");metadata.textContent=`${item.originVersion?`Versão ${item.originVersion}`:`Revisão ${item.draftRevision}`} · ${new Date(item.lastMovementAt||item.createdAt).toLocaleString()}`;const body=document.createElement("p");body.textContent=item.body;const location=document.createElement("p");location.className="muted";location.textContent=item.referenceLocated?"Trecho relacionado disponível":"Trecho não localizado nesta revisão";const reply=document.createElement("button");reply.type="button";reply.className="button button-small button-link";reply.textContent="Responder";reply.addEventListener("click",()=>{commentParentId=item.id;const form=studio.querySelector("[data-comment-form]");form.elements.reference.value=item.reference;form.elements.body.focus();});const history=document.createElement("button");history.type="button";history.className="button button-small button-link";history.textContent="Ver histórico";history.addEventListener("click",()=>article.querySelector("p").focus());const button=document.createElement("button");button.type="button";button.className="button button-small button-link";button.textContent=item.resolved?"Reabrir pendência":"Resolver pendência";button.addEventListener("click",async()=>{const operation=item.resolved?"reopen":"resolve";const observation=operation==="reopen"?window.prompt("Informe a justificativa para reabrir a pendência:"):null;if(operation==="reopen"&&!observation?.trim())return;const original=button.textContent;button.disabled=true;button.textContent="Processando…";try{await request(`${studio.dataset.commentsUrl}/${item.id}/${operation}`,{method:"POST",body:JSON.stringify({expectedResolved:item.resolved,observation})});await loadComments();}catch(error){button.disabled=false;button.textContent=original;state.textContent=error.message;state.dataset.state=error.message.includes("atualiz")?"conflict":"failed";}});article.append(heading,metadata,body,location,reply,history,button);return article;}));};
  studio.querySelector("[data-resolved]").addEventListener("change",loadComments);
  studio.querySelector("[data-comment-form]").addEventListener("submit",async event=>{event.preventDefault();const data=new FormData(event.currentTarget);try{await request(studio.dataset.commentsUrl,{method:"POST",body:JSON.stringify({draftRevision:version,reference:data.get("reference"),body:data.get("body"),parentId:commentParentId})});commentParentId=null;event.currentTarget.reset();await loadComments();}catch(error){state.textContent=error.message;state.dataset.state="failed";}});
  const loadChecklist=async()=>{const result=await request(`${studio.dataset.checklistUrl}?version=${version}`);const target=studio.querySelector("[data-checklist]");target.replaceChildren(...result.items.map(item=>{const button=document.createElement("button");button.type="button";button.className=`check-item ${item.severity}`;button.textContent=`${item.severity==="blocker"?"⛔":"⚠"} ${item.message}`;button.addEventListener("click",()=>{if(item.reference?.startsWith("field:"))studio.querySelector(`[data-field-editor="${CSS.escape(item.reference.slice(6))}"]`)?.focus();});return button;}));return result;};
  studio.querySelector("[data-open-checklist]").addEventListener("click",async event=>{const trigger=event.currentTarget;await openTab("checklist");const result=await loadChecklist();if(!result.canSubmit||localRevision!==acknowledgedRevision||conflict)return;const reviewers=await request(studio.dataset.reviewersUrl);if(!reviewers.length){state.textContent="Nenhum revisor autorizado disponível";state.dataset.state="failed";return;}const target=studio.querySelector("[data-checklist]");const label=document.createElement("label");label.textContent="Revisor";const select=document.createElement("select");for(const reviewer of reviewers)select.add(new Option(reviewer.name,reviewer.id));label.append(select);const submit=document.createElement("button");submit.type="button";submit.className="button button-primary";submit.textContent="Confirmar encaminhamento";submit.addEventListener("click",async()=>{submit.disabled=true;submit.textContent="Preparando versão…";const generationKey=crypto.randomUUID();try{const generated=await request(studio.dataset.generateUrl,{method:"POST",body:JSON.stringify({idempotencyKey:generationKey,expectedVersion:version})});submit.textContent="Encaminhando…";await request(studio.dataset.submitUrl,{method:"POST",body:JSON.stringify({generatedVersionId:generated.id,reviewerId:select.value,idempotencyKey:generationKey})});state.textContent="Encaminhado à revisão";state.dataset.state="saved";submit.textContent="Encaminhado";}catch(error){submit.disabled=false;submit.textContent="Tentar encaminhar novamente";state.textContent=error.message;state.dataset.state="failed";}});target.append(label,submit);trigger?.blur?.();});
  const dialog=studio.querySelector("[data-compare-dialog]");studio.querySelector("[data-open-compare]").addEventListener("click",async()=>{const versions=await loadVersions();for(const select of dialog.querySelectorAll("select")){select.replaceChildren(...versions.map(v=>new Option(`Versão ${v.number} · ${v.author}`,v.id)));}if(versions[1])dialog.querySelector("[data-before]").value=versions[1].id;dialog.showModal();});
  studio.querySelector("[data-run-compare]").addEventListener("click",async()=>{const before=dialog.querySelector("[data-before]").value,after=dialog.querySelector("[data-after]").value,target=dialog.querySelector("[data-comparison]");if(before===after){target.textContent="Selecione duas versões diferentes.";return;}const result=await request(`${studio.dataset.compareUrl}?before=${before}&after=${after}`);const categories=[...new Set(result.changes.map(x=>x.category))];const filters=dialog.querySelector("[data-diff-filters]");filters.replaceChildren(...categories.map(category=>{const label=document.createElement("label");const input=document.createElement("input");input.type="checkbox";input.checked=true;input.addEventListener("change",()=>target.querySelectorAll(`[data-category="${CSS.escape(category)}"]`).forEach(x=>x.hidden=!input.checked));label.append(input,` ${category}`);return label;}));target.replaceChildren(...result.changes.map(change=>{const row=document.createElement("article");row.className="diff-row";row.dataset.category=change.category;const title=document.createElement("strong");title.textContent=`${change.category} · ${change.reference}`;const old=document.createElement("pre");old.className="removed";old.textContent=`− ${change.before??""}`;const next=document.createElement("pre");next.className="added";next.textContent=`+ ${change.after??""}`;row.append(title,old,next);return row;}));});
  window.addEventListener("beforeunload",event=>{if(localRevision!==acknowledgedRevision){event.preventDefault();event.returnValue="";}});
  refresh();
}
