// The spin pad on a player's phone: put a finger on the circle, swirl it, let go. How fast the finger was going round the circle when it
// let go decides how hard the wheel is spun. The page never shows a number; the game receives a power from 0 to 1000 (the same scale the
// slider used), so the sweet spot is still just one particular speed to find.
window.wheelpad = (function () {
  // Finger speed, in degrees per second around the circle, that maps onto the power scale. Slower than TOO_SLOW is not counted as a spin
  // at all (a tap or a nudge), MIN_SPEED is the gentlest spin, MAX_SPEED the hardest. Change these to make the pad easier or harder.
  var TOO_SLOW = 70;
  var MIN_SPEED = 120;
  var MAX_SPEED = 1500;
  var WINDOW_MS = 150;     // the speed is measured over the last bit of the swirl before the finger lifts
  var MIN_SWEPT = 25;      // degrees the finger must have travelled in the whole swirl, or it was just a tap

  function powerFor(speed) {
    var p = (speed - MIN_SPEED) / (MAX_SPEED - MIN_SPEED);
    return Math.round(1000 * Math.min(1, Math.max(0, p)));
  }

  function norm180(d) {
    while (d > 180) d -= 360;
    while (d < -180) d += 360;
    return d;
  }

  return {
    powerFor: powerFor,

    // Safe to call after every render: a pad that is already set up is left alone, a new one (the turn came round again) gets set up.
    attach: function (id, dotnetRef) {
      var el = document.getElementById(id);
      if (!el || el._wfPad) return;
      el._wfPad = true;
      var disc = el.querySelector('.wf-pad-disc');
      var msg = document.getElementById(id + '-msg');
      var rot = 0, down = false, pointer = null, cx = 0, cy = 0, lastAngle = 0, cum = 0, samples = [], inertia = null;

      function say(text) { if (msg) msg.textContent = text; }
      function angleOf(e) { return Math.atan2(e.clientY - cy, e.clientX - cx) * 180 / Math.PI; }
      function show() { if (disc) disc.style.transform = 'rotate(' + rot + 'deg)'; }
      function cumAt(t) {
        var c = 0;
        for (var i = 0; i < samples.length; i++) { if (samples[i].t <= t) c = samples[i].c; else break; }
        return c;
      }

      el.addEventListener('pointerdown', function (e) {
        if (down) return;
        e.preventDefault();
        if (inertia) { cancelAnimationFrame(inertia); inertia = null; }
        var r = el.getBoundingClientRect();
        cx = r.left + r.width / 2;
        cy = r.top + r.height / 2;
        down = true;
        pointer = e.pointerId;
        try { el.setPointerCapture(e.pointerId); } catch (x) { }
        lastAngle = angleOf(e);
        cum = 0;
        samples = [{ t: e.timeStamp, c: 0 }];
        say('');
        el.classList.add('touching');
      });

      el.addEventListener('pointermove', function (e) {
        if (!down || e.pointerId !== pointer) return;
        e.preventDefault();
        var a = angleOf(e);
        var d = norm180(a - lastAngle);
        lastAngle = a;
        cum += d;
        rot += d;
        show();
        samples.push({ t: e.timeStamp, c: cum });
        if (samples.length > 80) samples.shift();
      });

      function finish(e) {
        if (!down || e.pointerId !== pointer) return;
        down = false;
        el.classList.remove('touching');
        try { el.releasePointerCapture(e.pointerId); } catch (x) { }

        var up = e.timeStamp;
        var start = Math.max(up - WINDOW_MS, samples[0].t);
        var span = (up - start) / 1000;
        var swept = cum - cumAt(start);
        var speed = span > 0.02 ? Math.abs(swept) / span : 0;
        var total = Math.abs(cum);

        if (e.type === 'pointercancel' || total < MIN_SWEPT || speed < TOO_SLOW) {
          say(total < MIN_SWEPT ? 'Swirl your finger around the wheel, then let go.' : 'Too slow! Swirl faster and let go while still moving.');
          return;
        }

        // Keep the pad turning for a moment, like a wheel that has been flicked.
        var omega = (swept < 0 ? -1 : 1) * speed;
        var last = performance.now();
        (function spinDown(now) {
          var dt = Math.min(0.05, (now - last) / 1000);
          last = now;
          rot += omega * dt;
          omega *= Math.exp(-2.2 * dt);
          show();
          if (Math.abs(omega) > 8) inertia = requestAnimationFrame(spinDown); else inertia = null;
        })(last);

        say('');
        dotnetRef.invokeMethodAsync('Swirled', powerFor(speed));
      }
      el.addEventListener('pointerup', finish);
      el.addEventListener('pointercancel', finish);
    }
  };
})();
