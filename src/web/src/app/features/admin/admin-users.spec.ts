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
    expect(fixture.nativeElement.querySelector('input[formControlName="password"]')).toBeNull();
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
    http.verify();
  });
});
