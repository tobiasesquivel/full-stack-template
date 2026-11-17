export interface CreateAttempt {
  readonly title: string;
  readonly key: string;
}

export function nextCreateAttempt(last: CreateAttempt | null, title: string): CreateAttempt {
  if (last !== null && last.title === title) {
    return last;
  }
  return { title, key: crypto.randomUUID() };
}
