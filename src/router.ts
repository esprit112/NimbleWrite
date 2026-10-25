// Minimal hash router: #/ (documents), #/trash, #/doc/<id>, #/settings
import { useEffect, useState } from 'react';

export type Route = { name: 'home'; trash: boolean } | { name: 'doc'; id: string } | { name: 'settings' };

export function parseRoute(hash: string): Route {
  const path = hash.replace(/^#/, '') || '/';
  const doc = /^\/doc\/([a-zA-Z0-9-]+)/.exec(path);
  if (doc) return { name: 'doc', id: doc[1] };
  if (path.startsWith('/settings')) return { name: 'settings' };
  return { name: 'home', trash: path.startsWith('/trash') };
}

export function navigate(path: string): void {
  if (window.location.hash !== `#${path}`) window.location.hash = path;
}

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parseRoute(window.location.hash));
  useEffect(() => {
    const onChange = () => setRoute(parseRoute(window.location.hash));
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return route;
}
