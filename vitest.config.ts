import { defineConfig } from 'vitest/config';

// Tests are plain Node: no React/Tailwind plugins needed (the editor engine is tested against real ProseMirror state).
export default defineConfig({
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
    testTimeout: 15_000,
  },
});
