# REQUESTER-PELAKSANA-SIGNATURE: Tanda tangan Pelaksana Pekerjaan dari akun Pemohon

- Status: ACCEPTED
- Owner: PO / HSE
- Approver: pemilik produk NR PTW Online (arahan pengguna, sesi 21 Oktober 2026)
- Tanggal keputusan: 21 Oktober 2026
- Berlaku mulai: 21 Oktober 2026
- Menggantikan: butir "Tanda tangan Pelaksana Pekerjaan ... tetap pada hardcopy" pada OPN-008
  (perilaku bukti persetujuan elektronik) sejauh menyangkut slot Pelaksana Pekerjaan Bagian 3

## Konteks

Sejak OPN-008 disahkan sebagian (25 September 2026), Bagian 3 paket cetak mengisi nama, jabatan,
departemen, spesimen tanda tangan berversi, dan waktu submit Sponsor dari `SponsorPrintEvidence`,
sedangkan slot tanda tangan Pelaksana Pekerjaan dibiarkan kosong untuk hardcopy karena belum ada
identitas digital pelaksana.

UAT-2026-10-12 memperkenalkan Pemohon (role `Requester`, Kontraktor) sebagai pengisi PTW awal yang
mengajukan draft ke Sponsor (`initial-submit`) dan disetujui Sponsor (`approve-initial`). Pemohon kini
memiliki akun terdaftar dengan profil dan spesimen tanda tangan yang sama seperti aktor lain, sehingga
pengajuan PTW ke Sponsor adalah tindakan yang dapat dibuktikan secara elektronik.

## Keputusan

1. Saat penerbitan, sistem membekukan akun Pemohon yang mengajukan PTW ke Sponsor sebagai
   `RequesterPrintEvidence` pada `PrintPackageSnapshotPayload.Requester`: Subject ID, nama profil,
   jabatan, departemen, waktu pengajuan terakhir ke Sponsor (`PermitInitiation.SubmittedToSponsorAt`),
   dan exact versi/hash/byte spesimen tanda tangan aktif Pemohon.
2. Renderer mencetak spesimen tersebut pada slot **Tanda Tangan — Pelaksana Pekerjaan** Bagian 3
   beserta `Tanggal : <waktu pengajuan WIB>` pada ketiga formulir terkontrol (HOT, COLD, CSE). Baris
   nama di bawah slot tetap memuat field `Nama pelaksana` (`PerformingAuthority`) yang dinyatakan pada
   formulir; keputusan ini tidak mengganti isi field tersebut.
3. Hanya PTW dengan inisiasi Pemohon berstatus `Approved` yang memuat bukti ini. Draft tanpa
   inisiasi (draft renewal, data legacy) tetap mencetak slot kosong untuk hardcopy, dan snapshot lama
   tanpa field `Requester` tetap dirender seperti sebelumnya.
4. Bentuknya tetap **bukti persetujuan elektronik**, bukan tanda tangan digital tersertifikasi (OPN-008).
   Layout formulir terkontrol tidak berubah; hanya slot yang sudah ada diisi dari snapshot immutable.

## Konsekuensi dan kontrol

- `PtwFormRenderer.RendererVersion` naik ke `ptw-form-renderer/4.2.0`; dokumen yang sudah `READY`
  tidak dirender ulang.
- Spesimen Pemohon diunggah Administrator seperti akun lain; bila belum ada, hanya tanggal pengajuan
  yang tercetak dan slot tanda tangan dibiarkan kosong untuk hardcopy.
- Testing: regresi render membuktikan slot berubah saat bukti Pemohon ada, saat spesimen hilang, dan
  deterministik pada ketiga kelas izin; integration test penerbitan membuktikan snapshot memuat
  profil dan spesimen akun Pemohon dengan waktu pengajuan sebelum waktu submit Sponsor.
- Jalur balik: menghapus pemanggilan `ResolveRequesterPrintEvidenceAsync` dan overlay
  `RequesterSignature`/`RequesterSignedAt` dengan superseding record.

## Bukti pengesahan

- Referensi: arahan pengguna "when pemohon ptw, submit to the sponsor for review, then the pelaksana
  TTD/sign will use that pemohon account on the generated PDF PTW document", sesi 21 Oktober 2026.
- Approver dan tanggal: pemilik produk NR PTW Online, 21 Oktober 2026
