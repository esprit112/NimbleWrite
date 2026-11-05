import { describe, expect, it } from 'vitest';
import { writeMessages } from '../server/prompts';
import type { Goals } from '../shared/types';

const goals: Goals = { audience: 'general', formality: 'neutral', domain: 'general', intent: [] };

const userTurn = (sourceLanguage?: string) =>
  writeMessages({ task: 'translate', text: 'Bonjour à tous.', targetLanguage: 'English (British)', sourceLanguage }, 'en-GB', goals).at(-1)!.content;

describe('translate prompt', () => {
  it('names the source language when the caller chose one (the web editor always does)', () => {
    expect(userTurn('French')).toBe('Translate the following text from French to English (British):\nBonjour à tous.');
  });

  it('lets the model detect the language when no source is given (the desktop panel default)', () => {
    const p = userTurn(undefined);
    expect(p).not.toContain('from English');
    expect(p).toBe('Translate the following text into English (British). Detect its language yourself; if it is already in English (British), return it unchanged:\nBonjour à tous.');
    expect(userTurn('  ')).toBe(p);
  });
});
