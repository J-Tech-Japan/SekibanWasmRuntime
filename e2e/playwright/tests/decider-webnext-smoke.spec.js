import { expect, test } from '@playwright/test';

test('webnext can login, add weather, and display generated test data', async ({ page }) => {
  const requiresAuthFlow = process.env.SKIP_AUTH !== '1';
  const unique = `${Date.now()}`;
  const location = `PW-WN-${unique}`;
  const displayName = `PW User ${unique}`;
  const email = `pw.${unique}@example.com`;
  const password = 'Sekiban1234%';

  if (requiresAuthFlow) {
    await page.goto('/register');
    await page.getByLabel('Display Name').fill(displayName);
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByLabel('Confirm Password').fill(password);
    await page.getByRole('button', { name: 'Create Account' }).click();
    await expect(page).toHaveURL(/\/reservations$/);
    await expect(page.getByText(displayName)).toBeVisible();
  }

  await page.goto('/weather');
  await expect(page.getByRole('heading', { name: 'Weather Forecasts' })).toBeVisible();
  await page.getByRole('button', { name: 'Add Forecast' }).click();

  const addModal = page.locator('form').filter({ has: page.getByText('Add Weather Forecast') });
  await addModal.getByPlaceholder('e.g., Tokyo, New York').fill(location);
  await addModal.locator('input[type="date"]').fill('2026-03-29');
  await addModal.locator('input[type="number"]').fill('24');
  await addModal.locator('select').selectOption('Warm');
  await addModal.getByRole('button', { name: 'Add Forecast' }).click();

  await expect(page.getByText(location)).toBeVisible({ timeout: 60000 });

  if (!requiresAuthFlow) {
    return;
  }

  await page.goto('/');
  await page.getByRole('button', { name: 'Generate Test Data' }).click();
  await expect(page.getByText(/Created \d+ rooms and \d+ reservations/)).toBeVisible();

  await page.goto('/meeting-rooms');
  await expect(page.getByRole('heading', { name: 'Meeting Rooms' })).toBeVisible();
  await expect(page.getByText('Conference Room A').first()).toBeVisible({ timeout: 60000 });

  await page.goto('/reservations');
  await expect(page.getByRole('heading', { name: 'Reservations' })).toBeVisible();
  await expect(page.getByText('Team Standup').first()).toBeVisible({ timeout: 60000 });
});
