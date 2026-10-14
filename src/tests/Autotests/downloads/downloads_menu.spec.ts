import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// #1550: the admin-only "Downloads" menu entry. The page picks a product and hands it to the existing
// /api/Agent/installer and /api/Agent/linux-installer endpoints. Whether the agent exe or the probe .deb
// is staged depends on how the server under test was built, so each card is checked in whichever state
// it is in: a staged one must issue the request for the chosen product, an unstaged one must say so
// instead of showing a button.
test.describe.configure({ mode: 'serial' });

test.describe('Downloads menu', () => {
  const productName = uniqueName('DlProduct');

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    try {
      await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
      await cleanup.product(page, productName);
    } finally {
      await page.close();
    }
  });

  test('Admin opens Downloads, picks a product and requests both bundles', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Products' }).click();
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible();

    await page.locator('#nav_link_Downloads').click();
    await expect(page).toHaveURL(/\/Downloads/);

    const cards = [
      { button: page.locator('#downloadsAgentButton'), notStaged: page.locator('#downloadsAgentNotStaged'), endpoint: '/api/Agent/installer?productId=' },
      { button: page.locator('#downloadsLinuxProbeButton'), notStaged: page.locator('#downloadsProbeNotStaged'), endpoint: '/api/Agent/linux-installer?productId=' },
    ];

    // Nothing chosen yet: every offered button is disabled.
    for (const card of cards) {
      if (await card.button.count())
        await expect(card.button).toBeDisabled();
      else
        await expect(card.notStaged).toBeVisible();
    }

    await page.locator('#downloadsProductFilter').fill(productName);
    const select = page.locator('#downloadsProductSelect');
    await select.selectOption({ label: productName });
    const productId = await select.inputValue();
    expect(productId).not.toBe('');

    for (const card of cards) {
      if (!await card.button.count())
        continue;

      await expect(card.button).toBeEnabled();
      // The response may be a bundle or an error toast (no https address, bundled default certificate);
      // what this page owns is that the chosen product's request is issued.
      const request = page.waitForRequest(r => r.url().includes(card.endpoint + productId));
      await card.button.click();
      await request;
    }
  });

  test('Viewer has no Downloads entry and is refused the page', async ({ page }) => {
    await login(page, testConfig.userName1, testConfig.user1password, testConfig.apiUrl);
    await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();

    await expect(page.locator('#nav_link_Downloads')).toHaveCount(0);

    const response = await page.goto('/Downloads');
    expect(response?.status()).toBe(401);
  });
});
