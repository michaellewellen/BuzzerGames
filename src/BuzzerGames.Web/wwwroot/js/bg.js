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

// The Password Plus bonus-round countdown. The server owns the real clock and ends the round itself; this only makes the
// numbers on screen tick down smoothly. An element with data-clock-end / data-clock-ms is counted down from the moment
// it first appears (or when data-clock-end changes, which means a new clock), so a device's own clock doesn't matter.
// It changes the text node's value in place, which Blazor's own updates to the page are fine with.
setInterval(function () {
  var now = Date.now();
  document.querySelectorAll('[data-clock-end]').forEach(function (el) {
    var key = el.getAttribute('data-clock-end');
    if (el._clockKey !== key) {
      el._clockKey = key;
      el._clockDeadline = now + Number(el.getAttribute('data-clock-ms'));
    }
    var seconds = Math.max(0, Math.ceil((el._clockDeadline - now) / 1000));
    if (el.firstChild && el.firstChild.nodeValue !== String(seconds)) el.firstChild.nodeValue = String(seconds);
    el.classList.toggle('low', seconds <= 10);
  });
}, 200);

// The Price Is Right announcer. The big screen reads announcements out loud with the browser's own text-to-speech, after a short
// brass fanfare for the big moments. Browsers only allow sound after a tap, so unlockSpeech() is called from a button.
(function () {
  var synth = window.speechSynthesis;
  var audio = null;
  var turn = 0;
  var voice = null;

  function chooseVoice() {
    if (!synth) return null;
    var all = synth.getVoices().filter(function (v) { return /^en/i.test(v.lang); });
    var wanted = [/Google US English/i, /Online \(Natural\)/i, /Samantha/i, /Daniel/i, /Alex/i, /Aaron/i, /Google UK English Male/i];
    for (var i = 0; i < wanted.length; i++) {
      var hit = all.filter(function (v) { return wanted[i].test(v.name); })[0];
      if (hit) return hit;
    }
    return all.filter(function (v) { return /en-US/i.test(v.lang); })[0] || all[0] || null;
  }

  if (synth && synth.addEventListener) synth.addEventListener('voiceschanged', function () { voice = chooseVoice(); });

  function fanfare() {
    try {
      if (!audio) return;
      var start = audio.currentTime + 0.02;
      [[523.25, 0, 0.12], [659.25, 0.12, 0.12], [783.99, 0.24, 0.12], [1046.5, 0.36, 0.45]].forEach(function (n) {
        var osc = audio.createOscillator();
        var gain = audio.createGain();
        osc.type = 'sawtooth';
        osc.frequency.value = n[0];
        gain.gain.setValueAtTime(0.0001, start + n[1]);
        gain.gain.exponentialRampToValueAtTime(0.22, start + n[1] + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, start + n[1] + n[2]);
        osc.connect(gain);
        gain.connect(audio.destination);
        osc.start(start + n[1]);
        osc.stop(start + n[1] + n[2] + 0.05);
      });
    } catch (e) { }
  }

  window.bg.unlockSpeech = function () {
    if (!synth) return false;
    try {
      var Ctx = window.AudioContext || window.webkitAudioContext;
      if (Ctx && !audio) audio = new Ctx();
      if (audio && audio.resume) audio.resume();
      var quiet = new SpeechSynthesisUtterance(' ');
      quiet.volume = 0;
      synth.speak(quiet);
      voice = chooseVoice();
      return true;
    } catch (e) { return false; }
  };

  // Reads the text one sentence at a time. (Some browsers stop in the middle of one long utterance.)
  window.bg.speak = function (text, withFanfare) {
    if (!synth) return;
    var mine = ++turn;
    synth.cancel();
    var parts = (text || '').match(/[^.!?]+[.!?]*/g) || [];
    if (parts.length === 0) return;
    if (!voice) voice = chooseVoice();

    var next = function (i) {
      if (mine !== turn || i >= parts.length) return;
      var sentence = parts[i].trim();
      if (!sentence) { next(i + 1); return; }
      var u = new SpeechSynthesisUtterance(sentence);
      if (voice) u.voice = voice;
      var excited = /!\s*$/.test(sentence);
      u.pitch = excited ? 1.15 : 1.0;
      u.rate = excited ? 1.05 : 0.98;
      u.onend = function () { next(i + 1); };
      u.onerror = function () { next(i + 1); };
      synth.speak(u);
    };

    if (withFanfare) {
      fanfare();
      setTimeout(function () { next(0); }, 700);
    } else {
      setTimeout(function () { next(0); }, 60);
    }
  };
})();
