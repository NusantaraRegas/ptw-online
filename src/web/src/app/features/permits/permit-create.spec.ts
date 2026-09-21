import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { PermitCreate } from './permit-create';

describe('PermitCreate', () => {
  it('separates five mandatory uploads from the optional Bagian 4 checklist', async () => {
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
      { code: 'ID', label: 'ID', uploadCategory: 'SUPPORTING', requiresMetadata: false },
      {
        code: 'BPJS_TK',
        label: 'BPJS TK',
        uploadCategory: 'SUPPORTING',
        requiresMetadata: false,
      },
      { code: 'FTW', label: 'FTW', uploadCategory: 'SUPPORTING', requiresMetadata: false },
      {
        code: 'ESIMI',
        label: 'E-SIMI',
        uploadCategory: 'SUPPORTING',
        requiresMetadata: false,
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
    expect(fixture.nativeElement.textContent).toContain('BPJS TK');
    expect(fixture.nativeElement.textContent).toContain('E-SIMI');
    expect(fixture.nativeElement.textContent).not.toContain('Deklarasi SIMOPS');
    expect(fixture.nativeElement.querySelector('[formControlName="simopsDeclaration"]')).toBeNull();

    const additionalFieldset = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('fieldset'),
    ).find((fieldset) => fieldset.querySelector('legend')?.textContent?.includes('Bagian 4'));
    expect(additionalFieldset?.textContent).toContain('MSDS');
    expect(additionalFieldset?.textContent).not.toContain('Job Safety Analisis');
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
    expect(options).toEqual([
      'Pilih lokasi',
      'HO \u2014 Wisma Nusantara',
      'ORF \u2014 Onshore Receiving Facility',
    ]);
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
      workOrderNumber: ' WO-2026-001 ',
      additionalHazardReference: ' Akses sisi utara licin ',
      plantArea: 'Area Metering',
      jsaDocumentNumber: 'JSA-001',
      jsaRevision: '1',
      jsaDate: '2026-09-16',
    });

    (fixture.componentInstance as unknown as { save(): void }).save();

    const request = httpTesting.expectOne('/api/v1/permits');
    expect(request.request.body.equipmentName).toBe('Gas inlet separator');
    expect(request.request.body.workOrderNumber).toBe('WO-2026-001');
    expect(request.request.body.additionalHazardReference).toBe('Akses sisi utara licin');
    expect(request.request.body.headerClassificationCodes).toEqual(['HOT_OPEN_FLAME', 'HOT_SPARK']);
    expect(request.request.body.clsrApplicable).toBe(false);
    expect(request.request.body.isolationPrecautionCodes).toEqual([]);
    expect(request.request.body.hazards).toEqual([]);
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
