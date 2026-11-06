import { describe, expect, it } from 'vitest';
import { parseRoute } from '../src/router';

describe('parseRoute', () => {
  it('reads the documents, bin and document routes as before', () => {
    expect(parseRoute('')).toEqual({ name: 'home', trash: false });
    expect(parseRoute('#/trash')).toEqual({ name: 'home', trash: true });
    expect(parseRoute('#/doc/abc-123')).toEqual({ name: 'doc', id: 'abc-123' });
  });

  it('opens settings on a section when the link names one (the desktop app links straight to it)', () => {
    expect(parseRoute('#/settings')).toEqual({ name: 'settings' });
    expect(parseRoute('#/settings/models')).toEqual({ name: 'settings', section: 'models' });
    expect(parseRoute('#/settings/appearance')).toEqual({ name: 'settings', section: 'appearance' });
  });

  it('ignores anything after the section that is not a plain section name', () => {
    expect(parseRoute('#/settings/<script>')).toEqual({ name: 'settings' });
    expect(parseRoute('#/settings/models/extra')).toEqual({ name: 'settings', section: 'models' });
  });
});
