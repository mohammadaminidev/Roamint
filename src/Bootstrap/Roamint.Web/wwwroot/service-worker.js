// Minimal service worker for Roamint (doc 0020).
//
// Roamint is Blazor Server — the app is a live SignalR circuit and CANNOT run offline.
// This SW exists only to (a) satisfy PWA install criteria (a fetch handler is required)
// and (b) serve a friendly offline.html when a *navigation* fails entirely. It never
// caches dynamic app responses — a stale Blazor page is worse than none — so it is
// strictly network-first and only falls back to the precached offline shell.
//
// Bump CACHE whenever offline.html or its icon changes, to evict the old precache.
const CACHE = 'roamint-shell-v1';
const SHELL = ['offline.html', 'icons/icon-192.png', 'manifest.webmanifest'];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  // Only intervene on top-level navigations; let everything else hit the network normally
  // (Blazor's SignalR/WebSocket, _framework, static assets all need the live server).
  if (req.mode !== 'navigate') return;
  event.respondWith(fetch(req).catch(() => caches.match('offline.html')));
});
