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

  it('presents the single completed HSE validation and atomic issuance decision', () => {
    const workflow: PermitWorkflow = {
      hse: {
        code: 'HSE',
        label: 'Validasi HSE',
        completed: true,
        actorId: 'hsse.validator.demo',
        statement: 'Sesuai.',
        completedAt: '2026-09-02T00:00:00Z',
      },
      approval: {
        completed: true,
        actorId: 'area.owner.orf.demo',
        actorPosition: 'Manager',
        capacity: 'MANAGER',
        principalManagerUserId: 'area.owner.orf.demo',
        principalPosition: 'Manager',
        authorizationId: 'authorization-id',
        actingAssignmentId: null,
        statement: 'Disetujui.',
        approvedAt: '2026-09-02T00:02:00Z',
      },
      suspension: {
        suspended: false,
        suspendedBy: null,
        reason: null,
        suspendedAt: null,
        resolvedBy: null,
        resolution: null,
        resolvedAt: null,
      },
      closure: {
        requested: false,
        printPackageId: null,
        signedFieldCopyAttachmentIds: [],
        requestedBy: null,
        completionStatement: null,
        requestedAt: null,
        revision: 0,
        replacementReason: null,
        closed: false,
        closedBy: null,
        closeStatement: null,
        closedAt: null,
      },
    };

    fixture.componentRef.setInput('workflow', workflow);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('1/1 selesai');
    expect(text).not.toContain('Distribusi Gas & Pengelolaan ORF');
    expect(text).toContain('area.owner.orf.demo');
    expect(text).toContain('Diterbitkan');
  });
});
