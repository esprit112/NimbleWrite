// Tolerant JSON extraction for LLM output: strips think-blocks and code fences, repairs trailing commas,
// and can salvage individual objects from truncated or malformed arrays.

export function stripThinking(raw: string): string {
  let s = raw.replace(/<think>[\s\S]*?<\/think>/gi, '');
  // An unterminated think block (truncated output) hides everything after it.
  const open = s.search(/<think>/i);
  if (open >= 0) s = s.slice(0, open);
  return s.trim();
}

/** Index of the bracket that closes the one at `start`, or -1 if unbalanced (e.g. truncated). */
export function balancedEnd(s: string, start: number): number {
  const open = s[start];
  const close = open === '{' ? '}' : open === '[' ? ']' : '';
  if (!close) return -1;
  let depth = 0;
  let inStr = false;
  for (let i = start; i < s.length; i++) {
    const c = s[i];
    if (inStr) {
      if (c === '\\') i++;
      else if (c === '"') inStr = false;
      continue;
    }
    if (c === '"') inStr = true;
    else if (c === '{' || c === '[') depth++;
    else if (c === '}' || c === ']') {
      depth--;
      if (depth === 0) return c === close ? i : -1;
    }
  }
  return -1;
}

function repair(s: string): string {
  return s
    .replace(/[“”]/g, '"')
    .replace(/,\s*([}\]])/g, '$1');
}

function tryParse(s: string): unknown {
  try {
    return JSON.parse(s);
  } catch {
    try {
      return JSON.parse(repair(s));
    } catch {
      return undefined;
    }
  }
}

export function parseLooseJson(raw: string): unknown | null {
  let s = stripThinking(raw);
  const fence = /```(?:json)?\s*([\s\S]*?)```/i.exec(s);
  if (fence) s = fence[1].trim();
  const direct = tryParse(s);
  if (direct !== undefined) return direct;
  const first = s.search(/[{[]/);
  if (first >= 0) {
    const end = balancedEnd(s, first);
    if (end > 0) {
      const v = tryParse(s.slice(first, end + 1));
      if (v !== undefined) return v;
    }
  }
  return null;
}

/** Recover every complete JSON object that has `requiredKey`, even from a truncated document. */
export function salvageObjects(raw: string, requiredKey: string): Record<string, unknown>[] {
  const s = stripThinking(raw);
  const out: Record<string, unknown>[] = [];
  let i = 0;
  while (i < s.length) {
    if (s[i] === '{') {
      const end = balancedEnd(s, i);
      if (end > 0) {
        const v = tryParse(s.slice(i, end + 1));
        if (v && typeof v === 'object' && !Array.isArray(v) && requiredKey in (v as object)) {
          out.push(v as Record<string, unknown>);
          i = end + 1;
          continue;
        }
      }
    }
    i++;
  }
  return out;
}
