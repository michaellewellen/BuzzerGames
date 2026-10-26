// Small helpers the Razor pages call through JS interop (IJSRuntime).
window.bg = {
  // Remember which seat this phone took, so a locked screen or a refresh doesn't lose it.
  // Wrapped in try/catch because some private-browsing modes block localStorage.
  getItem: function (key) {
    try { return window.localStorage.getItem(key); } catch (e) { return null; }
  },

  setItem: function (key, value) {
    try { window.localStorage.setItem(key, value); } catch (e) { }
  },

  // Draws a QR code for this room's join address into the element with the given id.
  // The address comes from the browser's own location, so it is always the real public URL
  // (Railway's, or a custom domain later) with no server-side guessing. Returns the full URL.
  renderQr: function (elementId, path) {
    var url = window.location.origin + path;
    var element = document.getElementById(elementId);
    if (element && window.qrcode) {
      var qr = window.qrcode(0, 'M');
      qr.addData(url);
      qr.make();
      element.innerHTML = qr.createSvgTag(6, 2, 'QR code to join the game', 'Scan to join');
    }
    return url;
  }
};
