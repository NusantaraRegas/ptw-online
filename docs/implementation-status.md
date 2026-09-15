# Status implementasi BRD/PRD/FSD v1.6

Tanggal pemeriksaan: 15 September 2026. Dokumen requirement v1.6 diperlakukan sebagai sumber kebutuhan; keputusan OPN-001–012 yang belum disahkan tidak diberi nilai bisnis fiktif.

## Ringkasan

| Prioritas/area | Status | Catatan |
| --- | --- | --- |
| P0 lifecycle dan explicit commands | Selesai untuk vertical slice | State lama dihapus dari domain/API/UI; migration kompatibel tersedia |
| Satu validator PIC HSE dan SoD Sponsor | Selesai | Satu task exact PermitVersion; self-validation ditolak |
| Approve-and-issue atomik | Selesai untuk Manager direct | Decision, status, audit, outbox, snapshot cetak atomik |
| Pejabat pengganti resmi | Partial/fail-closed | Delegation framework lama ada, tetapi field v1.6 lengkap belum tersedia; command menolak acting assignment |
| Pilot ORF | Selesai pada submit gate | Lokasi selain kode ORF ditolak jelas |
| Department/owner/release/config bundle | Partial | Master lokasi effective-dated ada; Department, owner mapping, LocationRelease, dan ConfigurationBundle belum lengkap |
| Form Bagian 1–5/7 dan JSA metadata | Partial | Field decision-neutral utama dan metadata JSA tersedia; hazards/controls bebas dihapus dari UI dan dikirim kosong untuk compatibility; wizard/autosave/rule preview belum tersedia |
| Rules engine/versioned evaluation snapshot | Partial/fail-closed | Activation gate dan version references ada; declarative ruleset dan RuleEvaluationSnapshot belum tersedia |
| Attachment/JSA | Partial/fail-closed | Metadata/category/lineage/PDF-JPEG-PNG/private storage, scanner port, evidence fields, dan quarantine download guard tersedia; adapter scanner produksi belum tersedia |
| Print package | Partial/fail-closed | Immutable snapshot dan render queue record tersedia; renderer/download/preview belum tersedia |
| Closure hardcopy | Backend guards partial | State/task/commands dan evidence validation tersedia; karena renderer/scanner belum ada, happy path tetap fail-closed |
| E-SIMI minimum | Belum tersedia | Draft hanya menyimpan nomor/external ID; verification model/port/endpoint belum ada |
| Contractor identity/company scope | Belum tersedia | OIDC/BFF dan ExternalUserCompany belum tersedia |
| P1 operations/report/SLA/notification/admin | Belum dimulai untuk v1.6 | Fondasi outbox dan sebagian admin lama dapat dipakai ulang |

## Traceability increment ini

| Requirement v1.6 | Komponen | Endpoint/data | Migration | Test |
| --- | --- | --- | --- | --- |
| Lifecycle 11 status | `Ptw.Domain/PermitStatus.cs`, `Permit.cs` | seluruh explicit command | `AlignPermitLifecycleV16` | `PermitStateMachineTests` |
| Tepat satu task HSE | `PermitStore.ApplyWorkflowTasksAsync` | `GET /api/v1/tasks`, `POST /tasks/{id}/validate` | rename legacy HSSE dan rekonsiliasi task | `SubmitCreatesExactlyOneHseTaskAndNoGasValidatorTask` |
| Sponsor HSE tidak self-validate | `Permit.ValidateSubmission` | task validate | n/a | domain + `SponsorCannotSelfValidateButAnotherHseValidatorCan` |
| Revisi membatalkan evidence/task | `Permit.RequestRevision`, `PermitStore` | `/tasks/{id}/revision` | n/a | domain + `RevisionCancelsAreaTaskAndResubmitCreatesFreshHseTaskForNewVersion` |
| ORF-only pilot | `PermitService.SubmitAsync` | `/permits/{id}/submit` | n/a | `PilotRejectsSubmissionOutsideOrf` |
| Approval + issuance satu command | `Permit.ApproveAndIssue`, `PermitService` | `/tasks/{id}/approve-and-issue` | tables `wf.Decision`, `doc.PrintPackageSnapshot`, `doc.GeneratedDocument` | `ApproveAndIssueCommitsDecisionStateAuditOutboxAndPrintSnapshotAtomically` |
| Suspend/resolve | `Permit.Suspend`, `ResolveSuspension` | `/suspensions`, `/suspensions/resolve` | lifecycle mapping | domain tests + Angular API tests |
| Renewal permit baru/tidak overlap | `Permit.CreateRenewal`, `RequestRenewal` | `/permits/{id}/renew` | existing renewal relation | domain tests |
| Closure owner-only, tanpa HSE task | `Permit.RequestClosure`, `Close`, `PermitStore` | `/closure-requests`, `/closure-tasks/...` | lifecycle/task compatibility | domain tests; integration happy path menunggu renderer/scanner |
| Signed field copy exact package/version | `PermitAttachmentService`, `PermitService.RequestClosureAsync` | multipart attachment, authorized clean-only download, closure request | `AddV16AttachmentEvidenceMetadata`, `AddMalwareScanEvidence` | integration: signature PDF/JPEG/PNG, mismatch, cross-Sponsor access, quarantine download, dan bukti `PENDING` ditolak saat closure; happy path menunggu scanner |
| Concurrency/idempotency | `PermitService`, stores | `If-Match`, `Idempotency-Key` | existing receipts/rowversion | `StaleIfMatchReturnsConflictAndIdempotencyPayloadMismatchIsRejected` |

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
| Domain tests | 27/27 lulus |
| API integration + SQL Server 2025 Testcontainers | 27/27 lulus |
| Angular production build | Lulus |
| Angular tests | 35/35 lulus |
| Prettier check | Lulus |
| `dotnet format --verify-no-changes` | Lulus |
| NuGet vulnerable audit | Lulus; tidak ada package rentan |
| npm audit `--audit-level=high` | Lulus; satu advisory transitive level moderate masih tercatat |
| Compose config | Lulus |

## Blocker keputusan dan implementasi produksi

- OPN-001–012 masih harus disahkan sesuai owner masing-masing; tidak ada checklist, ambang gas, SLA, retensi, signature policy, atau HA topology yang di-hard-code.
- Acting assignment v1.6 belum menyimpan document basis, department, maximum risk, reason, issued/checked/revoked evidence, principal position formal, dan revocation lifecycle. Approval acting tetap ditolak.
- Owner matrix target diketahui dari v1.6, tetapi aktivasi lokasi non-ORF menunggu effective-dated owner mapping, LocationRelease, dan ConfigurationBundle yang approved.
- Exact ruleset, print template, logo/campaign asset bundle belum tersedia. Production `IssuancePolicy.Approved` default `false`.
- PDF renderer, draft watermark preview, QR/hash download, dan private generated-document storage belum tersedia.
- Port malware scanner dan adapter unavailable tersedia; file baru tetap `PENDING`, production upload fail-closed, file karantina tidak dapat diunduh, dan closure tidak menerima evidence tersebut sebagai `CLEAN` sampai adapter produksi memberi evidence sah.
- Manual/API E-SIMI verification, contractor company assignment, production OIDC/BFF, cross-company tests, dan IDOR matrix belum tersedia.
- P1/P2 belum dinyatakan selesai.

## UAT manual increment lifecycle

1. Jalankan stack Development dan migration pada database kosong.
2. Sebagai Sponsor, buat draft ORF dan submit; pastikan hanya satu task `HSE_VALIDATION` muncul.
3. Coba validasi dengan identity Sponsor yang sama dan pastikan ditolak; validasi dengan PIC HSE lain.
4. Sebagai Manager ORF, jalankan `approve-and-issue`; verifikasi status **Diterbitkan**, satu decision, audit, outbox, snapshot, dan generated document `PENDING`.
5. Ulangi command dengan key/payload sama dan pastikan hasil pertama dikembalikan; ubah payload dengan key sama dan pastikan `409`.
6. Uji request revision dari HSE dan Manager, lalu pastikan evidence/task lama batal dan submit ulang membuat task baru.
7. Uji submit lokasi HO/FSRU/Site Office/Water-Based dan pastikan error pilot ORF fail-closed.
8. Uji suspend langsung dan resolve; pastikan UI tetap memperingatkan revalidasi hardcopy.
9. Uji renewal dengan periode overlap (ditolak) dan non-overlap (permit baru tanpa attachment/decision).
10. Jangan melakukan UAT closure happy path sebelum renderer dan malware scanner test adapter tersedia; pastikan request closure saat evidence belum `CLEAN` atau package belum `READY` ditolak.

## Risiko yang memblokir produksi

Journey ini belum boleh dipakai operasional produksi. Blocker utama adalah authorization acting/owner yang belum lengkap, ruleset dan asset belum disahkan, tidak adanya renderer/scanner, E-SIMI/contractor identity belum ada, acceptance test happy path closure/print belum lengkap, dan OPN-001–012 belum ditutup.
