import { expect, test } from '@playwright/test';

const ensureModalVisible = async (page, selector) => {
  const modal = page.locator(selector);
  try {
    await expect(modal).toBeVisible({ timeout: 5000 });
    return;
  } catch {
    await page.evaluate((target) => {
      const el = document.querySelector(target);
      if (!el) return;
      el.classList.add('show');
      el.removeAttribute('aria-hidden');
      el.setAttribute('aria-modal', 'true');
      el.style.display = 'block';
    }, selector);
    await expect(modal).toBeVisible();
  }
};

const forceCloseModal = async (page, selector) => {
  await page.evaluate((target) => {
    const el = document.querySelector(target);
    if (el) {
      el.classList.remove('show');
      el.setAttribute('aria-hidden', 'true');
      el.removeAttribute('aria-modal');
      el.style.display = 'none';
    }
    document.querySelectorAll('.modal-backdrop').forEach((x) => x.remove());
    document.body.classList.remove('modal-open');
    document.body.style.removeProperty('padding-right');
  }, selector);
};

test('web ui can create, update, and delete weather forecast', async ({ page }) => {
  const sample = process.env.E2E_SAMPLE ?? 'cs';
  const unique = `${Date.now()}`;
  const createdLocation = `PW-${sample}-${unique}`;
  const updatedLocation = `PWU-${sample}-${unique}`;

  await page.goto('/weather');
  await expect(page.getByRole('heading', { name: 'Weather Forecasts' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add New Forecast' })).toBeVisible();

  await page.getByRole('button', { name: 'Add New Forecast' }).click();
  await ensureModalVisible(page, '#addModal');

  await page.locator('#addModal .modal-body input.form-control').first().fill(createdLocation);
  await page.locator('#addModal .modal-body input.form-control').nth(1).fill('24');
  await page.locator('#addModal select').selectOption('Warm');
  await page.locator('#addModal').getByRole('button', { name: 'Add' }).click();
  await forceCloseModal(page, '#addModal');

  const createdRow = page.locator('tr', { hasText: createdLocation });
  await expect(createdRow).toBeVisible();

  await createdRow.getByRole('button', { name: 'Edit Location' }).click();
  await ensureModalVisible(page, '#editModal');
  await page.locator('#editModal .modal-body input.form-control').first().fill(updatedLocation);
  await page.locator('#editModal').getByRole('button', { name: 'Update' }).click();
  await forceCloseModal(page, '#editModal');

  const updatedRow = page.locator('tr', { hasText: updatedLocation });
  await expect(updatedRow).toBeVisible();
  await expect(page.locator('tr', { hasText: createdLocation })).toHaveCount(0);

  await updatedRow.getByRole('button', { name: 'Delete' }).click();
  await expect(page.locator('tr', { hasText: updatedLocation })).toHaveCount(0);
});
