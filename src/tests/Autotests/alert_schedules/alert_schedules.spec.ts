import { type Page } from '@playwright/test';
import { test, expect, uniqueName } from '../fixtures.ts';

const defaultScheduleYaml = `daySchedules:
    - days: [Mon, Tue, Wed, Thu, Fri]
      windows:
        - { start: "09:00", end: "18:00" }`;

const editedScheduleYaml = `daySchedules:
    - days: [Mon, Wed, Fri]
      windows:
        - { start: "10:00", end: "16:00" }`;

const invalidScheduleYaml = {
  empty: '',
  brokenSyntax: `daySchedules:
    - days: [Mon`,
  reversedWindow: `daySchedules:
    - days: [Mon]
      windows:
        - { start: "18:00", end: "09:00" }`,
  invalidTime: `daySchedules:
    - days: [Mon]
      windows:
        - { start: "25:00", end: "26:00" }`,
  overlappingWindows: `daySchedules:
    - days: [Mon]
      windows:
        - { start: "09:00", end: "12:00" }
        - { start: "11:00", end: "13:00" }`,
  invalidDate: `daySchedules:
    - days: [Mon]
      windows:
        - { start: "09:00", end: "18:00" }
disabledDates: ["2026-02-30"]`,
  emptyDaySchedules: `daySchedules: []`,
  customDateWithScheduleTypeAndWindows: `daySchedules:
    - days: [Mon]
      windows:
        - { start: "09:00", end: "18:00" }
overrides:
  customScheduleDates:
    - date: "2026-03-23"
      scheduleType: "Mon"
      windows:
        - { start: "11:00", end: "16:00" }`,
};

function exactText(text: string): RegExp {
  return new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);
}

async function navigateToAlertSchedules(page: Page): Promise<void> {
  await page.goto('/AlertSchedules');
  await expect(page.getByRole('heading', { name: 'Alert Schedules' })).toBeVisible();
}

async function waitForScheduleEditor(page: Page): Promise<void> {
  await page.waitForFunction(() => {
    const editor = (window as any).scheduleEditor;
    const container = document.getElementById('alert-schedule-editor-container');

    return !!editor && !!container && container.contains(editor.dom);
  });
}

async function openNewScheduleModal(page: Page): Promise<void> {
  await navigateToAlertSchedules(page);
  await page.getByRole('button', { name: 'Add Schedule' }).click();
  await expect(page.locator('#alertScheduleModal')).toBeVisible();
  await expect(page.locator('#alertScheduleForm')).toBeVisible();
  await waitForScheduleEditor(page);
}

async function openEditScheduleModal(page: Page, name: string): Promise<void> {
  await navigateToAlertSchedules(page);
  await openScheduleRowMenu(page, name);
  await page.locator('.dropdown-menu.show a.schedule-edit').click();
  await expect(page.locator('#alertScheduleModal')).toBeVisible();
  await waitForScheduleEditor(page);
  await expect(page.locator('#Name')).toHaveValue(name);
}

async function setScheduleEditorText(page: Page, yaml: string): Promise<void> {
  await page.locator('#Schedule').evaluate((element, value) => {
    (element as HTMLTextAreaElement).value = value;
  }, yaml);

  await page.evaluate((value) => {
    const editor = (window as any).scheduleEditor;
    editor.dispatch({
      changes: {
        from: 0,
        to: editor.state.doc.length,
        insert: value,
      },
    });
  }, yaml);
}

async function getScheduleEditorText(page: Page): Promise<string> {
  return page.evaluate(() => (window as any).scheduleEditor.state.doc.toString());
}

async function fillScheduleForm(page: Page, name: string, yaml = defaultScheduleYaml, timezone = 'UTC'): Promise<void> {
  await page.locator('#Name').fill(name);
  await page.locator('#Timezone').selectOption(timezone);
  await setScheduleEditorText(page, yaml);
  await expect(page.locator('#submitScheduleForm')).toBeEnabled();
}

function scheduleRows(page: Page, name: string) {
  return page.getByRole('row').filter({
    has: page.getByRole('cell', { name, exact: true }),
  });
}

async function expectScheduleCount(page: Page, name: string, count: number): Promise<void> {
  await expect(scheduleRows(page, name)).toHaveCount(count);
}

async function createSchedule(page: Page, name: string, yaml = defaultScheduleYaml, timezone = 'UTC'): Promise<void> {
  await openNewScheduleModal(page);
  await fillScheduleForm(page, name, yaml, timezone);
  await page.locator('#submitScheduleForm').click();
  await expect(page.locator('#alertScheduleModal')).toBeHidden();
  await expectScheduleCount(page, name, 1);
}

async function openScheduleRowMenu(page: Page, name: string): Promise<void> {
  const rows = scheduleRows(page, name);
  await expect(rows).toHaveCount(1);
  await rows.nth(0).locator('button[data-bs-toggle="dropdown"]').click();
  await expect(page.locator('.dropdown-menu.show')).toBeVisible();
}

async function editSchedule(page: Page, oldName: string, newName: string, yaml = editedScheduleYaml): Promise<void> {
  await openEditScheduleModal(page, oldName);
  await fillScheduleForm(page, newName, yaml);
  await page.locator('#submitScheduleForm').click();
  await expect(page.locator('#alertScheduleModal')).toBeHidden();
  await expectScheduleCount(page, oldName, 0);
  await expectScheduleCount(page, newName, 1);
}

async function removeOneSchedule(page: Page, name: string): Promise<void> {
  await navigateToAlertSchedules(page);
  await openScheduleRowMenu(page, name);

  const rows = scheduleRows(page, name);
  await Promise.all([
    page.waitForLoadState('domcontentloaded').catch(() => undefined),
    rows.nth(0).getByRole('button', { name: 'Remove' }).click(),
  ]);

  await navigateToAlertSchedules(page);
}

async function cleanupSchedules(page: Page, ...names: string[]): Promise<void> {
  await navigateToAlertSchedules(page);

  for (const name of names) {
    for (let attempt = 0; attempt < 5; attempt++) {
      const rows = scheduleRows(page, name);
      const count = await rows.count();

      if (count === 0)
        break;

      await rows.nth(0).locator('button[data-bs-toggle="dropdown"]').click();
      await rows.nth(0).getByRole('button', { name: 'Remove' }).click();
      await page.waitForLoadState('domcontentloaded').catch(() => undefined);
      await navigateToAlertSchedules(page);
    }
  }
}

async function closeScheduleModal(page: Page): Promise<void> {
  await page.locator('#alertScheduleModal .btn-close').click();
  await expect(page.locator('#alertScheduleModal')).toBeHidden();
}

async function cancelScheduleModal(page: Page): Promise<void> {
  await page.locator('#alertScheduleModal').getByRole('button', { name: 'Cancel' }).click();
  await expect(page.locator('#alertScheduleModal')).toBeHidden();
}

async function submitAndExpectScheduleValidation(page: Page, name: string): Promise<void> {
  await expect(page.locator('#submitScheduleForm')).toBeEnabled();
  await page.locator('#submitScheduleForm').click();
  await expect(page.locator('#alertScheduleModal')).toBeVisible();
  await expect(page.locator('#alertScheduleForm .text-danger').filter({ hasText: /\S/ }).first()).toBeVisible();
  await expectScheduleCount(page, name, 0);
}

async function openAlertPolicyPartial(page: Page) {
  await page.goto('/Home/AddDataPolicy?type=0&entityId=00000000-0000-0000-0000-000000000000');

  const select = page.locator('select[name="ScheduleId"]');
  await expect(select).toBeVisible();

  return select;
}

async function expectScheduleOption(page: Page, name: string, count: number): Promise<void> {
  const select = await openAlertPolicyPartial(page);
  await expect(select.locator('option').filter({ hasText: exactText(name) })).toHaveCount(count);
}

test.describe('Alert schedules', () => {
  test('Requires authentication for the schedules page', async ({ page }) => {
    await page.goto('/AlertSchedules');
    await expect(page.getByRole('textbox', { name: 'Username' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Password' })).toBeVisible();
  });

  test('Create schedule', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('AlertSchedule');

    try {
      await createSchedule(page, scheduleName);
      await expectScheduleCount(page, scheduleName, 1);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Modify schedule', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('AlertSchedule');
    const editedName = `${scheduleName}_edited`;

    try {
      await createSchedule(page, scheduleName);
      await editSchedule(page, scheduleName, editedName);
    } finally {
      await cleanupSchedules(page, scheduleName, editedName);
    }
  });

  test('Delete schedule', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('AlertSchedule');

    try {
      await createSchedule(page, scheduleName);
      await removeOneSchedule(page, scheduleName);
      await expectScheduleCount(page, scheduleName, 0);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject schedules with the same name', async ({ adminPage: page }) => {
    test.fail(true, 'Known product gap: Alert Schedules currently allow duplicate names.');

    const scheduleName = uniqueName('DuplicateAlertSchedule');

    try {
      await createSchedule(page, scheduleName);
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName);
      await submitAndExpectScheduleValidation(page, scheduleName);
      await expectScheduleCount(page, scheduleName, 1);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Disable Save when name is empty', async ({ adminPage: page }) => {
    await openNewScheduleModal(page);
    await page.locator('#Name').fill('');
    await page.locator('#Timezone').selectOption('UTC');
    await setScheduleEditorText(page, defaultScheduleYaml);
    await expect(page.locator('#submitScheduleForm')).toBeDisabled();
    await closeScheduleModal(page);
  });

  test('Disable Save when timezone is empty', async ({ adminPage: page }) => {
    await openNewScheduleModal(page);
    await page.locator('#Name').fill(uniqueName('AlertSchedule'));
    await page.locator('#Timezone').selectOption('');
    await setScheduleEditorText(page, defaultScheduleYaml);
    await expect(page.locator('#submitScheduleForm')).toBeDisabled();
    await closeScheduleModal(page);
  });

  test('Disable Save for broken YAML syntax', async ({ adminPage: page }) => {
    await openNewScheduleModal(page);
    await page.locator('#Name').fill(uniqueName('AlertSchedule'));
    await page.locator('#Timezone').selectOption('UTC');
    await setScheduleEditorText(page, invalidScheduleYaml.brokenSyntax);
    await expect(page.locator('#submitScheduleForm')).toBeDisabled();
    await closeScheduleModal(page);
  });

  test('Reject empty schedule YAML', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.empty);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject schedule window where start is after end', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.reversedWindow);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject invalid schedule time value', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.invalidTime);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject overlapping schedule windows', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.overlappingWindows);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject invalid disabled date', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.invalidDate);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject empty daySchedules collection', async ({ adminPage: page }) => {
    test.fail(true, 'Known product gap: the parser currently accepts daySchedules: [].');

    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.emptyDaySchedules);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reject custom schedule date with scheduleType and windows together', async ({ adminPage: page }) => {
    test.fail(true, 'Known product gap: customScheduleDates currently accept both scheduleType and windows.');

    const scheduleName = uniqueName('InvalidAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await fillScheduleForm(page, scheduleName, invalidScheduleYaml.customDateWithScheduleTypeAndWindows);
      await submitAndExpectScheduleValidation(page, scheduleName);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Create schedule from set sample link', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('SampleAlertSchedule');

    try {
      await openNewScheduleModal(page);
      await page.locator('#Name').fill(scheduleName);
      await page.locator('#Timezone').selectOption('UTC');
      await setScheduleEditorText(page, '');
      await page.locator('#setSampleLink').click();

      const sampleText = await getScheduleEditorText(page);
      expect(sampleText).toContain('daySchedules');
      expect(sampleText).toContain('disabledDates');

      await expect(page.locator('#submitScheduleForm')).toBeEnabled();
      await page.locator('#submitScheduleForm').click();
      await expect(page.locator('#alertScheduleModal')).toBeHidden();
      await expectScheduleCount(page, scheduleName, 1);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Cancel new schedule without saving it', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('CanceledAlertSchedule');

    await openNewScheduleModal(page);
    await fillScheduleForm(page, scheduleName);
    await cancelScheduleModal(page);
    await expectScheduleCount(page, scheduleName, 0);
  });

  test('Close new schedule with X without saving it', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('ClosedAlertSchedule');

    await openNewScheduleModal(page);
    await fillScheduleForm(page, scheduleName);
    await closeScheduleModal(page);
    await expectScheduleCount(page, scheduleName, 0);
  });

  test('Cancel edit keeps original schedule values', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('AlertSchedule');
    const canceledName = `${scheduleName}_canceled`;

    try {
      await createSchedule(page, scheduleName);
      await openEditScheduleModal(page, scheduleName);
      await fillScheduleForm(page, canceledName, editedScheduleYaml);
      await cancelScheduleModal(page);

      await expectScheduleCount(page, scheduleName, 1);
      await expectScheduleCount(page, canceledName, 0);
    } finally {
      await cleanupSchedules(page, scheduleName, canceledName);
    }
  });

  test('Persist schedule after reload and reopen edit modal', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('PersistentAlertSchedule');

    try {
      await createSchedule(page, scheduleName, editedScheduleYaml);
      await page.reload();
      await expectScheduleCount(page, scheduleName, 1);

      await openEditScheduleModal(page, scheduleName);
      await expect(page.locator('#Name')).toHaveValue(scheduleName);
      await expect(page.locator('#Timezone')).toHaveValue('UTC');

      const editorText = await getScheduleEditorText(page);
      expect(editorText).toContain('days: [Mon, Wed, Fri]');
      expect(editorText).toContain('start: "10:00"');

      await closeScheduleModal(page);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Show schedule in alert dropdown and remove it after deletion', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('DropdownAlertSchedule');

    try {
      await createSchedule(page, scheduleName);

      const select = await openAlertPolicyPartial(page);
      await expect(select.locator('option').filter({ hasText: exactText('any time') })).toHaveCount(1);
      await expect(select.locator('option').filter({ hasText: exactText(scheduleName) })).toHaveCount(1);

      await removeOneSchedule(page, scheduleName);
      await expectScheduleOption(page, scheduleName, 0);
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  test('Reflect renamed schedule in alert dropdown', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('DropdownAlertSchedule');
    const editedName = `${scheduleName}_edited`;

    try {
      await createSchedule(page, scheduleName);
      await expectScheduleOption(page, scheduleName, 1);

      await editSchedule(page, scheduleName, editedName);
      await expectScheduleOption(page, scheduleName, 0);
      await expectScheduleOption(page, editedName, 1);
    } finally {
      await cleanupSchedules(page, scheduleName, editedName);
    }
  });

  test('Show zero affected sensors for an unused schedule', async ({ adminPage: page }) => {
    const scheduleName = uniqueName('UnusedAlertSchedule');

    try {
      await createSchedule(page, scheduleName);
      const affectedSensorsCell = scheduleRows(page, scheduleName).locator('td').nth(2);

      await expect(affectedSensorsCell).toHaveText('0');
      await expect(affectedSensorsCell).toHaveAttribute('title', '');
    } finally {
      await cleanupSchedules(page, scheduleName);
    }
  });

  // The scenarios below are deferred (test.fixme) because they depend on server behaviour that is not
  // yet implemented — leaving them as documented placeholders is more honest than fake-green empty
  // bodies. Each needs a *saved alert bound to a schedule*, created through the sensor alert editor
  // (HomeController.EditAlerts / select[name="ScheduleId"]); the first two also need product features
  // that do not exist yet:
  //   * AlertSchedulesController.Remove(Guid) deletes unconditionally — there is no "used by a saved
  //     alert" guard, so delete-prevention cannot be asserted.
  //   * Alert Schedules is gated only by [Authorize]; there is no schedule-management permission, so a
  //     per-permission denial cannot be asserted (any authenticated user has full access).
  // The remaining two ("affected sensors count > 0" and "binding survives an edit") are implementable
  // once a helper exists to attach an alert to a schedule; tracked as follow-up under #1199.
  test.fixme('Prevent deleting a schedule that is used by a saved alert', async () => {});
  test.fixme('Show affected sensors count and tooltip for schedules used by saved alerts', async () => {});
  test.fixme('Keep saved alert binding after editing a linked schedule', async () => {});
  test.fixme('Deny Alert Schedules access for a user without schedule-management permissions', async () => {});
});
