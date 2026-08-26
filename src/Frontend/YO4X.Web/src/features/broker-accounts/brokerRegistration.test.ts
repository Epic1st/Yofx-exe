import type { BrokerAccountRegistrationOption } from '../../api/contracts';
import { createBrokerAccountRegistrationBinding } from './brokerRegistration';

const approvedOption: BrokerAccountRegistrationOption = {
  brokerProfileId: '30000000-0000-4000-8000-000000000003',
  directoryServerId: '40000000-0000-4000-8000-000000000004',
  brokerCompany: 'Broker Holdings Ltd',
  server: 'Broker-Demo',
  environment: 'DEMO',
  approved: true,
};

const unapprovedOption: BrokerAccountRegistrationOption = {
  ...approvedOption,
  brokerProfileId: null,
  approved: false,
};

/** Synthetic throughout: no demo credential ever appears in a test. */
const secret = 'synthetic-binding-secret';

describe('broker account registration binding', () => {
  it('matches C# LocalCredentialKey.Create(12345678UL, "Broker-Demo") exactly', async () => {
    await expect(createBrokerAccountRegistrationBinding('12345678', approvedOption, secret)).resolves.toEqual({
      request: {
        brokerProfileId: approvedOption.brokerProfileId,
        server: 'Broker-Demo',
        login: '12345678',
        maskedLogin: '******78',
        bindingFingerprint: 'ff86813c5e96c4bcdbb40541ce529d8f6d9c34b305f9da3188e157001876df75',
        environment: 'DEMO',
        password: secret,
      },
    });
  });

  it('keeps the password out of the binding fingerprint', async () => {
    const first = await createBrokerAccountRegistrationBinding('12345678', approvedOption, 'one');
    const second = await createBrokerAccountRegistrationBinding('12345678', approvedOption, 'two');

    // The fingerprint is persisted in PostgreSQL. If the secret contributed to
    // it, the database would hold a guessable digest of the password.
    expect(first.request.bindingFingerprint).toBe(second.request.bindingFingerprint);
  });

  it('requires a password before a binding is produced at all', async () => {
    await expect(createBrokerAccountRegistrationBinding('12345678', approvedOption, ''))
      .rejects.toThrow(/Enter the password/u);
  });

  it('uses canonical ulong formatting before masking and hashing', async () => {
    const canonical = await createBrokerAccountRegistrationBinding('000123', approvedOption, secret);
    const direct = await createBrokerAccountRegistrationBinding('123', approvedOption, secret);
    expect(canonical).toEqual(direct);
    expect(canonical.request.maskedLogin).toBe('*23');
  });

  it.each(['', '0', '12 34', '-1', '18446744073709551616'])('rejects invalid ulong login input: %s', async (login) => {
    await expect(createBrokerAccountRegistrationBinding(login, approvedOption, secret)).rejects.toThrow(/MT5 login|non-zero/u);
  });

  it('refuses to bind a login to a directory server the tenant has not approved', async () => {
    await expect(createBrokerAccountRegistrationBinding('12345678', unapprovedOption, secret))
      .rejects.toThrow(/Approve this broker server/u);
  });
});
