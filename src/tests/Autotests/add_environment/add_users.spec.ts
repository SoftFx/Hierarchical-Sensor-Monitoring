import { test } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { createUser, deleteUserIfPresent, openUsersPage } from '../users.ts';

// Loging
test('Add environment', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password, userName1, user1password, userName2, user2password } = testConfig;
  await login(page, admin_user, admin_user_password, apiUrl,);

  await openUsersPage(page);

  await deleteUserIfPresent(page, userName1);
  await createUser(page, userName1, user1password);

  await deleteUserIfPresent(page, userName2);
  await createUser(page, userName2, user2password);
})
