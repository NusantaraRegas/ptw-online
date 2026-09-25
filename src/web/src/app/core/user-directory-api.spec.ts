import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UserAccount, UserDirectoryApi } from './user-directory-api';

describe('UserDirectoryApi', () => {
  it('updates an existing profile with concurrency protection', () => {
    TestBed.configureTestingModule({
      providers: [UserDirectoryApi, provideHttpClient(), provideHttpClientTesting()],
    });
    const api = TestBed.inject(UserDirectoryApi);
    const http = TestBed.inject(HttpTestingController);
    const user = {
      subjectId: 'operator.one',
      userName: 'operator.one',
      displayName: 'Operator One',
      eTag: '"user-v1"',
    } as UserAccount;
    const update = { displayName: 'Operator Satu', position: 'Operator', department: null };

    api.update(user, update).subscribe();

    const request = http.expectOne('/api/v1/admin/users/operator.one');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.headers.get('If-Match')).toBe('"user-v1"');
    expect(request.request.body).toEqual(update);
    request.flush({});
    http.verify();
  });
});
