/* Live connection shared by every page.
   The topbar status is always driven from here; pages that want the whole frame
   register a listener with ExbLive.onFrame(). */
(function () {
  'use strict';

  const listeners = [];
  let latest = null;

  const el = {
    dot: document.getElementById('live-dot'),
    state: document.getElementById('live-state'),
    tracked: document.getElementById('live-tracked'),
    rate: document.getElementById('live-rate'),
    readers: document.getElementById('live-readers'),
  };

  function setState(text, cls) {
    if (el.state) el.state.textContent = text;
    if (el.dot) el.dot.className = 'dot' + (cls ? ' ' + cls : '');
  }

  function paintTopbar(frame) {
    if (el.tracked) el.tracked.textContent = frame.tracked;
    if (el.rate) el.rate.textContent = frame.readRateHz;
    if (el.readers) el.readers.textContent = frame.readersOnline + '/' + frame.totalReaders;

    // Readers online is the honest health signal: badges can look fine on a
    // stale map long after the hardware has stopped answering.
    if (frame.totalReaders === 0) setState('no readers', 'warn');
    else if (frame.readersOnline === 0) setState('readers offline', '');
    else if (frame.readersOnline < frame.totalReaders) setState('partial', 'warn');
    else setState('live', 'on');
  }

  if (typeof signalR === 'undefined') {
    setState('unavailable', '');
    return;
  }

  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/live')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on('frame', function (frame) {
    latest = frame;
    paintTopbar(frame);
    for (const fn of listeners) {
      try { fn(frame); } catch (e) { console.error(e); }
    }
  });

  connection.onreconnecting(() => setState('reconnecting', 'warn'));
  connection.onclose(() => setState('disconnected', ''));

  connection.start().catch(function (err) {
    console.error(err);
    setState('disconnected', '');
  });

  window.ExbLive = {
    onFrame: function (fn) {
      listeners.push(fn);
      if (latest) fn(latest);
    },
    latest: function () { return latest; },
  };
})();
