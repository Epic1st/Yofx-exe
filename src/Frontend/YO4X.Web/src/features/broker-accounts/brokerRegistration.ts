import type { BrokerAccountRegistrationOption, CreateBrokerAccountRequest } from '../../api/contracts';

const maximumUint64 = 18_446_744_073_709_551_615n;
const loginPattern = /^[0-9]{1,20}$/u;
const credentialKeyDomain = new TextEncoder().encode('YO4X/local-mt5-credential/v1\0');

export interface BrokerAccountRegistrationBinding {
  readonly request: CreateBrokerAccountRequest;
  readonly password: string;
}

function canonicalLogin(value: string): string {
  if (!loginPattern.test(value)) {
    throw new Error('Enter a numeric MT5 login with no spaces or separators.');
  }

  const login = BigInt(value);
  if (login === 0n || login > maximumUint64) {
    throw new Error('Enter a non-zero MT5 login within the supported 64-bit range.');
  }
  return login.toString(10);
}

function maskLogin(value: string): string {
  const visibleCharacters = value.length <= 2 ? 0 : 2;
  return `${'*'.repeat(value.length - visibleCharacters)}${value.slice(value.length - visibleCharacters)}`;
}

function uint64BigEndian(value: bigint): Uint8Array {
  const result = new Uint8Array(8);
  let remaining = value;
  for (let index = result.length - 1; index >= 0; index -= 1) {
    result[index] = Number(remaining & 0xffn);
    remaining >>= 8n;
  }
  return result;
}

function lowercaseHex(value: ArrayBuffer): string {
  return Array.from(new Uint8Array(value), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

/**
 * `password` is carried through untouched and unlogged. It is not part of the
 * binding fingerprint and never will be: the fingerprint names a vault entry
 * and is stored in PostgreSQL, so anything derived from the secret would put a
 * guessable digest of it in the database.
 */
export async function createBrokerAccountRegistrationBinding(
  loginInput: string,
  option: BrokerAccountRegistrationOption,
  password: string,
): Promise<BrokerAccountRegistrationBinding> {
  const login = canonicalLogin(loginInput);
  if (password.length === 0) {
    throw new Error('Enter the password for this MT5 account.');
  }
  // An option the tenant has not approved carries no broker profile at all, so
  // there is nothing to bind the login to. Refusing here keeps a search result
  // from being turned into a registration the server would reject anyway.
  if (!option.approved || option.brokerProfileId === null) {
    throw new Error('Approve this broker server before linking an account to it.');
  }

  const normalizedServer = option.server.trim().normalize('NFC');
  if (normalizedServer !== option.server || normalizedServer.length === 0) {
    throw new Error('The approved broker server is not canonical.');
  }

  const serverBytes = new TextEncoder().encode(normalizedServer.toUpperCase());
  const loginBytes = uint64BigEndian(BigInt(login));
  const hashInput = new Uint8Array(credentialKeyDomain.length + serverBytes.length + loginBytes.length);
  hashInput.set(credentialKeyDomain, 0);
  hashInput.set(serverBytes, credentialKeyDomain.length);
  hashInput.set(loginBytes, credentialKeyDomain.length + serverBytes.length);
  let bindingFingerprint: string;
  try {
    bindingFingerprint = lowercaseHex(await globalThis.crypto.subtle.digest('SHA-256', hashInput));
  } finally {
    hashInput.fill(0);
    serverBytes.fill(0);
    loginBytes.fill(0);
  }

  return {
    request: {
      brokerProfileId: option.brokerProfileId,
      server: option.server,
      login,
      maskedLogin: maskLogin(login),
      bindingFingerprint,
      environment: 'DEMO',
    },
    password,
  };
}

export function createRegistrationIdempotencyKey(): string {
  const bytes = new Uint8Array(24);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
}
