import { describe, expect, it } from 'vitest';
import type { CitationMeta } from '../shared/types';
import { extractDoi, formatCitation, metaFromHtml, parseAuthor } from '../server/citation';
import { checkPlagiarism } from '../server/plagiarism';
import { heuristicSignals } from '../server/detect';

const today = new Date(2026, 9, 24); // 24 October 2026

const web: CitationMeta = {
  type: 'webpage',
  title: 'How to write well',
  authors: [{ given: 'Jane', family: 'Smith' }],
  siteName: 'Example Blog',
  published: '2024-03-05',
  url: 'https://example.com/post',
};

const article: CitationMeta = {
  type: 'article',
  title: 'Deep learning',
  authors: [{ given: 'Yann', family: 'LeCun' }, { given: 'Yoshua', family: 'Bengio' }, { given: 'Geoffrey', family: 'Hinton' }],
  containerTitle: 'Nature',
  volume: '521',
  issue: '7553',
  pages: '436–444',
  published: '2015-05-28',
  doi: '10.1038/nature14539',
};

describe('citations', () => {
  it('parses author names', () => {
    expect(parseAuthor('Jane Smith')).toEqual({ given: 'Jane', family: 'Smith' });
    expect(parseAuthor('Smith, Jane')).toEqual({ family: 'Smith', given: 'Jane' });
    expect(parseAuthor('By John A. Doe')).toEqual({ given: 'John A.', family: 'Doe' });
    expect(parseAuthor('BBC News')).toEqual({ literal: 'BBC News' });
  });

  it('extracts DOIs', () => {
    expect(extractDoi('https://doi.org/10.1038/nature12373.')).toBe('10.1038/nature12373');
    expect(extractDoi('no doi here')).toBeNull();
  });

  it('formats a web page in APA 7', () => {
    const c = formatCitation(web, 'apa', today);
    expect(c.text).toBe('Smith, J. (2024, March 5). How to write well. Example Blog. https://example.com/post');
    expect(c.html).toContain('<i>How to write well</i>');
  });

  it('formats a web page in Harvard with an access date', () => {
    expect(formatCitation(web, 'harvard', today).text).toBe('Smith, J. (2024) How to write well. Available at: https://example.com/post (Accessed: 24 October 2026).');
  });

  it('formats a web page in MLA 9', () => {
    expect(formatCitation(web, 'mla', today).text).toBe('Smith, Jane. “How to write well.” Example Blog, 5 Mar. 2024, example.com/post.');
  });

  it('formats a journal article in APA 7 with a DOI link', () => {
    const c = formatCitation(article, 'apa', today);
    expect(c.text).toBe('LeCun, Y., Bengio, Y., & Hinton, G. (2015). Deep learning. Nature, 521(7553), 436–444. https://doi.org/10.1038/nature14539');
    expect(c.html).toContain('<i>Nature</i>');
    expect(c.html).toContain('<i>521</i>');
  });

  it('normalises hyphenated page ranges to an en dash', () => {
    expect(formatCitation({ ...article, pages: '436-444' }, 'chicago', today).text).toContain(': 436–444.');
  });

  it('formats Chicago without throwing for every type', () => {
    for (const type of ['webpage', 'article', 'book', 'report'] as const) {
      expect(formatCitation({ ...article, type }, 'chicago', today).text.length).toBeGreaterThan(10);
    }
  });

  it('escapes HTML in citation fields', () => {
    const c = formatCitation({ ...web, title: '<script>alert(1)</script>' }, 'apa', today);
    expect(c.html).not.toContain('<script>');
    expect(c.html).toContain('&lt;script&gt;');
  });

  it('reads metadata from page HTML and strips the site name from the title', () => {
    const html = `<html><head><title>Ignored</title>
      <meta property="og:title" content="Great Article - The Daily">
      <meta property="og:site_name" content="The Daily">
      <meta name="author" content="Jane Smith">
      <meta property="article:published_time" content="2024-02-01T10:00:00Z"></head><body></body></html>`;
    const m = metaFromHtml(html, 'https://www.thedaily.example/a');
    expect(m).toMatchObject({ type: 'webpage', title: 'Great Article', siteName: 'The Daily', authors: [{ given: 'Jane', family: 'Smith' }], published: '2024-02-01T10:00:00Z' });
  });
});

describe('plagiarism (local sources only, no network)', () => {
  const source = 'The industrial revolution transformed manufacturing across Britain, as steam power replaced hand tools and new factories drew workers from the countryside into rapidly growing towns.';
  it('finds copied passages and reports similarity', async () => {
    const text = `My essay begins here. ${source} That is my conclusion about the period.`;
    const r = await checkPlagiarism({ text, includeDocs: false, web: false, sources: [{ title: 'Textbook', text: source }] });
    expect(r.overall).toBeGreaterThan(40);
    expect(r.sources[0].title).toBe('Textbook');
    expect(r.sources[0].passages[0].text).toContain('steam power replaced hand tools');
    expect(r.webSearch).toBe('skipped');
  });
  it('reports 0% for original writing', async () => {
    const r = await checkPlagiarism({ text: 'Completely different words about cooking pasta with fresh basil, garlic and a little olive oil tonight at home.', includeDocs: false, web: false, sources: [{ title: 'Textbook', text: source }] });
    expect(r.overall).toBe(0);
    expect(r.sources).toEqual([]);
  });
});

describe('AI-detector heuristics', () => {
  it('scores stock AI phrasing higher than plain human writing', () => {
    const ai = 'In today’s fast-paced world, it is important to note that technology plays a pivotal role. Moreover, it fosters a rich tapestry of innovation. Furthermore, we must delve into the multifaceted landscape of digital transformation. In conclusion, embracing this journey is crucial for unlocking seamless growth and navigating the ever-evolving realm of possibilities.';
    const human = 'I missed the bus again this morning, so I walked. It took forty minutes and it rained the whole way. My shoes are still wet. Tomorrow I am leaving ten minutes earlier, no excuses, because my boss already gave me that look twice this week.';
    expect(heuristicSignals(ai).score).toBeGreaterThan(heuristicSignals(human).score);
    expect(heuristicSignals(ai).reasons.length).toBeGreaterThan(0);
  });
  it('treats spelling and grammar slips as human signals', () => {
    const clean = 'The report was written by the team in a hurry. We should have checked it more carefully, regardless of the deadline. Send me the files soon because the client is angry and we have no time to waste on this.';
    const slips = 'The report was written by the team in a hurry. We should of checked it more carefully, irregardless of the deadline. Send me the files soon because the client is really really angry and we has no time to waste on this.';
    const s = heuristicSignals(slips);
    expect(s.score).toBeLessThan(heuristicSignals(clean).score);
    expect(s.reasons.some((r) => r.startsWith('Spelling or grammar slips'))).toBe(true);
    expect(s.slips).toBeGreaterThanOrEqual(3);
    expect(heuristicSignals(clean).slips).toBe(0);
    expect(heuristicSignals('We should of gone.').slips).toBe(1); // counted even when too short to score
  });
});
