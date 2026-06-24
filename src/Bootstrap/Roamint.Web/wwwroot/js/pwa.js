// PWA wiring (doc 0020): register the service worker and surface a persistent
// "no internet connectivity" banner when the browser itself reports offline.
//
// The Blazor reconnect modal (#components-reconnect-modal in App.razor) covers the
// dropped-circuit case. This banner covers the cruder navigator.onLine case — e.g.
// the device is offline before/while the circuit would even try — and gives an
// immediate signal without waiting for SignalR retries to exhaust.
(() => {
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      navigator.serviceWorker.register('service-worker.js', { scope: '/' }).catch(() => {
        // Registration only works over HTTPS or localhost; on plain-HTTP LAN it's a no-op.
      });
    });
  }

  const banner = () => {
    let el = document.getElementById('offline-banner');
    if (!el) {
      el = document.createElement('div');
      el.id = 'offline-banner';
      el.textContent = 'No internet connectivity';
      document.body.appendChild(el);
    }
    return el;
  };

  const sync = () => banner().classList.toggle('offline-banner-show', !navigator.onLine);
  window.addEventListener('online', sync);
  window.addEventListener('offline', sync);
  document.addEventListener('DOMContentLoaded', sync);
})();
