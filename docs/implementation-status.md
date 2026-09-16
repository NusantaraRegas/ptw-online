# Status implementasi BRD/PRD/FSD v1.7

Tanggal pemeriksaan: 16 September 2026. Dokumen requirement v1.7 diperlakukan sebagai sumber kebutuhan; keputusan OPN-001–012 yang belum disahkan tidak diberi nilai bisnis fiktif.

## Ringkasan

| Prioritas/area | Status | Catatan |
| --- | --- | --- |
| P0 lifecycle dan explicit commands | Selesai untuk vertical slice | State lama dihapus dari domain/API/UI; migration kompatibel tersedia |
| Satu validator PIC HSE dan SoD Sponsor | Selesai | Satu task exact PermitVersion; self-validation ditolak |
| Approve-and-issue atomik | Selesai untuk Manager direct | Decision, status, audit, outbox, snapshot cetak atomik |
| Pejabat pengganti resmi | Partial/fail-closed | Delegation framework lama ada, tetapi field v1.7 lengkap belum tersedia; command menolak acting assignment |
| Release tiga wilayah aktif | Selesai untuk Development | ORF, Site-Office, dan Water-Based Activity aktif melalui konfigurasi server; lokasi lain ditolak fail-closed |
| Department/owner/release/config bundle | Partial | Routing Development mengikuti scope lokasi Manager; assignment effective-dated dan ConfigurationBundle production masih harus disahkan |
| Form Bagian 1–5/7 dan JSA metadata | Partial | Bagian 1 memakai katalog multi-select Sponsor; opsi `Lain-lain` membuka detail wajib maksimum 80 karakter. Bagian 2 menangkap nomor/nama equipment, Work Order No., dan referensi bahaya tambahan opsional yang tidak memengaruhi rules/approval; JSA tetap authoritative. Bagian 4 memakai 15 pilihan sesuai template: JSA wajib, lainnya opsional, masing-masing ditautkan ke lampiran dan divalidasi server sebelum submit. Bagian 5 hanya ditetapkan PIC HSE saat validasi. Seluruh pilihan dicetak dari snapshot; wizard/autosave/rule preview belum tersedia |
| Rules engine/versioned evaluation snapshot | Partial/fail-closed | Activation gate dan version references ada; declarative ruleset dan RuleEvaluationSnapshot belum tersedia |
| Attachment/JSA | Partial/fail-closed | Metadata/category/lineage/PDF-JPEG-PNG/private storage, relasi lampiran ke kode dokumen Bagian 4, validasi kesesuaian metadata JSA, scanner port, evidence fields, dan quarantine download guard tersedia; adapter scanner produksi belum tersedia |
| Print package | Selesai untuk formulir terkontrol | Halaman resmi A3 landscape FM-001/002/003 dipakai sebagai template vektor; renderer meng-overlay snapshot, Worker memakai render queue dengan retry, unduhan resmi ber-audit, dan pratinjau ber-watermark tersedia; governance master checklist masih pending |
| Closure hardcopy | Backend guards partial | State/task/commands, evidence validation, dan paket cetak `READY` tersedia; happy path masih menunggu adapter malware scanner produksi |
| E-SIMI minimum | Belum tersedia | Draft hanya menyimpan nomor/external ID; verification model/port/endpoint belum ada |
| Contractor identity/company scope | Belum tersedia | OIDC/BFF dan ExternalUserCompany belum tersedia |
| P1 operations/report/SLA/notification/admin | Belum dimulai untuk v1.7 | Fondasi outbox dan sebagian admin lama dapat dipakai ulang |

## Traceability increment ini

| Requirement v1.7 | Komponen | Endpoint/data | Migration | Test |
| --- | --- | --- | --- | --- |
| Lifecycle 11 status | `Ptw.Domain/PermitStatus.cs`, `Permit.cs` | seluruh explicit command | `AlignPermitLifecycleV16` | `PermitStateMachineTests` |
| Tepat satu task HSE | `PermitStore.ApplyWorkflowTasksAsync` | `GET /api/v1/tasks`, `POST /tasks/{id}/validate` | rename legacy HSSE dan rekonsiliasi task | `SubmitCreatesExactlyOneHseTaskAndNoGasValidatorTask` |
| Sponsor HSE tidak self-validate | `Permit.ValidateSubmission` | task validate | n/a | domain + `SponsorCannotSelfValidateButAnotherHseValidatorCan` |
| Bagian 5 ditetapkan PIC HSE | `PermitSafetyEquipmentCatalog`, `Permit.ValidateSubmission`, `Permit.ApproveAndIssue`, `PtwFormRenderer*` | `GET /reference-data/safety-equipment`, selector responsif desktop/mobile, evidence HSE, dan immutable print snapshot; approval legacy tanpa pilihan diblokir dan diarahkan ke revisi | n/a (tersimpan dalam JSON evidence/snapshot yang sudah ada) | domain negative/positive, API integration, Angular flow, dan printing regression |
| Bagian 4 dokumen pendukung | `PermitSupportingDocumentCatalog`, `PermitAttachmentService`, `PermitService`, `PtwFormRenderer*` | `GET /reference-data/supporting-documents`; selector responsif; upload membawa `supportingDocumentCode`; submit memerlukan bukti untuk setiap pilihan dan metadata JSA yang cocok; HSE/Manager read-only | `AddSupportingDocumentAttachmentCode` | domain catalog/legacy guard, API missing-evidence/issuance flow, Angular build/tests, printing catalog regression |
| Referensi perencanaan Bagian 2 | `PermitDraft`, `PermitMapper`, `PtwFormRenderer*` | `EquipmentName`, `WorkOrderNumber`, dan `AdditionalHazardReference`; seluruhnya opsional, dinormalisasi server, disimpan dalam JSON draft/snapshot, dan dicetak pada sel template resmi | n/a (JSON draft/snapshot) | domain normalization/length guards, API round-trip, Angular payload, dan printing regression |
| Revisi membatalkan evidence/task | `Permit.RequestRevision`, `PermitStore` | `/tasks/{id}/revision` | n/a | domain + `RevisionCancelsAreaTaskAndResubmitCreatesFreshHseTaskForNewVersion` |
| Location release dan routing pemilik wilayah | `LocationReleaseSettings`, `PermitService.SubmitAsync` | `/permits/{id}/submit`, task area approval | n/a | `SubmissionRejectsLocationOutsideTheReleasedRoutes`, `ReleasedLocationRoutesApprovalToManagerWithMatchingScope` |
| Approval + issuance satu command | `Permit.ApproveAndIssue`, `PermitService` | `/tasks/{id}/approve-and-issue` | tables `wf.Decision`, `doc.PrintPackageSnapshot`, `doc.GeneratedDocument` | `ApproveAndIssueCommitsDecisionStateAuditOutboxAndPrintSnapshotAtomically` |
| Suspend/resolve | `Permit.Suspend`, `ResolveSuspension` | `/suspensions`, `/suspensions/resolve` | lifecycle mapping | domain tests + Angular API tests |
| Renewal permit baru/tidak overlap | `Permit.CreateRenewal`, `RequestRenewal` | `/permits/{id}/renew` | existing renewal relation | domain tests |
| Closure owner-only, tanpa HSE task | `Permit.RequestClosure`, `Close`, `PermitStore` | `/closure-requests`, `/closure-tasks/...` | lifecycle/task compatibility | domain tests; integration happy path menunggu renderer/scanner |
| Signed field copy exact package/version | `PermitAttachmentService`, `PermitService.RequestClosureAsync` | multipart attachment, authorized clean-only download, closure request | `AddV16AttachmentEvidenceMetadata`, `AddMalwareScanEvidence` | integration: signature PDF/JPEG/PNG, mismatch, cross-Sponsor access, quarantine download, dan bukti `PENDING` ditolak saat closure; happy path menunggu scanner |
| Paket cetak formulir terkontrol | `Ptw.Infrastructure/Printing/PtwFormRenderer*`, `PrintPackageRenderExecutor` | `GET/POST /permits/{id}/print-packages...` | `AddPrintPackageRendererVersion` | `Ptw.Printing.Tests` (19) dan `PrintPackageApiTests` (4) |
| Concurrency/idempotency | `PermitService`, stores | `If-Match`, `Idempotency-Key` | existing receipts/rowversion | `StaleIfMatchReturnsConflictAndIdempotencyPayloadMismatchIsRejected` |
| Konsistensi aset antar-deploy | Nginx cache/fallback + Angular navigation error recovery | `index.html` revalidate; hashed JS/CSS immutable; missing chunk `404`; reload dibatasi satu kali per menit | n/a | `chunk-load-recovery.spec.ts` + smoke header/status |

## Migration baru

1. `20260915072103_AlignPermitLifecycleV16`
   - memetakan status legacy secara konservatif;
   - menormalkan `HSSE_VALIDATION` menjadi `HSE_VALIDATION`;
   - membatalkan task gas dan task workflow legacy;
   - membentuk task atomik `AREA_APPROVE_AND_ISSUE` untuk permit in-flight;
   - menambahkan decision, immutable print snapshot, dan generated document queue.
2. `20260915074426_AddV16AttachmentEvidenceMetadata`
   - menambahkan category, nomor/revisi/tanggal dokumen, target PermitVersion, PrintPackage, dan supersedes lineage;
   - mengubah status legacy `NOT_SCANNED` menjadi `PENDING`;
   - menambahkan FK, check constraints, dan indexes.
3. `20260915081658_AddMalwareScanEvidence`
   - menambahkan referensi evidence dan timestamp scan;
   - mewajibkan evidence untuk setiap status `CLEAN` melalui check constraint;
   - menurunkan status `CLEAN` legacy tanpa evidence menjadi `PENDING` secara fail-closed.
4. `20260915105217_AddPrintPackageRendererVersion`
   - menambahkan `RendererVersion` pada `doc.GeneratedDocument` agar setiap file resmi dapat ditelusuri ke build renderer yang menghasilkannya.
5. `20260916145609_AddSupportingDocumentAttachmentCode`
   - menambahkan kode dokumen Bagian 4 nullable pada setiap lampiran agar file dapat direkonsiliasi dengan checklist resmi;
   - menambahkan index aktif per permit/kode tanpa mengubah atau menghapus data lampiran lama.

Migration bersifat additive/forward dan mempertahankan kolom legacy `ActiveWorkPeriodId` sebagai compatibility column yang tidak lagi dipakai journey aktif. Penghapusan fisiknya memerlukan tahap contract migration terpisah setelah verifikasi data.

## Endpoint yang dihapus dari journey

- validasi HSSE dan Distribusi Gas legacy;
- approval dan issue terpisah;
- request/approval suspension dua tahap;
- seluruh completion confirmation digital tiga pihak;
- close HSE/area-owner legacy berbasis `WORK_COMPLETED`.

Penggantinya adalah task HSE tunggal, atomic approve-and-issue, direct suspend, dan closure berbasis signed hardcopy.

## Bukti quality gate terakhir

| Pemeriksaan | Hasil |
| --- | --- |
| .NET 10 solution build Release | Lulus, 0 warning/0 error |
| Domain tests | 43/43 lulus |
| Print/document regression tests | 20/20 lulus |
| API integration + SQL Server 2025 database test terisolasi | 41/41 lulus |
| Angular production build | Lulus |
| Angular tests | 53/53 lulus |
| Prettier check | Lulus |
| `dotnet format --verify-no-changes` | Lulus |
| NuGet vulnerable audit | Lulus; tidak ada package rentan |
| npm audit `--audit-level=high` | Lulus; satu advisory transitive level moderate masih tercatat |
| Compose config | Lulus |

## Blocker keputusan dan implementasi produksi

- OPN-001–012 masih harus disahkan sesuai owner masing-masing; tidak ada checklist, ambang gas, SLA, retensi, signature policy, atau HA topology yang di-hard-code.
- Acting assignment v1.7 belum menyimpan document basis, department, maximum risk, reason, issued/checked/revoked evidence, principal position formal, dan revocation lifecycle. Approval acting tetap ditolak.
- Development mengaktifkan ORF (Distribusi Gas dan Pengelolaan ORF), Site-Office (General Affair), dan Water-Based Activity (Transport & Operasi FSRU) berdasarkan arahan pengguna 16 September 2026. Production tetap fail-closed sampai assignment effective-dated, LocationRelease, dan ConfigurationBundle disahkan.
- Development memakai alur dan checklist sistem saat ini sebagai `DEV-PTW-FLOW-CHECKLIST-2026-09-16`, serta template resmi FM-001/002/003-B-002-NR-B220 dengan fingerprint SHA-256 `1C4E6AD366B4`. Campaign asset ditandai eksplisit `DEV-NOT-APPLICABLE-2026-09-16` berdasarkan arahan pengguna 16 September 2026. Production tetap fail-closed: ruleset/campaign bundle operasional dan sign-off HSSE masih harus disahkan, serta `IssuancePolicy.Approved` default `false`.
- Renderer PDF, pengisian pilihan Bagian 1/4/5 dari snapshot (termasuk detail adaptif untuk opsi Bagian 1 `Lain-lain`), nomor/nama equipment, Work Order No., referensi bahaya tambahan Bagian 2, perbaikan garis divider kontinu pada formulir COLD, serta hierarki tipografi dan baseline isian Bagian 2 telah tersedia. Pratinjau ber-watermark dan private generated-document storage juga tersedia. Yang belum: QR code pada lembar cetak dan master checklist terkontrol yang effective-dated.
- Paket cetak `READY` bersifat immutable dan tidak otomatis dirender ulang setelah renderer diperbarui. Command terkontrol untuk membuat paket pengganti yang tetap mempertahankan paket lama dan audit lineage belum tersedia.
- Port malware scanner dan adapter unavailable tersedia; file baru tetap `PENDING`, production upload fail-closed, file karantina tidak dapat diunduh, dan closure tidak menerima evidence tersebut sebagai `CLEAN` sampai adapter produksi memberi evidence sah.
- Manual/API E-SIMI verification, contractor company assignment, production OIDC/BFF, cross-company tests, dan IDOR matrix belum tersedia.
- P1/P2 belum dinyatakan selesai.

## UAT manual increment lifecycle

1. Jalankan stack Development dan migration pada database kosong.
2. Sebagai Sponsor, buat draft ORF, Site-Office, dan Water-Based Activity. Pastikan JSA otomatis wajib, pilih beberapa dokumen Bagian 4 opsional, unggah file dengan jenis yang sesuai, lalu submit; tanpa file JSA, metadata JSA yang cocok, atau salah satu file opsional terpilih, submit harus ditolak. Setelah lengkap, pastikan masing-masing hanya membuat satu task `HSE_VALIDATION`.
3. Coba validasi dengan identity Sponsor yang sama dan pastikan ditolak; validasi dengan PIC HSE lain, pilih minimal satu APD/perlengkapan safety Bagian 5, lalu pastikan pilihan tersimpan dan muncul sebagai centang pada paket cetak. Coba kirim pilihan kosong atau kode di luar katalog dan pastikan ditolak.
4. Sebagai Manager pemilik wilayah yang scope-nya cocok, jalankan `approve-and-issue`; pastikan Manager wilayah lain ditolak, lalu verifikasi status **Diterbitkan**, satu decision, audit, outbox, snapshot, dan generated document `PENDING`.
5. Ulangi command dengan key/payload sama dan pastikan hasil pertama dikembalikan; ubah payload dengan key sama dan pastikan `409`.
6. Uji request revision dari HSE dan Manager, lalu pastikan evidence/task lama batal dan submit ulang membuat task baru.
7. Uji submit HO dan FSRU lalu pastikan `permit.location.not_released`; pastikan ORF, Site-Office, dan Water-Based Activity dapat melanjutkan workflow.
8. Uji suspend langsung dan resolve; pastikan UI tetap memperingatkan revalidasi hardcopy.
9. Uji renewal dengan periode overlap (ditolak) dan non-overlap (permit baru tanpa attachment/decision).
10. Unduh paket cetak setelah status berubah menjadi `READY`, cetak pada A3 landscape, lalu bandingkan setiap Bagian dengan formulir terkontrol FM-001/002/003-B-002-NR-B220. Pastikan Bagian 6 dan Bagian 8-10 tercetak kosong dan cukup lebar untuk ditulis tangan.
11. Jangan melakukan UAT closure happy path sebelum malware scanner test adapter tersedia; pastikan request closure saat evidence belum `CLEAN` atau package belum `READY` ditolak.

## Risiko yang memblokir produksi

Journey ini belum boleh dipakai operasional produksi. Blocker utama adalah authorization acting/owner yang belum lengkap, ruleset dan asset belum disahkan, tidak adanya malware scanner produksi, E-SIMI/contractor identity belum ada, acceptance test happy path closure belum lengkap, dan OPN-001–012 belum ditutup. Paket cetak sudah dihasilkan, tetapi kesetiaan tata letak terhadap formulir terkontrol wajib diverifikasi dan disahkan HSSE sebelum dipakai di lapangan.
