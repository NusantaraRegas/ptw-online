import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PermitApi, PermitDraft } from '../../core/permit-api';

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
          <p>Isi data dasar, klasifikasi, bahaya, dan kontrol awal.</p>
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
        <label>Lokasi<input formControlName="locationId" placeholder="PROCESS-AREA-A" /></label>
        <label
          >Perusahaan pelaksana<input formControlName="company" placeholder="PT Mitra Kerja"
        /></label>
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
        <label
          >Nomor E-SIMI<input formControlName="eSimiNumber" placeholder="Akan divalidasi adapter"
        /></label>
        <label>Mulai<input type="datetime-local" formControlName="validFrom" /></label>
        <label
          >Selesai (maks. 7 hari)<input type="datetime-local" formControlName="validUntil"
        /></label>
        <label class="wide"
          >Bahaya <small>pisahkan dengan koma</small
          ><input formControlName="hazards" placeholder="Api terbuka, gas mudah terbakar"
        /></label>
        <label class="wide"
          >Kontrol <small>pisahkan dengan koma</small
          ><input formControlName="controls" placeholder="Fire watch, APAR, barricade"
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
  private readonly router = inject(Router);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    description: ['', Validators.required],
    locationId: ['', Validators.required],
    sponsorId: ['sponsor.demo', Validators.required],
    performingAuthority: ['', Validators.required],
    company: ['', Validators.required],
    permitClass: ['HotWork', Validators.required],
    riskLevel: ['High', Validators.required],
    validFrom: [localDate(1), Validators.required],
    validUntil: [localDate(9), Validators.required],
    eSimiNumber: [''],
    hazards: ['', Validators.required],
    controls: ['', Validators.required],
  });
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
      hazards: this.split(value.hazards),
      controls: this.split(value.controls),
      requiredDocumentCodes: [],
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
}
