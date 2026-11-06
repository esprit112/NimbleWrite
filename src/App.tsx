// App shell: loads settings + server health, applies the theme and routes between pages.
import { lazy, Suspense, useEffect, useState } from 'react';
import type { HealthResponse } from '../shared/types';
import { api } from './api';
import { useRoute } from './router';
import { useSettings } from './store';
import { Button, Logo, Spinner, Toaster } from './components/ui';

// Each page loads when it's first opened: "Open in NimbleWrite" goes straight to a document without first
// downloading the settings page, and the document list doesn't wait for the whole editor.
const HomePage = lazy(() => import('./pages/HomePage').then((m) => ({ default: m.HomePage })));
const SettingsPage = lazy(() => import('./pages/SettingsPage').then((m) => ({ default: m.SettingsPage })));
const EditorPage = lazy(() => import('./editor/EditorPage').then((m) => ({ default: m.EditorPage })));

const pageLoading = (
  <div className="flex h-full items-center justify-center">
    <Spinner className="size-6" />
  </div>
);

function useTheme(theme: 'light' | 'dark' | 'system' | undefined) {
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const apply = () => {
      const dark = theme === 'dark' || ((theme ?? 'system') === 'system' && mq.matches);
      document.documentElement.classList.toggle('dark', dark);
    };
    apply();
    mq.addEventListener('change', apply);
    return () => mq.removeEventListener('change', apply);
  }, [theme]);
}

export default function App() {
  const route = useRoute();
  const { settings, error, load } = useSettings();
  const [health, setHealth] = useState<HealthResponse | null>(null);
  useTheme(settings?.theme);

  useEffect(() => {
    void load();
    api.health().then(setHealth).catch(() => setHealth(null));
  }, [load]);

  if (!settings) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-4 p-6 text-center">
        <Logo />
        {error ? (
          <>
            <p className="max-w-md text-sm text-slate-600 dark:text-slate-400">{error}</p>
            <p className="max-w-md text-xs text-slate-400">Make sure NimbleWrite is running (look for its icon in the notification area), then retry. Developing NimbleWrite? Start the API with <code>npm run dev</code>.</p>
            <Button variant="primary" onClick={() => void load()}>Retry</Button>
          </>
        ) : (
          <Spinner className="size-6" />
        )}
        <Toaster />
      </div>
    );
  }

  return (
    <>
      <Suspense fallback={pageLoading}>
        {route.name === 'doc' ? <EditorPage id={route.id} health={health} /> : route.name === 'settings' ? <SettingsPage /> : <HomePage trash={route.trash} health={health} />}
      </Suspense>
      <Toaster />
    </>
  );
}
