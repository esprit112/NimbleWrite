// Prompt builders for every AI feature. Kept in one place so behaviour is easy to tune.
import type { CheckOptions, Dialect, Goals, ParaphraseMode, WriteRequest } from '../shared/types';
import { ISSUE_TYPES, TONES } from '../shared/meta';

export const DIALECT_NAME: Record<Dialect, string> = {
  'en-GB': 'British English', 'en-US': 'American English', 'en-CA': 'Canadian English', 'en-AU': 'Australian English', 'en-IN': 'Indian English',
};

const DIALECT_NOTE: Record<Dialect, string> = {
  'en-GB': 'British spellings (colour, organise, travelled, centre) are correct. Flag American spellings.',
  'en-US': 'American spellings (color, organize, traveled, center) are correct. Flag British spellings.',
  'en-CA': 'Canadian spelling: -our and -re endings (colour, centre) with -ize verbs (organize) and doubled L (travelled).',
  'en-AU': 'Australian spelling follows British conventions (colour, organise, travelled).',
  'en-IN': 'Indian English follows British spelling conventions (colour, organise, travelled).',
};

function goalGuidance(g: Goals): string {
  const lines: string[] = [];
  lines.push({
    general: 'Audience: general readers. Flag jargon, long sentences and complex words that would slow them down.',
    knowledgeable: 'Audience: knowledgeable readers. Moderate complexity is fine; still flag genuinely hard-to-follow sentences.',
    expert: 'Audience: experts. Technical vocabulary and dense sentences are acceptable; do not simplify terminology.',
  }[g.audience]);
  lines.push({
    informal: 'Formality: informal. Contractions, casual phrasing and emoji are fine; do not push formal wording.',
    neutral: 'Formality: neutral. Flag slang, but everyday expressions and contractions are fine.',
    formal: 'Formality: formal. Flag contractions, slang, colloquialisms and casual expressions.',
  }[g.formality]);
  lines.push({
    academic: 'Domain: academic. Apply rules strictly; flag vague words, casual voice and unsupported overstatement.',
    business: 'Domain: business. Apply most rules; prefer clear, direct, professional phrasing.',
    general: 'Domain: general. Apply rules with medium strictness.',
    email: 'Domain: email. Allow normal greetings and sign-offs; keep it clear and courteous.',
    casual: 'Domain: casual. Be lenient on style; focus on real errors and clarity.',
    creative: 'Domain: creative. Allow intentional rule-bending (fragments, stylistic repetition); flag only clear mistakes and weak phrasing.',
  }[g.domain]);
  if (g.intent.length) {
    const map = {
      inform: 'inform (favour precision and clarity)',
      describe: 'describe (favour vivid, specific words)',
      convince: 'convince (favour confident, persuasive wording; flag hedging)',
      tell_story: 'tell a story (allow narrative voice and rhythm)',
    } as const;
    lines.push(`Intent: ${g.intent.map((i) => map[i]).join('; ')}.`);
  }
  return lines.join('\n');
}

/** The writer's own standing preferences as one prompt line, or '' when there are none. */
export function preferencesLine(instructions: string | undefined, use: 'check' | 'write'): string {
  const text = (instructions ?? '').replace(/[ \t]+/g, ' ').replace(/\s*\n\s*/g, '\n').trim().slice(0, 1500);
  if (!text) return '';
  return use === 'check'
    ? `\nIMPORTANT, the writer's own rules. They override everything above except the JSON format. If a rule says not to flag something, report nothing of that kind; if it says to flag something, report it:\n${text}`
    : `The writer's standing preferences (follow them unless this request says otherwise): ${text}`;
}

export function checkSystemPrompt(o: CheckOptions): string {
  const dictionary = o.dictionary.filter(Boolean).slice(0, 300);
  return `You are the proofreading engine of a writing assistant.
Review each paragraph and report problems as JSON. Respond with ONLY a JSON object, no markdown:
{"issues":[{"p":"<paragraph id>","original":"<exact text copied character-for-character from that paragraph>","before":"<up to 3 words immediately before original, verbatim, or empty>","replacement":"<corrected text that replaces original>","category":"correctness|clarity|engagement|delivery","type":"<one code from the list>","explanation":"<what is wrong, max 16 words>"}]}

Rules:
- One issue per distinct mistake. Keep "original" as SHORT as possible: usually one word. Only span several words when the fix needs it (e.g. "me and him was" -> "he and I were").
- Never merge separate mistakes: "libary tomorow" is two issues.
- "replacement" must differ from "original". Keep the original capitalisation style. Use "" only to delete a redundant word.
- "explanation" says plainly what is wrong (e.g. "This word is misspelled.", "Use the plural verb with a plural subject."). Mention the English variant only when the issue is a British/American spelling difference.
- Type codes: ${ISSUE_TYPES.filter((t) => !['spacing', 'dialect', 'style_guide'].includes(t)).join(', ')}.
- Only report these categories: ${o.categories.join(', ')}. Correctness = spelling, grammar, punctuation, wrong word. Clarity = wordiness, redundancy, passive voice, hard-to-read sentences. Engagement = bland, repetitive or weak word choice. Delivery = tone, formality, politeness, confidence, inclusive language.
- English variant: ${DIALECT_NAME[o.dialect]}. ${DIALECT_NOTE[o.dialect]}
${goalGuidance(o.goals)}
${dictionary.length ? `- Never flag these words (personal dictionary): ${dictionary.join(', ')}.` : ''}
- Do not flag names, URLs, code, or style that is already fine. If a paragraph has no problems, report nothing for it. No issues at all -> {"issues":[]}${preferencesLine(o.instructions, 'check')}`;
}

export function toneSystemPrompt(): string {
  return `You are a tone detector. Decide how the text will sound to a reader.
Pick 1 to 3 tones, strongest first, ONLY from this list: ${Object.keys(TONES).join(', ')}.
Respond with ONLY JSON:
{"tones":[{"name":"<tone from the list>","score":<intensity 1-100>}],"summary":"<one sentence on how the text comes across>","formality":<0-100 where 0 is very casual and 100 is very formal>,"sentiment":"positive|neutral|negative"}`;
}

export function synonymsSystemPrompt(dialect: Dialect): string {
  return `You are the thesaurus inside a writing app. Given a word and the sentence it is in, suggest replacements that fit that sentence: same part of speech, tense, number and register, and the same meaning in context.
Group them by sense. Use ${DIALECT_NAME[dialect]} spelling.
Respond with ONLY JSON: {"senses":[{"meaning":"<2-6 word gloss>","synonyms":["<word or short phrase>"]}]}
At most 3 senses and 6 synonyms per sense, best fit first. Never repeat the original word.`;
}

const TASKS: Record<string, string> = {
  improve: 'Improve this text: fix any errors, sharpen clarity and flow, keep roughly the same length and keep the author\'s voice.',
  shorten: 'Make this text more concise. Cut filler and redundancy but keep every key point. Aim for about 60% of the original length.',
  expand: 'Make this text more detailed (about 1.5 to 2 times the length) with useful specifics and explanation. Do not invent statistics, quotes or facts.',
  simplify: 'Rewrite this text in plain, simple language that anyone can follow: short sentences and common words.',
  professional: 'Rewrite this text to sound more professional and polished for a workplace audience.',
  friendly: 'Rewrite this text to sound warmer and friendlier while staying clear and appropriate.',
  confident: 'Rewrite this text to sound more confident and direct: remove hedging (I think, maybe, just, sort of, a bit) and prefer the active voice.',
  persuasive: 'Rewrite this text to be more persuasive: lead with what matters to the reader, use strong verbs, and end with a clear call to action where it fits.',
  fix_grammar: 'Fix only spelling, grammar and punctuation mistakes. Do not change wording, tone or style beyond what the fix needs.',
  summarize: 'Summarise this text. Use 2 to 4 sentences, or 3 to 5 bullet points if it is long. Keep the key facts.',
  continue: 'Continue writing from where this text ends. Match its voice, tone and formatting. Write one or two new paragraphs. Output only the new text, not the original.',
  reply: 'Write a reply to this message. Address each point it raises, be helpful, and use a tone that suits it. Output only the reply.',
  outline: 'Create a clear outline for a piece of writing on this topic or draft, using Markdown headings and bullet points.',
  brainstorm: 'Brainstorm 8 to 10 fresh, specific ideas related to this text, as a Markdown bullet list with one short line each.',
  humanize: `Rewrite this text so it reads as if a thoughtful person wrote it. Remove AI writing patterns: inflated significance words (pivotal, testament, landscape, tapestry, delve, underscore, showcase, vibrant, crucial, seamless, foster), tacked-on -ing phrases (highlighting..., ensuring...), forced lists of three, "not only... but also", em-dash overuse, signposting ("Let's dive in"), filler ("It is important to note that"), generic upbeat conclusions and chatbot phrases. Vary sentence length and rhythm, prefer plain verbs (is, has, does), keep every specific fact and the author's meaning. Do not add new facts.`,
};

const PARAPHRASE: Record<ParaphraseMode, string> = {
  standard: 'natural, everyday rewording',
  fluent: 'smooth, natural and idiomatic phrasing that flows well',
  formal: 'a formal register with no contractions or slang',
  academic: 'an academic register: precise, objective and appropriately hedged',
  simple: 'plain words and short sentences that are easy to read',
  creative: 'fresh, vivid and expressive language',
  expand: 'more detail and nuance, about 1.5 times the length',
  shorten: 'a concise version, about 60% of the length',
  custom: 'the voice described below',
};

export function writeMessages(r: WriteRequest, dialect: Dialect, goals: Goals, instructions = ''): { role: 'system' | 'user' | 'assistant'; content: string }[] {
  const base = `You are NimbleWrite's AI writing assistant, an expert editor and writer.
Use ${DIALECT_NAME[dialect]} spelling and conventions.
Writing goals: ${goals.audience} audience, ${goals.formality} formality, ${goals.domain} domain${goals.intent.length ? `, intent: ${goals.intent.join(', ').replace('tell_story', 'tell a story')}` : ''}.
Output ONLY the requested text: no preamble, no explanation, no quotation marks around it, and never several alternative versions.
Preserve the author's meaning, facts, names and numbers. Use Markdown only for lists or headings when they genuinely help.${instructions.trim() ? `\n${preferencesLine(instructions, 'write')}` : ''}`;

  const textBlock = r.text.trim() ? `Text:\n"""\n${r.text}\n"""` : '';
  const ctx = r.context?.trim() && r.context.trim() !== r.text.trim()
    ? `\n\nThe surrounding document, for context only (do not rewrite it):\n"""\n${r.context.slice(0, 12_000)}\n"""` : '';

  if (r.task === 'chat') {
    const sys = `${base.replace('Output ONLY the requested text: no preamble, no explanation, no quotation marks around it, and never several alternative versions.', 'Answer helpfully and concisely. When you write text for the document, write it ready to paste.')}
The user's current document is below for context.
"""
${(r.context || r.text).slice(0, 14_000)}
"""`;
    return [{ role: 'system', content: sys }, ...(r.history ?? []).slice(-8), { role: 'user', content: r.instruction || 'Help me improve this document.' }];
  }

  if (r.task === 'translate') {
    // Riva Translate ignores system prompts; the instruction must be in the user turn (works for general LLMs too).
    return [
      { role: 'system', content: 'You are a professional translator. Preserve meaning, tone, formatting and line breaks. Output only the translation.' },
      { role: 'user', content: `Translate the following text from ${r.sourceLanguage || 'English'} to ${r.targetLanguage || 'French'}:\n${r.text}` },
    ];
  }

  let instruction: string;
  if (r.task === 'paraphrase') {
    const mode = r.mode ?? 'standard';
    instruction = `Paraphrase this text using ${PARAPHRASE[mode]}. Keep the meaning and every fact, but change the wording and sentence structure.${mode === 'custom' && r.instruction ? `\nVoice: ${r.instruction}` : ''}`;
  } else if (r.task === 'custom') {
    instruction = r.text.trim()
      ? `Apply this instruction to the text: ${r.instruction || 'Improve it.'}`
      : `${r.instruction || 'Write something helpful.'}`;
  } else if (r.task === 'compose') {
    instruction = `Write the following: ${r.instruction || 'a short paragraph'}`;
  } else {
    instruction = TASKS[r.task] ?? TASKS.improve;
  }
  return [
    { role: 'system', content: base },
    { role: 'user', content: `${instruction}${textBlock ? `\n\n${textBlock}` : ''}${ctx}` },
  ];
}

export function readerReactionsPrompt(reader: string): string {
  return `You simulate how a specific reader will react to a piece of writing. Reader: ${reader || 'a busy, intelligent general reader'}.
Be candid and specific, and quote the text where it helps.
Respond with ONLY JSON:
{"overall":"<2 sentences: this reader's likely overall reaction>","takeaways":["<what they will remember>"],"questions":["<questions they will still have>"],"confusions":[{"quote":"<short exact quote>","issue":"<why it may confuse them or land badly>","suggestion":"<how to fix it>"}],"suggestions":["<revision that would land better with this reader>"]}
Use at most 4 items in each list.`;
}

export function graderPrompt(level: string): string {
  return `You are an experienced, fair grader. Grade the submission against the assignment and rubric. Level: ${level || 'undergraduate'}.
Be specific and constructive, and quote the submission where useful.
Respond with ONLY JSON:
{"grade":"<letter grade such as A-, B+>","score":<0-100>,"summary":"<2-3 sentences>","criteria":[{"name":"<criterion>","score":<0-100>,"feedback":"<specific feedback>"}],"strengths":["<strength>"],"improvements":["<specific, actionable revision>"]}
If no rubric is given, use: Thesis and argument; Evidence and support; Organisation; Style and clarity; Mechanics.`;
}

export function claimsPrompt(): string {
  return `You are a research assistant. Find claims in the text that need a citation: statistics, research findings, factual or historical claims, quotes, and specific or contested assertions. Ignore opinions and common knowledge.
Respond with ONLY JSON:
{"claims":[{"claim":"<exact sentence or clause from the text>","reason":"<why it needs support>","query":"<precise web search query that would find a credible source>","strength":"needs_citation|recommended"}]}
At most 8 claims, most important first. No claims -> {"claims":[]}`;
}

export function aiDetectPrompt(): string {
  return `You estimate whether passages read as AI-generated or human-written.
Signs of AI text: very even sentence length and rhythm; inflated or generic words (delve, pivotal, tapestry, testament, landscape, crucial, showcase, foster, seamless, vibrant); formulaic transitions (Moreover, Furthermore, Additionally, In conclusion); lists of three; "not only... but also"; neutral, hedged tone; tidy structure with no personal detail.
Signs of human text: specific personal detail and experience, quirks, uneven rhythm, asides, opinions.
Spelling mistakes, typos and grammar slips (e.g. "should of", "alot", "we has", "me and him was", doubled words) are STRONG evidence of a human writer: current AI models almost never make them. Plain, clumsy or repetitive wording is also typical of people, not AI. Never cite errors or simple vocabulary as a reason for AI.
For each passage, FIRST list up to 3 short reasons (at most 15 words each) naming the evidence you see, THEN give "ai": the probability from 0 to 100 that the passage was AI-generated, where 0 means certainly written by a person and 100 means certainly AI. The number must agree with your reasons: when the evidence points to a human writer, "ai" must be below 30.
Respond with ONLY JSON, keys in this order: {"passages":[{"id":"<id>","reasons":["<reason>"],"ai":<0-100>}]}`;
}
