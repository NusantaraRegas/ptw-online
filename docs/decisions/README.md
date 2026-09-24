# Register keputusan OPN

Dokumen di folder ini mengubah isu terbuka BRD menjadi decision record yang dapat ditinjau dan disahkan. Status `DRAFT` atau `PROPOSED` bukan keputusan dan tidak boleh dipakai untuk mengunci aturan produksi.

Keputusan flow yang telah dikonfirmasi pengguna sampai 22 September 2026 dicatat sebagai DEC-101
sampai DEC-126 pada [BRD v1.8](../BRD-NR-PTW-Online-v1.8-ID.md) Bagian 2.2. Bagian "baseline
Development" pada record OPN merujuk keputusan tersebut dan tidak mengubah status record.

| ID | Topik | Owner | Status | Memblokir |
| --- | --- | --- | --- | --- |
| [OPN-001](OPN-001.md) | Lokasi/fasilitas dan pemilik area | Operasi | DRAFT | master lokasi, authority assignment |
| [OPN-002](OPN-002.md) | Risiko, review, approval, delegasi, SoD | HSE/Operasi | DRAFT | assignment SO/Officer dan Manager produksi, pengganti resmi |
| [OPN-003](OPN-003.md) | Checklist dan mapping formulir | HSE | DRAFT | master katalog effective-dated, item khusus |
| [OPN-004](OPN-004.md) | Gas test dan monitoring | HSE | DRAFT | field readiness dan retest |
| [OPN-005](OPN-005.md) | Urutan review dan SLA | PO/HSE/Operasi | DRAFT | SLA, reminder, eskalasi |
| [OPN-006](OPN-006.md) | Acknowledgement dan onboarding kontraktor | Legal/HSE/Operasi | DRAFT | contractor journey, scope perusahaan |
| [OPN-007](OPN-007.md) | SSO dan E-SIMI | TI | ACCEPTED (identitas produksi, 24 Sep 2026); E-SIMI DRAFT | kontrak E-SIMI (backlog) |
| [OPN-008](OPN-008.md) | Retensi, e-sign, klasifikasi, RPO/RTO | Legal/Records/TI | DRAFT | records/security/DR, status hukum bukti persetujuan |
| [OPN-009](OPN-009.md) | Topologi produksi dan HA | TI | ACCEPTED (24 Sep 2026) | observability/on-call belum diputuskan |
| [PROD-UPLOAD-SCAN](PROD-UPLOAD-SCAN.md) | Unggahan tanpa malware scanner | TI/HSE | ACCEPTED risiko (24 Sep 2026) | scanner resmi (backlog) |
| [PTW-RENEWAL](PTW-RENEWAL.md) | Renewal berbasis review pemilik wilayah | PO/Operasi | IMPLEMENTED DEVELOPMENT BASELINE | pengesahan produksi mengikuti OPN-002/010 |

OPN-010 (definisi tujuh hari dan batas renewal), OPN-011 (materi kampanye), dan OPN-012 (QR dan
kualitas scan) tercatat pada BRD v1.8 Bagian 13 dan belum memiliki record terpisah; ketiganya adalah
backlog, bukan blocker rilis awal.

## Aturan pengesahan

1. Owner melengkapi pilihan, alasan, tanggal efektif, bukti, dan approver.
2. Dampak keselamatan, hukum, keamanan, data, serta operasional ditinjau oleh fungsi terkait.
3. Status berubah menjadi `ACCEPTED` hanya setelah approver dan tanggal keputusan tercatat.
4. Implementasi mengacu pada versi decision record yang accepted; perubahan berikutnya membuat superseding record.

Gunakan [template](decision-record-template.md) untuk keputusan tambahan.

Untuk memfasilitasi keputusan awal yang memblokir authorization dan workflow, gunakan
[paket workshop OPN-001/002](OPN-001-002-workshop.md). Paket tersebut tidak menggantikan pengesahan
owner dan approver.
