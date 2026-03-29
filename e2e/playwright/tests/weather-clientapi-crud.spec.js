import { expect, test } from '@playwright/test';

const clientApiBaseUrl = process.env.CLIENT_API_BASE_URL ?? 'http://127.0.0.1:3002';

const createPayload = (forecastId, location, temperatureC, summary) => ({
  forecastId,
  location,
  temperatureC,
  summary
});

const findForecast = (items, forecastId) =>
  items.find((item) => item.forecastId === forecastId);

test('client api can create, update, and delete weather forecast', async ({ playwright }) => {
  const api = await playwright.request.newContext({ baseURL: clientApiBaseUrl });
  const unique = `${Date.now()}`;
  const forecastId = `00000000-0000-0000-0000-${unique.slice(-12)}`;
  const createdLocation = `PW-API-${unique}`;
  const updatedLocation = `PW-API-U-${unique}`;

  const createResponse = await api.post('/api/weatherforecast', {
    data: createPayload(forecastId, createdLocation, 24, 'Warm')
  });

  expect(createResponse.ok(), `create failed: ${await createResponse.text()}`).toBeTruthy();

  const createJson = await createResponse.json();
  expect(createJson.success ?? true).toBeTruthy();

  await expect
    .poll(async () => {
      const listResponse = await api.get('/api/weatherforecast', {
        params: { waitForSortableId: createJson.sortableUniqueId ?? undefined }
      });

      if (!listResponse.ok()) {
        return null;
      }

      const items = await listResponse.json();
      return findForecast(items, forecastId)?.location ?? null;
    })
    .toBe(createdLocation);

  const updateResponse = await api.post('/api/weatherforecast/update-location', {
    data: {
      forecastId,
      newLocation: updatedLocation
    }
  });

  expect(updateResponse.ok(), `update failed: ${await updateResponse.text()}`).toBeTruthy();

  const updateJson = await updateResponse.json();
  expect(updateJson.success ?? true).toBeTruthy();

  await expect
    .poll(async () => {
      const listResponse = await api.get('/api/weatherforecast', {
        params: { waitForSortableId: updateJson.sortableUniqueId ?? undefined }
      });

      if (!listResponse.ok()) {
        return null;
      }

      const items = await listResponse.json();
      return findForecast(items, forecastId)?.location ?? null;
    })
    .toBe(updatedLocation);

  const deleteResponse = await api.post('/api/weatherforecast/delete', {
    data: {
      forecastId
    }
  });

  expect(deleteResponse.ok(), `delete failed: ${await deleteResponse.text()}`).toBeTruthy();

  const deleteJson = await deleteResponse.json();
  expect(deleteJson.success ?? true).toBeTruthy();

  await expect
    .poll(async () => {
      const listResponse = await api.get('/api/weatherforecast', {
        params: { waitForSortableId: deleteJson.sortableUniqueId ?? undefined }
      });

      if (!listResponse.ok()) {
        return null;
      }

      const items = await listResponse.json();
      return findForecast(items, forecastId) ?? null;
    })
    .toBeNull();

  await api.dispose();
});
