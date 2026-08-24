import {
  isSafeSameOriginReference,
  parseCanonicalApiOrigin,
  resolveSameOriginApiPath,
} from './safeUrl';

const maximumLength = 4096;
const referenceBaseOrigin = 'https://frontend-contract.yo4x.invalid';

type BoundaryAttempt = (value: string) => void;

function requireSafeReference(value: string): void {
  if (!isSafeSameOriginReference(value)) {
    throw new Error('The reference must be a canonical same-origin path or fragment.');
  }
}

describe('safe same-origin reference screening', () => {
  it.each([
    '#section',
    '#//evil.example',
    '/reports/2026/august',
    '/a%20b',
  ])('accepts %s', (reference) => {
    expect(isSafeSameOriginReference(reference)).toBe(true);
  });

  it.each([
    '//evil.example',
    ' //evil.example',
    '/a b',
    '/a%20b#spoofed',
    'https://evil.example/sign-in',
    `/${'a'.repeat(maximumLength + 1)}`,
  ])('rejects %s', (reference) => {
    expect(isSafeSameOriginReference(reference)).toBe(false);
  });

  it('resolves percent-encoded paths without altering their encoded spelling', () => {
    const resolved = resolveSameOriginApiPath('/a%20b', referenceBaseOrigin);

    expect(resolved.origin).toBe(referenceBaseOrigin);
    expect(resolved.pathname).toBe('/a%20b');
  });

  it('rejects literal interior spaces instead of silently encoding them away', () => {
    expect(() => resolveSameOriginApiPath('/a b', referenceBaseOrigin)).toThrow(
      'API paths must resolve exactly to the configured origin.',
    );
  });
});

describe('safeUrl maximum-length boundaries', () => {
  function hostWithinLabelLimits(totalLength: number): string {
    const labels: string[] = [];
    let remaining = totalLength;
    while (remaining > 0) {
      const taken = Math.min(62, remaining);
      labels.push('a'.repeat(taken));
      remaining -= taken;
      if (remaining > 0) {
        remaining -= 1;
      }
    }
    return labels.join('.');
  }

  const boundarySuites: ReadonlyArray<readonly [
    label: string,
    attempt: BoundaryAttempt,
    atLimit: string,
  ]> = [
    [
      'canonical API origins',
      (value) => {
        parseCanonicalApiOrigin(value);
      },
      `https://${hostWithinLabelLimits(maximumLength - 'https://'.length)}`,
    ],
    [
      'same-origin API paths',
      (value) => {
        resolveSameOriginApiPath(value, referenceBaseOrigin);
      },
      `/${'a'.repeat(maximumLength - 1)}`,
    ],
    [
      'path-form references',
      requireSafeReference,
      `/${'a'.repeat(maximumLength - 1)}`,
    ],
    [
      'fragment references',
      requireSafeReference,
      `#${'a'.repeat(maximumLength - 1)}`,
    ],
  ];

  it.each(boundarySuites)(
    '%s accept input of exactly the maximum length',
    (_label, attempt, atLimit) => {
      expect(() => attempt(atLimit)).not.toThrow();
    },
  );

  it.each(boundarySuites)(
    '%s reject input beyond the maximum length',
    (_label, attempt, atLimit) => {
      expect(() => attempt(`${atLimit}a`)).toThrow();
    },
  );
});
