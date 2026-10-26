// The Press Your Luck Big Board, drawn in the browser.
//
// The server decides everything that matters (the light's script, and where it stopped). This file only
// DRAWS the board from what the server sent, so it can run the light smoothly without a round trip per flash:
//   mount(id, board)  builds the 18 squares and draws them at rest
//   start(spin)       runs the light from the server's script, in step with the server's clock
//   stop(result)      freezes the board on the square the server says the light stopped on
//   enableSound()     browsers only allow sound after a click, so the big screen has a button for it
//
// SOUNDS: drop files into src/BuzzerGames.Web/wwwroot/sounds/ named tick, start, stop, cash, big, whammy or
// corner (.mp3, .wav or .ogg) and they are used automatically. Anything missing falls back to a synthesized beep.
window.pyl = (() => {
    // Where each of the 18 squares sits on a 6-wide, 5-high grid as [row, column], clockwise from top-left.
    // This order matches BoardFactory on the server.
    const RING = [
        [1, 1], [1, 2], [1, 3], [1, 4], [1, 5], [1, 6],
        [2, 6], [3, 6], [4, 6],
        [5, 6], [5, 5], [5, 4], [5, 3], [5, 2], [5, 1],
        [4, 1], [3, 1], [2, 1],
    ];

    const WHAMMY_FACE = `
      <svg viewBox="0 0 200 200" class="whammy-face" aria-label="Whammy">
        <polygon points="45,70 55,10 90,52" fill="#8a0f18"/><polygon points="155,70 145,10 110,52" fill="#8a0f18"/>
        <circle cx="100" cy="110" r="78" fill="#e0242f" stroke="#5a0a10" stroke-width="6"/>
        <ellipse cx="72" cy="98" rx="17" ry="21" fill="#fff"/><ellipse cx="128" cy="98" rx="17" ry="21" fill="#fff"/>
        <circle cx="76" cy="102" r="8" fill="#111"/><circle cx="124" cy="102" r="8" fill="#111"/>
        <path d="M50 72 L90 88" stroke="#111" stroke-width="9" stroke-linecap="round"/>
        <path d="M150 72 L110 88" stroke="#111" stroke-width="9" stroke-linecap="round"/>
        <path d="M58 138 Q100 190 142 138 Z" fill="#2a0306" stroke="#111" stroke-width="4"/>
        <path d="M70 140 l8 14 l8 -14 M92 142 l8 16 l8 -16 M114 140 l8 14 l8 -14" fill="#fff"/>
      </svg>`;

    let boardEl = null, centerEl = null, squares = [], board = [];
    let spin = null, offsetMs = 0, frame = 0, lastTick = -1, litSquare = -1;
    let audio = null, soundOn = false;
    const files = {};

    const faceAt = (square, tick) => (Math.floor(tick / 2) + square) % 3;

    // ---------- drawing ----------

    function mount(elementId, boardData) {
        boardEl = document.getElementById(elementId);
        if (!boardEl) return;
        cancelAnimationFrame(frame);
        spin = null;
        board = boardData || [];
        boardEl.classList.remove("boom");
        boardEl.innerHTML = "";

        squares = RING.map(([row, col]) => {
            const el = document.createElement("div");
            el.className = "pyl-sq";
            el.style.gridRow = row;
            el.style.gridColumn = col;
            boardEl.appendChild(el);
            return el;
        });

        centerEl = document.createElement("div");
        centerEl.className = "pyl-center";
        boardEl.appendChild(centerEl);

        draw(0, -1);
    }

    // Shows every square's face at the given tick, with one square lit (or none, when lit is -1).
    function draw(tick, lit) {
        litSquare = lit;
        squares.forEach((el, i) => {
            const o = (board[i] || [])[faceAt(i, tick)];
            if (!o) { el.textContent = ""; return; }
            el.className = "pyl-sq k-" + o.kind + (i === lit ? " lit" : "");
            el.innerHTML = "";
            const t = document.createElement("span");
            t.className = "t";
            t.textContent = o.text;
            el.appendChild(t);
            if (o.sub) {
                const s = document.createElement("small");
                s.textContent = o.sub;
                el.appendChild(s);
            }
        });
    }

    // ---------- the light ----------

    function start(s) {
        if (!boardEl) return;
        spin = s;
        // The server's clock and this computer's clock disagree a little; measure the gap once, from the moment
        // the server stamped its "now" into the message.
        offsetMs = s.serverNowUnixMs - Date.now();
        lastTick = -1;
        boardEl.classList.remove("boom");
        centerEl.innerHTML = "";
        play("start");
        cancelAnimationFrame(frame);
        frame = requestAnimationFrame(run);
    }

    function run() {
        if (!spin) return;
        const elapsed = Date.now() + offsetMs - spin.startUnixMs;
        if (elapsed >= 0) {
            const tick = Math.floor(elapsed / spin.tickMs);
            if (tick !== lastTick) {
                lastTick = tick;
                draw(tick, spin.sequence[tick % spin.sequence.length]);
                play("tick");
            }
        }
        frame = requestAnimationFrame(run);
    }

    function stop(r) {
        if (!boardEl) return;
        cancelAnimationFrame(frame);
        spin = null;
        boardEl.classList.remove("boom");
        draw(r.tick, r.square);

        if (r.pending) {
            centerEl.innerHTML = `<div class="result"><span class="rt">PICK A CORNER!</span></div>`;
            play("corner");
            return;
        }

        const sub = r.sub ? `<small>${esc(r.sub)}</small>` : "";
        if (r.kind === "whammy") {
            centerEl.innerHTML = `<div class="result whammy">${WHAMMY_FACE}<span class="rt">WHAMMY!</span></div>`;
            boardEl.classList.remove("boom");
            void boardEl.offsetWidth;            // restart the flash animation
            boardEl.classList.add("boom");
            play("whammy");
        } else {
            centerEl.innerHTML = `<div class="result"><span class="rt">${esc(r.text)}</span>${sub}</div>`;
            play(r.sub === "+ SPIN" ? "big" : "cash");
        }
    }

    // Used when a screen opens mid-game: show the last result without any fanfare.
    function showResult(r) {
        if (!boardEl) return;
        draw(r.tick, r.square);
        if (r.kind === "whammy") centerEl.innerHTML = `<div class="result whammy">${WHAMMY_FACE}<span class="rt">WHAMMY!</span></div>`;
        else centerEl.innerHTML = `<div class="result"><span class="rt">${esc(r.text)}</span>${r.sub ? `<small>${esc(r.sub)}</small>` : ""}</div>`;
    }

    const esc = s => String(s).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));

    // ---------- sound ----------

    async function enableSound() {
        audio ??= new (window.AudioContext || window.webkitAudioContext)();
        await audio.resume();
        soundOn = true;
        loadFiles();
        play("start");
        return true;
    }

    // Looks for optional sound files; each one found replaces the synthesized sound of the same name.
    async function loadFiles() {
        for (const name of ["tick", "start", "stop", "cash", "big", "whammy", "corner"]) {
            for (const ext of ["mp3", "wav", "ogg"]) {
                try {
                    const r = await fetch(`/sounds/${name}.${ext}`);
                    if (!r.ok || !(r.headers.get("content-type") || "").startsWith("audio")) continue;
                    files[name] = await audio.decodeAudioData(await r.arrayBuffer());
                    break;
                } catch (e) { /* not there; keep the synthesized one */ }
            }
        }
    }

    function tone(freq, secs, type = "square", vol = 0.05, delay = 0) {
        const t = audio.currentTime + delay;
        const o = audio.createOscillator(), g = audio.createGain();
        o.type = type; o.frequency.value = freq; g.gain.value = vol;
        o.connect(g).connect(audio.destination);
        o.start(t); o.stop(t + secs);
    }

    function swoop(fromHz, toHz, delay, secs) {
        const t = audio.currentTime + delay;
        const o = audio.createOscillator(), g = audio.createGain();
        o.type = "sawtooth"; g.gain.value = 0.12;
        o.frequency.setValueAtTime(fromHz, t);
        o.frequency.exponentialRampToValueAtTime(toHz, t + secs);
        o.connect(g).connect(audio.destination);
        o.start(t); o.stop(t + secs);
    }

    const synth = {
        tick: () => tone(250 + Math.random() * 600, 0.05),
        start: () => { tone(440, 0.08, "triangle", 0.08); tone(660, 0.12, "triangle", 0.08, 0.1); },
        stop: () => tone(300, 0.1, "square", 0.08),
        cash: () => { tone(880, 0.1, "triangle", 0.1); tone(1320, 0.25, "triangle", 0.1, 0.1); },
        big: () => [660, 880, 1100, 1320, 1760].forEach((f, i) => tone(f, 0.18, "triangle", 0.1, i * 0.09)),
        corner: () => [440, 554, 660].forEach((f, i) => tone(f, 0.15, "square", 0.07, i * 0.1)),
        whammy: () => { swoop(700, 220, 0, 0.45); swoop(450, 60, 0.5, 0.9); },
    };

    function play(name) {
        if (!soundOn || !audio) return;
        if (files[name]) {
            const src = audio.createBufferSource();
            src.buffer = files[name];
            src.connect(audio.destination);
            src.start();
        } else if (synth[name]) {
            synth[name]();
        }
    }

    return { mount, start, stop, showResult, enableSound, _faceAt: faceAt };
})();
