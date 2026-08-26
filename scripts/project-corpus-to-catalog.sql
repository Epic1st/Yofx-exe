-- Projects the persisted MQL5 corpus into the strategy catalog the web
-- application reads.
--
-- Every value is derived from governance.strategy_source_files. Fields the
-- corpus genuinely does not carry — author, symbol, timeframe, version, rating,
-- active users, price — are written as explicit "unspecified" markers or zero
-- so the interface can state the absence rather than imply a fact.
--
-- Re-runnable: rows are keyed on the source file id and refreshed in place.

begin;

with source as
(
    select
        file.id,
        file.tenant_id,
        file.relative_path,
        file.source_kind,
        file.disposition,
        file.byte_length,
        file.source_sha256,
        pg_catalog.jsonb_array_length(file.features) as feature_count,
        pg_catalog.jsonb_array_length(file.includes) as include_count,
        pg_catalog.cardinality(file.entrypoints) as entrypoint_count,
        -- File name without directories, used as the display name.
        pg_catalog.regexp_replace(file.relative_path, '^.*[/\\]', '') as file_name
    from governance.strategy_source_files as file
),
shaped as
(
    select
        source.*,
        -- A slug must match ^[a-z0-9][a-z0-9._-]{0,99}$; fall back to the row id
        -- when a path cannot produce a legal leading character.
        case
            when pg_catalog.substring(
                pg_catalog.regexp_replace(
                    pg_catalog.lower(source.relative_path), '[^a-z0-9._-]+', '-', 'g'),
                '^[a-z0-9]') is null
            then 'mq-' || pg_catalog.replace(source.id::text, '-', '')
            else pg_catalog.left(
                pg_catalog.regexp_replace(
                    pg_catalog.lower(source.relative_path), '[^a-z0-9._-]+', '-', 'g'),
                100)
        end as slug_candidate,
        case source.source_kind
            when 'expert_or_program' then 'Expert or program'
            else 'Include header'
        end as category_label,
        case source.disposition
            when 'needs_semantic_validation' then 'Needs semantic validation'
            when 'needs_source' then 'Needs source'
            when 'unsupported' then 'Unsupported'
            else 'Rejected'
        end as disposition_label
    from source
)
insert into catalog.strategies as target
(
    id, tenant_id, slug, name, author_name, author_initials, category,
    symbol, timeframe, version, description, summary,
    rating_average, rating_count, active_users,
    is_free, cloud_price_monthly_cents, cloud_price_yearly_cents, currency
)
select
    shaped.id,
    shaped.tenant_id,
    shaped.slug_candidate,
    pg_catalog.left(shaped.file_name, 200),
    'Unattributed source',
    'MQ',
    shaped.category_label,
    'Unspecified',
    'Unspecified',
    'Unversioned',
    'Imported MQL5 source. Static analysis classified this file as '
        || shaped.disposition_label || '. It declares '
        || shaped.entrypoint_count::text || ' entrypoint(s), references '
        || shaped.include_count::text || ' include(s), and matched '
        || shaped.feature_count::text || ' analysed language feature(s) across '
        || shaped.byte_length::text || ' bytes. Source SHA-256 '
        || shaped.source_sha256 || '. No semantic conversion, compilation, '
        || 'reference-parity or runtime proof exists for this file, so it is '
        || 'not executable and cannot place an order.',
    shaped.disposition_label || ' · ' || shaped.feature_count::text || ' features',
    0, 0, 0,
    true, 0, 0, 'USD'
from shaped
on conflict (id) do update
set slug = excluded.slug,
    name = excluded.name,
    category = excluded.category,
    description = excluded.description,
    summary = excluded.summary,
    updated_at = pg_catalog.clock_timestamp();

-- Performance figures the corpus can actually support: byte size, feature count,
-- include count and entrypoint count. No profit, drawdown or trade statistic is
-- written, because none has ever been measured for these files.
delete from catalog.strategy_performance;

insert into catalog.strategy_performance (id, tenant_id, strategy_id, ordinal, label, value)
select
    pg_catalog.gen_random_uuid(),
    file.tenant_id,
    file.id,
    figure.ordinal,
    figure.label,
    figure.value
from governance.strategy_source_files as file
cross join lateral
(
    values
        (0, 'Source size', pg_catalog.to_char(file.byte_length, 'FM999,999,999') || ' bytes'),
        (1, 'Language features', pg_catalog.jsonb_array_length(file.features)::text),
        (2, 'Includes', pg_catalog.jsonb_array_length(file.includes)::text),
        (3, 'Entrypoints', pg_catalog.cardinality(file.entrypoints)::text)
) as figure(ordinal, label, value);

commit;

select
    (select pg_catalog.count(*) from catalog.strategies) as catalog_rows,
    (select pg_catalog.count(*) from catalog.strategy_performance) as performance_rows,
    (select pg_catalog.count(*) from governance.strategy_source_files) as corpus_rows;
