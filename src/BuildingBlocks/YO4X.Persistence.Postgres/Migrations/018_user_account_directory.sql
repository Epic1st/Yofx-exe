-- User account directory: hashed sign-in credentials, profile, MT5 login
-- numbers, marketplace listings, and purchases. Counts are derived, never stored.
--
-- Passwords are Argon2id encodings only. Plaintext YO4X passwords and MT5
-- master/investor passwords are not stored here. Broker secrets remain behind
-- operations.broker_accounts.credential_reference (vault), not in this schema.

alter table operations.broker_accounts
    add column if not exists login_number bigint
        check (login_number is null or login_number > 0);

create unique index if not exists broker_accounts_login_number_idx
    on operations.broker_accounts (tenant_id, login_number)
    where login_number is not null and state <> 'deleted';

create table identity.user_credentials
(
    user_id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    password_algorithm text not null default 'argon2id'
        check (password_algorithm = 'argon2id'),
    password_hash text not null
        check (length(password_hash) between 50 and 256)
        check (password_hash ~ '^\$argon2id\$v=19\$'),
    password_updated_at timestamptz not null,
    failed_sign_in_count integer not null default 0
        check (failed_sign_in_count >= 0 and failed_sign_in_count <= 10000),
    locked_until timestamptz,
    last_sign_in_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, user_id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    check (updated_at >= created_at),
    check (password_updated_at >= created_at),
    check (last_sign_in_at is null or last_sign_in_at >= created_at),
    check (locked_until is null or locked_until > created_at)
);

create table identity.user_profiles
(
    user_id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    display_name text not null
        check (length(btrim(display_name)) between 1 and 200),
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, user_id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    check (updated_at >= created_at)
);

create schema if not exists marketplace;
revoke all on schema marketplace from public;

create table marketplace.listings
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    seller_user_id uuid not null,
    strategy_id uuid not null,
    state text not null default 'draft'
        check (state in ('draft', 'listed', 'unlisted', 'suspended')),
    title text not null check (length(btrim(title)) between 1 and 200),
    summary text not null default '' check (length(summary) <= 4000),
    price_monthly_cents integer not null default 0
        check (price_monthly_cents >= 0),
    price_yearly_cents integer not null default 0
        check (price_yearly_cents >= 0),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    listed_at timestamptz,
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, strategy_id),
    foreign key (tenant_id, seller_user_id)
        references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, strategy_id)
        references catalog.strategies(tenant_id, id),
    check (updated_at >= created_at),
    check (state <> 'listed' or listed_at is not null)
);

create index listings_seller_idx
    on marketplace.listings (tenant_id, seller_user_id, state);
create index listings_listed_idx
    on marketplace.listings (tenant_id, listed_at desc, id)
    where state = 'listed';

create table marketplace.purchases
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    buyer_user_id uuid not null,
    listing_id uuid not null,
    strategy_id uuid not null,
    status text not null default 'paid'
        check (status in ('paid', 'refunded', 'revoked')),
    price_cents integer not null check (price_cents >= 0),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    purchased_at timestamptz not null default clock_timestamp(),
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, buyer_user_id)
        references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, listing_id)
        references marketplace.listings(tenant_id, id),
    foreign key (tenant_id, strategy_id)
        references catalog.strategies(tenant_id, id),
    check (updated_at >= created_at)
);

create unique index purchases_paid_buyer_strategy_idx
    on marketplace.purchases (tenant_id, buyer_user_id, strategy_id)
    where status = 'paid';
create index purchases_buyer_idx
    on marketplace.purchases (tenant_id, buyer_user_id, purchased_at desc, id);
create index purchases_listing_idx
    on marketplace.purchases (tenant_id, listing_id);

select control.apply_tenant_rls('identity.user_credentials'::regclass);
select control.apply_tenant_rls('identity.user_profiles'::regclass);

alter table marketplace.listings enable row level security;
alter table marketplace.listings force row level security;
alter table marketplace.purchases enable row level security;
alter table marketplace.purchases force row level security;

create policy tenant_select on marketplace.listings
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on marketplace.listings
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on marketplace.listings
    for update
    using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_delete on marketplace.listings
    for delete using (tenant_id = (select control.current_tenant_id()));

create policy tenant_select on marketplace.purchases
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on marketplace.purchases
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on marketplace.purchases
    for update
    using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_delete on marketplace.purchases
    for delete using (tenant_id = (select control.current_tenant_id()));

create view identity.user_account_directory
    with (security_invoker = true)
as
select
    usr.tenant_id,
    usr.id as user_id,
    usr.normalized_email,
    usr.security_state,
    usr.email_verified_at,
    usr.created_at,
    usr.updated_at,
    profile.display_name,
    (credential.user_id is not null) as has_password_credential,
    credential.last_sign_in_at,
    credential.failed_sign_in_count,
    (
        select count(*)::integer
        from operations.broker_accounts as account
        where account.tenant_id = usr.tenant_id
          and account.user_id = usr.id
          and account.state <> 'deleted'
    ) as mt5_account_count,
    (
        select count(*)::integer
        from marketplace.purchases as purchase
        where purchase.tenant_id = usr.tenant_id
          and purchase.buyer_user_id = usr.id
          and purchase.status = 'paid'
    ) as bots_purchased_count,
    (
        select count(*)::integer
        from marketplace.listings as listing
        where listing.tenant_id = usr.tenant_id
          and listing.seller_user_id = usr.id
          and listing.state = 'listed'
    ) as bots_listed_count,
    (
        select count(*)::integer
        from bots.bots as bot
        where bot.tenant_id = usr.tenant_id
          and bot.user_id = usr.id
    ) as bots_owned_count,
    (
        select count(*)::integer
        from bots.bots as bot
        where bot.tenant_id = usr.tenant_id
          and bot.user_id = usr.id
          and bot.status in ('STARTING', 'RUNNING')
    ) as bots_running_count
from identity.user_identities as usr
left join identity.user_profiles as profile
    on profile.tenant_id = usr.tenant_id
   and profile.user_id = usr.id
left join identity.user_credentials as credential
    on credential.tenant_id = usr.tenant_id
   and credential.user_id = usr.id;

comment on table identity.user_credentials is
    'Argon2id password encodings for YO4X sign-in. Plaintext passwords are never stored.';
comment on table identity.user_profiles is
    'Operator-visible profile fields for a YO4X user identity.';
comment on table marketplace.listings is
    'Strategies a user has listed on the marketplace.';
comment on table marketplace.purchases is
    'Strategies a user has purchased from the marketplace.';
comment on view identity.user_account_directory is
    'One row per user with derived MT5, purchase, listing, and bot counts.';
comment on column operations.broker_accounts.login_number is
    'MT5 account number. The MT5 password is never stored in PostgreSQL.';

grant select, insert, update on identity.user_credentials to yo4x_control_api;
grant select, insert, update on identity.user_profiles to yo4x_control_api;
grant select on identity.user_account_directory to yo4x_control_api;
grant select (login_number) on operations.broker_accounts to yo4x_control_api;
grant insert (login_number) on operations.broker_accounts to yo4x_control_api;

grant usage on schema marketplace to yo4x_control_api;
grant select, insert, update, delete on marketplace.listings to yo4x_control_api;
grant select, insert, update, delete on marketplace.purchases to yo4x_control_api;
