// Family Feud, big screen: every sound, made in the browser with Web Audio (no files). The celebration sounds (solve, applause) and the clock
// sounds (tick, timeup) are wheel.js's, so this file borrows them instead of repeating them.
window.feud = (function () {
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

  function unlock() {
    audio();
    document.body.classList.add('wf-sound-on');
  }
  document.addEventListener('pointerdown', unlock);
  document.addEventListener('mousedown', unlock);
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

  var sounds = {
    // The bright "ding" for a right answer: a bell with a high shimmer on top.
    ding: function () {
      tone(1319, 0, 1.0, 'sine', 0.26);
      tone(1976, 0, 0.9, 'sine', 0.12);
      tone(2637, 0, 0.5, 'sine', 0.06);
    },
    // The strike: a low double buzz.
    strike: function () {
      tone(120, 0, 0.45, 'sawtooth', 0.32, 95);
      tone(180, 0, 0.45, 'square', 0.12, 140);
      tone(120, 0.5, 0.55, 'sawtooth', 0.32, 80);
      tone(180, 0.5, 0.55, 'square', 0.12, 120);
    },
    // A buzzer for the face-off: short and sharp.
    buzzin: function () {
      tone(880, 0, 0.12, 'square', 0.18);
      tone(660, 0.1, 0.25, 'square', 0.18);
    },
    // A panel sliding away.
    slide: function () { tone(260, 0, 0.3, 'triangle', 0.13, 900); },
    // Zero points in Fast Money: a flat little downward note.
    dud: function () { tone(220, 0, 0.5, 'triangle', 0.2, 110); }
  };

  return {
    play: function (name) {
      var f = sounds[name];
      if (f) return f();
      if (window.wheel && window.wheel.play) window.wheel.play(name);
    },
    enableSound: unlock
  };
})();
