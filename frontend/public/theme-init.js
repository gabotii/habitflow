// Applies the saved theme before the first paint to avoid a light flash. It is a separate file because the CSP
// forbids inline scripts. Same logic as src/lib/theme.ts.
;(function () {
  var preference = 'system'
  try {
    preference = localStorage.getItem('habitflow.theme') || 'system'
  } catch (e) {
    // Storage blocked: follow the system.
  }
  var dark =
    preference === 'dark' ||
    (preference !== 'light' && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
  if (dark) document.querySelector('meta[name="theme-color"]').setAttribute('content', '#0f0e1a')
})()
