import { describe, expect, it, vi } from 'vitest';
import { isStaleChunkError, recoverFromStaleChunk } from './chunk-load-recovery';

describe('chunk load recovery', () => {
  it('recognizes lazy-module failures reported by Chromium and Firefox', () => {
    expect(
      isStaleChunkError(new Error('Failed to fetch dynamically imported module: /chunk-old.js')),
    ).toBe(true);
    expect(isStaleChunkError(new Error('error loading dynamically imported module'))).toBe(true);
    expect(isStaleChunkError(new Error('API request failed'))).toBe(false);
  });

  it('reloads once and prevents a reload loop for the next minute', () => {
    const values = new Map<string, string>();
    const reload = vi.fn();
    const dependencies = {
      storage: {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => values.set(key, value),
      },
      reload,
      now: () => 120_000,
    };
    const error = { error: new TypeError('Failed to fetch dynamically imported module') };

    expect(recoverFromStaleChunk(error, dependencies)).toBe(true);
    expect(recoverFromStaleChunk(error, dependencies)).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
