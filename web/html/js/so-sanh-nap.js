/* PC 3.04 — SO SÁNH 2 LẦN NẠP «TỒN KHO AN AN» (thuần, test được trong Node).
   snap: [{ ma, ten, dt, size, ton, kg }] đã gộp theo Mã hàng. compare(prev, next) → hàng chênh theo Mã, tổng theo nhóm đặc tính / Size. */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory(); else root.SoSanhNap = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';
  const S = (v) => String(v == null ? '' : v).trim();
  const num = (v) => { const n = Number(v); return isFinite(n) ? n : 0; };
  const EPS = 0.0005;
  // gộp các dòng thô thành snapshot theo Mã hàng: items [{ma,ten,dt,size,ton,kg}]
  function build(items) {
    const m = new Map();
    (items || []).forEach((it) => {
      const ma = S(it.ma); if (!ma) return;
      if (!m.has(ma)) m.set(ma, { ma, ten: S(it.ten), dt: S(it.dt), size: S(it.size), ton: 0, kg: 0 });
      const e = m.get(ma); e.ton += num(it.ton); e.kg += num(it.kg);
      if (!e.ten) e.ten = S(it.ten); if (!e.dt) e.dt = S(it.dt); if (!e.size) e.size = S(it.size);
    });
    return Array.from(m.values());
  }
  function compare(prev, next) {
    const P = new Map(build(prev).map((x) => [x.ma, x])), N = new Map(build(next).map((x) => [x.ma, x]));
    const keys = new Set([...P.keys(), ...N.keys()]), rows = [];
    keys.forEach((ma) => {
      const a = P.get(ma), b = N.get(ma), ta = a ? a.ton : 0, tb = b ? b.ton : 0, ka = a ? a.kg : 0, kb = b ? b.kg : 0;
      const ref = b || a;
      let st;
      if (!a || ta <= EPS) st = (tb > EPS) ? 'moi' : 'khong';
      else if (!b || tb <= EPS) st = 'het';
      else if (Math.abs(tb - ta) <= EPS) st = 'doi0';
      else st = tb > ta ? 'tang' : 'giam';
      if (st === 'khong') return;
      rows.push({ ma, ten: ref.ten, dt: ref.dt || '(chưa có đặc tính)', size: ref.size || '(không Size)', before: ta, after: tb, diff: tb - ta, kgBefore: ka, kgAfter: kb, kgDiff: kb - ka, status: st });
    });
    const grp = (field) => {
      const g = new Map();
      rows.forEach((r) => { const k = r[field]; if (!g.has(k)) g.set(k, { name: k, n: 0, before: 0, after: 0, kgBefore: 0, kgAfter: 0 }); const e = g.get(k); e.n++; e.before += r.before; e.after += r.after; e.kgBefore += r.kgBefore; e.kgAfter += r.kgAfter; });
      return Array.from(g.values()).map((e) => Object.assign(e, { diff: e.after - e.before, kgDiff: e.kgAfter - e.kgBefore })).sort((x, y) => Math.abs(y.kgDiff) - Math.abs(x.kgDiff));
    };
    const sum = { moi: 0, het: 0, tang: 0, giam: 0, doi0: 0, kgBefore: 0, kgAfter: 0 };
    rows.forEach((r) => { sum[r.status]++; sum.kgBefore += r.kgBefore; sum.kgAfter += r.kgAfter; });
    sum.kgDiff = sum.kgAfter - sum.kgBefore;
    const ord = { moi: 0, het: 1, giam: 2, tang: 3, doi0: 4 };
    rows.sort((x, y) => ord[x.status] - ord[y.status] || Math.abs(y.kgDiff) - Math.abs(x.kgDiff));
    return { rows, sum, byDt: grp('dt'), bySize: grp('size') };
  }
  return { build, compare };
});
