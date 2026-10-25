// Types shared by the NimbleWrite server and the browser client.

export type Category = 'correctness' | 'clarity' | 'engagement' | 'delivery';
export const CATEGORIES: Category[] = ['correctness', 'clarity', 'engagement', 'delivery'];

export type Dialect = 'en-GB' | 'en-US' | 'en-CA' | 'en-AU' | 'en-IN';
export const DIALECTS: { id: Dialect; label: string }[] = [
  { id: 'en-GB', label: 'British English' },
  { id: 'en-US', label: 'American English' },
  { id: 'en-CA', label: 'Canadian English' },
  { id: 'en-AU', label: 'Australian English' },
  { id: 'en-IN', label: 'Indian English' },
];

export type Audience = 'general' | 'knowledgeable' | 'expert';
export type Formality = 'informal' | 'neutral' | 'formal';
export type Domain = 'academic' | 'business' | 'general' | 'email' | 'casual' | 'creative';
export type Intent = 'inform' | 'describe' | 'convince' | 'tell_story';

export interface Goals {
  audience: Audience;
  formality: Formality;
  domain: Domain;
  intent: Intent[];
}

export const DEFAULT_GOALS: Goals = { audience: 'knowledgeable', formality: 'neutral', domain: 'general', intent: [] };

/** A suggestion relative to one paragraph's plain text. */
export interface Issue {
  offset: number;
  length: number;
  original: string;
  replacements: string[];
  category: Category;
  type: string;
  title: string;
  explanation: string;
  source: 'ai' | 'rule' | 'style';
}

export interface StyleRule {
  id: string;
  find: string;
  replace: string;
  note?: string;
  caseSensitive?: boolean;
}

export interface Snippet {
  id: string;
  trigger: string;
  text: string;
}

export interface ModelSettings {
  check: string;
  write: string;
  translate: string;
}

export interface Settings {
  dialect: Dialect;
  defaultGoals: Goals;
  showGoalsOnNewDoc: boolean;
  categories: Record<Category, boolean>;
  localRules: boolean;
  dictionary: string[];
  styleRules: StyleRule[];
  snippets: Snippet[];
  models: ModelSettings;
  theme: 'light' | 'dark' | 'system';
}

export interface DocSummary {
  id: string;
  title: string;
  snippet: string;
  wordCount: number;
  score: number | null;
  createdAt: number;
  updatedAt: number;
  deletedAt: number | null;
}

/** A full document. `snippet` is summary-only (derived from `text` when listing). */
export interface Doc extends Omit<DocSummary, 'snippet'> {
  html: string;
  text: string;
  goals: Goals;
  dismissed: string[];
}

export interface CheckParagraph {
  id: string;
  text: string;
}

export interface CheckOptions {
  dialect: Dialect;
  goals: Goals;
  dictionary: string[];
  categories: Category[];
  model?: string;
}

export interface CheckResult {
  id: string;
  issues: Issue[];
}

export interface CheckResponse {
  results: CheckResult[];
  model: string;
  ms: number;
}

/** `/api/ai/assist`: AI findings and local rules merged per paragraph, for thin clients such as the desktop companion. */
export interface AssistResponse {
  results: CheckResult[];
  /** Check model used, or null when the model was not called. */
  model: string | null;
  ms: number;
  /** True when the AI check ran (or was not needed); false when only local rules are included. */
  ai: boolean;
  aiError?: string;
  /** False when no NVIDIA key is set up: only the local rules can run, and that isn't an error. */
  aiConfigured: boolean;
  /** Changes whenever a setting that affects suggestions changes, so clients can drop cached results. */
  fingerprint: string;
}

export interface ToneScore {
  name: string;
  emoji: string;
  score: number;
}

export interface ToneResult {
  tones: ToneScore[];
  summary: string;
  formality: number;
  sentiment: 'positive' | 'neutral' | 'negative';
}

export interface SynonymSense {
  meaning: string;
  synonyms: string[];
}

export type WriteTask =
  | 'improve' | 'shorten' | 'expand' | 'simplify' | 'professional' | 'friendly' | 'confident' | 'persuasive'
  | 'fix_grammar' | 'summarize' | 'continue' | 'reply' | 'outline' | 'brainstorm' | 'custom' | 'compose'
  | 'paraphrase' | 'humanize' | 'translate' | 'chat';

export type ParaphraseMode = 'standard' | 'fluent' | 'formal' | 'academic' | 'simple' | 'creative' | 'expand' | 'shorten' | 'custom';

export interface WriteRequest {
  task: WriteTask;
  text: string;
  context?: string;
  instruction?: string;
  mode?: ParaphraseMode;
  targetLanguage?: string;
  sourceLanguage?: string;
  goals?: Goals;
  dialect?: Dialect;
  history?: { role: 'user' | 'assistant'; content: string }[];
}

export interface ReaderReactions {
  overall: string;
  takeaways: string[];
  questions: string[];
  confusions: { quote: string; issue: string; suggestion: string }[];
  suggestions: string[];
}

export interface GraderResult {
  grade: string;
  score: number;
  summary: string;
  criteria: { name: string; score: number; feedback: string }[];
  strengths: string[];
  improvements: string[];
}

export interface ClaimToCite {
  claim: string;
  reason: string;
  query: string;
  strength: 'needs_citation' | 'recommended';
}

export interface AiDetectPassage {
  text: string;
  score: number;
  reasons: string[];
}

export interface AiDetectResult {
  score: number;
  verdict: string;
  passages: AiDetectPassage[];
  signals: { label: string; value: string }[];
}

export interface PlagiarismPassage {
  offset: number;
  length: number;
  text: string;
}

export interface PlagiarismSource {
  title: string;
  url?: string;
  kind: 'doc' | 'pasted' | 'web';
  docId?: string;
  similarity: number;
  passages: PlagiarismPassage[];
}

export interface PlagiarismResult {
  overall: number;
  sources: PlagiarismSource[];
  webSearch: 'used' | 'not_configured' | 'failed' | 'skipped';
  checkedSources: number;
  note?: string;
}

export interface CitationAuthor {
  given?: string;
  family?: string;
  literal?: string;
}

export type CitationType = 'webpage' | 'article' | 'book' | 'report';

export interface CitationMeta {
  type: CitationType;
  title: string;
  authors: CitationAuthor[];
  siteName?: string;
  containerTitle?: string;
  publisher?: string;
  published?: string;
  accessed?: string;
  volume?: string;
  issue?: string;
  pages?: string;
  doi?: string;
  url?: string;
}

export type CitationStyle = 'apa' | 'mla' | 'chicago' | 'harvard';

export interface FormattedCitation {
  style: CitationStyle;
  text: string;
  html: string;
}

export interface HealthResponse {
  ok: boolean;
  nimConfigured: boolean;
  searchConfigured: boolean;
  models: ModelSettings;
  version: string;
  /** True for the installed Windows app (its server is started by the desktop companion). */
  packaged: boolean;
}

/** Result of testing an NVIDIA API key before the desktop app saves it. The key itself is never sent back. */
export interface KeyTestResult {
  ok: boolean;
  /**
   * invalid: not shaped like a key. rejected: NVIDIA refused it. model / rate: NVIDIA accepted it, with a caveat.
   * network / error: it couldn't be checked right now.
   */
  reason?: 'invalid' | 'rejected' | 'model' | 'rate' | 'network' | 'error';
  message?: string;
}

export interface DayStats {
  accepted: Record<Category, number>;
  checks: number;
  wordsChecked: number;
}

export interface InsightsResponse {
  days: { date: string; stats: DayStats }[];
  totals: { docs: number; words: number; accepted: number };
}
