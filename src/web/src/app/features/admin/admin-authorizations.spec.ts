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
});

function setSelect(fixture: { nativeElement: HTMLElement }, name: string, value: string): void {
  const select = fixture.nativeElement.querySelector(
    `select[formControlName="${name}"]`,
  ) as HTMLSelectElement;
  select.value = value;
  select.dispatchEvent(new Event('change'));
}
