# Status implementasi terhadap baseline v1.0

Dokumen BRD/PRD/FSD adalah spesifikasi dan sumber kebutuhan, bukan instruksi untuk mengarang kebijakan keselamatan. FSD bagian 21 membolehkan pembangunan fondasi sambil melarang penguncian journey final sebelum OPN-001–009 diputuskan.

## Increment yang diimplementasikan

| Area | Status | Bukti utama |
| --- | --- | --- |
| Struktur modular .NET 10 | Selesai | `PtwOnline.sln`, batas Domain/Application/Infrastructure/API/Worker |
| Domain state machine | Fondasi selesai | `Ptw.Domain/Permit.cs`, unit tests |
| Draft PTW | Vertical slice awal | create/list/get/update/submit, Angular create/list |
| Concurrency dan idempotency | Fondasi selesai | ETag/If-Match, request hash, unique idempotency record |
| Data SQL Server | Fondasi selesai | schemas `ptw`, `audit`, `intg`, initial migration |
| Audit dan outbox | Fondasi selesai | audit+outbox ditulis bersama perubahan aggregate |
| Identity/authorization | Adapter development | actor abstraction, role/ownership/location scope; OIDC menunggu OPN-007 |
| E-SIMI | Kontrak data pada draft saja | live adapter menunggu kontrak OPN-007 |
| Angular SPA | Shell dan vertical slice draft | dashboard, nav, list, form, responsive layout |
| Compose | Development topology | web, API, worker, migrator, SQL Server 2025 |

## Belum diimplementasikan karena membutuhkan keputusan bisnis

- location/area authority master (OPN-001);
- risk matrix, review route, final authority, delegation, dan SoD detail (OPN-002);
- checklist per permit class dan mapping final formulir (OPN-003);
- gas thresholds, units, test lifetime, retest, dan continuous monitoring (OPN-004);
- urutan serta SLA HSE–Operations review (OPN-005);
- contractor acknowledgement mechanism (OPN-006);
- production IdP/SSO dan kontrak API/webhook E-SIMI (OPN-007);
- retention, electronic signature, data classification, RPO/RTO final (OPN-008);
- production topology dan HA (OPN-009).

## Urutan implementasi berikutnya

1. Buat decision records OPN-001–009 bersama owner pada BRD.
2. Implement master/effective dating dan authorization assignments memakai hasil OPN-001/002.
3. Tambahkan E-SIMI adapter dan contract tests memakai hasil OPN-007.
4. Implement declarative ruleset + simulation tanpa free-form scripting.
5. Lengkapi workflow review/approval dan field operations berdasarkan OPN-002–006.
6. Tambahkan attachment quarantine/malware integration, reports, print, notifications, integration/E2E/security tests.

Tidak ada default gas threshold, approval matrix, atau safety checklist yang ditanam diam-diam di source code.
