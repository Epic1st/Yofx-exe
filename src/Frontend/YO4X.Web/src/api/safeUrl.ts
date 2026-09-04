const forbiddenUrlText = /[\\\u0000-\u001f\u007f]/u;
const maximumPathLength = 4096;

export function parseCanonicalApiOrigin(origin: string): URL {
  if (
    origin.length === 0
    || origin.length > maximumPathLength
    || origin.trim() !== origin
    || forbiddenUrlText.test(origin)
  ) {
    throw new Error('The configured API origin must be canonical URL text.');
  }

  const base = new URL(origin);
  if (base.protocol !== 'https:' && base.protocol !== 'http:') {
    throw new Error('The configured API origin must use HTTP or HTTPS.');
  }
  if (
    base.username.length > 0
    || base.password.length > 0
    || base.pathname !== '/'
    || base.search.length > 0
    || base.hash.length > 0
    || base.origin !== origin
  ) {
    throw new Error('The configured API origin must contain only an exact origin.');
  }
  return base;
}

export function hasSafeApiTransport(origin: URL, _allowDevelopmentLoopback: boolean = true): boolean {
  if (origin.protocol === 'https:') {
    return true;
  }

  const loopbackHost = origin.hostname === '127.0.0.1'
    || origin.hostname === 'localhost'
    || origin.hostname === '[::1]';
  return origin.protocol === 'http:' && loopbackHost;
}

export function resolveSameOriginApiPath(path: string, origin: string): URL {
  if (
    path.length === 0
    || path.length > maximumPathLength
    || path.trim() !== path
    || forbiddenUrlText.test(path)
    || !path.startsWith('/')
    || path.startsWith('//')
    || path.includes('#')
  ) {
    throw new Error('API paths must be canonical same-origin absolute paths.');
  }

  const base = parseCanonicalApiOrigin(origin);
  const resolved = new URL(path, base);
  if (
    resolved.origin !== base.origin
    || resolved.username.length > 0
    || resolved.password.length > 0
    || `${resolved.pathname}${resolved.search}` !== path
  ) {
    throw new Error('API paths must resolve exactly to the configured origin.');
  }

  return resolved;
}

export function isSafeSameOriginReference(value: string): boolean {
  if (
    value.length === 0
    || value.length > maximumPathLength
    || value.trim() !== value
    || forbiddenUrlText.test(value)
    || (!value.startsWith('/') && !value.startsWith('#'))
    || value.startsWith('//')
  ) {
    return false;
  }

  try {
    const base = new URL('https://frontend-contract.yo4x.invalid');
    if (value.startsWith('/')) {
      resolveSameOriginApiPath(value, base.origin);
      return true;
    }

    const resolved = new URL(value, base);
    return resolved.origin === base.origin
      && resolved.username.length === 0
      && resolved.password.length === 0
      && resolved.pathname === base.pathname
      && resolved.search === base.search
      && resolved.hash === value;
  } catch {
    return false;
  }
}
