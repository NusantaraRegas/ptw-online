import { Component } from '@angular/core';

const template = `<div class="page-title"><div><p class="eyebrow">Fondasi Increment 1</p><h1>{{ title }}</h1><p class="subtitle">{{ description }}</p></div></div><section class="card"><span>{{ icon }}</span><h2>Modul siap dikembangkan</h2><p>Shell navigasi dan boundary modul sudah tersedia. Journey final menunggu decision record OPN-001–009 agar tidak mengunci kebijakan keselamatan yang belum disahkan.</p></section>`;
const styles = [
  `section{padding:60px;max-width:720px;text-align:center}section>span{width:52px;height:52px;display:grid;place-items:center;margin:0 auto 15px;border-radius:12px;color:#206879;background:#e3f0f3;font-size:24px}section h2{font-size:16px;color:#294650}section p{max-width:520px;margin:0 auto;color:#819298;font-size:11px;line-height:1.7}`,
];

@Component({ selector: 'app-tasks', template, styles })
export class TasksPage {
  title = 'Tugas Saya';
  description = 'Review, approval, dan field action sesuai otorisasi.';
  icon = '✓';
}
@Component({ selector: 'app-operations', template, styles })
export class OperationsPage {
  title = 'Papan Operasi';
  description = 'Visibilitas OPEN, SUSPENDED, expiring, dan awaiting handback.';
  icon = '◉';
}
@Component({ selector: 'app-reports', template, styles })
export class ReportsPage {
  title = 'Pencarian & Laporan';
  description = 'Pencarian scoped, filter, ekspor, dan printable permit.';
  icon = '⌕';
}
