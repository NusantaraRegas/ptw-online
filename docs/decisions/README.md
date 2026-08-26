# Register keputusan OPN

Dokumen di folder ini mengubah isu terbuka BRD menjadi decision record yang dapat ditinjau dan disahkan. Status `DRAFT` atau `PROPOSED` bukan keputusan dan tidak boleh dipakai untuk mengunci aturan produksi.

| ID | Topik | Owner | Status | Memblokir |
| --- | --- | --- | --- | --- |
| [OPN-001](OPN-001.md) | Lokasi/fasilitas dan pemilik area | Operasi | DRAFT | master lokasi, authority assignment |
| [OPN-002](OPN-002.md) | Risiko, review, approval, delegasi, SoD | HSE/Operasi | DRAFT | workflow review/approval |
| [OPN-003](OPN-003.md) | Checklist dan mapping formulir | HSE | DRAFT | ruleset dan form final |
| [OPN-004](OPN-004.md) | Gas test dan monitoring | HSE | DRAFT | field readiness dan retest |
| [OPN-005](OPN-005.md) | Urutan review dan SLA | PO/HSE/Operasi | DRAFT | task routing dan reminder |
| [OPN-006](OPN-006.md) | Acknowledgement kontraktor | Legal/HSE/Operasi | DRAFT | contractor journey |
| [OPN-007](OPN-007.md) | SSO dan E-SIMI | TI | DRAFT | production identity/integration |
| [OPN-008](OPN-008.md) | Retensi, e-sign, klasifikasi, RPO/RTO | Legal/Records/TI | DRAFT | records/security/DR |
| [OPN-009](OPN-009.md) | Topologi produksi dan HA | TI | DRAFT | production deployment |

## Aturan pengesahan

1. Owner melengkapi pilihan, alasan, tanggal efektif, bukti, dan approver.
2. Dampak keselamatan, hukum, keamanan, data, serta operasional ditinjau oleh fungsi terkait.
3. Status berubah menjadi `ACCEPTED` hanya setelah approver dan tanggal keputusan tercatat.
4. Implementasi mengacu pada versi decision record yang accepted; perubahan berikutnya membuat superseding record.

Gunakan [template](decision-record-template.md) untuk keputusan tambahan.
