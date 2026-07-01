import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  /* Run tests in files in parallel */
  testMatch: '**/*.spec.ts',
  
  fullyParallel: false,
  /* Fail the build on CI if you accidentally left test. only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,
  /* Run tests sequentially — all tests share the same admin session */
  workers: 1,
  /* Abort the whole suite well before the 6h GitHub Actions job cap so a hung test fails fast. */
  globalTimeout: 20 * 60 * 1000,
  /* GitHub annotations in the PR diff on CI; HTML report both locally and on CI (uploaded as artifact). */
  reporter: process.env.CI ? [['github'], ['html']] : 'html',
  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    // Явно указываем headless режим
    // Если переменная CI существует (на GitHub), то true, иначе false (для локальных тестов)
    headless: process.env.CI? true: false, 
    
    // Takes URL from GitHub Actions
    baseURL: process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:44333',
    
    // Ignor HTTPErrors
    ignoreHTTPSErrors: true,
    viewport: { width: 1280, height: 720 },
    trace: 'on-first-retry',
    video: 'on-first-retry',
  },

  /* Configure projects for major browsers */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
   ],
});

