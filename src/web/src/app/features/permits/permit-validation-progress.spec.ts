import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PermitWorkflow } from '../../core/permit-api';
import { PermitValidationProgress } from './permit-validation-progress';

describe('PermitValidationProgress', () => {
  let fixture: ComponentFixture<PermitValidationProgress>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PermitValidationProgress],
    }).compileComponents();
    fixture = TestBed.createComponent(PermitValidationProgress);
  });

  it('presents completed HSSE validation and the area owner', () => {
    const workflow: PermitWorkflow = {
      hsse: {
        code: 'HSSE',
        label: 'Validasi HSSE',
        completed: true,
        actorId: 'hsse.validator.demo',
        statement: 'Sesuai.',
        completedAt: '2026-09-02T00:00:00Z',
      },
      gasDistribution: {
        code: 'GAS_DISTRIBUTION',
        label: 'Validasi Distribusi Gas & Pengelolaan ORF',
        completed: true,
        actorId: 'gas.validator.demo',
        statement: 'Sesuai.',
        completedAt: '2026-09-02T00:01:00Z',
      },
      approvedBy: 'area.owner.fsru.demo',
      approvalStatement: 'Disetujui.',
      approvedAt: '2026-09-02T00:02:00Z',
      suspension: {
        requested: false,
        requestedBy: null,
        reason: null,
        requestedAt: null,
        approved: false,
        approvedBy: null,
        approvalStatement: null,
        approvedAt: null,
      },
      completion: {
        sponsor: {
          code: 'SPONSOR',
          label: '',
          completed: false,
          actorId: null,
          statement: null,
          completedAt: null,
        },
        hsse: {
          code: 'HSSE',
          label: '',
          completed: false,
          actorId: null,
          statement: null,
          completedAt: null,
        },
        areaOwner: {
          code: 'AREA_OWNER',
          label: '',
          completed: false,
          actorId: null,
          statement: null,
          completedAt: null,
        },
      },
    };

    fixture.componentRef.setInput('workflow', workflow);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('1/1 selesai');
    expect(text).not.toContain('Distribusi Gas & Pengelolaan ORF');
    expect(text).toContain('area.owner.fsru.demo');
    expect(text).toContain('Disetujui');
  });
});
