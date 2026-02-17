import { expect, test } from '@playwright/test';

test('web ui can create, update, and delete weather forecast', async ({ page }) => {
  const sample = process.env.E2E_SAMPLE ?? 'cs';
  const unique = `${Date.now()}`;
  const createdLocation = `PW-${sample}-${unique}`;
  const updatedLocation = `${createdLocation}-updated`;

  await page.goto('/weather');
  await expect(page.getByRole('heading', { name: 'Weather Forecasts' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add New Forecast' })).toBeVisible();

  await page.getByRole('button', { name: 'Add New Forecast' }).click();
  await expect(page.locator('#addModal')).toBeVisible();

  await page.locator('#addModal input').first().fill(createdLocation);
  await page.locator('#addModal input[type="number"]').fill('24');
  await page.locator('#addModal select').selectOption('Warm');
  await page.locator('#addModal').getByRole('button', { name: 'Add' }).click();

  const createdRow = page.locator('tr', { hasText: createdLocation });
  await expect(createdRow).toBeVisible();

  await createdRow.getByRole('button', { name: 'Edit Location' }).click();
  await expect(page.locator('#editModal')).toBeVisible();
  await page.locator('#editModal input').first().fill(updatedLocation);
  await page.locator('#editModal').getByRole('button', { name: 'Update' }).click();

  const updatedRow = page.locator('tr', { hasText: updatedLocation });
  await expect(updatedRow).toBeVisible();
  await expect(page.locator('tr', { hasText: createdLocation })).toHaveCount(0);

  await updatedRow.getByRole('button', { name: 'Delete' }).click();
  await expect(page.locator('tr', { hasText: updatedLocation })).toHaveCount(0);
});
