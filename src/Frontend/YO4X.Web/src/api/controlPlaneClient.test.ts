import { createControlPlaneClient } from './controlPlaneClient';
import { ApiProblemError } from './problemDetails';

describe('ControlPlaneClient', () => {
  it('uses the injected in-memory access token and authenticated browser credentials', async () => {
    window.__YO4X_AUTH__ = { getAccessToken: async () => 'ephemeral-token' };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      id: '10000000-0000-4000-8000-000000000001',
      maskedEmail: 'a***@example.test',
      emailVerified: true,
      securityState: 'ACTIVE',
      assurance: 'TOTP',
    }), { status: 200, headers: { 'content-type': 'application/json' } }));

    const client = createControlPlaneClient('https://control.example', fetchMock);
    await expect(client.getMe()).resolves.toEqual(expect.objectContaining({ assurance: 'TOTP' }));

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe('https://control.example/v1/me');
    expect(init?.credentials).toBe('include');
    expect(new Headers(init?.headers).get('authorization')).toBe('Bearer ephemeral-token');
    expect(init?.redirect).toBe('error');
  });

  it('preserves safe RFC 7807 metadata for unauthorized handling', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      type: 'https://errors.yo4x.test/authentication-required',
      title: 'Authentication is required.',
      status: 401,
      code: 'AUTHENTICATION_REQUIRED',
      correlationId: '80000000-0000-4000-8000-000000000008',
    }), { status: 401, headers: { 'content-type': 'application/problem+json' } }));

    const client = createControlPlaneClient('https://control.example', fetchMock);
    const error = await client.getMe().catch((reason: unknown) => reason);

    expect(error).toBeInstanceOf(ApiProblemError);
    expect((error as ApiProblemError).problem).toEqual(expect.objectContaining({
      status: 401,
      code: 'AUTHENTICATION_REQUIRED',
      correlationId: '80000000-0000-4000-8000-000000000008',
    }));
  });

  it('rejects successful responses with an unexpected content type', async () => {
    const fetchMock = vi.fn(async () => new Response('<html>not json</html>', {
      status: 200,
      headers: { 'content-type': 'text/html' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);
    await expect(client.getMe()).rejects.toThrow('unsupported response format');
  });

  it('deduplicates concurrent access-token reads', async () => {
    const getAccessToken = vi.fn(async () => 'concurrent-token');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ status: 'healthy' }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await Promise.all([client.getReadiness(), client.getReadiness(), client.getReadiness()]);

    expect(getAccessToken).toHaveBeenCalledOnce();
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('rejects unsafe access-token text before creating a request', async () => {
    window.__YO4X_AUTH__ = { getAccessToken: async () => 'token\r\ninjected' };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getReadiness()).rejects.toThrow('invalid access token');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('uses only the fixed compatibility route for a selected corpus', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      analyzedFileCount: 0,
      totalFileCount: 0,
      items: [],
    }), { status: 200, headers: { 'content-type': 'application/json' } }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.getStrategyCompatibility('0198f000-0000-7000-8000-000000000001');

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls[0]![0].toString()).toBe(
      'https://control.example/v1/strategy-source-corpora/0198f000-0000-7000-8000-000000000001/compatibility',
    );
  });

  it.each([
    '//evil.example/readiness',
    '/\\evil.example/readiness',
    '/readiness\u0000tail',
    '/safe/../unexpected',
  ])('rejects an API path escape before reading a token or issuing fetch: %s', async (path) => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getRuntimeReadiness(path)).rejects.toThrow(/API paths/u);
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects a configured API origin containing user information before authentication', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://user@control.example', fetchMock);

    await expect(client.getReadiness()).rejects.toThrow('must contain only an exact origin');
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each(['ftp://control.example', 'ws://control.example', 'wss://control.example'])(
    'rejects a non-HTTP API scheme before authentication: %s',
    async (origin) => {
      const getAccessToken = vi.fn(async () => 'must-not-be-read');
      window.__YO4X_AUTH__ = { getAccessToken };
      const fetchMock = vi.fn(async () => new Response());
      const client = createControlPlaneClient(origin, fetchMock);

      await expect(client.getReadiness()).rejects.toThrow('must use HTTP or HTTPS');
      expect(getAccessToken).not.toHaveBeenCalled();
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it('rejects an insecure same-origin fallback before reading authentication', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('', fetchMock, 'http://non-loopback.example');

    await expect(client.getReadiness()).rejects.toThrow('must use HTTPS');
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
