import { expect, test } from '@playwright/test';

const encodeCommand = (command) => JSON.stringify(command);
const sample = process.env.E2E_SAMPLE ?? 'cs';
const appHostKind = process.env.E2E_APPHOST_KIND ?? 'apphost';

test('serialized command execute + commit works', async ({ playwright }) => {
  test.skip(
    sample === 'rust' || appHostKind === 'generic',
    'Common runtime host and Rust sample execute commands through ClientApi; serialized command execute is not exposed.'
  );

  const wasmApiBaseUrl = process.env.WASM_API_BASE_URL ?? 'http://127.0.0.1:3000';
  const api = await playwright.request.newContext({ baseURL: wasmApiBaseUrl });
  const suffix = `${Date.now()}`;
  const forecastId = `pw-${sample}-${suffix}`;

  const createCommand = {
    forecastId,
    location: `Tokyo-${sample}`,
    temperatureC: 21,
    summary: 'Warm'
  };

  const execute = await api.post('/api/sekiban/serialized/command/execute', {
    data: {
      commandName: 'CreateWeatherForecast',
      commandJson: encodeCommand(createCommand),
      consistencyTags: null,
      options: null
    }
  });

  expect(execute.ok(), `execute failed: ${await execute.text()}`).toBeTruthy();
  const executeJson = await execute.json();
  expect(Array.isArray(executeJson.eventCandidates)).toBeTruthy();
  expect(executeJson.eventCandidates.length).toBeGreaterThan(0);

  const commit = await api.post('/api/sekiban/serialized/commit', {
    data: {
      eventCandidates: executeJson.eventCandidates.map((candidate) => ({
        payload: candidate.payloadBase64,
        eventPayloadName: candidate.eventPayloadName,
        tags: candidate.tags
      })),
      consistencyTags: executeJson.consistencyTags ?? []
    }
  });

  expect(commit.ok(), `commit failed: ${await commit.text()}`).toBeTruthy();
  const commitJson = await commit.json();
  expect(commitJson).toBeTruthy();

  await api.dispose();
});
