# PTW-WORKFLOW-BASELINE: Pengesahan alur sistem PTW Online sebagai baseline operasional

- Status: ACCEPTED
- Owner: Product Owner (pengguna)
- Approver: Product Owner (25 September 2026); dikukuhkan Product Owner bersama stakeholder Operasi, HSSE, General Affair, Transport & Operasi FSRU, dan TI (Amendemen 2, 25 September 2026)
- Tanggal keputusan: 25 September 2026
- Berlaku mulai: 25 September 2026
- Menggantikan: status "Development/UAT" pada alur, checklist, dan paket cetak yang tercatat di
  `docs/implementation-status.md` sebelum tanggal ini

## Konteks

Sejak baseline v1.7 (September 2026) alur sistem dibangun bertahap dan setiap perilaku
dikonfirmasi pengguna melalui DEC-117 sampai DEC-127 pada BRD v1.8. Pada 25 September 2026 pengguna
menyatakan bahwa sistem yang berjalan saat ini beserta alurnya telah disetujui. Record ini
mencatat pengesahan tersebut agar dokumentasi tidak lagi menggambarkan alur sebagai menunggu UAT,
sign-off HSSE, atau pengesahan lanjutan.

## Keputusan

Alur berikut, sebagaimana diimplementasikan pada commit yang memuat record ini, disahkan sebagai
baseline operasional NR PTW Online:

1. Lifecycle sebelas status dengan explicit command; `CLOSED`, `REJECTED`, `CANCELLED`, dan
   `EXPIRED` terminal; validity maksimum tujuh hari; tidak ada endpoint `setStatus`.
2. Submit Sponsor membuat tepat satu task `HSE_VALIDATION`; PIC HSE adalah validator tunggal dan
   menetapkan Bagian 5; Sponsor tidak memvalidasi PTW miliknya sendiri.
3. Setelah validasi HSE, satu task `AREA_OPERATION_REVIEW` untuk pool SO/Officer pemilik wilayah
   menetapkan Bagian 7; setelah itu satu task `AREA_APPROVE_AND_ISSUE` untuk Manager pemilik
   wilayah menjalankan approval dan penerbitan secara atomik dengan separation of duty terhadap
   Sponsor, validator HSE, dan reviewer.
4. Permintaan revisi membatalkan task pending dan membuat notifikasi `SPONSOR_REVISION`; submit
   ulang menaikkan `PermitVersion` tepat satu.
5. Suspend berlaku seketika dan resolve kembali ke `ISSUED`.
6. Closure memakai signed field copy exact package/version dan satu task pool
   `AREA_CLOSE_VERIFICATION` (Manager atau SO/Officer) dengan verifikasi Bagian 10 terstruktur.
7. Renewal berbasis review pemilik wilayah sesuai `PTW-RENEWAL.md`; draft penerus lahir atomik
   hanya saat approval.
8. Dokumen dasar JSA, Prosedur Pekerjaan, ID, BPJS TK, FTW, dan E-SIMI wajib berlampiran sebelum
   submit; JSA dan Prosedur Pekerjaan otomatis menjadi item wajib Bagian 4 (DEC-127).
9. Katalog terkontrol klasifikasi header, Bagian 1, 4, 5, dan 7 serta paket cetak dua halaman A3
   yang setia pada FM-001/002/003-B-002-NR-B220; Bagian 6 dan 8-10 tetap hardcopy.
10. Setiap keputusan validator/approver wajib berkomentar; nama aktor berasal dari profil akun
    server.
11. Lokasi aktif ORF, Site Office, dan Water-Based Activity dengan pemilik wilayah sesuai
    klarifikasi 16 September 2026, dirilis pada konfigurasi Development dan Production.
12. Identitas, topologi, dan kebijakan unggahan produksi mengikuti OPN-007 (bagian 1), OPN-009,
    dan PROD-UPLOAD-SCAN.

Konfigurasi dasar `appsettings.json` tetap fail-closed; pengesahan ini diwujudkan melalui
`appsettings.Development.json`, `appsettings.Production.json`, dan `IssuancePolicy`
`PTW-FLOW-CHECKLIST-2026-09-24`.

## Yang tidak diputuskan oleh record ini

Lihat Amendemen 2 untuk disposisi setiap OPN setelah pengesahan stakeholder. Yang tetap terbuka
sebagai backlog: assignment acting/delegasi (fail-closed), SLA dan penerima eskalasi, periode retensi
dan angka RPO/RTO, klasifikasi data, observability/on-call, integrasi API E-SIMI, malware scanner,
scope perusahaan Kontraktor, dan QR code. Ketiadaan keputusan tersebut tidak menghalangi pemakaian
alur yang disahkan di atas, tetapi jalur yang memerlukannya tetap fail-closed sampai ada record baru.

## Konsekuensi dan kontrol

- Perubahan terhadap alur di atas memerlukan superseding record; tidak boleh diubah diam-diam.
- `docs/implementation-status.md`, `README.md`, `AGENTS.md`, dan `CLAUDE.md` merujuk record ini
  sebagai status alur, bukan sebagai "Development/UAT".
- Invariant keselamatan (Gate A `CLAUDE.md`) dan test regresi yang ada adalah bukti teknis baseline.

## Amendemen 1 (25 September 2026): scope baca lintas lokasi untuk pemilik wilayah ORF

- Status: ACCEPTED, 25 September 2026, Product Owner.
- Keputusan: SO/Officer dan Manager Pemilik Wilayah yang memiliki scope lokasi `ORF` memperoleh
  scope **baca** lintas lokasi, setara Administrator, untuk daftar dan detail PTW, lampiran, paket
  cetak, history, dan Papan Operasi termasuk seluruh status non-draft. Implementasi:
  `PermitMonitoringAccess` di `Ptw.Application`.
- Batas: perluasan ini hanya untuk pemantauan. Task pool, command, dan keputusan (review Bagian 7,
  approve-and-issue, closure, renewal, suspend) tetap memakai assignment dan scope lokasi asli
  aktor. Tidak ada role atau lokasi lain yang memperoleh perluasan ini.
- Hubungan dengan OPN-001/002: amendemen ini menutup pertanyaan otoritas baca lintas lokasi untuk
  pemilik wilayah ORF pada baseline saat ini; master lokasi effective-dated dan matriks otoritas
  command tetap backlog OPN-001/002. Perubahan berikutnya memerlukan superseding record.

## Amendemen 2 (25 September 2026): pengesahan stakeholder dan disposisi OPN

- Status: ACCEPTED, 25 September 2026, Product Owner bersama stakeholder Operasi, HSSE, General Affair, Transport & Operasi FSRU, dan TI.
- Keputusan: keadaan sistem dan alur saat ini (termasuk Amendemen 1, hardening sesi/kredensial,
  seed master lokasi dan assignment, serta Papan Operasi) disahkan stakeholder sebagai baseline
  operasional. Pengesahan ini memenuhi butir 1-4 dan 6 kriteria sign-off BRD v1.8 Bagian 15.
- Disposisi decision record berdasarkan keadaan sistem yang disahkan:

| Record | Disposisi | Yang ditutup oleh keadaan sistem | Yang tetap backlog |
| --- | --- | --- | --- |
| OPN-001 | ACCEPTED (scope rilis awal) | Tiga area root `ORF`, `SITE_OFFICE`, `WATER_BASED` dengan departemen pemilik; master lokasi ter-seed dan disetujui (`SeedLocationMasterAndUserSignatures`); tanpa hierarchy/descendant; pekerjaan lintas area dipecah menjadi PTW terpisah; perubahan master lewat maker-checker Administrator | Aktivasi `HO`/`FSRU`, struktur sublokasi/equipment, frekuensi rekonsiliasi sumber data |
| OPN-002 | ACCEPTED (alur dan SoD baseline) | Enam role (`Administrator`, `Sponsor`, `HSEValidator`, `AreaOwnerSeniorOfficer`, `AreaOwnerManager`, `Auditor`) dengan action code server-side; route tunggal per lokasi tanpa matriks risiko (klasifikasi header hanya checklist formulir); pasangan SoD sebagaimana diimplementasikan; fail-safe DENY saat assignment tidak efektif; assignment effective-dated per orang dikelola Administrator | Approval sebagai pejabat pengganti (`ActingAssignmentId` tetap ditolak), kompetensi formal, pasangan SoD tambahan untuk review renewal/close |
| OPN-003 | ACCEPTED | Katalog statis di `Ptw.Domain` (header, Bagian 1, 4, 5, 7) dan `PermitMandatoryDocumentCatalog` adalah master checklist baseline; versinya terikat `IssuancePolicy.RuleVersion` dan `RendererVersion` | Master katalog effective-dated di database, item khusus pekerjaan, rules engine deklaratif |
| OPN-004 | ACCEPTED (hybrid) | Gas test, monitoring, dan retest sepenuhnya pada hardcopy Bagian 6/8 sesuai SOP HSSE; sistem tidak menyimpan ambang, pembacaan, atau umur hasil dan tidak mengaitkannya dengan `ISSUED` | Digitalisasi gas test (bila diminta kelak) |
| OPN-005 | ACCEPTED (urutan review) | Urutan dan entry/exit criteria validasi HSE, review Bagian 7, approve-and-issue, revisi, suspend, closure sebagaimana tercantum pada record | SLA per task, kalender kerja, reminder, penerima eskalasi |
| OPN-006 | ACCEPTED (baseline) | Kontraktor dan User Sponsor memakai role `Sponsor`; Sponsor merekam pelaksana dan mewakili crew; tanda tangan Pelaksana Pekerjaan pada hardcopy; akun Kontraktor didaftarkan Administrator dan terikat scope Sponsor | Scope perusahaan eksplisit (`ExternalUserCompany`), acknowledgement digital berdasar hukum |
| OPN-007 bagian 2 | ACCEPTED (berbasis bukti) | Keterkaitan E-SIMI dibuktikan nomor referensi pada draft dan lampiran berkode `ESIMI` yang wajib sebelum submit | Integrasi API/verifikasi otomatis E-SIMI |
| OPN-008 | ACCEPTED sebagian | Spesimen tanda tangan visual berversi adalah bukti persetujuan elektronik (bukan PSrE); audit append-only; lampiran tidak dihapus fisik; tidak ada penghapusan otomatis; backup ditarik sistem internal | Periode retensi/legal hold, klasifikasi data formal, angka RPO/RTO eksplisit |
| OPN-009 | ACCEPTED (24 Sep 2026) | Topologi satu host sesuai record | Observability/on-call |
| OPN-010 | ACCEPTED | Masa berlaku maksimum tujuh hari dari `validFrom` sampai `validUntil` yang dipilih Sponsor; renewal membuat PTW dan nomor baru non-overlap; tidak ada batas jumlah renewal berantai pada baseline | Tidak ada |
| OPN-011 | ACCEPTED | Materi kampanye tidak termasuk rilis (`CampaignAssetVersion` `NOT-APPLICABLE`) | Halaman kampanye bila diminta kelak |
| OPN-012 | ACCEPTED sebagian | Keterbacaan hardcopy dikonfirmasi reviewer (`EvidenceReadable`) pada renewal dan closure; pemeriksaan signature PDF/JPEG/PNG dan batas ukuran | QR/reference pada lembar cetak, resolusi scan minimum |

- Record OPN-001 sampai OPN-006 dan OPN-008 diperbarui statusnya merujuk amendemen ini; form
  keputusan yang masih memuat `TBD` pada record tersebut adalah lembar kerja untuk perluasan
  di masa depan, bukan prasyarat baseline.

## Bukti pengesahan

- Referensi: pernyataan pengguna 25 September 2026 bahwa sistem dan alur saat ini telah disetujui,
  lalu bahwa keadaan sistem dan alur telah disetujui stakeholder; DEC-117 sampai DEC-128 pada
  BRD v1.8.
- Approver dan tanggal: Product Owner, 25 September 2026 (record dan Amendemen 1); Product Owner bersama stakeholder Operasi, HSSE, General Affair, Transport & Operasi FSRU, dan TI, 25 September 2026 (Amendemen 2).
