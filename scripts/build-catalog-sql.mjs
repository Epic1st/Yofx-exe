/*
 * Generates the catalog projection SQL from the verified static manifest.
 *
 * The manifest is byte-checked evidence produced by the conversion worker from
 * the real 198-file corpus. Every catalog value below is derived from it.
 * Fields the corpus genuinely does not carry — author, symbol, timeframe,
 * version, rating, active users, price — are written as explicit "unspecified"
 * markers or zero, never invented.
 */
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const manifestPath = process.argv[2] ?? resolve('docs/backend/mq5-static-manifest.v1.json');
const outputPath = process.argv[3] ?? resolve('.local/development/catalog-projection.sql');
const tenantId = process.argv[4] ?? '019c8d27-763d-7000-8000-000000000001';

const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
if (!Array.isArray(manifest.files) || manifest.files.length === 0) {
  throw new Error('The manifest contains no files.');
}

/** Deterministic, well-formed UUID derived from the corpus digest and file path. */
function stableId(relativePath) {
  const digest = createHash('sha256')
    .update(`${manifest.corpusSha256} ${relativePath}`, 'utf8')
    .digest();
  const hex = digest.subarray(0, 16).toString('hex').split('');
  hex[12] = '7';                                             // version nibble
  hex[16] = '89ab'[parseInt(hex[16], 16) % 4];               // variant nibble
  const value = hex.join('');
  return `${value.slice(0, 8)}-${value.slice(8, 12)}-${value.slice(12, 16)}-${value.slice(16, 20)}-${value.slice(20)}`;
}

const quote = (value) => `'${String(value).replace(/'/gu, "''")}'`;

const dispositionLabel = {
  needsSemanticValidation: 'Needs semantic validation',
  needsSource: 'Needs source',
  unsupported: 'Unsupported',
  rejected: 'Rejected',
};

const used = new Set();
function uniqueSlug(relativePath, id) {
  let base = relativePath.toLowerCase().replace(/[^a-z0-9._-]+/gu, '-').replace(/^-+/u, '').slice(0, 90);
  if (!/^[a-z0-9]/u.test(base)) base = `mq-${base}`.slice(0, 90);
  if (!/^[a-z0-9]/u.test(base)) base = `mq-${id.replace(/-/gu, '')}`.slice(0, 100);
  let candidate = base;
  let suffix = 1;
  while (used.has(candidate)) candidate = `${base}-${suffix++}`.slice(0, 100);
  used.add(candidate);
  return candidate;
}

const strategyRows = [];
const performanceRows = [];

for (const file of manifest.files) {
  const id = stableId(file.relativePath);
  const slug = uniqueSlug(file.relativePath, id);
  const name = file.relativePath.replace(/^.*[/\\]/u, '').slice(0, 200);
  const category = file.kind === 'expertOrProgram' ? 'Expert or program' : 'Include header';
  const label = dispositionLabel[file.disposition] ?? file.disposition;
  const featureCount = Array.isArray(file.features) ? file.features.length : 0;
  const includeCount = Array.isArray(file.includes) ? file.includes.length : 0;
  const entrypointCount = Array.isArray(file.entrypoints) ? file.entrypoints.length : 0;
  const supported = Array.isArray(file.features)
    ? file.features.filter((feature) => feature.support === 'supportedSubsetCandidate').length
    : 0;

  const description =
    `Imported MQL5 source from the verified corpus. Static analysis classified this file as ` +
    `${label}. It declares ${entrypointCount} entrypoint(s), references ${includeCount} include(s), ` +
    `and matched ${featureCount} analysed language feature(s) of which ${supported} fall inside the ` +
    `supported subset. Encoding ${file.textEncoding}, ${file.byteLength} bytes, SHA-256 ${file.sha256}. ` +
    `No semantic conversion, compilation, reference-parity or runtime proof exists for this file: it is ` +
    `not executable and cannot place an order.`;

  const summary = `${label} · ${featureCount} features · ${entrypointCount} entrypoints`;

  strategyRows.push(
    `(${quote(id)}::uuid, ${quote(tenantId)}::uuid, ${quote(slug)}, ${quote(name)}, ` +
    `'Unattributed source', 'MQ', ${quote(category)}, 'Unspecified', 'Unspecified', 'Unversioned', ` +
    `${quote(description)}, ${quote(summary)}, 0, 0, 0, true, 0, 0, 'USD')`,
  );

  const figures = [
    ['Source size', `${file.byteLength.toLocaleString('en-GB')} bytes`],
    ['Language features', String(featureCount)],
    ['Supported subset', `${supported} of ${featureCount}`],
    ['Entrypoints', String(entrypointCount)],
  ];
  figures.forEach(([figureLabel, value], ordinal) => {
    performanceRows.push(
      `(pg_catalog.gen_random_uuid(), ${quote(tenantId)}::uuid, ${quote(id)}::uuid, ${ordinal}, ${quote(figureLabel)}, ${quote(value)})`,
    );
  });
}

const sql = `-- Generated from ${manifestPath}
-- Corpus SHA-256 ${manifest.corpusSha256}
-- ${manifest.fileCount} files, ${manifest.totalBytes} bytes. Re-runnable.

begin;

insert into catalog.strategies
    (id, tenant_id, slug, name, author_name, author_initials, category,
     symbol, timeframe, version, description, summary,
     rating_average, rating_count, active_users,
     is_free, cloud_price_monthly_cents, cloud_price_yearly_cents, currency)
values
${strategyRows.join(',\n')}
on conflict (id) do update
set slug = excluded.slug,
    name = excluded.name,
    category = excluded.category,
    description = excluded.description,
    summary = excluded.summary,
    updated_at = pg_catalog.clock_timestamp();

delete from catalog.strategy_performance
where strategy_id in (select id from catalog.strategies where tenant_id = ${quote(tenantId)}::uuid);

insert into catalog.strategy_performance (id, tenant_id, strategy_id, ordinal, label, value)
values
${performanceRows.join(',\n')};

commit;

select
    (select pg_catalog.count(*) from catalog.strategies) as catalog_rows,
    (select pg_catalog.count(*) from catalog.strategy_performance) as performance_rows;
`;

writeFileSync(outputPath, sql, 'utf8');
console.log(`wrote ${outputPath}`);
console.log(`  ${strategyRows.length} strategies, ${performanceRows.length} performance figures`);
console.log(`  corpus ${manifest.corpusSha256}`);
