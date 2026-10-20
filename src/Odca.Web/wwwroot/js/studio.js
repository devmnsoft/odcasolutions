import { formatCurrency, isPartialDecimal, parseDecimal } from "./studio-decimal.js";
const t = (window.OdcaI18n && window.OdcaI18n.t) || ((k) => k);
// D-OC22: timestamps de UI seguem a língua ativa; o conteúdo do documento mantém formato canônico pt-BR.
const uiCulture = (window.OdcaI18n && window.OdcaI18n.culture) || "pt-BR";

const studio = document.querySelector("[data-studio]");
if (studio) {
  const content = JSON.parse(document.querySelector("#studio-content").textContent);
  const fields = JSON.parse(document.querySelector("#studio-fields").textContent);
  const initialValues = JSON.parse(document.querySelector("#studio-values").textContent);
  const defId = def => String(def?.id ?? def?.Id ?? def?.fieldId ?? def?.FieldId ?? "");
  const nodeFieldId = node => String(node?.fieldId ?? node?.FieldId ?? "");
  const normalizeFieldType = type => {
    if (typeof type === "number") {
      const map = ["ShortText", "LongText", "Date", "Number", "Currency", "BrazilianDocument", "Choice", "Formula"];
      return map[type] || "ShortText";
    }
    const s = String(type || "").toLowerCase();
    if (s === "longtext" || s === "textarea") return "LongText";
    if (s === "date") return "Date";
    if (s === "number") return "Number";
    if (s === "currency") return "Currency";
    if (s === "braziliandocument" || s === "cpf_cnpj" || s === "document") return "BrazilianDocument";
    if (s === "choice" || s === "select") return "Choice";
    if (s === "formula" || s === "calculated") return "Formula";
    return "ShortText";
  };

  const originLabels = {
    organization: t("studio.origin_organization"),
    patient: t("studio.origin_patient"),
    contractor: t("studio.origin_contractor"),
    representative: t("studio.origin_representative"),
    manual: t("studio.origin_manual")
  };
  const fieldOrigin = definition => String(definition?.origin ?? definition?.Origin ?? "manual").toLowerCase();
  // B.3.4 — client-side CPF/CNPJ: mask on typing, check digits on commit. Mirrors
  // Odca.Application.Onboarding.BrazilianDocument (IN RFB 2229/2024 for CNPJ).
  const brDocumentDigits = text => String(text || "").replace(/[^\dA-Za-z]/g, "").toUpperCase();
  const brDocumentValid = text => {
    const value = brDocumentDigits(text);
    if (value.length === 11 && /^\d{11}$/.test(value)) {
      if (/^(\d)\1{10}$/.test(value)) return false;
      const digit = (length, initialWeight) => {
        let sum = 0, weight = initialWeight;
        for (let index = 0; index < length; index++) {
          sum += Number(value[index]) * weight--;
          if (weight === 1) weight = 1;
        }
        const remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
      };
      return digit(9, 10) === Number(value[9]) && digit(10, 11) === Number(value[10]);
    }
    if (value.length === 14 && /^\d{14}$/.test(value)) {
      if (/^(\d)\1{13}$/.test(value)) return false;
      const digit = (length, initialWeight) => {
        let sum = 0, weight = initialWeight;
        for (let index = 0; index < length; index++) {
          sum += (value.charCodeAt(index) - 48) * weight--;
          if (weight === 1) weight = 9;
        }
        const remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
      };
      return digit(12, 5) === Number(value[12]) && digit(13, 6) === Number(value[13]);
    }
    return false;
  };
  const maskBrDocument = text => {
    const value = String(text || "");
    if (/[A-Za-z]/.test(value)) return value;
    const digits = value.replace(/\D/g, "").slice(0, 14);
    if (digits.length <= 11) {
      if (digits.length > 6) return `${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6, 9)}${digits.length > 9 ? `-${digits.slice(9, 11)}` : ""}`;
      if (digits.length > 3) return `${digits.slice(0, 3)}.${digits.slice(3)}`;
      return digits;
    }
    const base = `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8, 12)}`;
    return digits.length > 12 ? `${base}-${digits.slice(12, 14)}` : base;
  };
  // B.3.4 — explicit formula: Σ(quantity × unit) with BigInt cents math, identical to
  // StructuredContractDocument.TryComputeFormulaTotal (round half away from zero).
  const formulaTerms = definition => definition.formulaTerms ?? definition.FormulaTerms ?? [];
  const formulaTotal = definition => {
    const terms = formulaTerms(definition);
    if (!terms.length) return null;
    let sumScaled = 0n;
    let active = false;
    const qtyPattern = /^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/;
    const unitPattern = /^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/;
    for (const term of terms) {
      const quantity = String(values.get(term.quantityFieldId ?? term.QuantityFieldId)?.value ?? "").trim();
      const unit = String(values.get(term.unitFieldId ?? term.UnitFieldId)?.value ?? "").trim();
      if (!quantity && !unit) continue;
      if (!quantity || !unit) return null;
      if (!qtyPattern.test(quantity) || !unitPattern.test(unit)) return null;
      const [qInt, qFrac = ""] = quantity.split(".");
      const [uInt, uFrac = ""] = unit.split(".");
      const qtyScaled = BigInt(qInt + qFrac.padEnd(6, "0"));
      const unitCents = BigInt(uInt + uFrac.padEnd(2, "0"));
      sumScaled += qtyScaled * unitCents; // scale 10^8
      active = true;
    }
    if (!active) return "0.00";
    if (sumScaled < 0n) return null;
    let cents = sumScaled / 1000000n;
    if ((sumScaled % 1000000n) * 2n >= 1000000n) cents += 1n;
    return `${cents / 100n}.${String(cents % 100n).padStart(2, "0")}`;
  };
  const formulaExpression = definition => {
    const terms = formulaTerms(definition);
    const total = formulaTotal(definition);
    if (total === null) return t("studio.formula_pending");
    const parts = terms.map(term => {
      const quantityId = term.quantityFieldId ?? term.QuantityFieldId;
      const unitId = term.unitFieldId ?? term.UnitFieldId;
      const quantity = String(values.get(quantityId)?.value ?? "").trim();
      const unit = String(values.get(unitId)?.value ?? "").trim();
      if (!quantity && !unit) return null;
      return `${quantity || "?"} × R$ ${unit ? formatCurrency(unit) : "?"}`;
    }).filter(Boolean);
    return `${parts.length ? parts.join(" + ") + " = " : ""}R$ ${formatCurrency(total)}`;
  };
  const recomputeFormulas = () => {
    let changed = false;
    for (const definition of fields) {
      const id = defId(definition);
      if (!id || normalizeFieldType(definition.type ?? definition.Type) !== "Formula") continue;
      const value = values.get(id) || { fieldId: id, value: "", confirmed: false, source: "manual" };
      values.set(id, value);
      const total = formulaTotal(definition);
      if (total === null) continue;
      if (value.value !== total || !value.confirmed) {
        value.value = total;
        value.confirmed = true;
        value.draft = null;
        changed = true;
      }
    }
    return changed;
  };
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
    localRevision += 1; state.textContent = t("studio.ed_state_dirty"); state.dataset.state = "dirty";
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
      button.dataset.fieldId = fId; button.setAttribute("aria-label", `${label}: ${value?.confirmed ? t("studio.field_confirmed") : t("studio.field_pending")}`);
      const smartHelp = String(definition?.help ?? definition?.Help ?? "");
      if (smartHelp) button.title = smartHelp;
      const shownFieldType = normalizeFieldType(definition?.type ?? definition?.Type);
      const shown = (shownFieldType === "Currency" || shownFieldType === "Formula") && value?.value ? formatCurrency(value.value) : value?.value;
      button.textContent = shown ? shown : `⚠ ${label}`;
      button.addEventListener("click", () => document.querySelector(`[data-field-editor="${CSS.escape(fId)}"]`)?.focus());
      parent.append(button); return;
    }
    const tags = { document: "div", paragraph: "p", heading: `h${node.level || 2}`, bulletList: "ul", orderedList: "ol", listItem: "li", table: "table", tableRow: "tr", tableCell: "td", pageBreak: "hr" };
    const element = document.createElement(tags[node.type] || "div"); if (node.type === "pageBreak") element.className = "page-break";
    if (node.alignment) element.style.textAlign = node.alignment; for (const child of node.content || []) renderNode(child, element); parent.append(element);
  };
  const refresh = () => { recomputeFormulas(); paper.replaceChildren(); renderNode(content, paper); renderFields(); };
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
  const searchInput = studio.querySelector("[data-field-search]");
  const searchEmpty = document.createElement("p");
  searchEmpty.className = "muted";
  searchEmpty.dataset.fieldSearchEmpty = "true";
  searchEmpty.textContent = t("studio.field_search_empty");
  searchEmpty.hidden = true;
  // D-OC31 B.3 — busca no painel de campos: filtra rótulo/identificador e esconde grupos vazios.
  // O termo sobrevive às re-renderizações (refresh após cada commit).
  const applyFieldSearch = () => {
    const term = (searchInput ? searchInput.value : "").trim().toLowerCase();
    let anyVisible = false;
    for (const box of panel.querySelectorAll(".field-editor")) {
      const hit = !term || (box.dataset.fieldKey || "").includes(term);
      box.hidden = !hit;
      if (hit) anyVisible = true;
    }
    for (const g of panel.querySelectorAll(".field-group")) {
      g.hidden = ![...g.querySelectorAll(".field-editor")].some(b => !b.hidden);
    }
    searchEmpty.hidden = anyVisible || !term;
    panel.append(searchEmpty);
  };
  if (searchInput) searchInput.addEventListener("input", applyFieldSearch);

  const renderFields = () => {
    panel.replaceChildren(); let pending = 0;
    // D-OC31 B.3 — campos agrupados por origem declarada (organização/paciente/contratante/representante/manual).
    const groups = new Map();
    const ensureGroup = origin => {
      let g = groups.get(origin);
      if (!g) {
        g = document.createElement("section"); g.className = "field-group";
        const h = document.createElement("h3"); h.className = "field-group-title";
        h.textContent = originLabels[origin] || origin;
        g.append(h); groups.set(origin, g); panel.append(g);
      }
      return g;
    };
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
      let formulaHint = null;
      if (fieldType === "LongText") {
        input = document.createElement("textarea"); input.rows = 3; input.value = value.draft ?? value.value ?? "";
      } else if (fieldType === "Choice") {
        input = document.createElement("select");
        const defaultOption = document.createElement("option"); defaultOption.value = ""; defaultOption.textContent = t("studio.field_select_option"); input.append(defaultOption);
        const choices = definition.choices ?? definition.Choices ?? [];
        for (const choice of choices) {
          const opt = document.createElement("option"); opt.value = choice; opt.textContent = choice;
          if (choice === value.value) opt.selected = true;
          input.append(opt);
        }
        if (!input.value && value.value) input.value = value.value;
      } else if (fieldType === "Formula") {
        input = document.createElement("input");
        input.type = "text"; input.readOnly = true; input.dataset.formula = "true";
        input.placeholder = t("studio.field_formula_placeholder");
        input.value = value.value ? formatCurrency(value.value) : "";
        formulaHint = document.createElement("small"); formulaHint.className = "field-formula";
        formulaHint.textContent = formulaExpression(definition);
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
      source.textContent = t("studio.field_source", originLabels[configuredOrigin] || configuredOrigin, originLabels[value.source] || value.source || "manual");
      const fieldError = document.createElement("small"); fieldError.className = "field-error"; fieldError.hidden = !value.error; fieldError.textContent = value.error || "";
      const confirm = document.createElement("button"); confirm.type = "button"; confirm.className = "button button-small button-link"; confirm.textContent = value.confirmed ? t("studio.field_confirmed_btn") : t("studio.field_confirm_btn");
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
          value.error = t("studio.dec_incomplete");
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
      const commitDocument = confirmValue => {
        const typed = input.value;
        const digits = brDocumentDigits(typed);
        if (!digits) {
          assignValue(value, "");
          value.confirmed = false;
          value.error = null;
          values.set(id, value);
          markDirty();
          return true;
        }
        if (digits.length < 11 || !brDocumentValid(typed)) {
          const message = digits.length < 11
            ? t("studio.doc_required")
            : t("studio.doc_invalid");
          value.draft = typed;
          value.confirmed = false;
          value.error = message;
          fieldError.hidden = false;
          fieldError.textContent = message;
          if (localRevision === acknowledgedRevision) localRevision += 1;
          state.textContent = `${fieldLabelText}: ${message}`;
          state.dataset.state = "failed";
          return false;
        }
        assignValue(value, maskBrDocument(typed));
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
          state.textContent = t("studio.ed_typing_numeric"); state.dataset.state = "dirty";
          if (localRevision === acknowledgedRevision) localRevision += 1;
          clearTimeout(timer);
        });
        input.addEventListener("blur", () => { if (commitNumeric(false)) refresh(); });
      } else if (fieldType === "BrazilianDocument") {
        input.dataset.documentField = "true";
        input.addEventListener("input", () => {
          const masked = maskBrDocument(input.value);
          if (masked !== input.value) input.value = masked;
          value.draft = input.value;
          value.error = null;
          fieldError.hidden = true;
          state.textContent = t("studio.ed_typing_document"); state.dataset.state = "dirty";
          if (localRevision === acknowledgedRevision) localRevision += 1;
          clearTimeout(timer);
        });
        input.addEventListener("blur", () => { if (commitDocument(false)) refresh(); });
      } else if (input.tagName === "SELECT") {
        input.addEventListener("change", () => { if (commitText(false)) refresh(); });
      } else {
        input.addEventListener("input", () => commitText(false));
      }
      confirm.addEventListener("click", () => {
        const ok = fieldType === "Currency" || fieldType === "Number" ? commitNumeric(true)
          : fieldType === "BrazilianDocument" ? commitDocument(true)
          : commitText(true);
        if (ok) refresh();
      });
      const boxParts = [label, source, helpEl];
      if (formulaHint) boxParts.push(formulaHint);
      boxParts.push(fieldError);
      if (fieldType !== "Formula") boxParts.push(confirm);
      label.append(input);
      box.append(...boxParts);
      box.dataset.fieldKey = `${fieldLabelText} ${id}`.toLowerCase();
      ensureGroup(configuredOrigin).append(box);
    }
    applyFieldSearch();
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
      if (isPartialDecimal(typed)) return show(t("studio.dec_incomplete"));
      const parsed = parseDecimal(typed, item.currency);
      if (!parsed.ok) return show(`${parsed.error} ${t("studio.dec_not_saved")}`);
    }
    return null;
  };
  const rejectInvalidDrafts = () => {
    for (const input of panel.querySelectorAll("[data-document-field='true']")) {
      const typed = input.value;
      if (!String(typed).trim()) continue;
      const label = input.closest(".field-editor")?.querySelector("label")?.textContent ?? t("studio.field_default_document");
      const digits = brDocumentDigits(typed);
      if (digits.length < 11) return `${label}: ${t("studio.doc_required_inline")}`;
      if (!brDocumentValid(typed)) return `${label}: ${t("studio.doc_invalid_inline")}`;
    }
    for (const input of panel.querySelectorAll("input[type='date']")) {
      const typed = input.value;
      if (!typed) continue;
      const label = input.closest(".field-editor")?.querySelector("label")?.textContent ?? t("studio.field_default_date");
      if (!/^\d{4}-\d{2}-\d{2}$/.test(typed) || Number.isNaN(Date.parse(`${typed}T00:00:00Z`)))
        return `${label}: ${t("studio.date_required_inline")}`;
    }
    return null;
  };
  const save = async force => {
    if (conflict || activeRequest) return;
    const editingDocument = [...panel.querySelectorAll("[data-document-field='true'], [data-numeric='true']")]
      .some(input => input === document.activeElement);
    if (!force && editingDocument) {
      state.textContent = t("studio.ed_typing"); state.dataset.state = "dirty"; return;
    }
    const blocked = rejectNumericDrafts();
    if (blocked) {
      state.textContent = blocked; state.dataset.state = "failed";
      if (localRevision === acknowledgedRevision) localRevision += 1;
      return;
    }
    const invalidDraft = rejectInvalidDrafts();
    if (invalidDraft) {
      state.textContent = invalidDraft; state.dataset.state = "failed";
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
    recomputeFormulas();
    if (localRevision === acknowledgedRevision) return;
    const sentRevision = localRevision; state.textContent = t("studio.ed_state_saving"); state.dataset.state = "saving";
    const requestId = crypto.randomUUID(); const token = studio.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const cleanValues = [...values.values()].filter(v => Boolean(v.fieldId)).map(v => ({ fieldId: v.fieldId, value: v.value ?? "", confirmed: Boolean(v.confirmed), source: v.source || "manual" }));
    activeRequest = fetch(studio.dataset.saveUrl, { method: "PUT", headers: { "Content-Type": "application/json", ...(token ? { "RequestVerificationToken": token } : {}) }, body: JSON.stringify({ content, fields, values: cleanValues, expectedVersion: version, clientRevision: requestId }) });
    try {
      const response = await activeRequest;
      if (response.redirected || response.status === 401) { state.textContent = t("studio.ed_session_expired"); state.dataset.state = "failed"; window.location.assign("/entrar?ReturnUrl=" + encodeURIComponent(window.location.pathname + window.location.search)); return; }
      if (response.status === 409) { conflict = true; state.textContent = t("studio.ed_state_conflict"); state.dataset.state = "conflict"; studio.querySelector("[data-conflict]").hidden = false; return; }
      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        state.textContent = problem.detail || problem.title || t("studio.ed_failed_save"); state.dataset.state = "failed"; return;
      }
      const result = await response.json(); version = result.version; studio.dataset.version = String(version); acknowledgedRevision = Math.max(acknowledgedRevision, sentRevision);
      if (localRevision === sentRevision) { state.textContent = t("studio.ed_state_saved"); state.dataset.state = "saved"; savedAt.textContent = ` ${new Date(result.savedAt).toLocaleString(uiCulture)}`; refresh(); }
      else { state.textContent = t("studio.ed_state_dirty"); state.dataset.state = "dirty"; timer = setTimeout(() => save(false), 150); }
    }
    catch { state.textContent = t("studio.ed_failed_comm"); state.dataset.state = "failed"; }
    finally { activeRequest = null; }
  };
  for (const button of studio.querySelectorAll("[data-add]")) button.addEventListener("click", () => {
    const type=button.dataset.add; const node=type==="pageBreak"?{type}:{type, ...(type==="heading"?{level:2}:{}), content:[{type:"text",text:t("studio.ed_new_block")}]}; content.content.push(node); markDirty(); refresh();
  });
  for (const button of studio.querySelectorAll("[data-command]")) button.addEventListener("click", () => {
    if(!selectedText)return; const mark=button.dataset.command; selectedText.node.marks=selectedText.node.marks||[]; const index=selectedText.node.marks.indexOf(mark); if(index>=0)selectedText.node.marks.splice(index,1);else selectedText.node.marks.push(mark); markDirty(); refresh();
  });
  studio.querySelector("[data-save]").addEventListener("click",()=>save(true));
  studio.querySelector("[data-retry]").addEventListener("click",()=>{conflict=false;save();});
  // D-OC34 B.7: conflito de versao com decisao explicita — recarregar descarta o estado local;
  // manter preserva as edicoes nao salvas e permite repetir o salvamento depois.
  studio.querySelector("[data-conflict-reload]").addEventListener("click",()=>{window.location.assign(studio.dataset.draftUrl);});
  studio.querySelector("[data-conflict-keep]").addEventListener("click",()=>{conflict=false;studio.querySelector("[data-conflict]").hidden=true;});
  studio.querySelector("[data-zoom]").addEventListener("change",event=>paper.style.zoom=`${event.target.value}%`);
  studio.querySelector("[data-reading]").addEventListener("click",event=>{const reading=studio.classList.toggle("reading-mode");event.currentTarget.setAttribute("aria-pressed",String(reading));});
  const openTab = async name => {
    for(const section of studio.querySelectorAll("[data-panel]"))section.hidden=section.dataset.panel!==name;
    for(const tab of studio.querySelectorAll("[data-tab]"))tab.setAttribute("aria-selected",String(tab.dataset.tab===name));
    if(name==="comments")await loadComments();if(name==="checklist")await loadChecklist();if(name==="history")await loadVersions();if(name==="documents")await loadDocuments();
  };
  for(const tab of studio.querySelectorAll("[data-tab]"))tab.addEventListener("click",()=>openTab(tab.dataset.tab));
  const request = async (url, options={}) => {const token=studio.querySelector('input[name="__RequestVerificationToken"]')?.value;const response=await fetch(url,{...options,headers:{...(options.body?{"Content-Type":"application/json"}:{}),...(token?{"RequestVerificationToken":token}:{}),...options.headers}});if(!response.ok)throw new Error((await response.json().catch(()=>({}))).title||t("studio.ed_unconfirmed"));return response.status===204?null:response.json();};
  const loadVersions=async()=>{const versions=await request(studio.dataset.versionsUrl);const history=studio.querySelector("[data-history]");history.replaceChildren(...versions.map(item=>{const p=document.createElement("p");p.append(document.createTextNode(t("studio.v_line",item.number,item.author,new Date(item.createdAt).toLocaleString(uiCulture))+" "));const b=document.createElement("button");b.type="button";b.className="button button-small button-link";b.textContent=t("studio.v_restore");b.addEventListener("click",()=>{if(!confirm(t("studio.v_restore_confirm")))return;if(localRevision!==acknowledgedRevision&&!confirm(t("studio.v_restore_confirm_dirty")))return;state.textContent=t("studio.ed_state_saving");state.dataset.state="saving";request(studio.dataset.restoreUrl,{method:"POST",body:JSON.stringify({versionId:item.id,expectedVersion:version})}).then(()=>window.location.reload()).catch(error=>{state.textContent=error.message||t("studio.ed_failed_comm");state.dataset.state="failed";});});p.append(b);return p;}));return versions;};
  const loadComments=async()=>{const items=await request(`${studio.dataset.commentsUrl}?resolved=${studio.querySelector("[data-resolved]").checked}`);const target=studio.querySelector("[data-comments]");studio.querySelector("[data-comment-count]").textContent=String(items.filter(item=>!item.resolved).length);target.replaceChildren(...items.map(item=>{const article=document.createElement("article");article.className=`comment${item.parentId?" reply":""}`;article.id=`comment-${item.id}`;const heading=document.createElement("strong");heading.textContent=`${item.author} · ${item.reference}`;const metadata=document.createElement("small");metadata.textContent=`${item.originVersion?t("studio.cm_origin_version",item.originVersion):t("studio.cm_origin_review",item.draftRevision)} · ${new Date(item.lastMovementAt||item.createdAt).toLocaleString(uiCulture)}`;const body=document.createElement("p");body.textContent=item.body;const location=document.createElement("p");location.className="muted";location.textContent=item.referenceLocated?t("studio.cm_excerpt_found"):t("studio.cm_excerpt_missing");const reply=document.createElement("button");reply.type="button";reply.className="button button-small button-link";reply.textContent=t("studio.cm_reply");reply.addEventListener("click",()=>{commentParentId=item.id;const form=studio.querySelector("[data-comment-form]");form.elements.reference.value=item.reference;form.elements.body.focus();});const history=document.createElement("button");history.type="button";history.className="button button-small button-link";history.textContent=t("studio.cm_history");history.addEventListener("click",()=>article.querySelector("p").focus());const button=document.createElement("button");button.type="button";button.className="button button-small button-link";button.textContent=item.resolved?t("studio.cm_reopen"):t("studio.cm_resolve");button.addEventListener("click",async()=>{const operation=item.resolved?"reopen":"resolve";const observation=operation==="reopen"?window.prompt(t("studio.cm_reopen_prompt")):null;if(operation==="reopen"&&!observation?.trim())return;const original=button.textContent;button.disabled=true;button.textContent=t("studio.cm_processing");try{await request(`${studio.dataset.commentsUrl}/${item.id}/${operation}`,{method:"POST",body:JSON.stringify({expectedResolved:item.resolved,observation})});await loadComments();}catch(error){button.disabled=false;button.textContent=original;state.textContent=error.message;state.dataset.state=error.message.includes("atualiz")?"conflict":"failed";}});article.append(heading,metadata,body,location,reply,history,button);return article;}));};
  studio.querySelector("[data-resolved]").addEventListener("change",loadComments);
  studio.querySelector("[data-comment-form]").addEventListener("submit",async event=>{event.preventDefault();const data=new FormData(event.currentTarget);try{await request(studio.dataset.commentsUrl,{method:"POST",body:JSON.stringify({draftRevision:version,reference:data.get("reference"),body:data.get("body"),parentId:commentParentId})});commentParentId=null;event.currentTarget.reset();await loadComments();}catch(error){state.textContent=error.message;state.dataset.state="failed";}});
  const loadChecklist=async()=>{const result=await request(`${studio.dataset.checklistUrl}?version=${version}`);const target=studio.querySelector("[data-checklist]");target.replaceChildren(...result.items.map(item=>{const button=document.createElement("button");button.type="button";button.className=`check-item ${item.severity}`;button.textContent=`${item.severity==="blocker"?"⛔":"⚠"} ${item.message}`;button.addEventListener("click",()=>{if(item.reference?.startsWith("field:"))studio.querySelector(`[data-field-editor="${CSS.escape(item.reference.slice(6))}"]`)?.focus();});return button;}));return result;};
  studio.querySelector("[data-open-checklist]").addEventListener("click",async event=>{const trigger=event.currentTarget;await openTab("checklist");const result=await loadChecklist();if(!result.canSubmit||localRevision!==acknowledgedRevision||conflict)return;const reviewers=await request(studio.dataset.reviewersUrl);if(!reviewers.length){state.textContent=t("studio.cl_no_reviewer");state.dataset.state="failed";return;}const target=studio.querySelector("[data-checklist]");const label=document.createElement("label");label.textContent=t("studio.cl_reviewer");const select=document.createElement("select");for(const reviewer of reviewers)select.add(new Option(reviewer.name,reviewer.id));label.append(select);const submit=document.createElement("button");submit.type="button";submit.className="button button-primary";submit.textContent=t("studio.cl_submit_btn");submit.addEventListener("click",async()=>{submit.disabled=true;submit.textContent=t("studio.cl_preparing");const generationKey=crypto.randomUUID();try{const generated=await request(studio.dataset.generateUrl,{method:"POST",body:JSON.stringify({idempotencyKey:generationKey,expectedVersion:version})});submit.textContent=t("studio.cl_submitting");await request(studio.dataset.submitUrl,{method:"POST",body:JSON.stringify({generatedVersionId:generated.id,reviewerId:select.value,idempotencyKey:generationKey})});state.textContent=t("studio.cl_submitted_state");state.dataset.state="saved";submit.textContent=t("studio.cl_submitted_btn");}catch(error){submit.disabled=false;submit.textContent=t("studio.cl_retry_btn");state.textContent=error.message;state.dataset.state="failed";}});target.append(label,submit);trigger?.blur?.();});
  const dialog=studio.querySelector("[data-compare-dialog]");studio.querySelector("[data-open-compare]").addEventListener("click",async()=>{const versions=await loadVersions();for(const select of dialog.querySelectorAll("select")){select.replaceChildren(...versions.map(v=>new Option(t("studio.v_option",v.number,v.author),v.id)));}if(versions[1])dialog.querySelector("[data-before]").value=versions[1].id;dialog.showModal();});
  studio.querySelector("[data-run-compare]").addEventListener("click",async()=>{const before=dialog.querySelector("[data-before]").value,after=dialog.querySelector("[data-after]").value,target=dialog.querySelector("[data-comparison]");if(before===after){target.textContent=t("studio.cmp_select_two");return;}const result=await request(`${studio.dataset.compareUrl}?before=${before}&after=${after}`);const categories=[...new Set(result.changes.map(x=>x.category))];const filters=dialog.querySelector("[data-diff-filters]");filters.replaceChildren(...categories.map(category=>{const label=document.createElement("label");const input=document.createElement("input");input.type="checkbox";input.checked=true;input.addEventListener("change",()=>target.querySelectorAll(`[data-category="${CSS.escape(category)}"]`).forEach(x=>x.hidden=!input.checked));label.append(input,` ${category}`);return label;}));target.replaceChildren(...result.changes.map(change=>{const row=document.createElement("article");row.className="diff-row";row.dataset.category=change.category;const title=document.createElement("strong");title.textContent=`${change.category} · ${change.reference}`;const old=document.createElement("pre");old.className="removed";old.textContent=`− ${change.before??""}`;const next=document.createElement("pre");next.className="added";next.textContent=`+ ${change.after??""}`;row.append(title,old,next);return row;}));});
  // D-OC32: painel de documentos do contrato + extração de texto aplicada ao rascunho.
  let docVersions = []; let reuploadDocumentId = null; let extractionId = null; let extractionTimer = 0;
  const suggestionDecisions = new Map();
  const securityKey = { pending: "studio.sec_pending", scanning: "studio.sec_scanning", safe: "studio.sec_safe", quarantined: "studio.sec_quarantined", failed: "studio.sec_failed" };
  const securityLabel = status => securityKey[status] ? t(securityKey[status]) : status;
  const formatBytes = bytes => { let value = Number(bytes || 0); if (!value) return "0 B"; const units = ["B", "KB", "MB", "GB"]; let index = 0; while (value >= 1024 && index < units.length - 1) { value /= 1024; index++; } return `${value >= 10 || index === 0 ? Math.round(value) : value.toFixed(1)} ${units[index]}`; };
  const renderDocuments = () => {
    const target = studio.querySelector("[data-document-list]");
    if (!docVersions.length) { const p = document.createElement("p"); p.className = "muted"; p.textContent = t("studio.doc_empty"); target.replaceChildren(p); return; }
    const groups = new Map();
    for (const item of docVersions) { if (!groups.has(item.id)) groups.set(item.id, []); groups.get(item.id).push(item); }
    target.replaceChildren(...[...groups.values()].map(versions => {
      const article = document.createElement("article"); article.className = "doc-entry";
      const head = document.createElement("div"); head.className = "doc-entry-head";
      const name = document.createElement("strong"); name.textContent = versions[0].title || versions[0].display_name || versions[0].id;
      const replace = document.createElement("button"); replace.type = "button"; replace.className = "button button-small button-link"; replace.textContent = t("studio.doc_reupload");
      replace.addEventListener("click", () => { reuploadDocumentId = versions[0].id; const input = studio.querySelector("[data-upload-form] input[type=file]"); input.value = ""; input.focus(); });
      head.append(name, " ", replace); article.append(head);
      for (const item of versions) {
        const row = document.createElement("p"); row.className = "muted";
        row.append(document.createTextNode(`${t("studio.doc_version", item.version_number)} · ${item.detected_type || "?"} · ${formatBytes(item.byte_size)} · ${new Date(item.uploaded_at).toLocaleString(uiCulture)} · `));
        const badge = document.createElement("span"); badge.className = `status-chip sec-${item.security_status}`; badge.textContent = securityLabel(item.security_status); row.append(badge);
        if (item.security_status === "safe") {
          const extract = document.createElement("button"); extract.type = "button"; extract.className = "button button-small button-link"; extract.textContent = t("studio.doc_extract");
          extract.addEventListener("click", () => startExtraction(versions[0].id, item.version_id));
          const aiButton = document.createElement("button"); aiButton.type = "button"; aiButton.className = "button button-small button-link"; aiButton.textContent = t("studio.ai_analyze");
          aiButton.addEventListener("click", () => startAiAnalysis(versions[0].id, item.version_id, row));
          row.append(" ", extract, " ", aiButton);
        }
        article.append(row);
      }
      return article;
    }));
  };
  const loadDocuments = async () => { docVersions = await request(studio.dataset.documentsUrl) || []; renderDocuments(); return docVersions; };
  studio.querySelector("[data-upload-form]").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const input = form.elements.file;
    if (!input.files.length) return;
    const statusEl = form.querySelector("[data-upload-status]");
    const token = studio.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const payload = new FormData(); payload.append("file", input.files[0]); if (reuploadDocumentId) payload.append("documentId", reuploadDocumentId);
    try {
      const response = await fetch(studio.dataset.uploadUrl, { method: "POST", headers: token ? { RequestVerificationToken: token } : {}, body: payload });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.title || result.detail || t("studio.up_fail"));
      statusEl.textContent = result.replayed ? t("studio.up_replayed") : t("studio.up_ok");
      reuploadDocumentId = null; input.value = "";
      await loadDocuments();
    } catch (error) { statusEl.textContent = error.message || t("studio.up_fail"); }
  });
  const conceptKey = { title: "studio.x_title", reference: "studio.x_reference", start_date: "studio.x_start", end_date: "studio.x_end", renewal_notice_days: "studio.x_notice", value: "studio.x_value", currency: "studio.x_value", party_document: "studio.x_document" };
  const renderSuggestions = items => {
    studio.querySelector("[data-suggestions-empty]").hidden = items.length > 0;
    studio.querySelector("[data-apply]").hidden = !items.length;
    const target = studio.querySelector("[data-suggestions]");
    target.replaceChildren(...items.map(item => {
      const card = document.createElement("article"); card.className = "suggestion";
      const heading = document.createElement("strong"); heading.textContent = conceptKey[item.field_name] ? t(conceptKey[item.field_name]) : item.field_name;
      const evidence = document.createElement("p"); evidence.className = "muted";
      evidence.textContent = `${item.evidence || item.extracted_value || ""}${item.page_number ? ` · ${t("studio.x_page", item.page_number)}` : ""}`;
      const control = document.createElement("label"); control.textContent = `${t("studio.x_suggested")} `;
      const valueInput = document.createElement("input"); valueInput.type = "text"; valueInput.value = item.normalized_value ?? item.extracted_value ?? "";
      control.append(valueInput);
      let decision = "accepted";
      const sync = () => { suggestionDecisions.set(item.id, { status: decision, value: valueInput.value.trim() }); card.dataset.decision = decision; };
      const makeButton = status => { const button = document.createElement("button"); button.type = "button"; button.className = "button button-small button-link"; button.textContent = t(status === "accepted" ? "studio.x_accept" : status === "edited" ? "studio.x_edit" : "studio.x_reject"); button.addEventListener("click", () => { decision = status; sync(); }); return button; };
      const actions = document.createElement("div"); actions.append(makeButton("accepted"), " ", makeButton("edited"), " ", makeButton("rejected"));
      valueInput.addEventListener("input", () => { if (decision !== "rejected") { decision = "edited"; } sync(); });
      sync();
      card.append(heading, evidence, control, actions);
      return card;
    }));
  };
  const startExtraction = async (documentId, versionId) => {
    const box = studio.querySelector("[data-extraction]"); box.hidden = false;
    const statusEl = studio.querySelector("[data-extraction-status]");
    const scopeEl = studio.querySelector("[data-extraction-scope]");
    suggestionDecisions.clear(); studio.querySelector("[data-suggestions]").replaceChildren();
    studio.querySelector("[data-suggestions-empty]").hidden = true;
    studio.querySelector("[data-apply]").hidden = true;
    studio.querySelector("[data-apply-result]").textContent = "";
    scopeEl.textContent = ""; scopeEl.hidden = true;
    clearInterval(extractionTimer);
    statusEl.textContent = t("studio.x_queued");
    try {
      const created = await request(studio.dataset.extractUrl, { method: "POST", body: JSON.stringify({ documentId, versionId }) });
      extractionId = created.extractionId;
      extractionTimer = setInterval(async () => {
        let data;
        try { data = await request(`${studio.dataset.extractionBase}${extractionId}`); } catch (error) { statusEl.textContent = error.message; clearInterval(extractionTimer); return; }
        const job = data.job || {};
        if (job.status === "failed") { clearInterval(extractionTimer); statusEl.textContent = `${t("studio.x_failed")} (${job.failure_code ?? "?"})`; return; }
        if (job.status !== "ready_for_review") return;
        clearInterval(extractionTimer);
        // D-OC34: analise parcial nunca aparece como integral — pagina processada/total e a
        // proxima acao ficam declaradas junto das sugestoes (revisao humana preservada).
        scopeEl.textContent = "";
        if (job.truncated === true) {
          const processed = Number(job.pages_processed ?? 0);
          scopeEl.textContent = (job.pages_total != null
            ? t("studio.x_scope_partial", processed, Number(job.pages_total), Math.max(0, Number(job.pages_total) - processed))
            : t("studio.x_scope_unknown", processed)) + " " + t("studio.x_scope_next");
          scopeEl.hidden = false;
        } else { scopeEl.hidden = true; }
        renderSuggestions(data.suggestions || []);
      }, 2500);
    } catch (error) { statusEl.textContent = error.message; }
  };
  // D-OC52 (Sprint 18) + D-OC53 (Sprint 19): analise IA do texto extraído por versão exata.
  // O botão gera/persiste a análise (POST); novo clique reaproveita o último resultado da mesma
  // versão (cache do cliente; o servidor também deduplica por hash de conteúdo). Estados honestos,
  // rótulos via bag i18n, score 0-100 com fatores e delta vs cláusulas padrão — sem inventar.
  const aiClauseKey = { price_change:"studio.ai_clause_price_change", termination_notice:"studio.ai_clause_termination_notice", penalty:"studio.ai_clause_penalty", exclusivity:"studio.ai_clause_exclusivity", data_protection:"studio.ai_clause_data_protection", liability_limit:"studio.ai_clause_liability_limit", indemnity:"studio.ai_clause_indemnity", jurisdiction:"studio.ai_clause_jurisdiction" };
  const riskKey = risk => t(`studio.ai_risk_${risk === "low" ? "low" : risk === "high" ? "high" : "medium"}`);
  const aiFactorLabel = code => code === "atypical_clause" ? t("studio.ai_factor_atypical") : aiClauseKey[code] ? t(aiClauseKey[code]) : /^overall_(low|medium|high)$/.test(code) ? riskKey(code.slice(8)) : code;
  const aiCache = new Map();
  const renderAiResult = (data, panel, reused) => {
    panel.replaceChildren();
    if (!data || !data.available) {
      const p=document.createElement("p");
      p.className=data && (data.reason === "no_text" || data.reason === "module_not_enabled") ? "warning" : "muted";
      p.textContent=data && data.reason === "no_text"?t("studio.ai_no_text"):data && data.reason === "module_not_enabled"?t("studio.ai_module_not_enabled"):t("studio.ai_unavailable");
      panel.append(p); return;
    }
    if (reused){const note=document.createElement("p");note.className="muted";note.textContent=t("studio.ai_reused");panel.append(note);}
    const heading=document.createElement("p"); heading.textContent=t("studio.ai_result_h");
    if (data.overallRisk){const chip=document.createElement("span");chip.className="status-chip";chip.textContent=riskKey(data.overallRisk);heading.append(" ",chip);}
    panel.append(heading);
    if (data.summary){const summary=document.createElement("p");summary.textContent=data.summary;panel.append(summary);}
    if (data.score && typeof data.score.value === "number"){
      const scoreBox=document.createElement("div"); scoreBox.className="suggestion";
      const title=document.createElement("strong");
      title.textContent=`${t("studio.ai_score_h")}: ${t("studio.ai_score_value", data.score.value)}`;
      scoreBox.append(title);
      if (Array.isArray(data.score.factors) && data.score.factors.length){
        const factors=document.createElement("p"); factors.className="muted";
        for(const f of data.score.factors){ factors.append(`${aiFactorLabel(f.code)} (+${f.weight}) `); }
        scoreBox.append(factors);
      }
      panel.append(scoreBox);
    }
    if (data.templateComparison){
      const tc=data.templateComparison;
      const tcBox=document.createElement("div"); tcBox.className="suggestion";
      const tcTitle=document.createElement("strong"); tcTitle.textContent=t("studio.ai_template_h"); tcBox.append(tcTitle);
      const groups=[["detected","\u2713","studio.ai_template_present"],["missing","\u25CB","studio.ai_template_missing"],["unexpected","!","studio.ai_template_unexpected"]];
      for(const [key,mark,keyName] of groups){
        const codes=tc[key]||[]; if(!codes.length) continue;
        const line=document.createElement("p"); line.className="muted";
        line.append(t(keyName)+": ");
        for(const code of codes){ const chip=document.createElement("span"); chip.className="status-chip"; chip.textContent=`${mark} ${aiClauseKey[code]?t(aiClauseKey[code]):code}`; line.append(chip," "); }
        tcBox.append(line);
      }
      panel.append(tcBox);
    }
    const findingsTitle=document.createElement("strong");findingsTitle.textContent=t("studio.ai_findings_title");panel.append(findingsTitle);
    if(!data.findings.length){const none=document.createElement("p");none.className="success";none.textContent=t("studio.ai_no_findings");panel.append(none);}
    else{for(const finding of data.findings){const card=document.createElement("article");card.className="suggestion";const label=document.createElement("strong");label.textContent=aiClauseKey[finding.code]?t(aiClauseKey[finding.code]):finding.code;const chip=document.createElement("span");chip.className="status-chip";chip.textContent=riskKey(finding.risk);const evidence=document.createElement("p");evidence.className="muted";evidence.textContent=finding.evidence||"";card.append(label," ",chip,evidence);panel.append(card);}}
    if(data.categories.length){const categoriesTitle=document.createElement("strong");categoriesTitle.textContent=t("studio.ai_categories_title");const chips=document.createElement("p");for(const category of data.categories){const chip=document.createElement("span");chip.className="status-chip";chip.textContent=category;chips.append(chip," ");}panel.append(categoriesTitle,chips);}
    const footer=document.createElement("small");footer.className="muted";footer.textContent=t("studio.ai_footer",data.providerName||"?",Number(data.analyzedChars||0).toLocaleString(uiCulture),(data.contentHash||"").slice(0,12));
    panel.append(footer);
  };
  const startAiAnalysis = async (documentId, versionId, row) => {
    const cacheKey=documentId+"/"+versionId;
    const panel = document.createElement("div"); panel.setAttribute("role","status"); panel.className="muted";
    if (aiCache.has(cacheKey)) { renderAiResult(aiCache.get(cacheKey), panel, true); row.after(panel); return; }
    panel.textContent=t("studio.ai_loading"); row.after(panel);
    let data;
    try { data = await request(`${studio.dataset.aiBase}${documentId}/versoes/${versionId}/analise-ia`, { method: "POST" }); }
    catch (error) { panel.textContent=error.message; return; }
    if (data && data.available) aiCache.set(cacheKey, data);
    renderAiResult(data, panel, false);
  };
  studio.querySelector("[data-apply]").addEventListener("click", async () => {
    const resultEl = studio.querySelector("[data-apply-result]");
    if (!extractionId) return;
    const decisions = [...suggestionDecisions.entries()].map(([id, entry]) => ({ suggestionId: id, status: entry.status, value: entry.status === "rejected" ? null : entry.value }));
    const token = studio.querySelector('input[name="__RequestVerificationToken"]')?.value;
    try {
      const response = await fetch(studio.dataset.applyExtractionUrl, { method: "POST", headers: { "Content-Type": "application/json", ...(token ? { RequestVerificationToken: token } : {}) }, body: JSON.stringify({ extractionId, expectedVersion: version, idempotencyKey: crypto.randomUUID(), decisions }) });
      const data = await response.json().catch(() => ({}));
      if (response.status === 409 && data.code === "draft.save.conflict") { resultEl.textContent = t("studio.x_conflict", data.currentVersion ?? "?"); setTimeout(() => window.location.reload(), 1800); return; }
      if (!response.ok) throw new Error(data.detail || data.title || t("studio.ed_unconfirmed"));
      const appliedCount = (data.applied || []).length; const skippedCount = (data.skipped || []).length;
      resultEl.textContent = data.repeated ? t("studio.x_repeated") : t("studio.x_applied", appliedCount, skippedCount);
      setTimeout(() => window.location.reload(), data.repeated ? 1200 : 1800);
    } catch (error) { resultEl.textContent = error.message; }
  });
  window.addEventListener("beforeunload",event=>{if(localRevision!==acknowledgedRevision){event.preventDefault();event.returnValue="";}});
  refresh();
}
