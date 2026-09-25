# PTW Renewal: baseline Development

- Status: ACCEPTED melalui PTW-WORKFLOW-BASELINE (25 September 2026, dikukuhkan stakeholder pada Amendemen 2)
- Sumber arahan: permintaan pengguna 4 September 2026; direvisi mengikuti verifikasi sistem berjalan 22 September 2026 (BRD v1.8 DEC-121)
- Menggantikan: baseline renewal 4 September 2026 (draft penerus dibuat langsung oleh Sponsor)
- Pengesahan produksi: OPN-002 dan OPN-010 disahkan 25 September 2026 (tujuh hari dari `validFrom` sampai `validUntil`, tanpa batas renewal berantai pada baseline)

## Keputusan implementasi

- Renewal tidak memperpanjang, mengubah status, atau mengubah masa berlaku PTW asal.
- Sponsor pemilik mengajukan **permintaan renewal** ketika PTW asal berstatus `ISSUED` atau `EXPIRED`,
  belum memasuki proses penutupan, dan belum memiliki PTW penerus. Permintaan wajib memuat:
  - paket cetak resmi `READY` milik PTW asal;
  - satu atau lebih hardcopy hasil verifikasi lapangan (`SIGNED_FIELD_COPY`) yang `CLEAN`, aktif,
    tidak superseded, bermetadata nomor/revisi/tanggal, dan cocok dengan exact PrintPackage serta
    PermitVersion tersebut;
  - periode baru maksimum tujuh hari yang dimulai pada atau setelah akhir masa berlaku PTW asal;
  - pernyataan kelanjutan pekerjaan.
- Permintaan menaikkan versi PTW asal dan membentuk tepat satu task `AREA_RENEWAL_REVIEW` untuk
  Manager pemilik wilayah (`AreaOwnerManager`) dengan scope lokasi PTW. Selama review `PENDING`,
  lampiran PTW asal terkunci dan closure tidak dapat diajukan.
- Manager pemilik wilayah memutuskan salah satu:
  - **Minta hardcopy ulang**: status review `RevisionRequired`; Sponsor wajib mengajukan lagi dengan
    evidence yang berbeda dari sebelumnya;
  - **Tolak**: status review `Rejected`; PTW asal tetap pada statusnya;
  - **Setujui** setelah mengonfirmasi verifikasi lapangan dan keterbacaan hardcopy.
- Approval membuat PTW penerus berstatus `DRAFT` **secara atomik** dalam transaksi yang sama dengan
  keputusan pada PTW asal. Penerus menyalin data perencanaan Bagian 1–4 dari PTW asal dengan periode
  yang disetujui, memiliki Sponsor dan lokasi yang sama, tidak membawa lampiran, Bagian 5, Bagian 7,
  approval, atau isian lapangan, dan terhubung melalui `RenewedFromPermitId`.
- Nomor PTW penerus dialokasikan saat submit. Penerus menjalani submit, validasi PIC HSE, review
  Bagian 7 SO/Officer, dan approve-and-issue Manager secara normal, lalu menghasilkan paket cetak baru.
- Satu PTW asal hanya memiliki satu penerus pada baseline ini.
- Renewal dan closure saling eksklusif: permintaan renewal ditolak setelah closure dimulai, dan
  closure ditolak selama review renewal aktif atau setelah penerus dibuat.

## Invariant teknis

- `POST /api/v1/permits/{id}/renew` (Sponsor) serta `POST /api/v1/renewal-tasks/{taskId}/request-evidence`,
  `/reject`, dan `/approve` (Manager) memerlukan `If-Match` dan `Idempotency-Key`.
- Evidence permintaan (`PermitRenewalRequestEvidence`: paket, lampiran, periode, pernyataan, revisi,
  status, keputusan) disimpan pada workflow evidence PTW asal.
- Approval menulis PTW asal, PTW penerus, version snapshot, audit event, outbox message, dan
  idempotency receipt dalam satu transaksi SQL; server memverifikasi ulang evidence sebelum commit.
- `RenewedFromPermitId` memiliki foreign key dan unique filtered index.
- UI memakai istilah Diterbitkan untuk `ISSUED`; tidak ada status `OPEN`.

## Keputusan lanjutan yang masih terbuka

- Apakah Sponsor boleh mengajukan renewal lagi setelah permintaan ditolak, dan batas renewal berantai (OPN-010).
- Definisi tujuh hari (kalender/kerja) dan awal masa berlaku (OPN-010).
- Expiry otomatis oleh Worker dan handover tepat waktu antara PTW asal dan penerus.
- SLA review renewal, notifikasi eksternal, dan mapping assignment Manager produksi (OPN-002/005).
