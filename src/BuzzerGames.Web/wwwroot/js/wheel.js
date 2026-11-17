// Wheel of Fortune, big screen: the spin animation, the clicker flaps, and every sound (all made in the browser with Web Audio, no files).
window.wheel = (function () {
  var ctx = null;

  function audio() {
    if (!ctx) {
      var AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return null;
      ctx = new AC();
    }
    if (ctx.state === 'suspended') ctx.resume();
    return ctx;
  }

  // Browsers keep sound off until the page has been touched. The first click or key press turns it on.
  function unlock() {
    audio();
    document.body.classList.add('wf-sound-on');
  }
  document.addEventListener('pointerdown', unlock);
  document.addEventListener('keydown', unlock);

  function tone(freq, start, length, type, volume, endFreq) {
    var a = audio();
    if (!a) return;
    var t0 = a.currentTime + start;
    var osc = a.createOscillator();
    var gain = a.createGain();
    osc.type = type || 'sine';
    osc.frequency.setValueAtTime(freq, t0);
    if (endFreq) osc.frequency.exponentialRampToValueAtTime(endFreq, t0 + length);
    gain.gain.setValueAtTime(0.0001, t0);
    gain.gain.exponentialRampToValueAtTime(volume || 0.2, t0 + 0.01);
    gain.gain.exponentialRampToValueAtTime(0.0001, t0 + length);
    osc.connect(gain);
    gain.connect(a.destination);
    osc.start(t0);
    osc.stop(t0 + length + 0.05);
  }

  // A crowd clapping: filtered noise whose loudness crackles with lots of little overlapping claps, swelling in and fading out over ~3.5 s.
  function applause() {
    var a = audio();
    if (!a) return;
    var seconds = 3.5, rate = a.sampleRate, n = Math.floor(seconds * rate);
    var buffer = a.createBuffer(1, n, rate);
    var data = buffer.getChannelData(0);
    var clap = 0;
    for (var i = 0; i < n; i++) {
      var t = i / rate;
      var swell = Math.min(t / 0.35, 1) * (t < 2.6 ? 1 : Math.max((seconds - t) / (seconds - 2.6), 0));
      if (Math.random() < 0.0022) clap = 0.6 + Math.random() * 0.4;   // someone claps
      clap *= 0.9985;                                                   // and the clap dies away
      data[i] = (Math.random() * 2 - 1) * (0.18 + clap) * swell;
    }
    var src = a.createBufferSource();
    src.buffer = buffer;
    var band = a.createBiquadFilter();
    band.type = 'bandpass';
    band.frequency.value = 2200;
    band.Q.value = 0.45;
    var gain = a.createGain();
    gain.gain.value = 1.6;
    src.connect(band);
    band.connect(gain);
    gain.connect(a.destination);
    src.start();
  }

  var sounds = {
    applause: applause,
    click: function () { tone(1500, 0, 0.03, 'square', 0.07, 700); },
    tick: function () { tone(1250, 0, 0.09, 'triangle', 0.18); },
    bell: function () { tone(1046, 0, 0.8, 'sine', 0.22); tone(1568, 0, 0.6, 'sine', 0.1); },
    ding: function () { tone(1318, 0, 0.9, 'sine', 0.25); tone(1976, 0, 0.9, 'sine', 0.12); },
    land: function () { tone(660, 0, 0.18, 'triangle', 0.18); },
    buzz: function () { tone(150, 0, 0.55, 'sawtooth', 0.22, 110); },
    bankrupt: function () { tone(300, 0, 0.35, 'sawtooth', 0.22, 120); tone(200, 0.35, 0.6, 'sawtooth', 0.22, 70); },
    lose: function () { tone(520, 0, 0.25, 'triangle', 0.2); tone(390, 0.25, 0.4, 'triangle', 0.2); },
    million: function () { [523, 659, 784, 1047, 1319].forEach(function (f, i) { tone(f, i * 0.12, 0.5, 'triangle', 0.22); }); },
    solve: function () { [523, 659, 784, 1047].forEach(function (f, i) { tone(f, i * 0.14, 0.6, 'triangle', 0.22); }); tone(1319, 0.6, 1.0, 'triangle', 0.22); }
  };

  function norm(a) { a = a % 360; return a < 0 ? a + 360 : a; }

  // Index of the slice whose start is at or just before angle a (starts sorted ascending, first is 0).
  function sliceIndex(starts, a) {
    var lo = 0, hi = starts.length - 1;
    while (lo < hi) {
      var mid = (lo + hi + 1) >> 1;
      if (starts[mid] <= a) lo = mid; else hi = mid - 1;
    }
    return lo;
  }

  function flick(i) {
    var el = document.getElementById('wf-flap-' + i);
    if (!el) return;
    el.classList.remove('flick');
    void el.getBoundingClientRect();
    el.classList.add('flick');
  }

  return {
    play: function (name) { var f = sounds[name]; if (f) f(); },
    enableSound: unlock,

    // At rest: put the wheel at this angle (the game's own bookkeeping). Does nothing while a spin is playing.
    sync: function (id, angle) {
      var el = document.getElementById(id);
      if (!el || el._spinning) return;
      el._angle = angle;
      el.setAttribute('transform', 'rotate(' + angle + ')');
    },

    // Plays one spin: from start to end degrees over duration seconds, fast then slowing (ease-out cubic, the same curve the game uses).
    // pegAngles is every peg's angle on the wheel (one every 5 degrees). Whenever a peg passes a clicker, that clicker flicks, and the active one clicks.
    spin: function (id, start, end, duration, clickerAngle, pegAngles, clickerAngles, active) {
      return new Promise(function (resolve) {
        var tries = 0;
        (function go() {
          var el = document.getElementById(id);
          if (!el) { if (tries++ < 50) return setTimeout(go, 20); return resolve(); }
          el._spinning = true;
          var t0 = performance.now();
          var last = clickerAngles.map(function (c) { return sliceIndex(pegAngles, norm(c - start)); });
          function frame(now) {
            var u = Math.min(1, (now - t0) / 1000 / duration);
            var p = 1 - Math.pow(1 - u, 3);
            var theta = start + (end - start) * p;
            el.setAttribute('transform', 'rotate(' + theta + ')');
            for (var k = 0; k < clickerAngles.length; k++) {
              var idx = sliceIndex(pegAngles, norm(clickerAngles[k] - theta));
              if (idx !== last[k]) {
                last[k] = idx;
                flick(k);
                if (k === active) sounds.click();
              }
            }
            if (u < 1) requestAnimationFrame(frame);
            else { el._spinning = false; el._angle = end; resolve(); }
          }
          requestAnimationFrame(frame);
        })();
      });
    }
  };
})();
