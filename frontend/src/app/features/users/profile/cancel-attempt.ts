export interface CancelAttempt {
  readonly closureId: string;
  readonly key: string;
}

export function nextCancelAttempt(last: CancelAttempt | null, closureId: string): CancelAttempt {
  if (last !== null && last.closureId === closureId) {
    return last;
  }
  return { closureId, key: crypto.randomUUID() };
}
