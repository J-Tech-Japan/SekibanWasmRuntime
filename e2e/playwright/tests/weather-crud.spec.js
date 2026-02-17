import { expect, test } from '@playwright/test';

const encodeCommand = (command) => JSON.stringify(command);

test('serialized command execute + commit works', async ({ request }) => {
  const sample = process.env.E2E_SAMPLE ?? 'cs';
  const suffix = `${Date.now()}`;
  const forecastId = `pw-${sample}-${suffix}`;

  const createCommand = {
    forecastId,
    location: `Tokyo-${sample}`,
    temperatureC: 21,
    summary: 'Warm'
  };

  const execute = await request.post('/api/sekiban/serialized/command/execute', {
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

  const commit = await request.post('/api/sekiban/serialized/commit', {
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
});
