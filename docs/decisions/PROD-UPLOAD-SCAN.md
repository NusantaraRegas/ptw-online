# PROD-UPLOAD-SCAN: unggahan tanpa malware scanner (accepted risk)

- Status: ACCEPTED (risiko diterima)
- Owner: TI / HSE
- Approver: pemilik produk NR PTW Online (arahan pengguna, sesi 24 September 2026)
- Tanggal keputusan: 24 September 2026
- Berlaku mulai: rilis produksi pertama, sampai scanner tersedia
- Menggantikan: aturan fail-closed "adapter upload tepercaya hanya Development"

## Konteks

Setiap PTW memerlukan lampiran wajib (JSA, Prosedur Pekerjaan, ID, BPJS TK, FTW, E-SIMI) sebelum
submit, dan hanya lampiran `CLEAN` yang dapat diunduh. Default kode mewajibkan malware scanner
(`Attachments:RequireMalwareScan=true`) dan hanya menyediakan adapter unavailable, sehingga tanpa
scanner tidak ada PTW yang dapat diajukan. Organisasi belum memiliki cara menyediakan scanner.

## Keputusan

Produksi berjalan dengan `Attachments:RequireMalwareScan=false`. Unggahan yang lolos pemeriksaan
signature PDF/JPEG/PNG dicatat `CLEAN` oleh `TrustedUploadScanner` dengan evidence
`trusted-upload:<sha256>` agar dapat dibedakan dari hasil scan sesungguhnya. Saklar ini tidak lagi
terikat pada environment `Development`; default konfigurasi dasar tetap `true` (fail-closed).

## Konsekuensi dan kontrol

- Risiko residual: PDF dapat membawa konten aktif dan dibuka reviewer pada workstation korporat.
- Kontrol kompensasi: jenis file dibatasi PDF/JPEG/PNG lewat signature, ukuran maksimum 10 MB, unduhan hanya untuk aktor berwenang sebagai attachment dengan `nosniff` dan `Cache-Control: no-store`, endpoint protection pada workstation reviewer wajib memindai unduhan.
- Jalur balik: mengimplementasikan `IMalwareScanner` (misalnya ClamAV di network `backend`) dan mengembalikan `RequireMalwareScan=true`; evidence lama tetap terbaca sebagai `trusted-upload`.

## Bukti pengesahan

- Referensi: arahan pengguna "we don't need malware scanner at all for now" dan penegasan ulang "we have no way to include malware scanner right now", 24 September 2026
- Approver dan tanggal: pemilik produk NR PTW Online, 24 September 2026
