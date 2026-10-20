import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { PermitCreate } from './permit-create';

describe('PermitCreate', () => {
  it('automatically selects JSA and Prosedur Pekerjaan outside the optional Bagian 4 list', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);

    httpTesting.expectOne('/api/v1/reference-data/mandatory-documents').flush([
      {
        code: 'JSA',
        label: 'Job Safety Analisis (JSA)',
        uploadCategory: 'JSA',
        requiresMetadata: true,
      },
      {
        code: 'WORK_PROCEDURE',
        label: 'Prosedur Pekerjaan',
        uploadCategory: 'SUPPORTING',
        requiresMetadata: false,
      },
      { code: 'ID', label: 'ID', uploadCategory: 'SUPPORTING', requiresMetadata: false },
      {
        code: 'BPJS_TK',
        label: 'BPJS TK',
        uploadCategory: 'SUPPORTING',
        requiresMetadata: false,
      },
      { code: 'FTW', label: 'FTW', uploadCategory: 'SUPPORTING', requiresMetadata: false },
    ]);
    httpTesting.expectOne('/api/v1/reference-data/supporting-documents').flush([
      {
        code: 'JSA',
        label: 'Job Safety Analisis (JSA)',
        templateColumn: 0,
        templateIndex: 0,
        required: true,
        requiresMetadata: true,
      },
      {
        code: 'WORK_PROCEDURE',
        label: 'Prosedur Pekerjaan',
        templateColumn: 0,
        templateIndex: 3,
        required: false,
        requiresMetadata: false,
      },
      {
        code: 'MSDS',
        label: 'MSDS',
        templateColumn: 1,
        templateIndex: 3,
        required: false,
        requiresMetadata: false,
      },
    ]);
    fixture.detectChanges();

    const mandatoryCards = fixture.nativeElement.querySelectorAll('.mandatory-document-option');
    expect(mandatoryCards.length).toBe(5);
    expect(fixture.nativeElement.textContent).toContain('Prosedur Pekerjaan');
    expect(fixture.nativeElement.textContent).toContain('BPJS TK');
    expect(fixture.nativeElement.textContent).not.toContain('E-SIMI');
    expect(fixture.nativeElement.textContent).not.toContain('Deklarasi SIMOPS');
    expect(fixture.nativeElement.querySelector('[formControlName="simopsDeclaration"]')).toBeNull();

    const additionalFieldset = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('fieldset'),
    ).find((fieldset) => fieldset.querySelector('legend')?.textContent?.includes('Bagian 4'));
    expect(additionalFieldset?.textContent).toContain(
      'JSA dan Prosedur Pekerjaan otomatis dicentang',
    );
    expect(additionalFieldset?.querySelectorAll('input').length).toBe(1);
    expect(additionalFieldset?.textContent).toContain('MSDS');
    const selectableLabels = Array.from<HTMLElement>(
      additionalFieldset?.querySelectorAll('label') ?? [],
    ).map((label) => label.textContent);
    expect(selectableLabels).not.toContainEqual(expect.stringContaining('Prosedur Pekerjaan'));

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    expect(form.controls['requiredDocumentCodes'].value).toEqual(['JSA', 'WORK_PROCEDURE']);
  });

  it('renders approved scoped locations as selectable options', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);
    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    httpTesting.expectOne('/api/v1/locations').flush({
      items: [
        { id: 'location-ho', code: 'HO', name: 'Wisma Nusantara' },
        { id: 'location-orf', code: 'ORF', name: 'Onshore Receiving Facility' },
      ],
      count: 2,
    });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([
      {
        permitClass: 'HotWork',
        options: [
          { code: 'HOT_GRINDING', label: 'Menggerinda' },
          { code: 'HOT_WELDING', label: 'Mengelas' },
        ],
      },
    ]);
    httpTesting.expectOne('/api/v1/reference-data/header-classifications').flush([
      {
        permitClass: 'HotWork',
        selectionMode: 'MULTIPLE',
        options: [
          { code: 'HOT_OPEN_FLAME', label: 'Api Terbuka' },
          { code: 'HOT_SPARK', label: 'Percikan Api' },
        ],
      },
    ]);
    fixture.detectChanges();

    const options = Array.from(
      fixture.nativeElement.querySelectorAll('select[formControlName="locationId"] option'),
    ).map((option) => (option as HTMLOptionElement).textContent?.trim());
    expect(options).toEqual(['Pilih lokasi', 'Wisma Nusantara', 'Onshore Receiving Facility']);
    const values = Array.from(
      fixture.nativeElement.querySelectorAll('select[formControlName="locationId"] option'),
    ).map((option) => (option as HTMLOptionElement).value);
    expect(values).toEqual(['', 'HO', 'ORF']);
  });

  it('uses the authenticated user as the draft sponsor', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);

    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    httpTesting.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([]);

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    expect(form.controls['sponsorId'].value).toBe('sponsor.only.demo');
  });

  it('uses checkboxes for HOT, radios for COLD, and no extra header choice for CSE', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);

    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    httpTesting.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([]);
    httpTesting.expectOne('/api/v1/reference-data/header-classifications').flush([
      {
        permitClass: 'HotWork',
        selectionMode: 'MULTIPLE',
        options: [
          { code: 'HOT_OPEN_FLAME', label: 'Api Terbuka' },
          { code: 'HOT_SPARK', label: 'Percikan Api' },
        ],
      },
      {
        permitClass: 'ColdWork',
        selectionMode: 'SINGLE',
        options: [
          { code: 'COLD_LOW_RISK', label: 'Low Risk' },
          { code: 'COLD_HIGH_RISK', label: 'High Risk' },
        ],
      },
      { permitClass: 'ConfinedSpaceEntry', selectionMode: 'NONE', options: [] },
    ]);
    fixture.detectChanges();

    const headerInputs = () =>
      Array.from<HTMLInputElement>(
        fixture.nativeElement.querySelectorAll('input[name="header-classification"]'),
      );
    expect(headerInputs().map((input) => input.type)).toEqual(['checkbox', 'checkbox']);

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    form.controls['permitClass'].setValue('ColdWork');
    fixture.detectChanges();
    expect(headerInputs().map((input) => input.type)).toEqual(['radio', 'radio']);

    form.controls['permitClass'].setValue('ConfinedSpaceEntry');
    fixture.detectChanges();
    expect(headerInputs().length).toBe(0);
    expect(form.controls['headerClassificationCodes'].valid).toBe(true);
  });

  it('allows multiple controlled work types to be selected', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);

    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    httpTesting.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([
      {
        permitClass: 'HotWork',
        options: [
          { code: 'HOT_GRINDING', label: 'Menggerinda' },
          { code: 'HOT_WELDING', label: 'Mengelas' },
        ],
      },
    ]);
    fixture.detectChanges();

    const checkboxes = Array.from<HTMLInputElement>(
      fixture.nativeElement.querySelectorAll('.work-type-option input'),
    );
    checkboxes.forEach((checkbox) => {
      checkbox.checked = true;
      checkbox.dispatchEvent(new Event('change'));
    });

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    expect(form.controls['workTypeCodes'].value).toEqual(['HOT_GRINDING', 'HOT_WELDING']);
  });

  it('requires a description only when the controlled Other work type is selected', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);

    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    httpTesting.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([
      {
        permitClass: 'HotWork',
        options: [
          { code: 'HOT_WELDING', label: 'Mengelas', requiresDetail: false },
          { code: 'HOT_OTHER', label: 'Lain - Lain :', requiresDetail: true },
        ],
      },
    ]);
    fixture.detectChanges();

    const otherCheckbox = Array.from<HTMLInputElement>(
      fixture.nativeElement.querySelectorAll('.work-type-option input'),
    )[1];
    otherCheckbox.checked = true;
    otherCheckbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    expect(fixture.nativeElement.querySelector('#other-work-type-description')).not.toBeNull();
    expect(form.controls['otherWorkTypeDescription'].hasError('required')).toBe(true);

    form.controls['otherWorkTypeDescription'].setValue('Pemanasan bearing');
    expect(form.controls['otherWorkTypeDescription'].valid).toBe(true);

    otherCheckbox.checked = false;
    otherCheckbox.dispatchEvent(new Event('change'));
    expect(form.controls['otherWorkTypeDescription'].value).toBe('');
    expect(form.controls['otherWorkTypeDescription'].hasError('required')).toBe(false);
  });

  it('submits the optional template reference fields without making hazard text authoritative', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    const httpTesting = TestBed.inject(HttpTestingController);

    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    httpTesting.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([
      {
        permitClass: 'HotWork',
        options: [{ code: 'HOT_WELDING', label: 'Mengelas', requiresDetail: false }],
      },
    ]);
    httpTesting.expectOne('/api/v1/reference-data/supporting-documents').flush([
      {
        code: 'JSA',
        label: 'Job Safety Analisis (JSA)',
        templateColumn: 0,
        templateIndex: 0,
        required: true,
        requiresMetadata: true,
      },
      {
        code: 'WORK_PROCEDURE',
        label: 'Prosedur Pekerjaan',
        templateColumn: 0,
        templateIndex: 3,
        required: true,
        requiresMetadata: false,
      },
    ]);

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    form.patchValue({
      title: 'Penggantian gasket',
      description: 'Sesuai JSA',
      locationId: 'ORF',
      performingAuthority: 'Pelaksana',
      company: 'PT Mitra',
      workTypeCodes: ['HOT_WELDING'],
      headerClassificationCodes: ['HOT_OPEN_FLAME', 'HOT_SPARK'],
      equipmentName: ' Gas inlet separator ',
      workOrderNumber: '   ',
      additionalHazardReference: ' Akses sisi utara licin ',
      plantArea: 'Area Metering',
      jsaDocumentNumber: 'JSA-001',
      jsaRevision: '1',
      jsaDate: '2026-09-16',
    });

    (fixture.componentInstance as unknown as { save(): void }).save();

    const request = httpTesting.expectOne('/api/v1/permits');
    expect(request.request.body.equipmentName).toBe('Gas inlet separator');
    expect(request.request.body.workOrderNumber).toBeNull();
    expect(fixture.nativeElement.querySelector('#work-order-help')?.textContent).toContain(
      'Opsional',
    );
    expect(request.request.body.additionalHazardReference).toBe('Akses sisi utara licin');
    expect(request.request.body.headerClassificationCodes).toEqual(['HOT_OPEN_FLAME', 'HOT_SPARK']);
    expect(request.request.body.clsrApplicable).toBe(false);
    expect(request.request.body.isolationPrecautionCodes).toEqual([]);
    expect(request.request.body.hazards).toEqual([]);
    expect(request.request.body.requiredDocumentCodes).toEqual(['JSA', 'WORK_PROCEDURE']);
  });

  function flushCatalogs(httpTesting: HttpTestingController, isDevelopmentIdentity: boolean) {
    httpTesting.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity,
    });
    httpTesting.expectOne('/api/v1/locations').flush({
      items: [
        { id: 'location-ho', code: 'HO', name: 'Wisma Nusantara' },
        { id: 'location-orf', code: 'ORF', name: 'Onshore Receiving Facility' },
      ],
      count: 2,
    });
    httpTesting.expectOne('/api/v1/reference-data/work-types').flush([
      {
        permitClass: 'HotWork',
        options: [
          { code: 'HOT_WELDING', label: 'Mengelas', requiresDetail: false },
          { code: 'HOT_OTHER', label: 'Lain-lain', requiresDetail: true },
        ],
      },
      {
        permitClass: 'ColdWork',
        options: [{ code: 'COLD_PAINTING', label: 'Pengecatan', requiresDetail: false }],
      },
      {
        permitClass: 'ConfinedSpaceEntry',
        options: [{ code: 'CSE_INSPECTION', label: 'Inspeksi', requiresDetail: false }],
      },
    ]);
    httpTesting.expectOne('/api/v1/reference-data/header-classifications').flush([
      {
        permitClass: 'HotWork',
        selectionMode: 'MULTIPLE',
        options: [
          { code: 'HOT_OPEN_FLAME', label: 'Api Terbuka' },
          { code: 'HOT_SPARK', label: 'Percikan Api' },
        ],
      },
      {
        permitClass: 'ColdWork',
        selectionMode: 'SINGLE',
        options: [
          { code: 'COLD_LOW_RISK', label: 'Low Risk' },
          { code: 'COLD_HIGH_RISK', label: 'High Risk' },
        ],
      },
      { permitClass: 'ConfinedSpaceEntry', selectionMode: 'NONE', options: [] },
    ]);
    httpTesting.expectOne('/api/v1/reference-data/mandatory-documents').flush([]);
    httpTesting.expectOne('/api/v1/reference-data/supporting-documents').flush([
      {
        code: 'JSA',
        label: 'Job Safety Analisis (JSA)',
        templateColumn: 0,
        templateIndex: 0,
        required: true,
        requiresMetadata: true,
      },
      {
        code: 'MSDS',
        label: 'MSDS',
        templateColumn: 1,
        templateIndex: 3,
        required: false,
        requiresMetadata: false,
      },
    ]);
  }

  it('hides the demo auto-fill button outside a server-asserted demo identity', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    flushCatalogs(TestBed.inject(HttpTestingController), false);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.demo-fill-button')).toBeNull();
  });

  it('fills every field with catalog-valid random values in demo mode', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    flushCatalogs(TestBed.inject(HttpTestingController), true);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.demo-fill-button') as HTMLButtonElement;
    expect(button?.textContent).toContain('Isi otomatis (demo)');
    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    expect(form.valid).toBe(false);

    for (let attempt = 0; attempt < 25; attempt++) {
      button.click();
      fixture.detectChanges();
      const value = form.getRawValue();

      expect(form.valid).toBe(true);
      expect(['HO', 'ORF']).toContain(value.locationId);
      expect(value.sponsorId).toBe('sponsor.only.demo');
      expect(value.requiredDocumentCodes.slice(0, 2)).toEqual(['JSA', 'WORK_PROCEDURE']);
      expect(value.workTypeCodes.length).toBeGreaterThan(0);
      if (value.permitClass === 'HotWork') {
        expect(value.headerClassificationCodes.length).toBeGreaterThan(0);
        expect(value.workTypeCodes.every((code: string) => code.startsWith('HOT_'))).toBe(true);
        if (value.workTypeCodes.includes('HOT_OTHER')) {
          expect(value.otherWorkTypeDescription.trim().length).toBeGreaterThan(0);
        }
      } else if (value.permitClass === 'ColdWork') {
        expect(value.headerClassificationCodes.length).toBe(1);
        expect(value.workTypeCodes).toEqual(['COLD_PAINTING']);
        expect(value.riskLevel).toBe(
          value.headerClassificationCodes[0] === 'COLD_LOW_RISK' ? 'Low' : 'High',
        );
      } else {
        expect(value.permitClass).toBe('ConfinedSpaceEntry');
        expect(value.headerClassificationCodes).toEqual([]);
        expect(value.workTypeCodes).toEqual(['CSE_INSPECTION']);
      }
      const validityHours =
        (new Date(value.validUntil).getTime() - new Date(value.validFrom).getTime()) / 3_600_000;
      expect(validityHours).toBeGreaterThan(0);
      expect(validityHours).toBeLessThan(7 * 24);
    }
  });

  it('does not expose CLSR or isolation precaution fields to the Sponsor', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[formControlName="clsrApplicable"]')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('[formControlName="isolationPrecautionCodes"]'),
    ).toBeNull();
  });
});
