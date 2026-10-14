import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// #1550: the admin-only "Downloads" entry of the Configuration menu. The page picks a product and hands it to the existing
// /api/Agent/installer and /api/Agent/linux-installer endpoints. Whether the agent exe or the probe .deb
// is staged depends on how the server under test was built, so each card is checked in whichever state
// it is in: a staged one must issue the request for the chosen product, an unstaged one must say so
// instead of showing a button. The selection, filter and preselection checks do not depend on staging.
test.describe.configure({ mode: 'serial' });

test.describe('Downloads menu', () => {
  const productName = uniqueName('DlProduct');
  let productId = '';

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    try {
      await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
      await cleanup.product(page, productName);
    } finally {
      await page.close();
    }
  });

  test('Admin opens Downloads, filters and picks a product, and requests the staged bundles', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Products' }).click();
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible();

    // The entry lives in the Configuration dropdown, so open it first.
    await page.locator('#optionsDropdown').click();
    await page.locator('#nav_link_Downloads').click();
    await expect(page).toHaveURL(/\/Downloads/);

    const select = page.locator('#downloadsProductSelect');
    const cards = [
      { button: page.locator('#downloadsAgentButton'), notStaged: page.locator('#downloadsAgentNotStaged'), endpoint: '/api/Agent/installer?productId=' },
      { button: page.locator('#downloadsLinuxProbeButton'), notStaged: page.locator('#downloadsProbeNotStaged, #downloadsProbeAddressWarning'), endpoint: '/api/Agent/linux-installer?productId=' },
    ];

    // Nothing chosen yet: every offered button is disabled.
    for (const card of cards) {
      if (await card.button.count())
        await expect(card.button).toBeDisabled();
      else
        await expect(card.notStaged).toBeVisible();
    }

    // The filter hides every product but the matching one (the name is unique to this run).
    await page.locator('#downloadsProductFilter').fill(productName);
    const visibleOptions = select.locator('option:not([hidden])');
    await expect(visibleOptions).toHaveCount(1);
    await expect(visibleOptions).toHaveText(productName);
    expect(await select.locator('option').count()).toBeGreaterThan(1); // the self-monitoring product is hidden

    await select.selectOption({ label: productName });
    productId = await select.inputValue();
    expect(productId).not.toBe('');

    // Runs only for cards whose bundle is staged on the server under test; the CI image stages none,
    // so there this loop checks nothing and the not-staged notices above are what is asserted.
    for (const card of cards) {
      if (!await card.button.count())
        continue;

      await expect(card.button).toBeEnabled();
      // The response may be a bundle or an error toast (e.g. the bundled default certificate); what this
      // page owns is that the chosen product's request is issued.
      const request = page.waitForRequest(r => r.url().includes(card.endpoint + productId));
      await card.button.click();
      await request;
    }
  });

  test('?productId= preselects the product', async ({ adminPage: page }) => {
    expect(productId).not.toBe('');

    await page.goto(`/Downloads?productId=${productId}`);
    await expect(page.locator('#downloadsProductSelect')).toHaveValue(productId);
  });

  test('The product page links to Downloads with the product preselected', async ({ adminPage: page }) => {
    expect(productId).not.toBe('');

    await page.getByRole('link', { name: 'Products' }).click();
    await page.getByRole('link', { name: productName, exact: true }).click();

    await page.locator(`a[href*="/Downloads"][href*="productId=${productId}"]`).click();
    await expect(page).toHaveURL(new RegExp(`/Downloads(/Index)?\\?productId=${productId}`));
    await expect(page.locator('#downloadsProductSelect')).toHaveValue(productId);
  });

  test('Viewer has no Downloads entry and is refused the page', async ({ page }) => {
    await login(page, testConfig.userName1, testConfig.user1password, testConfig.apiUrl);
    await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();

    // The Configuration dropdown still opens for a viewer, with its viewer items, but without Downloads.
    await page.locator('#optionsDropdown').click();
    await expect(page.getByRole('link', { name: 'Access keys' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Users', includeHidden: true })).not.toBeAttached();
    await expect(page.locator('#nav_link_Downloads')).toHaveCount(0);

    const response = await page.goto('/Downloads');
    expect(response?.status()).toBe(401);
  });
});
