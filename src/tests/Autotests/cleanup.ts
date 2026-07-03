import { type Page } from '@playwright/test';

const GUID_SRC = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';

/**
 * Best-effort teardown: delete a folder by its display name via the server's RemoveFolder endpoint,
 * located through the folder's EditFolder link in the Products tree. A no-op when the folder is
 * absent, and it never throws — so an afterEach hook can't fail an otherwise-green run. Reuses the
 * already-authenticated test page.
 */
export async function removeFolderByName(page: Page, name: string): Promise<void> {
  try {
    await page.goto('/Product/Index');
    await page.waitForLoadState('networkidle');

    const folderId = await page.evaluate(
      ({ target, guidSrc }) => {
        const guid = new RegExp(guidSrc, 'i');
        for (const a of Array.from(document.querySelectorAll('a[href*="/Folders/EditFolder?folderId="]'))) {
          const box = a.closest('.d-flex') ?? a.parentElement;
          const nameEl = box?.querySelector('div[style*="font-weight"], div[style*="bold"]');
          if ((nameEl?.textContent ?? '').trim() === target) {
            const href = a.getAttribute('href') ?? '';
            return (href.match(guid) ?? [])[0] ?? '';
          }
        }
        return '';
      },
      { target: name, guidSrc: GUID_SRC }
    );

    if (folderId)
      await page.request.post(`/Folders/RemoveFolder?folderId=${folderId}`);
  } catch {
    // best-effort cleanup — never fail the run on teardown
  }
}

/**
 * Best-effort teardown: delete a product by its display name via the server's RemoveProduct endpoint,
 * located through its `inputName_<guid>` anchor in the Products tree. No-op when absent; never throws.
 */
export async function removeProductByName(page: Page, name: string): Promise<void> {
  try {
    await page.goto('/Product/Index');
    await page.waitForLoadState('networkidle');

    const productId = await page.evaluate((target) => {
      for (const a of Array.from(document.querySelectorAll('a[id^="inputName_"]'))) {
        if ((a.textContent ?? '').trim() === target)
          return a.id.replace('inputName_', '');
      }
      return '';
    }, name);

    if (productId)
      await page.request.get(`/Product/RemoveProduct?product=${productId}`);
  } catch {
    // best-effort cleanup — never fail the run on teardown
  }
}
