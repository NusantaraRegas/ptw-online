import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PermitApi } from '../../core/permit-api';
import { PermitHistory } from './permit-history';

describe('PermitHistory', () => {
  it('shows revision and rejection comments as readable PTW history', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitHistory],
      providers: [
        {
          provide: PermitApi,
          useValue: {
            listActivity: () =>
              of({
                items: [
                  {
                    sequence: 2,
                    eventType: 'permit_rejected',
                    actorId: 'area.manager',
                    occurredAt: '2026-09-23T01:00:00Z',
                    payload: { reason: 'Kondisi operasi belum aman.' },
                    correlationId: 'correlation-2',
                  },
                  {
                    sequence: 1,
                    eventType: 'revision_requested',
                    actorId: 'hse.validator',
                    occurredAt: '2026-09-23T00:00:00Z',
                    payload: { Reason: 'JSA perlu diperbarui.' },
                    correlationId: 'correlation-1',
                  },
                ],
                count: 2,
              }),
            listVersions: () => of({ items: [], count: 0 }),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermitHistory);
    fixture.componentRef.setInput('permitId', 'permit-id');
    fixture.componentRef.setInput('revision', 1);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('PTW ditolak');
    expect(text).toContain('Kondisi operasi belum aman.');
    expect(text).toContain('Revisi diminta');
    expect(text).toContain('JSA perlu diperbarui.');
    expect(fixture.nativeElement.querySelectorAll('blockquote')).toHaveLength(2);
  });
});
