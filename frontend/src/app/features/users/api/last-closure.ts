export interface LastClosure {
  closureId: string;
  outcome: 'rejected';
  reason: string | null;
  decidedAt: string;
}
