import { test, expect, request as playwrightRequest } from '@playwright/test';

test('Create sensor via API', async () => {
  const apiContext = await playwrightRequest.newContext({
    baseURL: 'https://localhost:44333',
    ignoreHTTPSErrors: true,
  });

  const response = await apiContext.post('/api/Sensors/bool', {
    headers: {
      Key: '9643e4ce-1480-47f8-8186-644fec277f11',
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