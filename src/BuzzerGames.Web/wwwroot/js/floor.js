// The Floor: the on-screen clocks, and the picture uploader on the topics page.

// ---- Clocks ---------------------------------------------------------------------------------------------------------------
// The server owns the real clocks. This only makes the numbers tick down smoothly between its updates. An element with
// data-fl-ms (milliseconds left when the server last sent it) counts down from the moment that value arrives, if data-fl-run is "1".
// data-fl-key changes with every server update, so a new value always restarts the count from the server's number.
setInterval(function () {
  var now = Date.now();
  document.querySelectorAll('[data-fl-ms]').forEach(function (el) {
    var key = el.getAttribute('data-fl-key') + '|' + el.getAttribute('data-fl-ms') + '|' + el.getAttribute('data-fl-run');
    if (el._flKey !== key) {
      el._flKey = key;
      el._flBase = now;
    }
    var ms = Number(el.getAttribute('data-fl-ms'));
    var left = el.getAttribute('data-fl-run') === '1' ? Math.max(0, ms - (now - el._flBase)) : ms;
    var seconds = Math.ceil(left / 1000);
    if (el.firstChild && el.firstChild.nodeValue !== String(seconds)) el.firstChild.nodeValue = String(seconds);
    el.classList.toggle('low', seconds <= 10 && seconds > 0);
    // On the big screen only: a tick each second of the last ten, from whichever clock is running.
    if (el._flLast !== seconds) {
      el._flLast = seconds;
      if (el.getAttribute('data-fl-run') === '1' && seconds > 0 && seconds <= 10 && el.closest('.fl-tv') && window.floorSound) window.floorSound.play('tick', seconds);
    }
  });
}, 100);

// ---- Sound -----------------------------------------------------------------------------------------------------------------
// Every sound is made from oscillators and noise by the browser, so there are no files and nothing to license. Only the big screen
// makes sound. Browsers keep a page silent until someone has clicked it once, so the TV has a "Tap for sound" button.
window.floorSound = (function () {
  var ctx = null, master = null, muted = false, noiseBuffer = null;
  try { muted = localStorage.getItem('bg.floor.mute') === '1'; } catch (e) { /* storage can be blocked; sound still works */ }

  function reflect() {
    document.documentElement.setAttribute('data-fl-sound', ctx && !muted ? '1' : '0');
  }

  function remember() {
    try { localStorage.setItem('bg.floor.mute', muted ? '1' : '0'); } catch (e) { /* not important */ }
  }

  function ensureContext() {
    if (ctx) return true;
    var AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return false;
    ctx = new AC();
    var limiter = ctx.createDynamicsCompressor();   // keeps loud moments from clipping
    master = ctx.createGain();
    master.gain.value = 0.7;
    master.connect(limiter);
    limiter.connect(ctx.destination);
    return true;
  }

  function enable() {
    try {
      if (!ensureContext()) return;
      if (ctx.state === 'suspended') ctx.resume();
      var wasMuted = muted;
      muted = false;
      remember();
      reflect();
      if (wasMuted || !enable.once) { enable.once = true; build(ctx, master, 'correct', undefined, ctx.currentTime + 0.02); }
    } catch (e) { /* no sound is better than a broken page */ }
  }

  function toggle() {
    if (!ctx || muted) { enable(); return; }
    muted = true;
    remember();
    reflect();
  }

  // -- building blocks --

  function shape(c, out, t, attack, dur, peak) {
    var g = c.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(Math.max(peak, 0.0002), t + attack);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    g.connect(out);
    return g;
  }

  function tone(c, out, t, type, f0, f1, dur, peak, attack) {
    var o = c.createOscillator();
    o.type = type;
    o.frequency.setValueAtTime(f0, t);
    if (f1 && f1 !== f0) o.frequency.exponentialRampToValueAtTime(f1, t + dur);
    o.connect(shape(c, out, t, attack || 0.01, dur, peak));
    o.start(t);
    o.stop(t + dur + 0.05);
  }

  function noise(c, out, t, dur, peak, kind, f0, f1) {
    if (!noiseBuffer || noiseBuffer.sampleRate !== c.sampleRate) {
      noiseBuffer = c.createBuffer(1, c.sampleRate, c.sampleRate);
      var data = noiseBuffer.getChannelData(0);
      for (var i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
    }
    var src = c.createBufferSource();
    src.buffer = noiseBuffer;
    src.loop = true;
    var f = c.createBiquadFilter();
    f.type = kind;
    f.frequency.setValueAtTime(f0, t);
    if (f1 && f1 !== f0) f.frequency.exponentialRampToValueAtTime(f1, t + dur);
    src.connect(f);
    f.connect(shape(c, out, t, 0.02, dur, peak));
    src.start(t);
    src.stop(t + dur + 0.05);
  }

  function kick(c, out, t, peak, low) {
    tone(c, out, t, 'sine', 150, low || 42, 0.35, peak, 0.004);
    noise(c, out, t, 0.08, peak * 0.35, 'lowpass', 900, 300);
  }

  function chord(c, out, t, freqs, dur, peak) {
    freqs.forEach(function (f) { tone(c, out, t, 'triangle', f, f, dur, peak, 0.02); });
  }

  // A buzzer: held at full level, then cut, which is what makes it sound like "wrong" or "out of time".
  function buzz(c, out, t, dur) {
    [110, 113.5, 220, 226].forEach(function (f, i) {
      var o = c.createOscillator();
      o.type = i < 2 ? 'sawtooth' : 'square';
      o.frequency.setValueAtTime(f, t);
      var g = c.createGain();
      var peak = i < 2 ? 0.2 : 0.07;
      g.gain.setValueAtTime(0.0001, t);
      g.gain.linearRampToValueAtTime(peak, t + 0.03);
      g.gain.setValueAtTime(peak, t + dur - 0.12);
      g.gain.linearRampToValueAtTime(0.0001, t + dur);
      o.connect(g);
      g.connect(out);
      o.start(t);
      o.stop(t + dur + 0.05);
    });
  }

  // -- the sounds --

  var sounds = {
    // a right answer: two quick rising notes
    correct: function (c, out, t) {
      tone(c, out, t, 'triangle', 880, 880, 0.14, 0.28);
      tone(c, out, t + 0.09, 'triangle', 1318, 1318, 0.24, 0.28);
    },
    // a pass: a low, sagging "bwomp"
    pass: function (c, out, t) {
      tone(c, out, t, 'sawtooth', 260, 105, 0.4, 0.28, 0.02);
      tone(c, out, t, 'square', 130, 62, 0.4, 0.14, 0.02);
    },
    // each of the last ten seconds, higher and louder as it runs down
    tick: function (c, out, t, seconds) {
      var s = Math.min(10, Math.max(1, seconds || 10));
      tone(c, out, t, 'square', 600 + (10 - s) * 70, 0, 0.05, s <= 3 ? 0.2 : 0.1, 0.004);
    },
    // out of time
    timeup: function (c, out, t) {
      noise(c, out, t, 0.35, 0.3, 'bandpass', 3000, 400);
      buzz(c, out, t, 1.4);
    },
    // the duel begins
    start: function (c, out, t) {
      tone(c, out, t, 'square', 660, 0, 0.09, 0.14, 0.005);
      tone(c, out, t + 0.13, 'square', 990, 0, 0.2, 0.14, 0.005);
    },
    // a challenge is made: drum hit and a metal ping
    clash: function (c, out, t) {
      kick(c, out, t, 0.6);
      noise(c, out, t, 0.25, 0.22, 'bandpass', 2400, 900);
      tone(c, out, t + 0.02, 'triangle', 1760, 1760, 0.6, 0.1, 0.005);
    },
    // the final duel: two heavy hits, a rising growl, then a dark chord
    final: function (c, out, t) {
      kick(c, out, t, 0.7, 36);
      kick(c, out, t + 0.5, 0.7, 36);
      tone(c, out, t + 0.1, 'sawtooth', 90, 230, 1.3, 0.16, 0.3);
      chord(c, out, t + 1.1, [220, 261.63, 329.63], 1.2, 0.13);
      noise(c, out, t + 1.1, 0.4, 0.15, 'bandpass', 3000, 1200);
    },
    // a turn passed on: a quick rushing sweep
    whoosh: function (c, out, t) {
      noise(c, out, t, 0.5, 0.26, 'bandpass', 400, 3200);
    },
    // a territory taken: a rush, one thud per square as each flips, then a bright chord
    takeover: function (c, out, t, count) {
      var n = Math.min(12, Math.max(1, count || 1));
      noise(c, out, t, 0.45, 0.22, 'bandpass', 300, 2500);
      for (var i = 0; i < n; i++) kick(c, out, t + 0.4 + i * 0.35, 0.5);
      var end = t + 0.4 + (n - 1) * 0.35 + 0.4;
      chord(c, out, end, [523.25, 659.25, 783.99], 1.1, 0.16);
      tone(c, out, end, 'triangle', 1046.5, 1046.5, 0.9, 0.1, 0.02);
    },
    // the winner
    fanfare: function (c, out, t) {
      [523.25, 659.25, 783.99].forEach(function (f, i) {
        tone(c, out, t + i * 0.18, 'triangle', f, f, 0.28, 0.24, 0.01);
        tone(c, out, t + i * 0.18, 'square', f, f, 0.28, 0.05, 0.01);
      });
      var last = t + 0.54;
      chord(c, out, last, [523.25, 659.25, 783.99, 1046.5], 1.8, 0.17);
      tone(c, out, last, 'square', 523.25, 523.25, 1.2, 0.05, 0.02);
      [2093, 2637, 3136].forEach(function (f, i) { tone(c, out, last + 0.1 + i * 0.12, 'sine', f, f, 0.5, 0.05, 0.01); });
    }
  };

  function build(c, out, name, arg, t) {
    if (sounds[name]) sounds[name](c, out, t, arg);
  }

  function play(name, arg) {
    try {
      if (!ctx || muted) return;
      if (ctx.state === 'suspended') ctx.resume();
      build(ctx, master, name, arg, ctx.currentTime + 0.02);
    } catch (e) { /* never let a sound break the game */ }
  }

  // Buttons anywhere on the site can ask for sound: data-fl-play="timeup" plays that sound; data-fl-sound-toggle turns sound on or off.
  document.addEventListener('click', function (e) {
    var t = e.target.closest && e.target.closest('[data-fl-sound-toggle]');
    if (t) { toggle(); return; }
    var p = e.target.closest && e.target.closest('[data-fl-play]');
    if (p) {
      enable();
      play(p.getAttribute('data-fl-play'), Number(p.getAttribute('data-fl-arg')) || undefined);
    }
  });

  // Anyone who taps the big screen at all gets sound, unless they turned it off.
  function firstTouch() {
    document.removeEventListener('pointerdown', firstTouch);
    document.removeEventListener('keydown', firstTouch);
    if (!muted && document.querySelector('.fl-tv')) enable();
  }
  document.addEventListener('pointerdown', firstTouch);
  document.addEventListener('keydown', firstTouch);

  reflect();
  return { enable: enable, toggle: toggle, play: play, names: Object.keys(sounds), _build: build };
})();

// ---- Uploader --------------------------------------------------------------------------------------------------------------
window.floorAdmin = (function () {
  var MAX_SIDE = 900;      // pictures are shrunk to this many pixels on the longest side before upload
  var QUALITY = 0.85;
  var state = null;

  function titleFromFile(name) {
    var stem = name.replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' ').replace(/\s*\(\d+\)$/, '').trim();
    if (stem && stem === stem.toLowerCase()) stem = stem.replace(/\b\w/g, function (c) { return c.toUpperCase(); });
    return stem;
  }

  // Shrinks a picture and turns it into a JPEG. Screen clips are large PNGs; this makes them a fraction of the size.
  function shrink(file) {
    return new Promise(function (resolve, reject) {
      var url = URL.createObjectURL(file);
      var img = new Image();
      img.onload = function () {
        var scale = Math.min(1, MAX_SIDE / Math.max(img.naturalWidth, img.naturalHeight));
        var canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(img.naturalWidth * scale));
        canvas.height = Math.max(1, Math.round(img.naturalHeight * scale));
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#fff';            // JPEG has no transparency
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
        URL.revokeObjectURL(url);
        canvas.toBlob(function (blob) { blob ? resolve({ blob: blob, preview: canvas.toDataURL('image/jpeg', 0.5) }) : reject(new Error('Could not read that picture.')); }, 'image/jpeg', QUALITY);
      };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error('That file is not a picture the browser can read.')); };
      img.src = url;
    });
  }

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text) e.textContent = text;
    return e;
  }

  function mount(id, topic, pass, dotnet) {
    unmount();
    var root = document.getElementById(id);
    if (!root) return;
    root.textContent = '';

    var drop = el('div', 'fl-drop', 'Paste a screen clip (Ctrl+V), drop pictures here, or choose files');
    drop.tabIndex = 0;
    var chooser = el('input'); chooser.type = 'file'; chooser.accept = 'image/*'; chooser.multiple = true;
    var list = el('div', 'fl-pending');
    var saveAll = el('button', 'btn btn-start', 'Save all'); saveAll.style.display = 'none';
    var note = el('p', 'hint');
    root.appendChild(drop); root.appendChild(chooser); root.appendChild(list); root.appendChild(saveAll); root.appendChild(note);

    var items = [];

    function updateSaveButton() {
      saveAll.style.display = items.some(function (i) { return !i.saved; }) ? '' : 'none';
    }

    function addFile(file, focus) {
      if (!file || file.type.indexOf('image/') !== 0) { note.textContent = 'That wasn\'t a picture.'; return; }
      shrink(file).then(function (shrunk) {
        var row = el('div', 'fl-pending-item');
        var thumb = el('img', 'fl-pending-thumb'); thumb.src = shrunk.preview; thumb.alt = '';
        var answer = el('input', 'text-input'); answer.placeholder = 'Answer (e.g. Golden Retriever)'; answer.maxLength = 80;
        answer.value = file.name && file.name !== 'image.png' ? titleFromFile(file.name) : '';
        var alts = el('input', 'text-input'); alts.placeholder = 'Other accepted answers (optional, separated by ;)'; alts.maxLength = 200;
        var status = el('span', 'fl-pending-status');
        var remove = el('button', 'btn btn-quiet btn-small', 'Remove');
        var fields = el('div', 'fl-pending-fields');
        fields.appendChild(answer); fields.appendChild(alts); fields.appendChild(status);
        row.appendChild(thumb); row.appendChild(fields); row.appendChild(remove);
        list.prepend(row);

        var item = { blob: shrunk.blob, answer: answer, alts: alts, status: status, row: row, saved: false, busy: false };
        items.push(item);
        remove.onclick = function () { items.splice(items.indexOf(item), 1); row.remove(); updateSaveButton(); };
        // Enter in the answer box saves just this one: paste, type, Enter, next.
        answer.onkeydown = alts.onkeydown = function (e) { if (e.key === 'Enter') { e.preventDefault(); save(item); } };
        updateSaveButton();
        if (focus) answer.focus();
      }).catch(function (err) { note.textContent = err.message; });
    }

    function save(item) {
      if (item.saved || item.busy) return Promise.resolve();
      if (!item.answer.value.trim()) { item.status.textContent = 'Type the answer first.'; item.status.className = 'fl-pending-status bad'; return Promise.resolve(); }
      item.busy = true;
      item.status.textContent = 'Saving…'; item.status.className = 'fl-pending-status';
      var form = new FormData();
      form.append('topic', topic);
      form.append('answer', item.answer.value.trim());
      form.append('alternates', item.alts.value.trim());
      form.append('file', item.blob, 'picture.jpg');
      return fetch('/the-floor/upload', { method: 'POST', headers: { 'X-Floor-Pass': pass }, body: form })
        .then(function (r) { return r.json(); })
        .then(function (r) {
          item.busy = false;
          if (r.ok) {
            item.saved = true;
            item.status.textContent = 'Saved'; item.status.className = 'fl-pending-status good';
            item.row.classList.add('saved');
            setTimeout(function () { var i = items.indexOf(item); if (i >= 0) items.splice(i, 1); item.row.remove(); updateSaveButton(); }, 900);
            if (dotnet) dotnet.invokeMethodAsync('OnUploaded');
          } else {
            item.status.textContent = r.error || 'Could not save.'; item.status.className = 'fl-pending-status bad';
          }
        })
        .catch(function () { item.busy = false; item.status.textContent = 'Could not reach the server.'; item.status.className = 'fl-pending-status bad'; });
    }

    saveAll.onclick = function () {
      items.slice().reduce(function (p, item) { return p.then(function () { return save(item); }); }, Promise.resolve());
    };

    chooser.onchange = function () { Array.prototype.forEach.call(chooser.files, function (f) { addFile(f, false); }); chooser.value = ''; };

    drop.ondragover = function (e) { e.preventDefault(); drop.classList.add('over'); };
    drop.ondragleave = function () { drop.classList.remove('over'); };
    drop.ondrop = function (e) {
      e.preventDefault(); drop.classList.remove('over');
      Array.prototype.forEach.call(e.dataTransfer.files, function (f) { addFile(f, false); });
    };

    function onPaste(e) {
      var target = e.target;
      var files = [];
      Array.prototype.forEach.call((e.clipboardData && e.clipboardData.items) || [], function (item) {
        if (item.kind === 'file' && item.type.indexOf('image/') === 0) files.push(item.getAsFile());
      });
      if (!files.length) return;                       // plain text pastes go to whatever box has focus
      if (target && target.tagName === 'TEXTAREA') return;
      e.preventDefault();
      files.forEach(function (f) { addFile(f, true); });
    }
    document.addEventListener('paste', onPaste);

    state = { onPaste: onPaste };
  }

  function unmount() {
    if (state) document.removeEventListener('paste', state.onPaste);
    state = null;
  }

  return { mount: mount, unmount: unmount };
})();
