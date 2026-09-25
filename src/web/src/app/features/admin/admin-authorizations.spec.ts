import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminAuthorizations } from './admin-authorizations';

describe('AdminAuthorizations', () => {
  it('uses controlled role and location selectors with an optional end date', async () => {
    await TestBed.configureTestingModule({
      imports: [AdminAuthorizations],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(AdminAuthorizations);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/admin/authorizations').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/admin/users').flush({
      items: [
        {
          subjectId: 'area.manager',
          userName: 'area.manager',
          displayName: 'Manager Area',
          isActive: true,
        },
      ],
      count: 1,
    });
    http.expectOne('/api/v1/locations').flush({
      items: [{ id: 'location-id', code: 'ORF', name: 'Onshore Receiving Facility' }],
      count: 1,
    });
    http.expectOne('/api/v1/admin/authorizations/direct-role-options').flush({
      items: [
        {
          code: 'AreaOwnerManager',
          label: 'Manager Pemilik Wilayah',
          locationRequired: true,
        },
      ],
      count: 1,
    });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.page-title button') as HTMLButtonElement).click();
    fixture.detectChanges();

    setSelect(fixture, 'subjectId', 'area.manager');
    setSelect(fixture, 'roleCode', 'AreaOwnerManager');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Action codes');
    expect(fixture.nativeElement.textContent).toContain('Area kewenangan');
    const locationOption = fixture.nativeElement.querySelector(
      'select[formControlName="locationId"] option[value="location-id"]',
    ) as HTMLOptionElement;
    expect(locationOption.textContent?.trim()).toBe('Onshore Receiving Facility');
    expect(locationOption.textContent).not.toContain('ORF');

    setSelect(fixture, 'locationId', 'location-id');
    const neverExpires = fixture.nativeElement.querySelector(
      'input[formControlName="neverExpires"]',
    ) as HTMLInputElement;
    expect(neverExpires.checked).toBe(true);
    expect(
      fixture.nativeElement.querySelector('input[formControlName="effectiveUntil"]'),
    ).toBeNull();

    neverExpires.click();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('input[formControlName="effectiveUntil"]'),
    ).not.toBeNull();

    neverExpires.click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );

    const create = http.expectOne('/api/v1/admin/authorizations/direct');
    expect(create.request.body).toMatchObject({
      subjectId: 'area.manager',
      roleCode: 'AreaOwnerManager',
      locationId: 'location-id',
      effectiveUntil: null,
    });
    expect(create.request.body.actionCodes).toBeUndefined();
    create.flush({});
    http.verify();
  });

  it('edits a controlled direct assignment while it is still a draft', async () => {
    await TestBed.configureTestingModule({
      imports: [AdminAuthorizations],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(AdminAuthorizations);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const assignment = {
      id: 'assignment-id',
      subjectId: 'hse.validator',
      roleCode: 'HSEValidator',
      actionCodes: ['permit.validate'],
      locationId: null,
      includeDescendants: false,
      requiredCompetencyCodes: [],
      kind: 'DIRECT',
      sourceAuthorizationId: null,
      effectiveFrom: '2026-09-25T00:00:00.000Z',
      effectiveUntil: null,
      status: 'DRAFT',
      isEffective: false,
      version: 1,
      makerId: 'superadmin.local',
      checkerId: null,
      approvedAt: null,
      createdAt: '2026-09-25T00:00:00.000Z',
      updatedAt: '2026-09-25T00:00:00.000Z',
      eTag: '"authorization-v1"',
    };
    http.expectOne('/api/v1/admin/authorizations').flush({ items: [assignment], count: 1 });
    http.expectOne('/api/v1/admin/users').flush({
      items: [
        {
          subjectId: 'hse.validator',
          displayName: 'Validator HSE',
          isActive: true,
        },
      ],
      count: 1,
    });
    http.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/admin/authorizations/direct-role-options').flush({
      items: [{ code: 'HSEValidator', label: 'PIC HSE', locationRequired: false }],
      count: 1,
    });
    fixture.detectChanges();

    (
      fixture.nativeElement.querySelector(
        '.assignment-row .row-actions button',
      ) as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Edit draft assignment');
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );

    const update = http.expectOne('/api/v1/admin/authorizations/assignment-id/direct-draft');
    expect(update.request.method).toBe('PATCH');
    expect(update.request.headers.get('If-Match')).toBe('"authorization-v1"');
    expect(update.request.body).toMatchObject({
      subjectId: 'hse.validator',
      roleCode: 'HSEValidator',
      locationId: null,
      effectiveUntil: null,
    });
    update.flush({ ...assignment, version: 2, eTag: '"authorization-v2"' });
    http.verify();
  });
});

function setSelect(fixture: { nativeElement: HTMLElement }, name: string, value: string): void {
  const select = fixture.nativeElement.querySelector(
    `select[formControlName="${name}"]`,
  ) as HTMLSelectElement;
  select.value = value;
  select.dispatchEvent(new Event('change'));
}
