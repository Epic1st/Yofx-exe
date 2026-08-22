import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';

afterEach(() => {
  cleanup();
  document.body.innerHTML = '';
  window.history.replaceState({}, '', '/');
  delete window.__YO4X_AUTH__;
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});
