# PTW Renewal: baseline Development

- Status: IMPLEMENTED DEVELOPMENT BASELINE
- Sumber arahan: permintaan pengguna 4 September 2026
- Pengesahan produksi: mengikuti OPN-002 dan konfigurasi policy effective-dated

## Keputusan implementasi

- Sponsor pemilik dapat mengajukan renewal ketika PTW asal sedang Diterbitkan, memiliki active work
  period, dan belum melewati akhir masa berlaku.
- Renewal selalu membuat permit baru. Draft baru menyimpan hubungan ke PTW asal, sedangkan nomor PTW
  baru dialokasikan pada saat submit.
- Snapshot pekerjaan disalin, lalu dapat diperbarui melalui aturan edit draft. Sponsor dan lokasi
  tidak boleh menyimpang dari PTW asal, dan awal periode baru tidak boleh overlap dengan periode asal.
- Validity setiap renewal tetap maksimum tujuh hari.
- Renewal menjalani validasi HSSE, approval PIC pemilik area, dan guard penerbitan normal.
- Penerbitan renewal ditolak selama PTW asal masih aktif atau belum ditutup. Status `APPROVED` pada
  renewal tetap tidak memberikan hak kerja.
- Satu PTW asal hanya memiliki satu successor renewal pada baseline ini.
- Attachment tidak disalin otomatis; Sponsor mengunggah ulang PDF yang masih relevan pada draft
  renewal.

## Invariant teknis

- `POST /api/v1/permits/{id}/renewals` memerlukan `If-Match` dan `Idempotency-Key`.
- Update version PTW asal, insert draft renewal, version snapshots, audit events, outbox messages,
  authorization evidence, dan idempotency receipt berada dalam satu transaksi SQL.
- Relasi `RenewedFromPermitId` memiliki foreign key dan unique filtered index.
- UI menggunakan istilah Diterbitkan; domain internal tetap menggunakan `OPEN`.

## Keputusan lanjutan yang masih terbuka

- Apakah Sponsor boleh membuat successor pengganti setelah renewal pertama ditolak atau dibatalkan.
- Mekanisme expiry otomatis dan handover tepat waktu antara PTW asal dan renewal.
- SLA, notifikasi, lead time pengajuan, serta mapping action/assignment production.
