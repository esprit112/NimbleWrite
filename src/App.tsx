// App shell: loads settings + server health, applies the theme and routes between pages.
import { useEffect, useState } from 'react';
import type { HealthResponse } from '../shared/types';
import { api } from './api';
import { useRoute } from './router';
import { useSettings } from './store';
import { Button, Logo, Spinner, Toaster } from './components/ui';
import { HomePage } from './pages/HomePage';
import { SettingsPage } from './pages/SettingsPage';
import { EditorPage } from './editor/EditorPage';

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
      {route.name === 'doc' ? <EditorPage id={route.id} health={health} /> : route.name === 'settings' ? <SettingsPage /> : <HomePage trash={route.trash} health={health} />}
      <Toaster />
    </>
  );
}
