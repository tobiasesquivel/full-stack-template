export interface LastClosure {
  closureId: string;
  outcome: 'rejected' | 'cancelled';
  reason: string | null;
  decidedAt: string;
}
