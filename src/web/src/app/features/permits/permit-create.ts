import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { IdentityApi } from '../../core/development-identity';
import { LocationApi, LocationOption } from '../../core/location-api';
import { PermitApi, PermitDraft, PermitWorkTypeOption } from '../../core/permit-api';

function localDate(hoursFromNow: number): string {
  const date = new Date(Date.now() + hoursFromNow * 3_600_000);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

@Component({
  selector: 'app-permit-create',
  imports: [ReactiveFormsModule, RouterLink],
  template: ` <div class="page-title">
      <div>
        <p class="eyebrow">Permit Management</p>
        <h1>Buat draft PTW</h1>
        <p class="subtitle">
          Draft belum memiliki nomor resmi dan belum mengizinkan pekerjaan dimulai.
        </p>
      </div>
      <a class="back" routerLink="/permits">← Kembali</a>
    </div>
    <form class="card" [formGroup]="form" (ngSubmit)="save()">
      <div class="form-head">
        <span>1</span>
        <div>
          <h2>Informasi pekerjaan</h2>
          <p>Isi data dasar dan klasifikasi. Rincian bahaya/kontrol bersumber dari JSA.</p>
        </div>
      </div>
      <div class="grid">
        <label class="wide"
          >Judul pekerjaan<input
            formControlName="title"
            placeholder="Contoh: Pengelasan support pipa"
        /></label>
        <label class="wide"
          >Uraian pekerjaan<textarea
            formControlName="description"
            rows="3"
            placeholder="Jelaskan ruang lingkup dan metode kerja"
          ></textarea>
        </label>
        <label
          >Lokasi<select formControlName="locationId" [attr.aria-describedby]="'location-help'">
            <option value="" disabled>
              {{ loadingLocations() ? 'Memuat lokasi...' : 'Pilih lokasi' }}
            </option>
            @for (location of locations(); track location.id) {
              <option [value]="location.code">
                {{ location.code }} &mdash; {{ location.name }}
              </option>
            }
          </select>
          <small id="location-help">
            @if (locationError()) {
              {{ locationError() }}
            } @else if (!loadingLocations() && locations().length === 0) {
              Tidak ada lokasi yang disetujui dan efektif untuk scope Anda.
            } @else {
              Hanya lokasi aktif sesuai scope Anda yang ditampilkan.
            }
          </small></label
        >
        <label
          >Perusahaan pelaksana<input formControlName="company" placeholder="PT Mitra Kerja"
        /></label>
        <label
          >Tipe pengaju<select formControlName="submitterType">
            <option value="USER_SPONSOR">User Sponsor</option>
            <option value="CONTRACTOR">Kontraktor</option>
          </select></label
        >
        <label
          >Nama pelaksana<input
            formControlName="performingAuthority"
            placeholder="Nama penanggung jawab"
        /></label>
        <label
          >Kelas izin<select formControlName="permitClass">
            <option value="HotWork">Pekerjaan Panas</option>
            <option value="ColdWork">Pekerjaan Dingin</option>
            <option value="ConfinedSpaceEntry">Memasuki Ruang Terbatas</option>
          </select></label
        >
        <label
          >Tingkat risiko<select formControlName="riskLevel">
            <option value="Low">Rendah</option>
            <option value="Medium">Sedang</option>
            <option value="High">Tinggi</option>
            <option value="Extreme">Ekstrem</option>
          </select></label
        >
        <fieldset class="work-types wide" aria-describedby="work-type-help">
          <legend>Jenis pekerjaan</legend>
          <small id="work-type-help">
            Pilih satu atau lebih jenis pekerjaan sesuai Bagian 1 formulir PTW.
          </small>
          @if (loadingWorkTypes()) {
            <p class="work-type-state">Memuat daftar jenis pekerjaan...</p>
          } @else if (workTypeError()) {
            <p class="work-type-state error-text" role="alert">{{ workTypeError() }}</p>
          } @else {
            <div class="work-type-options">
              @for (option of workTypeOptions(); track option.code) {
                <label class="work-type-option">
                  <input
                    type="checkbox"
                    [checked]="isWorkTypeSelected(option.code)"
                    (change)="toggleWorkType(option.code, $event)"
                  />
                  <span>{{ option.label }}</span>
                </label>
              }
            </div>
          }
          @if (form.controls.workTypeCodes.touched && form.controls.workTypeCodes.invalid) {
            <span class="field-error" role="alert">Pilih minimal satu jenis pekerjaan.</span>
          }
        </fieldset>
        <label>Equipment/tag<input formControlName="equipmentTag" /></label>
        <label>Plant/area<input formControlName="plantArea" /></label>
        <label class="clsr-option">
          <input type="checkbox" formControlName="clsrApplicable" />
          <span>CLSR berlaku</span>
        </label>
        <label class="wide"
          >Deklarasi SIMOPS<textarea formControlName="simopsDeclaration" rows="2"></textarea>
        </label>
        <label class="wide"
          >APD/perlengkapan safety <small>kode dipisahkan koma</small
          ><input formControlName="safetyEquipmentCodes"
        /></label>
        <label class="wide"
          >Isolation/precaution <small>kode dipisahkan koma</small
          ><input formControlName="isolationPrecautionCodes"
        /></label>
        <label>Nomor JSA<input formControlName="jsaDocumentNumber" /></label>
        <label>Revisi JSA<input formControlName="jsaRevision" /></label>
        <label>Tanggal JSA<input type="date" formControlName="jsaDate" /></label>
        <label
          >Nomor E-SIMI<input formControlName="eSimiNumber" placeholder="Akan divalidasi adapter"
        /></label>
        <label>Mulai<input type="datetime-local" formControlName="validFrom" /></label>
        <label
          >Selesai (maks. 7 hari)<input type="datetime-local" formControlName="validUntil"
        /></label>
      </div>
      @if (error()) {
        <div class="error">{{ error() }}</div>
      }
      <footer>
        <span>Data tersimpan sebagai DRAFT.</span
        ><button class="primary-button" type="submit" [disabled]="saving() || form.invalid">
          {{ saving() ? 'Menyimpan…' : 'Simpan draft' }}
        </button>
      </footer>
    </form>`,
  styles: [
    `
      .back {
        color: var(--nr-blue-dark);
        font-size: 11px;
        font-weight: 700;
        text-decoration: none;
      }
      form {
        max-width: 900px;
        margin: 0 auto;
        overflow: hidden;
      }
      .form-head {
        padding: 22px 26px;
        display: flex;
        gap: 13px;
        align-items: center;
        border-bottom: 1px solid #e5eaeb;
      }
      .form-head > span {
        width: 32px;
        height: 32px;
        display: grid;
        place-items: center;
        border-radius: 50%;
        color: white;
        background: linear-gradient(145deg, var(--nr-blue), #005f9b);
        font-size: 11px;
        font-weight: 800;
      }
      .form-head h2 {
        margin: 0 0 3px;
        color: var(--nr-ink);
        font-size: 15px;
      }
      .form-head p {
        margin: 0;
        color: #8c9ba0;
        font-size: 10px;
      }
      .grid {
        padding: 26px;
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 18px;
      }
      .wide {
        grid-column: 1/-1;
      }
      label {
        display: grid;
        gap: 7px;
        color: #516970;
        font-size: 10px;
        font-weight: 700;
      }
      label small {
        color: #98a6aa;
        font-weight: 400;
      }
      input,
      select,
      textarea {
        width: 100%;
        padding: 11px 12px;
        border: 1px solid #d7e0e2;
        border-radius: 7px;
        color: var(--nr-ink);
        background: white;
        font-size: 11px;
      }
      textarea {
        resize: vertical;
      }
      footer {
        min-height: 68px;
        padding: 13px 26px;
        display: flex;
        justify-content: space-between;
        align-items: center;
        border-top: 1px solid #e5eaeb;
        background: #f7fafc;
      }
      footer span {
        color: #87979c;
        font-size: 9px;
      }
      .error {
        margin: 0 26px 15px;
        padding: 11px;
        border-radius: 7px;
        color: var(--nr-red-dark);
        background: var(--nr-red-soft);
        font-size: 10px;
      }
      button:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
      @media (max-width: 650px) {
        .grid {
          grid-template-columns: 1fr;
          padding: 18px;
        }
        .wide {
          grid-column: auto;
        }
        footer {
          padding: 12px 18px;
        }
      }
    `,
  ],
})
export class PermitCreate {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(PermitApi);
  private readonly identityApi = inject(IdentityApi);
  private readonly locationApi = inject(LocationApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly locations = signal<LocationOption[]>([]);
  protected readonly loadingLocations = signal(true);
  protected readonly locationError = signal('');
  protected readonly workTypeCatalog = signal<Record<string, PermitWorkTypeOption[]>>({});
  protected readonly loadingWorkTypes = signal(true);
  protected readonly workTypeError = signal('');
  protected readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    description: ['', Validators.required],
    locationId: ['', Validators.required],
    sponsorId: ['', Validators.required],
    performingAuthority: ['', Validators.required],
    company: ['', Validators.required],
    submitterType: this.fb.nonNullable.control<'CONTRACTOR' | 'USER_SPONSOR'>('USER_SPONSOR', {
      validators: [Validators.required],
    }),
    permitClass: ['HotWork', Validators.required],
    riskLevel: ['High', Validators.required],
    workTypeCodes: this.fb.nonNullable.control<string[]>([], {
      validators: [Validators.required],
    }),
    equipmentTag: [''],
    plantArea: ['', Validators.required],
    clsrApplicable: [false],
    simopsDeclaration: [''],
    safetyEquipmentCodes: [''],
    isolationPrecautionCodes: [''],
    jsaDocumentNumber: ['', Validators.required],
    jsaRevision: ['', Validators.required],
    jsaDate: ['', Validators.required],
    validFrom: [localDate(1), Validators.required],
    validUntil: [localDate(9), Validators.required],
    eSimiNumber: [''],
  });

  constructor() {
    this.identityApi
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (identity) => this.form.controls.sponsorId.setValue(identity.userId),
        error: (response) => {
          this.error.set(
            response?.error?.detail ??
              'Identitas pengguna gagal dimuat. Muat ulang halaman sebelum menyimpan draft.',
          );
        },
      });

    this.locationApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.locations.set(page.items);
          this.loadingLocations.set(false);
        },
        error: (response) => {
          this.loadingLocations.set(false);
          this.locationError.set(
            response?.error?.detail ?? 'Daftar lokasi gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });

    this.api
      .listWorkTypes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.workTypeCatalog.set(
            Object.fromEntries(catalog.map((item) => [item.permitClass, item.options])),
          );
          this.loadingWorkTypes.set(false);
          this.reconcileWorkTypeSelection();
        },
        error: (response) => {
          this.loadingWorkTypes.set(false);
          this.workTypeError.set(
            response?.error?.detail ??
              'Daftar jenis pekerjaan gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });

    this.form.controls.permitClass.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reconcileWorkTypeSelection());
  }

  protected workTypeOptions(): PermitWorkTypeOption[] {
    return this.workTypeCatalog()[this.form.controls.permitClass.value] ?? [];
  }

  protected isWorkTypeSelected(code: string): boolean {
    return this.form.controls.workTypeCodes.value.includes(code);
  }

  protected toggleWorkType(code: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.form.controls.workTypeCodes.value;
    const next = checked
      ? [...new Set([...current, code])]
      : current.filter((item) => item !== code);
    this.form.controls.workTypeCodes.setValue(next);
    this.form.controls.workTypeCodes.markAsTouched();
  }

  protected save(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.error.set('');
    const value = this.form.getRawValue();
    const draft: PermitDraft = {
      ...value,
      validFrom: new Date(value.validFrom).toISOString(),
      validUntil: new Date(value.validUntil).toISOString(),
      eSimiExternalId: value.eSimiNumber || null,
      eSimiNumber: value.eSimiNumber || null,
      hazards: [],
      controls: [],
      requiredDocumentCodes: [],
      workTypeCode: null,
      equipmentTag: value.equipmentTag || null,
      plantArea: value.plantArea || null,
      simopsDeclaration: value.simopsDeclaration || null,
      safetyEquipmentCodes: this.split(value.safetyEquipmentCodes),
      isolationPrecautionCodes: this.split(value.isolationPrecautionCodes),
      jsaDocumentNumber: value.jsaDocumentNumber || null,
      jsaRevision: value.jsaRevision || null,
      jsaDate: value.jsaDate ? new Date(`${value.jsaDate}T00:00:00`).toISOString() : null,
    };
    this.api.create(draft).subscribe({
      next: () => void this.router.navigateByUrl('/permits'),
      error: (error) => {
        this.saving.set(false);
        this.error.set(
          error?.error?.detail ?? 'Draft gagal disimpan. Periksa koneksi API dan data formulir.',
        );
      },
    });
  }

  private split(value: string): string[] {
    return value
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);
  }

  private reconcileWorkTypeSelection(): void {
    const validCodes = new Set(this.workTypeOptions().map((option) => option.code));
    const current = this.form.controls.workTypeCodes.value;
    const next = current.filter((code) => validCodes.has(code));
    if (next.length !== current.length) {
      this.form.controls.workTypeCodes.setValue(next);
    }
  }
}
