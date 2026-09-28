import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminUsers } from './admin-users';

describe('AdminUsers', () => {
  it('edits an existing user profile without changing its immutable identity', async () => {
    await TestBed.configureTestingModule({
      imports: [AdminUsers],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(AdminUsers);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const user = {
      subjectId: 'operator.one',
      userName: 'operator.one',
      displayName: 'Operator One',
      position: 'Operator',
      department: 'Operasi',
      isActive: true,
      version: 1,
      createdAt: '2026-09-25T00:00:00.000Z',
      updatedAt: '2026-09-25T00:00:00.000Z',
      lockedUntil: null,
      signature: null,
      eTag: '"user-v1"',
    };
    http.expectOne('/api/v1/admin/users').flush({ items: [user], count: 1 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.row-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('input[formControlName="subjectId"]').readOnly).toBe(
      true,
    );
    expect(fixture.nativeElement.querySelector('input[formControlName="userName"]').readOnly).toBe(
      true,
    );
    const password = fixture.nativeElement.querySelector(
      'input[formControlName="password"]',
    ) as HTMLInputElement;
    expect(password).not.toBeNull();
    expect(password.value).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Password lokal baru');
    expect(fixture.nativeElement.textContent).toContain('12-128 karakter');
    expect(fixture.nativeElement.textContent).toContain('Tidak memuat username');
    const displayName = fixture.nativeElement.querySelector(
      'input[formControlName="displayName"]',
    ) as HTMLInputElement;
    displayName.value = 'Operator Satu';
    displayName.dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );

    const update = http.expectOne('/api/v1/admin/users/operator.one');
    expect(update.request.method).toBe('PATCH');
    expect(update.request.headers.get('If-Match')).toBe('"user-v1"');
    expect(update.request.body).toEqual({
      displayName: 'Operator Satu',
      position: 'Operator',
      department: 'Operasi',
    });
    update.flush({ ...user, displayName: 'Operator Satu', eTag: '"user-v2"' });
    http.expectNone('/api/v1/admin/users/operator.one/password');
    http.verify();
  });

  it('sets a new local password after the profile update using the fresh ETag', async () => {
    await TestBed.configureTestingModule({
      imports: [AdminUsers],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(AdminUsers);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const user = {
      subjectId: 'operator.one',
      userName: 'operator.one',
      displayName: 'Operator One',
      position: 'Operator',
      department: 'Operasi',
      isActive: true,
      version: 1,
      createdAt: '2026-09-25T00:00:00.000Z',
      updatedAt: '2026-09-25T00:00:00.000Z',
      lockedUntil: null,
      signature: null,
      eTag: '"user-v1"',
    };
    http.expectOne('/api/v1/admin/users').flush({ items: [user], count: 1 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.row-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const password = fixture.nativeElement.querySelector(
      'input[formControlName="password"]',
    ) as HTMLInputElement;

    password.value = 'operator.one2026';
    password.dispatchEvent(new Event('input'));
    password.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Password lokal baru belum memenuhi syarat',
    );
    expect(fixture.nativeElement.querySelector('#user-password-error')).not.toBeNull();
    http.expectNone('/api/v1/admin/users/operator.one');

    password.value = 'SandiBaru2026Aman';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelectorAll('.password-requirements li[data-met="true"]').length,
    ).toBe(5);
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );

    const update = http.expectOne('/api/v1/admin/users/operator.one');
    expect(update.request.method).toBe('PATCH');
    update.flush({ ...user, version: 2, eTag: '"user-v2"' });

    const reset = http.expectOne('/api/v1/admin/users/operator.one/password');
    expect(reset.request.method).toBe('POST');
    expect(reset.request.headers.get('If-Match')).toBe('"user-v2"');
    expect(reset.request.body).toEqual({ password: 'SandiBaru2026Aman' });
    reset.flush({ ...user, version: 3, eTag: '"user-v3"' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    http.verify();
  });
});
