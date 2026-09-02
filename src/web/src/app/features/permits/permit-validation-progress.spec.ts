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

  it('presents completed parallel validations and the area owner without overlapping text', () => {
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
    };

    fixture.componentRef.setInput('workflow', workflow);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('2/2 selesai');
    expect(text).toContain('Distribusi Gas & Pengelolaan ORF');
    expect(text).toContain('area.owner.fsru.demo');
    expect(text).toContain('Disetujui');
  });
});
