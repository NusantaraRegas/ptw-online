# HSE-SPONSOR-SELF-VALIDATION: Sponsor yang memegang PIC HSE memvalidasi PTW-nya sendiri

- Status: ACCEPTED
- Owner: HSE / PO
- Approver: pemilik produk NR PTW Online (arahan pengguna, sesi 20 Oktober 2026)
- Tanggal keputusan: 20 Oktober 2026
- Berlaku mulai: 20 Oktober 2026
- Menggantikan: baris "Validasi HSE — Sponsor PTW yang sama — DENY" pada OPN-002 Bagian C
  (`permit.validation.self_validation_forbidden`)

## Konteks

Sejak baseline, domain menolak validasi HSE oleh akun yang sama dengan Sponsor PTW
(`permit.validation.self_validation_forbidden`), termasuk penetapan APD Bagian 5 oleh Sponsor.
Aturan itu dicatat OPN-002 Bagian C sebagai pasangan separation of duties pertama dan UI
menyembunyikan tombol "Validasi sebagai PIC HSE" bila aktor adalah Sponsor PTW.

Kondisi organisasi berubah: satu orang kini memegang peran Sponsor sekaligus PIC HSE untuk
pengajuan yang sama (contoh: Admin HSSE yang menjadi Sponsor PTW Kontraktor). Dengan aturan lama,
PTW tersebut terhenti di `UNDER_VALIDATION` sampai PIC HSE lain tersedia, padahal validasi HSE pada
alur UAT-2026-10-12 sudah didahului persetujuan awal Sponsor atas PTW Kontraktor dan tetap diikuti
review Bagian 7 oleh SO/Officer serta approve-and-issue Manager yang keduanya wajib orang berbeda.

## Keputusan

1. Sponsor PTW yang memegang role `HSEValidator` **boleh** memvalidasi PTW-nya sendiri dan
   menetapkan/menyimpan pilihan APD Bagian 5 pada PTW tersebut. Kode
   `permit.validation.self_validation_forbidden` dihapus dari domain.
2. Pasangan separation of duties lainnya **tidak berubah**: Sponsor atau validator HSE tidak boleh
   menjadi reviewer Bagian 7 (`permit.area_operations.separation_of_duty`), dan Manager yang
   menerbitkan harus berbeda dari Sponsor, validator HSE, dan reviewer
   (`permit.approval.separation_of_duty`). Bila Sponsor memvalidasi sendiri, orang itu otomatis
   tersingkir dari kedua tahap berikutnya.
3. Validasi tetap memerlukan role `HSEValidator`, task `HSE_VALIDATION` pada versi PTW yang tepat,
   komentar keputusan, pilihan APD, dan self check PIC HSE untuk versi di atas 1. Keputusan ini tidak
   mengubah urutan review, task pool, atau scope lokasi.

## Konsekuensi dan kontrol

- Risiko residual: pemeriksaan independen pertama atas pengajuan hilang bila Sponsor dan PIC HSE
  adalah orang yang sama. Kontrol kompensasi: review Bagian 7 dan penerbitan oleh dua orang lain
  yang berbeda tetap wajib, evidence HSE membekukan identitas aktor dari profil akun, dan audit
  event `hse_validated` mencatat aktor sehingga kasus self-validation dapat ditelusuri.
- Authorization: tidak ada perubahan role, assignment, atau scope. UI hanya menghapus syarat
  "aktor bukan Sponsor" pada tombol validasi; server tetap menjadi penentu.
- Testing: unit domain memastikan Sponsor dapat memvalidasi dan tetap ditolak sebagai reviewer
  Bagian 7; integration test memastikan akun Sponsor+HSEValidator memvalidasi pengajuannya sendiri
  dan evidence HSE memuat nama profilnya.
- Jalur balik: mengembalikan `EnsureNotSponsor` pada `ValidateSubmission` dan
  `SaveSafetyEquipmentSelection` beserta syarat UI, dengan superseding record.

## Bukti pengesahan

- Referensi: arahan pengguna "yes do no.2 since the condition has changed", sesi 20 Oktober 2026,
  setelah rekomendasi untuk mempertahankan aturan disampaikan dan ditolak.
- Approver dan tanggal: pemilik produk NR PTW Online, 20 Oktober 2026
