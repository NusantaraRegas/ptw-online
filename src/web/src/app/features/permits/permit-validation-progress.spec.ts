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

  function issuedWorkflow(): PermitWorkflow {
    return {
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
  }

  it('presents HSE, Senior Officer, and Manager as a three-stage workflow', () => {
    fixture.componentRef.setInput('workflow', issuedWorkflow());
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('3/3 selesai');
    expect(text).not.toContain('Persetujuan PTW awal');
    expect(fixture.nativeElement.querySelectorAll('.validation-item')).toHaveLength(2);
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

  it('adds the Sponsor initial approval as the first stage of a Contractor-initiated PTW', () => {
    const workflow = issuedWorkflow();
    workflow.hse = { ...workflow.hse, completed: false, actorName: null, statement: null };
    workflow.areaOperations = { ...workflow.areaOperations, completed: false, statement: null };
    workflow.approval = { ...workflow.approval, completed: false, statement: null };
    workflow.initiation = {
      initiated: true,
      requesterId: 'requester.demo',
      requesterName: 'Pemohon (Kontraktor) Demo',
      status: 'AWAITING_SPONSOR_APPROVAL',
      submittedToSponsorAt: '2026-10-20T06:00:00Z',
      sponsorStatement: null,
      decidedAt: null,
    };

    fixture.componentRef.setInput('workflow', workflow);
    fixture.componentRef.setInput('sponsorName', 'Sponsor Demo');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('0/4 selesai');
    expect(text).toContain('Persetujuan Sponsor atas PTW awal');
    expect(text).toContain('Sponsor Demo');
    expect(text).not.toContain('requester.demo');
    const items = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('.validation-item'),
    );
    expect(items).toHaveLength(3);
    expect(items[0].textContent).toContain('Sponsor');
    expect(items[0].textContent).toContain('Menunggu');
    const markers = items.map((item) =>
      item.querySelector('.validation-marker')?.textContent?.trim(),
    );
    expect(markers).toEqual(['1', '2', '3']);
  });

  it('counts an approved Sponsor stage and shows the Sponsor note', () => {
    const workflow = issuedWorkflow();
    workflow.initiation = {
      initiated: true,
      requesterId: 'requester.demo',
      requesterName: 'Pemohon (Kontraktor) Demo',
      status: 'APPROVED',
      submittedToSponsorAt: '2026-10-20T06:00:00Z',
      sponsorStatement: 'Lampiran lengkap, diajukan ke PIC HSE.',
      decidedAt: '2026-10-20T07:00:00Z',
    };

    fixture.componentRef.setInput('workflow', workflow);
    fixture.componentRef.setInput('sponsorName', null);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('4/4 selesai');
    expect(text).toContain('Nama tidak tersedia');
    expect(text).toContain('Lampiran lengkap, diajukan ke PIC HSE.');
    expect(fixture.nativeElement.querySelector('.validation-item')?.classList).toContain(
      'completed',
    );
    expect(fixture.nativeElement.querySelectorAll('.decision-comment')).toHaveLength(4);
  });

  it('marks a returned Sponsor stage as halted without counting it', () => {
    const workflow = issuedWorkflow();
    workflow.initiation = {
      initiated: true,
      requesterId: 'requester.demo',
      requesterName: 'Pemohon (Kontraktor) Demo',
      status: 'RETURNED',
      submittedToSponsorAt: '2026-10-20T06:00:00Z',
      sponsorStatement: 'JSA belum revisi terbaru.',
      decidedAt: '2026-10-20T07:00:00Z',
    };

    fixture.componentRef.setInput('workflow', workflow);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('3/4 selesai');
    expect(text).toContain('Dikembalikan');
    expect(fixture.nativeElement.querySelector('.validation-item')?.classList).toContain('halted');
  });
});
