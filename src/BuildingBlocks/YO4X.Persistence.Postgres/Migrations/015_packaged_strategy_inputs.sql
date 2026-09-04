-- Converted packages and their source projections can be separate catalog rows.
-- Materialize the source declaration on every existing v2 package whose canonical
-- name matches after removing the transport extension. Runtime projections also
-- resolve this relationship dynamically, so packages published after this migration
-- remain editable even before a materialized copy is written.

with package_sources as
(
    select package.tenant_id, package.id as package_id, source.id as source_id
    from catalog.strategies as package
    cross join lateral
    (
        select candidate.id
        from catalog.strategies as candidate
        where candidate.tenant_id = package.tenant_id
          and candidate.id <> package.id
          and coalesce(candidate.package_format_version, 1)
              < package.package_format_version
          and regexp_replace(lower(candidate.name), '\.(mq5|yo4x)$', '')
              = regexp_replace(lower(package.name), '\.(mq5|yo4x)$', '')
          and exists
          (
              select 1
              from catalog.strategy_inputs as declared
              where declared.tenant_id = candidate.tenant_id
                and declared.strategy_id = candidate.id
          )
        order by
            coalesce(candidate.package_format_version, 1) desc,
            candidate.updated_at desc,
            candidate.id desc
        limit 1
    ) as source
    where package.package_format_version >= 2
)
insert into catalog.strategy_inputs
(
    id, tenant_id, strategy_id, ordinal, name, label, group_label,
    declared_type, value_kind, default_value, enum_type_name, source_line
)
select
    pg_catalog.gen_random_uuid(),
    relationship.tenant_id,
    relationship.package_id,
    declared.ordinal,
    declared.name,
    declared.label,
    declared.group_label,
    declared.declared_type,
    declared.value_kind,
    declared.default_value,
    declared.enum_type_name,
    declared.source_line
from package_sources as relationship
join catalog.strategy_inputs as declared
  on declared.tenant_id = relationship.tenant_id
 and declared.strategy_id = relationship.source_id
on conflict (tenant_id, strategy_id, name) do nothing;

with package_sources as
(
    select package.tenant_id, package.id as package_id, source.id as source_id
    from catalog.strategies as package
    cross join lateral
    (
        select candidate.id
        from catalog.strategies as candidate
        where candidate.tenant_id = package.tenant_id
          and candidate.id <> package.id
          and coalesce(candidate.package_format_version, 1)
              < package.package_format_version
          and regexp_replace(lower(candidate.name), '\.(mq5|yo4x)$', '')
              = regexp_replace(lower(package.name), '\.(mq5|yo4x)$', '')
          and exists
          (
              select 1
              from catalog.strategy_inputs as declared
              where declared.tenant_id = candidate.tenant_id
                and declared.strategy_id = candidate.id
          )
        order by
            coalesce(candidate.package_format_version, 1) desc,
            candidate.updated_at desc,
            candidate.id desc
        limit 1
    ) as source
    where package.package_format_version >= 2
)
insert into catalog.strategy_enum_members
(
    id, tenant_id, strategy_id, enum_type_name, ordinal,
    member_name, member_value, label
)
select
    pg_catalog.gen_random_uuid(),
    relationship.tenant_id,
    relationship.package_id,
    member.enum_type_name,
    member.ordinal,
    member.member_name,
    member.member_value,
    member.label
from package_sources as relationship
join catalog.strategy_enum_members as member
  on member.tenant_id = relationship.tenant_id
 and member.strategy_id = relationship.source_id
on conflict (tenant_id, strategy_id, enum_type_name, member_name) do nothing;
