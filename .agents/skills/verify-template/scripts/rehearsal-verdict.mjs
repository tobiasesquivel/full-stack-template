export function stageVerdict(expected, checks) {
  const failedBy = [...new Set(checks.filter(check => !check.ok).map(check => check.release))].sort();
  const ok = failedBy.length === 0;
  const passed = expected === 'pass' ? ok : failedBy.length === 1 && failedBy[0] === 'previous';
  return { ok, failedBy, passed };
}
