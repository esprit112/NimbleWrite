import { describe, expect, it } from 'vitest';
import { IMAGE_PROMPT_TARGETS, type ImagePromptTarget } from '../shared/types';
import { cleanImagePrompt, imagePromptParams, imagePromptSystemPrompt } from '../server/prompts';

describe('image prompt formatter targets', () => {
  it('lists the three Klein/Turbo variants alongside the generic FLUX targets', () => {
    expect([...IMAGE_PROMPT_TARGETS]).toEqual(['flux2-klein', 'flux3-klein', 'zimage-turbo', 'flux2', 'flux3']);
  });

  it('gives every target its own guide, with the shared output contract', () => {
    const prompts = IMAGE_PROMPT_TARGETS.map((t) => imagePromptSystemPrompt(t, true));
    expect(new Set(prompts).size).toBe(IMAGE_PROMPT_TARGETS.length);
    for (const p of prompts) expect(p).toContain('Output ONLY the finished prompt text');
  });

  it('FLUX.2 Klein: prose, subject first, lighting, no negatives or weights', () => {
    const p = imagePromptSystemPrompt('flux2-klein', true);
    expect(p).toContain('FLUX.2 [klein]');
    expect(p).toMatch(/most important element/);
    expect(p).toMatch(/Do not invent hex codes/);
    expect(p).toMatch(/Never write a negative-prompt list/);
  });

  it('FLUX.3 Klein: acts as the upsampler with the caption grammar', () => {
    const p = imagePromptSystemPrompt('flux3-klein', true);
    expect(p).toContain('acting as that upsampler');
    expect(p).toMatch(/medium and style/);
    expect(p).toMatch(/BEFORE any subject detail/);
    expect(p).toMatch(/frame-relative positions/);
    expect(p).toMatch(/Full-color outdoor photograph/);
    expect(p).toMatch(/present facts/);
    expect(p).toContain('line breaks as \\n');
  });

  it('Z-Image Turbo: positive-only, quoted text, no meta tags', () => {
    const p = imagePromptSystemPrompt('zimage-turbo', true);
    expect(p).toContain('no negative prompt');
    expect(p).toMatch(/verbatim inside double quotes/);
    expect(p).toMatch(/Never use quality meta-tags/);
    expect(p).toMatch(/never more than 250/);
  });

  it('keeps the enhance / restructure toggle for every target', () => {
    for (const t of IMAGE_PROMPT_TARGETS) {
      expect(imagePromptSystemPrompt(t, true)).toContain('invent concrete, well-judged specifics');
      const strict = imagePromptSystemPrompt(t, false);
      expect(strict).toContain('Restructure and rephrase ONLY');
      expect(strict).toContain('leave it out rather than inventing it');
      expect(strict).toContain('you are only reformatting');
      expect(imagePromptSystemPrompt(t, true)).not.toContain('you are only reformatting');
      expect(strict).not.toContain('invent concrete, well-judged specifics');
    }
  });

  it('uses a low temperature for restructure and gives FLUX.3 captions the most room', () => {
    const targets: ImagePromptTarget[] = [...IMAGE_PROMPT_TARGETS];
    for (const t of targets) {
      expect(imagePromptParams(t, false).temperature).toBeLessThan(imagePromptParams(t, true).temperature);
    }
    expect(imagePromptParams('flux3-klein', true).maxTokens).toBeGreaterThan(imagePromptParams('zimage-turbo', true).maxTokens);
  });
});

describe('cleanImagePrompt', () => {
  it('strips wrapping quotes and code fences', () => {
    expect(cleanImagePrompt('"A red fox asleep."')).toBe('A red fox asleep.');
    expect(cleanImagePrompt("'A red fox asleep.'")).toBe('A red fox asleep.');
    expect(cleanImagePrompt('```\nA red fox asleep.\n```')).toBe('A red fox asleep.');
  });

  it('keeps the closing quote of embedded image text', () => {
    const p = 'A neon sign above the door reads "OPEN"';
    expect(cleanImagePrompt(p)).toBe(p);
    const q = 'A banner reads "ASD" in gold, and a poster reads "AUDIO"';
    expect(cleanImagePrompt(q)).toBe(q);
  });

  it('does not unwrap a reply that starts and ends with separate quoted strings', () => {
    const p = '"OPEN" above the door and "CLOSED" below';
    expect(cleanImagePrompt(p)).toBe(p);
  });
});
