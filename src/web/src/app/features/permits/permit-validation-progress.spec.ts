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

  it('presents HSE, Senior Officer, and Manager as a three-stage workflow', () => {
    const workflow: PermitWorkflow = {
      hse: {
        code: 'HSE',
        label: 'Validasi HSE',
        completed: true,
        actorId: 'hsse.validator.demo',
        actorName: 'Darsono',
        statement: 'Sesuai.',
        completedAt: '2026-09-02T00:00:00Z',
        safetyEquipmentCodes: ['SAFETY_FIRE_EXTINGUISHER'],
      },
      areaOperations: {
        completed: true,
        actorId: 'area.senior-officer.orf.demo',
        actorName: 'Senior Officer ORF Demo',
        actorPosition: 'Senior Officer Distribusi Gas dan Manajemen ORF',
        authorizationId: 'senior-officer-authorization-id',
        conditionCodes: ['OPS_DEPRESSURIZED'],
        otherConditionDetail: null,
        statement: 'Kondisi operasi telah diperiksa.',
        reviewedAt: '2026-09-02T00:01:00Z',
      },
      approval: {
        completed: true,
        actorId: 'area.owner.orf.demo',
        actorName: 'Yosep Ismail Zulkarnain',
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
    expect(text).toContain('3/3 selesai');
    expect(text).toContain('Darsono');
    expect(text).toContain('Senior Officer ORF Demo');
    expect(text).not.toContain('Distribusi Gas & Pengelolaan ORF');
    expect(text).toContain('Yosep Ismail Zulkarnain');
    expect(text).not.toContain('hsse.validator.demo');
    expect(text).not.toContain('area.owner.orf.demo');
    expect(text).toContain('Diterbitkan');
    expect(text).toContain('Sesuai.');
    expect(text).toContain('Kondisi operasi telah diperiksa.');
    expect(text).toContain('Disetujui.');
    expect(fixture.nativeElement.querySelectorAll('.decision-comment')).toHaveLength(3);
  });
});
