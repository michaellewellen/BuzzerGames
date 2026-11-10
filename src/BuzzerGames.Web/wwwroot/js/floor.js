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
  });
}, 100);

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
