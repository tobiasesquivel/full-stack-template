import { defineConfig } from '@playwright/test';
declare const process: { env: Record<string, string | undefined>; cwd(): string };

const baseURL = process.env['E2E_BASE_URL'];
if (!baseURL) {
  throw new Error(
    'Set E2E_BASE_URL to the frontUrl of a verify-template instance, for example http://127.0.0.1:4200',
  );
}

const outputSetting = process.env['E2E_OUTPUT_DIR'];
const outputDir =
  outputSetting === undefined || /^([A-Za-z]:)?[\\/]/.test(outputSetting)
    ? (outputSetting ?? 'test-results')
    : `${process.cwd()}/${outputSetting}`;

export default defineConfig({
  testDir: '.',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 10_000 },
  outputDir,
  reporter: [['list'], ['json', { outputFile: `${outputDir}/report.json` }]],
  use: {
    baseURL,
    channel: process.env['E2E_CHANNEL'] || undefined,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
