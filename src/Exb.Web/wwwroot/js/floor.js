/* Canvas floor plan.
   Stands are drawn from the layout the server sends once; badges are redrawn
   from each live frame. Keeping the two apart matters — the stand layout is
   hundreds of filled rectangles with labels, and redrawing it every second
   alongside the badges makes a wall display crawl. */
(function () {
  'use strict';

  const COLOURS = {
    floor: '#0f1a26',
    grid: '#1b2b3d',
    stand: '#22354a',
    standEdge: '#31506e',
    standBusy: '#1d4b76',
    label: '#7f95ac',
    badge: '#35d07f',
    badgeOnStand: '#4aa8ff',
    badgeStale: '#7a8899',
    antenna: '#f0a742',
    text: '#c9d6e3',
  };

  function ExbFloor(canvas, options) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.hall = null;
    this.stands = [];
    this.antennas = [];
    this.badges = [];
    this.showAntennas = (options && options.showAntennas) || false;
    this.occupancy = {};
    this.selected = null;
    this.onSelect = (options && options.onSelect) || null;

    const self = this;
    canvas.addEventListener('click', function (ev) {
      if (!self.hall) return;
      const rect = canvas.getBoundingClientRect();
      const x = (ev.clientX - rect.left) / rect.width * canvas.width;
      const y = (ev.clientY - rect.top) / rect.height * canvas.height;
      const world = self.toWorld(x, y);
      const hit = self.stands.find(s =>
        world.x >= s.x && world.x <= s.x + s.w && world.y >= s.y && world.y <= s.y + s.d);
      self.selected = hit ? hit.id : null;
      if (self.onSelect) self.onSelect(hit || null);
      self.draw();
    });
  }

  ExbFloor.prototype.setHall = function (hall, stands, antennas) {
    this.hall = hall;
    this.stands = stands || [];
    this.antennas = antennas || [];
    this.resize();
  };

  ExbFloor.prototype.resize = function () {
    if (!this.hall) return;

    // Fixed internal resolution scaled by CSS, so the map is crisp on a
    // projector without re-laying out on every window nudge.
    const width = 1600;
    const aspect = this.hall.depthM / this.hall.widthM;
    this.canvas.width = width;
    this.canvas.height = Math.round(width * aspect);
    this.pad = 24;
    this.scale = (this.canvas.width - this.pad * 2) / this.hall.widthM;
    this.draw();
  };

  ExbFloor.prototype.toScreen = function (x, y) {
    // Hall coordinates put +y north; screen coordinates put +y down.
    return {
      x: this.pad + x * this.scale,
      y: this.canvas.height - this.pad - y * this.scale,
    };
  };

  ExbFloor.prototype.toWorld = function (sx, sy) {
    return {
      x: (sx - this.pad) / this.scale,
      y: (this.canvas.height - this.pad - sy) / this.scale,
    };
  };

  ExbFloor.prototype.setBadges = function (badges) {
    this.badges = badges || [];
    this.occupancy = {};
    for (const b of this.badges) if (b.k) this.occupancy[b.k] = (this.occupancy[b.k] || 0) + 1;
    this.draw();
  };

  ExbFloor.prototype.draw = function () {
    const ctx = this.ctx;
    if (!this.hall) return;

    ctx.fillStyle = COLOURS.floor;
    ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);

    this.drawGrid();
    this.drawStands();
    if (this.showAntennas) this.drawAntennas();
    this.drawBadges();
  };

  ExbFloor.prototype.drawGrid = function () {
    const ctx = this.ctx;
    const step = 5;
    ctx.strokeStyle = COLOURS.grid;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = 0; x <= this.hall.widthM; x += step) {
      const p = this.toScreen(x, 0);
      ctx.moveTo(p.x, this.pad);
      ctx.lineTo(p.x, this.canvas.height - this.pad);
    }
    for (let y = 0; y <= this.hall.depthM; y += step) {
      const p = this.toScreen(0, y);
      ctx.moveTo(this.pad, p.y);
      ctx.lineTo(this.canvas.width - this.pad, p.y);
    }
    ctx.stroke();
  };

  ExbFloor.prototype.drawStands = function () {
    const ctx = this.ctx;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';

    for (const s of this.stands) {
      const here = this.occupancy[s.id] || 0;
      const topLeft = this.toScreen(s.x, s.y + s.d);
      const w = s.w * this.scale;
      const h = s.d * this.scale;

      ctx.fillStyle = here > 0 ? COLOURS.standBusy : (s.colour || COLOURS.stand);
      ctx.fillRect(topLeft.x, topLeft.y, w, h);

      ctx.strokeStyle = this.selected === s.id ? '#ffffff' : COLOURS.standEdge;
      ctx.lineWidth = this.selected === s.id ? 3 : 1;
      ctx.strokeRect(topLeft.x, topLeft.y, w, h);

      // Only label stands with room for the text; a 3 m shell at hall scale has none.
      if (w > 46 && h > 22) {
        ctx.fillStyle = COLOURS.label;
        ctx.font = '600 13px ui-monospace, Consolas, monospace';
        ctx.fillText(s.stand, topLeft.x + w / 2, topLeft.y + h / 2 - (w > 92 ? 8 : 0));

        if (w > 92) {
          ctx.fillStyle = COLOURS.text;
          ctx.font = '12px system-ui, sans-serif';
          const name = s.name.length > Math.floor(w / 7) ? s.name.slice(0, Math.floor(w / 7) - 1) + '…' : s.name;
          ctx.fillText(name, topLeft.x + w / 2, topLeft.y + h / 2 + 9);
        }
      }

      if (here > 0) {
        ctx.fillStyle = COLOURS.badgeOnStand;
        ctx.beginPath();
        ctx.arc(topLeft.x + w - 9, topLeft.y + 9, 8, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#fff';
        ctx.font = '600 11px system-ui, sans-serif';
        ctx.fillText(String(here), topLeft.x + w - 9, topLeft.y + 10);
      }
    }
  };

  ExbFloor.prototype.drawAntennas = function () {
    const ctx = this.ctx;
    ctx.fillStyle = COLOURS.antenna;
    for (const a of this.antennas) {
      const p = this.toScreen(a.x, a.y);
      ctx.beginPath();
      ctx.arc(p.x, p.y, a.kind === 1 ? 2.5 : 3.5, 0, Math.PI * 2);
      ctx.fill();
    }
  };

  ExbFloor.prototype.drawBadges = function () {
    const ctx = this.ctx;
    for (const b of this.badges) {
      const p = this.toScreen(b.x, b.y);

      // The uncertainty halo is drawn to scale, so a fix the system is unsure
      // about visibly looks unsure instead of being a confident dot.
      if (b.u > 0.5) {
        ctx.fillStyle = 'rgba(74, 168, 255, .10)';
        ctx.beginPath();
        ctx.arc(p.x, p.y, b.u * this.scale, 0, Math.PI * 2);
        ctx.fill();
      }

      ctx.fillStyle = b.s === 1 ? COLOURS.badgeStale : (b.k ? COLOURS.badgeOnStand : COLOURS.badge);
      ctx.beginPath();
      ctx.arc(p.x, p.y, 4.5, 0, Math.PI * 2);
      ctx.fill();
    }
  };

  window.ExbFloor = ExbFloor;
})();
