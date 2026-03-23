import { test, expect, request as playwrightRequest } from '@playwright/test';
import { testConfig } from '../config.ts';

test('Create sensor via API', async () => {
  const { apiUrl, sensorApiKey } = testConfig;
  const baseURL = new URL(apiUrl).origin;

  const apiContext = await playwrightRequest.newContext({
    baseURL,
    ignoreHTTPSErrors: true,
  });

  const response = await apiContext.post('/api/Sensors/bool', {
    headers: {
      Key: sensorApiKey,
      ClientName: 'autotest-client',
    },
    data: {
      path: 'sensor1',
      value: false,
    },
  });

  expect(response.ok()).toBeTruthy();

  const body = await response.json();
  console.log(body);
});