import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PermitCreate } from './permit-create';

describe('PermitCreate', () => {
  it('renders approved scoped locations as selectable options', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitCreate],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitCreate);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/locations')
      .flush({
        items: [
          { id: 'location-ho', code: 'HO', name: 'Wisma Nusantara' },
          { id: 'location-orf', code: 'ORF', name: 'Onshore Receiving Facility' },
        ],
        count: 2,
      });
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
});
