const studio = document.querySelector("[data-studio]");
if (studio) {
  const content = JSON.parse(document.querySelector("#studio-content").textContent);
  const fields = JSON.parse(document.querySelector("#studio-fields").textContent);
  const initialValues = JSON.parse(document.querySelector("#studio-values").textContent);
  const values = new Map(initialValues.map(value => [value.fieldId ?? value.FieldId, value]));
  const paper = studio.querySelector("[data-document]");
  const panel = studio.querySelector("[data-fields]");
  const state = studio.querySelector("[data-save-state]");
  let version = Number(studio.dataset.version), localRevision = 0, acknowledgedRevision = 0;
  let timer = 0, activeRequest = null, conflict = false, selectedText = null;

  const key = object => object.fieldId ?? object.FieldId;
  const markDirty = () => {
    localRevision += 1; state.textContent = "Alterado"; state.dataset.state = "dirty";
    clearTimeout(timer); timer = setTimeout(save, 900);
  };
  const renderNode = (node, parent) => {
    if (node.type === "text") {
      const span = document.createElement("span"); span.textContent = node.text || ""; span.contentEditable = "true"; span.spellcheck = true;
      for (const mark of node.marks || []) span.classList.add(`mark-${mark}`);
      span.addEventListener("focus", () => { selectedText = { node, span }; }); span.addEventListener("input", () => { node.text = span.textContent; markDirty(); }); parent.append(span); return;
    }
    if (node.type === "field") {
      const definition = fields.find(item => key(item) === node.fieldId); const value = values.get(node.fieldId);
      const button = document.createElement("button"); button.type="button"; button.className=`smart-field ${value?.confirmed ? "confirmed" : "pending"}`;
      button.dataset.fieldId=node.fieldId; button.setAttribute("aria-label", `${definition?.label || node.fieldId}: ${value?.confirmed ? "confirmado" : "pendente"}`);
      button.textContent=value?.value || `⚠ ${definition?.label || node.fieldId}`; button.addEventListener("click",()=>document.querySelector(`[data-field-editor="${CSS.escape(node.fieldId)}"]`)?.focus()); parent.append(button); return;
    }
    const tags={document:"div",paragraph:"p",heading:`h${node.level || 2}`,bulletList:"ul",orderedList:"ol",listItem:"li",table:"table",tableRow:"tr",tableCell:"td",pageBreak:"hr"};
    const element=document.createElement(tags[node.type] || "div"); if(node.type==="pageBreak")element.className="page-break";
    if(node.alignment)element.style.textAlign=node.alignment; for(const child of node.content || [])renderNode(child,element); parent.append(element);
  };
  const refresh = () => { paper.replaceChildren(); renderNode(content,paper); renderFields(); };
  const renderFields = () => {
    panel.replaceChildren(); let pending=0;
    for(const definition of fields){const id=key(definition), value=values.get(id)||{fieldId:id,value:"",confirmed:false,source:"manual"};if(!value.confirmed && (definition.required ?? definition.Required))pending++;
      const box=document.createElement("div");box.className="field-editor";const label=document.createElement("label");label.textContent=definition.label ?? definition.Label;const input=document.createElement("input");input.value=value.value||"";input.dataset.fieldEditor=id;
      input.type=(definition.type===2||definition.Type===2)?"date":"text";const source=document.createElement("small");source.textContent=`Origem: ${value.source || "entrada manual"}`;const confirm=document.createElement("button");confirm.type="button";confirm.className="button button-small button-link";confirm.textContent=value.confirmed?"Confirmado":"Confirmar valor";
      const update=()=>{value.value=input.value;value.confirmed=false;values.set(id,value);markDirty();};input.addEventListener("input",update);confirm.addEventListener("click",()=>{value.value=input.value;value.confirmed=Boolean(input.value.trim());value.source=value.source||"manual";values.set(id,value);markDirty();refresh();});label.append(input);box.append(label,source,confirm);panel.append(box);}
    studio.querySelector("[data-pending-count]").textContent=String(pending);
  };
  const save = async () => {
    if(conflict || activeRequest || localRevision===acknowledgedRevision)return; const sentRevision=localRevision; state.textContent="Salvando…";state.dataset.state="saving";
    const requestId=crypto.randomUUID(); const token=studio.querySelector('input[name="__RequestVerificationToken"]')?.value;
    activeRequest=fetch(studio.dataset.saveUrl,{method:"PUT",headers:{"Content-Type":"application/json",...(token?{"RequestVerificationToken":token}:{})},body:JSON.stringify({content,fields,values:[...values.values()],expectedVersion:version,clientRevision:requestId})});
    try{const response=await activeRequest;if(response.status===409){conflict=true;state.textContent="Conflito — conteúdo local preservado";state.dataset.state="conflict";return;}if(!response.ok)throw new Error();const result=await response.json();version=result.version;acknowledgedRevision=Math.max(acknowledgedRevision,sentRevision);if(localRevision===sentRevision){state.textContent="Salvo";state.dataset.state="saved";}else{state.textContent="Alterado";timer=setTimeout(save,150);}}
    catch{state.textContent="Falha ao salvar — tentar novamente";state.dataset.state="failed";}finally{activeRequest=null;}
  };
  for (const button of studio.querySelectorAll("[data-add]")) button.addEventListener("click", () => {
    const type=button.dataset.add; const node=type==="pageBreak"?{type}:{type, ...(type==="heading"?{level:2}:{}), content:[{type:"text",text:"Novo bloco"}]}; content.content.push(node); markDirty(); refresh();
  });
  for (const button of studio.querySelectorAll("[data-command]")) button.addEventListener("click", () => {
    if(!selectedText)return; const mark=button.dataset.command; selectedText.node.marks=selectedText.node.marks||[]; const index=selectedText.node.marks.indexOf(mark); if(index>=0)selectedText.node.marks.splice(index,1);else selectedText.node.marks.push(mark); markDirty(); refresh();
  });
  studio.querySelector("[data-save]").addEventListener("click",save);
  studio.querySelector("[data-zoom]").addEventListener("change",event=>paper.style.zoom=`${event.target.value}%`);
  window.addEventListener("beforeunload",event=>{if(localRevision!==acknowledgedRevision){event.preventDefault();event.returnValue="";}});
  refresh();
}
