import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, errorMessage } from './api';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('api client', () => {
  it('sends the student id header and parses the response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([{ id: 'c_1' }]));
    vi.stubGlobal('fetch', fetchMock);

    const classes = await api.myHolds('20152691234567890');

    expect(classes).toEqual([{ id: 'c_1' }]);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain('/me/holds');
    expect((init as RequestInit).headers).toMatchObject({ 'X-Student-Id': '20152691234567890' });
  });

  it('turns a 409 API error into a readable ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(() =>
        Promise.resolve(jsonResponse({ error: 'NO_SEATS_AVAILABLE', message: 'Sorry, this class is full.' }, 409))
      )
    );

    await expect(api.createHold('c_1', '20152691234567890')).rejects.toMatchObject({
      code: 'NO_SEATS_AVAILABLE',
      status: 409
    });

    try {
      await api.createHold('c_1', '20152691234567890');
    } catch (error) {
      expect(errorMessage(error)).toBe('Sorry, this class is full.');
    }
  });

  it('falls back to a friendly message when the body is not JSON', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(() => Promise.resolve(new Response('<html>oops</html>', { status: 500 })))
    );

    await expect(api.listClasses()).rejects.toBeInstanceOf(ApiError);
    await expect(api.listClasses()).rejects.toMatchObject({
      message: expect.stringMatching(/server ran into a problem/i)
    });
  });

  it('explains a connection failure instead of leaving the user with a blank page', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    try {
      await api.listClasses();
      throw new Error('should have thrown');
    } catch (error) {
      expect(errorMessage(error)).toMatch(/cannot reach the admission server/i);
    }
  });
});
