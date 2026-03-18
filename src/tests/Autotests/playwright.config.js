import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  /* Run tests in files in parallel */
  testMatch: '**/*.spec.ts',
  
  fullyParallel: true,
  /* Fail the build on CI if you accidentally left test. only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,
  /* Opt out of parallel tests on CI. */
  workers: process.env.CI? 1: undefined,
  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: 'html',
  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    // Явно указываем headless режим
    // Если переменная CI существует (на GitHub), то true, иначе false (для локальных тестов)
    headless: process.env.CI? true: false, 
    
    // Takes URL from GitHub Actions
    baseURL: process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:44333',
    
    // Ignor HTTPErrors
    ignoreHTTPSErrors: true,
    trace: 'on-first-retry',
  },

  /* Configure projects for major browsers */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
   ],
});

