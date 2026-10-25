// Labels and lookup tables shared by server and client.
import type { Audience, Category, Domain, Formality, Intent, ParaphraseMode } from './types';

export const CATEGORY_META: Record<Category, { label: string; color: string; description: string }> = {
  correctness: { label: 'Correctness', color: '#e5484d', description: 'Spelling, grammar and punctuation' },
  clarity: { label: 'Clarity', color: '#3e63dd', description: 'Concise, easy-to-follow sentences' },
  engagement: { label: 'Engagement', color: '#18a058', description: 'Vivid, varied word choice' },
  delivery: { label: 'Delivery', color: '#8e4ec6', description: 'Tone, formality and confidence' },
};

const TYPE_INFO: Record<string, { category: Category; title: string }> = {
  spelling: { category: 'correctness', title: 'Correct your spelling' },
  grammar: { category: 'correctness', title: 'Fix the grammar' },
  agreement: { category: 'correctness', title: 'Fix the agreement' },
  verb_form: { category: 'correctness', title: 'Change the verb form' },
  word_confusion: { category: 'correctness', title: 'Use the right word' },
  article: { category: 'correctness', title: 'Fix the article' },
  preposition: { category: 'correctness', title: 'Change the preposition' },
  pronoun: { category: 'correctness', title: 'Use the right pronoun' },
  punctuation: { category: 'correctness', title: 'Fix the punctuation' },
  comma: { category: 'correctness', title: 'Fix the comma' },
  capitalization: { category: 'correctness', title: 'Fix the capital letter' },
  repeated_word: { category: 'correctness', title: 'Remove the repeated word' },
  missing_word: { category: 'correctness', title: 'Add the missing word' },
  spacing: { category: 'correctness', title: 'Fix the spacing' },
  dialect: { category: 'correctness', title: 'Match your English variant' },
  wordiness: { category: 'clarity', title: 'Remove wordiness' },
  redundancy: { category: 'clarity', title: 'Remove the redundancy' },
  passive_voice: { category: 'clarity', title: 'Use the active voice' },
  hard_to_read: { category: 'clarity', title: 'Make this easier to read' },
  run_on: { category: 'clarity', title: 'Split the long sentence' },
  word_choice: { category: 'engagement', title: 'Choose a more precise word' },
  repetition: { category: 'engagement', title: 'Avoid repetition' },
  bland_word: { category: 'engagement', title: 'Replace the bland word' },
  weak_intensifier: { category: 'engagement', title: 'Drop the weak intensifier' },
  tone: { category: 'delivery', title: 'Adjust the tone' },
  formality: { category: 'delivery', title: 'Adjust the formality' },
  politeness: { category: 'delivery', title: 'Sound more polite' },
  confidence: { category: 'delivery', title: 'Sound more confident' },
  inclusive_language: { category: 'delivery', title: 'Use inclusive language' },
  style_guide: { category: 'delivery', title: 'Follow your style guide' },
};

export const ISSUE_TYPES = Object.keys(TYPE_INFO);

const FALLBACK_TITLE: Record<Category, string> = {
  correctness: 'Correct this',
  clarity: 'Improve clarity',
  engagement: 'Make it more engaging',
  delivery: 'Adjust the delivery',
};

export function normaliseType(type: unknown): string {
  return String(type ?? '').toLowerCase().trim().replace(/[\s-]+/g, '_');
}

export function categoryForType(type: string, fallback?: unknown): Category {
  const info = TYPE_INFO[normaliseType(type)];
  if (info) return info.category;
  const c = String(fallback ?? '').toLowerCase();
  if (c === 'correctness' || c === 'clarity' || c === 'engagement' || c === 'delivery') return c;
  return 'correctness';
}

export function titleForType(type: string, category: Category): string {
  return TYPE_INFO[normaliseType(type)]?.title ?? FALLBACK_TITLE[category];
}

export const TONES: Record<string, string> = {
  Confident: '😎', Friendly: '🤗', Formal: '🤝', Informal: '😊', Optimistic: '🙂', Joyful: '😄', Appreciative: '🙏',
  Curious: '🤔', Encouraging: '💪', Empathetic: '🫶', Respectful: '🙇', Neutral: '😐', Concerned: '😟', Worried: '😰',
  Disapproving: '🙁', Accusatory: '👉', Egocentric: '🧍', Assertive: '✊', Direct: '🎯', Unassuming: '🌱', Sad: '😢',
  Surprised: '😮', Excited: '🤩', Cautionary: '⚠️', Forceful: '😤', Defensive: '🛡️', Regretful: '😔', Apologetic: '🙇‍♂️',
  Urgent: '⏰', Serious: '🧐', Thoughtful: '💭', Witty: '😏', Sarcastic: '🙃', Frustrated: '😣', Angry: '😠',
  Confused: '😕', Impersonal: '🤖', Professional: '💼', Persuasive: '🗣️', Playful: '😜', Loving: '🥰', Nostalgic: '🕰️',
};

export const GOAL_OPTIONS: {
  audience: { id: Audience; label: string; hint: string }[];
  formality: { id: Formality; label: string; hint: string }[];
  domain: { id: Domain; label: string; hint: string }[];
  intent: { id: Intent; label: string; hint: string }[];
} = {
  audience: [
    { id: 'general', label: 'General', hint: 'Easy for anyone to read with minimal effort.' },
    { id: 'knowledgeable', label: 'Knowledgeable', hint: 'Requires focus to read and understand.' },
    { id: 'expert', label: 'Expert', hint: 'May require rereading to understand.' },
  ],
  formality: [
    { id: 'informal', label: 'Informal', hint: 'Relaxed: contractions, slang and emoji are fine.' },
    { id: 'neutral', label: 'Neutral', hint: 'Restricts slang but allows standard casual expressions.' },
    { id: 'formal', label: 'Formal', hint: 'Restricts slang, colloquialisms and casual expressions.' },
  ],
  domain: [
    { id: 'academic', label: 'Academic', hint: 'Strictly applies all rules and formal writing conventions.' },
    { id: 'business', label: 'Business', hint: 'Applies almost all rules, with some leeway for informality.' },
    { id: 'general', label: 'General', hint: 'Applies most rules and conventions with medium strictness.' },
    { id: 'email', label: 'Email', hint: 'Applies most rules, adjusted for email conventions.' },
    { id: 'casual', label: 'Casual', hint: 'Applies most rules but allows stylistic flexibility.' },
    { id: 'creative', label: 'Creative', hint: 'Allows some intentional bending of rules for expressive writing.' },
  ],
  intent: [
    { id: 'inform', label: 'Inform', hint: 'Explain facts or ideas.' },
    { id: 'describe', label: 'Describe', hint: 'Paint a picture with detail.' },
    { id: 'convince', label: 'Convince', hint: 'Persuade the reader.' },
    { id: 'tell_story', label: 'Tell a story', hint: 'Narrate events.' },
  ],
};

export const PARAPHRASE_MODES: { id: ParaphraseMode; label: string; hint: string }[] = [
  { id: 'standard', label: 'Standard', hint: 'Reword while keeping the meaning' },
  { id: 'fluent', label: 'Fluent', hint: 'Smooth and natural' },
  { id: 'formal', label: 'Formal', hint: 'Polished and professional' },
  { id: 'academic', label: 'Academic', hint: 'Scholarly and precise' },
  { id: 'simple', label: 'Simple', hint: 'Plain and easy to read' },
  { id: 'creative', label: 'Creative', hint: 'Fresh, vivid phrasing' },
  { id: 'expand', label: 'Expand', hint: 'Add depth and detail' },
  { id: 'shorten', label: 'Shorten', hint: 'Keep only what matters' },
  { id: 'custom', label: 'Custom voice', hint: 'Describe the voice you want' },
];

export const LANGUAGES = [
  'Arabic', 'Chinese (Simplified)', 'Chinese (Traditional)', 'Czech', 'Danish', 'Dutch', 'English (British)', 'English (American)',
  'Finnish', 'French', 'German', 'Greek', 'Hindi', 'Hungarian', 'Indonesian', 'Italian', 'Japanese', 'Korean', 'Norwegian',
  'Polish', 'Portuguese (Brazil)', 'Portuguese (Portugal)', 'Romanian', 'Russian', 'Spanish', 'Swedish', 'Thai', 'Turkish',
  'Ukrainian', 'Vietnamese', 'Welsh',
];
