export const isStoredCurrency = value => /^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/.test(value);

export const formatCurrency = value => {
  if (!isStoredCurrency(value)) return value;
  const [integer, fraction = ""] = String(value).split(".");
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  return `${grouped},${(fraction + "00").slice(0, 2)}`;
};

export const isPartialDecimal = raw => {
  let text = String(raw ?? "").trim().replace(/\u00A0/g, "");
  if (/^r\$/i.test(text)) text = text.slice(2).trim();
  if (!text) return false;
  return /^[+-]?$/.test(text) || /[.,]$/.test(text) || /^[.,]/.test(text);
};

export const parseDecimal = (raw, currency) => {
  let text = String(raw ?? "").trim().replace(/\u00A0/g, "");
  if (/^r\$/i.test(text)) text = text.slice(2).trim();
  if (!text) return { ok: false, error: "Informe um valor." };
  if (/[a-zA-Z\s]/.test(text)) return { ok: false, error: "Informe apenas o valor numérico." };
  let negative = false;
  if (text.startsWith("+")) text = text.slice(1);
  else if (text.startsWith("-")) { negative = true; text = text.slice(1); }
  if (!text) return { ok: false, error: "O valor numérico é inválido." };
  if (negative && currency) return { ok: false, error: "Valor monetário negativo não é permitido." };
  if (!/^[\d.,]+$/.test(text)) return { ok: false, error: "O valor numérico é inválido." };
  const comma = text.lastIndexOf(",");
  const dot = text.lastIndexOf(".");
  let integer = "";
  let fraction = "";
  const groups = (value, separator) => {
    const parts = value.split(separator);
    if (parts.length < 2 || parts.some(part => !/^\d+$/.test(part))) return null;
    if (parts[0].length < 1 || parts[0].length > 3 || (parts[0].length > 1 && parts[0].startsWith("0"))) return null;
    if (parts.slice(1).some(part => part.length !== 3)) return null;
    return parts.join("");
  };
  if (comma >= 0 && dot >= 0) {
    const decimalSeparator = comma > dot ? "," : ".";
    const groupSeparator = decimalSeparator === "," ? "." : ",";
    const split = text.lastIndexOf(decimalSeparator);
    fraction = text.slice(split + 1);
    integer = groups(text.slice(0, split), groupSeparator);
    if (integer === null || fraction.length === 0 || text.indexOf(decimalSeparator) !== split)
      return { ok: false, error: "Separe milhar e decimal de forma explícita." };
  } else if (comma < 0 && dot < 0) {
    if (text.length > 1 && text.startsWith("0")) return { ok: false, error: "O valor numérico é inválido." };
    integer = text;
  } else {
    const separator = comma >= 0 ? "," : ".";
    const pieces = text.split(separator);
    const tail = pieces.at(-1);
    if (pieces.length === 2 && tail.length === 3) return { ok: false, error: "Valor ambíguo. Use 1.250,00 ou 1250.00." };
    if (pieces.length === 2) {
      integer = pieces[0];
      fraction = tail;
      if (!/^\d+$/.test(integer) || !/^\d+$/.test(fraction) || integer.length === 0 || (integer.length > 1 && integer.startsWith("0")))
        return { ok: false, error: "O valor numérico é inválido." };
    } else {
      integer = groups(text, separator);
      if (integer === null) return { ok: false, error: "O separador de milhar está inconsistente." };
    }
  }
  if (integer.length > 1 && integer.startsWith("0")) return { ok: false, error: "O valor numérico é inválido." };
  if (currency && fraction.length > 2) return { ok: false, error: "A moeda aceita no máximo duas casas decimais." };
  if (!currency && fraction.length > 6) return { ok: false, error: "O número excede seis casas decimais." };
  const canonical = currency
    ? `${integer}.${(fraction + "00").slice(0, 2)}`
    : `${negative ? "-" : ""}${integer}${fraction ? "." + fraction : ""}`;
  return { ok: true, canonical };
};
