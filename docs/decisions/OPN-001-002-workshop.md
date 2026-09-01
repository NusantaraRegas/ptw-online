# Paket workshop keputusan OPN-001 dan OPN-002

Dokumen ini adalah agenda pengumpulan keputusan, bukan kebijakan produksi. Output workshop baru dapat
dipakai setelah owner, approver, tanggal efektif, referensi bukti, dan versi policy tercatat pada
masing-masing decision record.

## Tujuan

1. Menetapkan katalog lokasi, hierarchy, ownership, dan aturan lintas area untuk OPN-001.
2. Menetapkan role/action, risk route, kompetensi, delegasi, dan SoD untuk OPN-002.
3. Menyetujui expected outcome skenario UAT dan policy version yang akan diuji.
4. Menentukan maker, checker, data steward, serta proses perubahan setelah go-live.

## Peserta wajib

| Fungsi | Tanggung jawab workshop | Nama |
| --- | --- | --- |
| Operasi/Area owner | Lokasi, hierarchy, ownership, action operasional | TBD |
| HSE | Risk route, review, competency, SoD, fail-safe | TBD |
| Product Owner | Scope MVP, acceptance, prioritas | TBD |
| Administrator master data | Import, maker-checker, effective dating | TBD |
| Security/IT | Identity attributes, audit, least privilege | TBD |
| Legal/Records bila diperlukan | Bukti keputusan dan retensi | TBD |
| Approver keputusan | Pengesahan final | TBD |

## Pre-read dan data yang harus dibawa

- SOP PTW dan matriks authority yang sedang berlaku;
- daftar fasilitas/area resmi beserta owner dan sumber datanya;
- daftar personel/role tanpa memasukkan PII ke repository;
- register kompetensi dan masa berlakunya;
- contoh PTW rendah, sedang, tinggi, lintas area, dan SIMOPS;
- kasus delegasi, cuti/shift change, assignment kedaluwarsa, serta konflik SoD;
- daftar pengecualian yang memang diizinkan SOP dan siapa approver-nya.

## Urutan sesi

### Sesi 1 — OPN-001

1. Bekukan scope lokasi MVP dan aturan kode.
2. Susun hierarchy serta owner per periode.
3. Putuskan descendant coverage, overlap, lintas area, dan handover ownership.
4. Setujui fail-safe dan skenario UAT lokasi.
5. Tunjuk data steward, maker, checker, dan sumber rekonsiliasi.

### Sesi 2 — OPN-002

1. Validasi katalog role dan action terhadap SOP.
2. Isi matriks class/risk/location menuju review route dan final authority.
3. Isi pasangan SoD per action dan konteks beserta pengecualian.
4. Putuskan delegasi, periode efektif, kompetensi, serta perubahan di tengah workflow.
5. Setujui fail-safe dan expected outcome UAT authorization.

### Sesi 3 — Validasi dan sign-off

1. Cari gap, route overlap, authority ambiguity, dan keputusan yang saling bertentangan.
2. Tetapkan policy version dan tanggal efektif.
3. Muat master melalui maker-checker pada environment UAT.
4. Jalankan paket UAT immutable dan review coverage/checksum.
5. Catat report ID, checksum, notulen, approver, dan tanggal pada OPN-001/002.
6. Ubah status ke `ACCEPTED` hanya bila seluruh acceptance checklist telah terpenuhi.

## Output wajib

| Output | Owner | Status |
| --- | --- | --- |
| Katalog dan hierarchy lokasi resmi | Operasi | TBD |
| Matriks ownership/location authority | Operasi | TBD |
| Katalog role/action/competency | HSE/Operasi | TBD |
| Matriks risk/review/final authority | HSE/Operasi | TBD |
| Matriks SoD dan pengecualian | HSE/Operasi | TBD |
| Aturan delegasi dan effective dating | HSE/Operasi | TBD |
| Expected outcome dan coverage UAT | Product Owner/HSE/Operasi | TBD |
| Referensi pengesahan dan policy version | Approver | TBD |

## Kondisi berhenti

Workshop belum menghasilkan keputusan yang dapat diaktifkan bila masih ada lokasi tanpa owner,
action tanpa role/kompetensi, route tanpa final authority, overlap tanpa resolusi, SoD tanpa scope,
atau skenario fail-safe tanpa expected outcome. Enforcement harus tetap nonaktif.
