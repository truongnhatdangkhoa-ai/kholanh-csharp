const E = require('/home/user/kholanh-anan-pc/app/html/js/c3d-engine.js');
const C = require('/home/user/kholanh-anan-pc/app/html/js/c3d-cbm.js');
const P = (id) => E.PRESETS.find((p) => p.id === id);
const sk = (o) => Object.assign({ L: 520, W: 280, H: 190, kg: 10, allowRotate: true, allowStand: true, allowLay: false }, o);
const cases = [
  { name: 'demo4', cont: P('rf40hc'), skus: [sk({ id: 'a', code: 'VRHLCK', size: '31/40', date: '15/09/2026', qty: 420 }), sk({ id: 'b', code: 'VRHLCK', size: '31/40', date: '22/09/2026', qty: 360 }), sk({ id: 'c', code: 'VRHLCK', size: '41/50', date: '20/09/2026', qty: 480 }), sk({ id: 'd', code: 'VRPDTO', size: '51/60', date: '18/09/2026', qty: 300, L: 600, W: 400, H: 220, kg: 12, sole: true })], opts: {} },
  { name: 'one', cont: P('rf20'), skus: [sk({ id: 'a', code: 'A', qty: 300 })], opts: {} },
  { name: 'few-spread', cont: P('rf40'), skus: [sk({ id: 'a', code: 'A', qty: 200 }), sk({ id: 'b', code: 'B', qty: 150, L: 400, W: 300, H: 250 })], opts: { spread: true } },
  { name: 'overflow', cont: P('rf20'), skus: [sk({ id: 'a', code: 'A', qty: 900 }), sk({ id: 'b', code: 'B', qty: 900, allowLay: true })], opts: {} },
  { name: 'tpl', cont: P('rf40hc'), skus: [sk({ id: 'a', code: 'A', qty: 500, rowPlan: '6 nằm 2 đứng', allowLay: true }), sk({ id: 'b', code: 'B', qty: 260, rowSet: { 0: 'nam', 2: 'dung' }, allowLay: true })], opts: { mergeTail: false } },
  { name: 'heavy', cont: Object.assign({}, P('rf40hc'), { maxPayload: 5000 }), skus: [sk({ id: 'a', code: 'A', qty: 400, kg: 20 }), sk({ id: 'b', code: 'B', qty: 300, kg: 15 })], opts: { sampleOn: false } },
  { name: 'orient', cont: P('rf40hc'), skus: [sk({ id: 'a', code: 'X1', qty: 350, orient: 'WLH', maxLayers: 8 }), sk({ id: 'b', code: 'X2', qty: 250, L: 390, W: 290, H: 160, allowLay: true })], opts: { stackTop: false, dateDir: 'desc' } },
  { name: 'manual', cont: P('rf40hc'), skus: [sk({ id: 'b', code: 'Z', qty: 100 }), sk({ id: 'a', code: 'A', qty: 120, L: 450, W: 350, H: 200 }), sk({ id: 'c', code: 'M', qty: 90, L: 300, W: 300, H: 300 })], opts: { sortMode: 'manual', heightRule: false } },
];
const out = cases.map((cs) => {
  const p = E.solve({ container: cs.cont, skus: cs.skus }, cs.opts);
  return { name: cs.name, cont: cs.cont, skus: cs.skus, opts: cs.opts,
    res: { loaded: p.totals.loaded, left: p.totals.left, rows: p.totals.rows, sample: p.totals.sample, maxTier: p.maxTier, ok: p.verify.ok, nviol: p.verify.violations.length,
      usedLength: Math.round(p.totals.usedLength), errors: p.errors, warnings: p.warnings, layoutNote: p.layoutNote || '', spreadNote: p.spreadNote || '',
      groups: p.groups.map((g) => ({ id: g.sku.id, loaded: g.loaded, sample: g.sample, reasons: g.reasons, patterns: g.patterns.map((x) => x.text) })),
      compare: p.compare ? { loaded: p.compare.loaded, delta: p.compare.delta } : null,
      hints: (p.hints || []).map((h) => ({ id: h.id, gain: h.gain, loaded: h.loaded, text: h.text })),
      boxes: p.boxes.map((b) => [b.x, b.y, b.z, b.dx, b.dy, b.dz, b.tier, b.group, b.row, b.sample ? 1 : 0].map((v) => Math.round(v * 1000) / 1000)) } };
});
const pal = { L: 1200, W: 1000, base: 150, maxH: 1800, maxKg: 1000 };
const cbm = C.summarize(cases[0].skus.map((k) => E.normSku(k, 0)), E.normContainer(cases[0].cont), pal);
require('fs').writeFileSync('c3d-golden.json', JSON.stringify({ cases: out, cbm: { totalPallets: cbm.totalPallets, floor: cbm.floor.floor, tiers: cbm.floor.tiers, per: cbm.items.map((x) => x.plan.perPallet), pack: C.pack2D(1000, 1200, 520, 280) } }));
out.forEach((c) => console.log(c.name, c.res.loaded, c.res.left, c.res.rows, c.res.boxes.length, c.res.ok, c.res.hints.length, c.res.spreadNote.slice(0,40)));
