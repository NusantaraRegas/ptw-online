# Register keputusan OPN

Dokumen di folder ini mengubah isu terbuka BRD menjadi decision record yang dapat ditinjau dan disahkan. Status `DRAFT` atau `PROPOSED` bukan keputusan dan tidak boleh dipakai untuk mengunci aturan produksi.

Keputusan flow yang telah dikonfirmasi pengguna dicatat sebagai DEC-101 sampai DEC-128 pada
[BRD v1.8](../BRD-NR-PTW-Online-v1.8-ID.md) Bagian 2.2. Pada 25 September 2026 stakeholder mengesahkan
keadaan sistem dan alur saat ini sebagai baseline operasional
([PTW-WORKFLOW-BASELINE](PTW-WORKFLOW-BASELINE.md) Amendemen 2); status record di bawah mengikuti
disposisi pada amendemen tersebut. Bagian yang masih `TBD` pada form keputusan adalah lembar kerja
untuk perluasan di masa depan, bukan prasyarat baseline.

| ID | Topik | Owner | Status | Memblokir |
| --- | --- | --- | --- | --- |
| [OPN-001](OPN-001.md) | Lokasi/fasilitas dan pemilik area | Operasi | ACCEPTED scope rilis awal (25 Sep 2026) | tidak ada; HO/FSRU dan sublokasi backlog |
| [OPN-002](OPN-002.md) | Risiko, review, approval, delegasi, SoD | HSE/Operasi | ACCEPTED alur dan SoD baseline (25 Sep 2026) | tidak ada; pejabat pengganti/delegasi fail-closed (backlog) |
| [OPN-003](OPN-003.md) | Checklist dan mapping formulir | HSE | ACCEPTED (25 Sep 2026) | tidak ada; master katalog effective-dated dan item khusus backlog |
| [OPN-004](OPN-004.md) | Gas test dan monitoring | HSE | ACCEPTED hybrid/hardcopy (25 Sep 2026) | tidak ada |
| [OPN-005](OPN-005.md) | Urutan review dan SLA | PO/HSE/Operasi | ACCEPTED urutan review (25 Sep 2026); SLA backlog | tidak ada; SLA/reminder/eskalasi backlog |
| [OPN-006](OPN-006.md) | Acknowledgement dan onboarding kontraktor | Legal/HSE/Operasi | ACCEPTED baseline (25 Sep 2026) | tidak ada; scope perusahaan backlog |
| [OPN-007](OPN-007.md) | Identitas produksi dan E-SIMI | TI | ACCEPTED (identitas 24 Sep 2026; E-SIMI berbasis bukti 25 Sep 2026) | tidak ada; integrasi API E-SIMI backlog |
| [OPN-008](OPN-008.md) | Retensi, e-sign, klasifikasi, RPO/RTO | Legal/Records/TI | ACCEPTED sebagian (e-sign dan penanganan data, 25 Sep 2026) | tidak ada; periode retensi, klasifikasi, angka RPO/RTO backlog |
| [OPN-009](OPN-009.md) | Topologi produksi dan HA | TI | ACCEPTED (24 Sep 2026) | observability/on-call belum diputuskan |
| [PROD-UPLOAD-SCAN](PROD-UPLOAD-SCAN.md) | Unggahan tanpa malware scanner | TI/HSE | ACCEPTED risiko (24 Sep 2026) | scanner resmi (backlog) |
| [PTW-RENEWAL](PTW-RENEWAL.md) | Renewal berbasis review pemilik wilayah | PO/Operasi | ACCEPTED melalui PTW-WORKFLOW-BASELINE (25 Sep 2026) | batas renewal berantai (OPN-010, backlog) |
| [PTW-WORKFLOW-BASELINE](PTW-WORKFLOW-BASELINE.md) | Pengesahan alur sistem saat ini sebagai baseline operasional; Amendemen 1: scope baca lintas lokasi pemilik wilayah ORF | PO | ACCEPTED (25 Sep 2026, termasuk Amendemen 1) | tidak ada; OPN yang masih DRAFT adalah backlog |

OPN-010 (tujuh hari, tanpa batas renewal berantai) dan OPN-011 (materi kampanye tidak termasuk rilis)
disahkan pada PTW-WORKFLOW-BASELINE Amendemen 2; OPN-012 disahkan sebagian (keterbacaan hardcopy
dikonfirmasi reviewer) dengan QR/reference sebagai backlog. Ketiganya tidak memiliki record terpisah.

## Aturan pengesahan

1. Owner melengkapi pilihan, alasan, tanggal efektif, bukti, dan approver.
2. Dampak keselamatan, hukum, keamanan, data, serta operasional ditinjau oleh fungsi terkait.
3. Status berubah menjadi `ACCEPTED` hanya setelah approver dan tanggal keputusan tercatat.
4. Implementasi mengacu pada versi decision record yang accepted; perubahan berikutnya membuat superseding record.

Gunakan [template](decision-record-template.md) untuk keputusan tambahan.

Untuk memfasilitasi keputusan awal yang memblokir authorization dan workflow, gunakan
[paket workshop OPN-001/002](OPN-001-002-workshop.md). Paket tersebut tidak menggantikan pengesahan
owner dan approver.
