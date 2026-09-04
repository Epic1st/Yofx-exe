# PostgreSQL baseline and compatibility policy

Status date: 2026-08-26 UTC

This repository is explicitly pre-release and greenfield. No durable deployed
YO4X database or legacy proof-key inventory is available. The current
`001_foundation.sql` is therefore the first supported database baseline, not an
in-place upgrade from an earlier working-tree version.

The canonical SQL release inputs are UTF-8 with LF line endings. Their SHA-256
digests are:

- `001_foundation.sql`: `1de1cad6257edbd1a2c9eacd969171222b950d38b8cfa2f09ea5525506279db6`
- `002_user_operation_invocation_protocol.sql`: `827598ac1aa9924ca1cfe9df383599d608148a44ac4cc6989a78af38ca35a934`
- `003_pending_demo_broker_account_registration.sql`: `748cd68f378c81ebed6ef6f98673e4b6314ee23494ed50a56c35070bd17ed5d4`
- `004_local_development_identity_provisioning.sql`: `0e5db247b6e4e54dbf806307bb134d9429c3e1deeb1e831e93294dee0b17767c`
- `005_frontend_projections.sql`: `8811cd182063f9e1b99565918d50e13d459b63a116f45d1b358d8eb9d310a787`
- `006_strategy_inputs_and_backtests.sql`: `ec5efbabb8747f3fe510b2653912a01ccee7cbde0755926fbbb2e3bbe848bc10`
- `007_broker_server_catalogue.sql`: `15f5903cf97c1fd4d6eff2180e4afd0631377a5f13e750dd2b01ace960f31e6a`
- `008_backtest_queue_worker_access.sql`: `da172066c80bca3fc665649933a0c1dccfc442b07afab0fbf140291151e3ed27`
- `009_backtest_equity_curve.sql`: `4fcc53e9d451600438e68e047cb8631927f304f40f3bcc434ef1b90ec7cd685f`
- `010_bot_settings_and_broker_symbols.sql`: `bc545183be6187a4e1eec75c6772b4cbed52eb5c406e503c561cc579ecb8f6a2`
- `least_privilege_roles.sql`: `17de46699761981c7747be190d8b91f178ade24662ad25bfd2774b13a7bc8c1d`

The expected catalog semantic fingerprint is
`8772e5e7b8044ef68e185772d569128e771a11fb4b6f06dca7df1260b3822eba`.
It is derived from the migrated catalog and effective role capabilities; it is
not a substitute for any exact SQL-file checksum above.

`.gitattributes` pins every SQL file to LF so a clean checkout cannot change an
embedded migration checksum. Once this baseline is released or applied to a
durable environment, `001_foundation.sql` is immutable. Every later schema
change must use a new, ordered additive migration with an explicit data,
compatibility, privilege, and rollback/forward-fix review.

The catalog semantic baseline is currently proven on PostgreSQL 18 using the
Windows EDB 18.6-1 test runtime. The pinned Testcontainers fallback is
`postgres:18.6-alpine3.23@sha256:697c180dbf244d3ce4a8f4cbc0156cde840af055c1bf8b76aebe422a4822086f`,
but that container image has not been executed in the local Windows evidence
run. Every test/deployment database must be created from `template0` with
`ENCODING 'UTF8'`, `LOCALE_PROVIDER libc`, `LC_COLLATE 'C'`, and `LC_CTYPE 'C'`.
The semantic readiness fingerprint also pins the effective PostgreSQL major,
encoding, locale provider, collation/ctype, and database posture and therefore
fails closed on an unqualified target rather than assuming portability.

## Fail-closed checksum behavior

The migrator stores each exact embedded SQL checksum in
`control.schema_migrations`. It serializes migration attempts with a PostgreSQL
advisory transaction lock. A known migration identifier with any other checksum
aborts the transaction before later migration SQL runs. Operators must never
edit `control.schema_migrations`, bypass the mismatch, or replay the monolithic
foundation over an existing schema.

## Older 001 databases

An existing database carrying any older `001_foundation` checksum is not a
supported automatic upgrade target. Startup migration must remain failed and
the application must remain unavailable. The operator must first take and
verify a restorable backup, retain the old database without mutation, and choose
one reviewed path:

1. For disposable pre-release data only, provision a new empty database, apply
   the canonical baseline, verify catalog/ACL/readiness fingerprints, and cut
   over explicitly. Deleting the old database is a separate operator action
   after backup verification and acceptance; the application performs no reset.
2. For any data that must survive, commission a staged additive upgrade. It must
   inventory the actually stored checksum and historical proof keys, bridge
   legacy/current proof formats, backfill only cryptographically verified rows,
   preserve immutable idempotency/audit references, wait through replay and
   lease windows, remove obsolete function overloads and grants, and prove that
   upgraded and fresh catalogs/ACLs are equivalent.

Unknown proof keys, incompatible legacy proof records, partial backfills, or an
unverified backup block migration. A checksum rewrite or destructive schema
rebuild is never treated as an upgrade.

## Release checks

- The LF migration and role-script checksum test must match the values above.
- Applying the baseline to a fresh PostgreSQL database and applying it again
  must produce exactly the ten recorded migrations listed above, in that
  order, and no domain seed rows. The MetaTrader 5 broker-server directory that
  `007_broker_server_catalogue.sql` creates is deliberately left empty: rows
  arrive only from an offline `YO4X.Mt5.BrokerCatalogueImport` run. The two
  tables `010_bot_settings_and_broker_symbols.sql` adds are empty for the same
  reason: `bots.bot_inputs` gains a row only when an operator changes an EA
  input away from the value the strategy source declares, and
  `bots.broker_symbols` only when a broker's own instrument list is imported.
- A tampered recorded checksum must fail without changing the recorded value or
  applying schema work.
- Runtime readiness must verify the current schema and exact role capabilities.
- Future migrations must have fresh-database, previous-release upgrade,
  concurrent-migrator, data-preservation, and catalog/ACL equivalence tests.
