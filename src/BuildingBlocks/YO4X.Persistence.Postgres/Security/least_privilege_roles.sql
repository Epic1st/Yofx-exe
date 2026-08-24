-- Apply after schema migrations using an offline, deployment-controlled
-- PostgreSQL superuser on the dedicated cluster. This is the only executor
-- model exercised by the integration harness; provider-specific equivalents
-- require their own qualification before use.
-- This script deliberately does not create LOGIN roles or passwords. The platform
-- must provision the fifteen named roles before running it and keep runtime logins out
-- of the yo4x_migrator role.

begin;

do $$
begin
    if session_user <> current_user
        or not exists
        (
            select 1
            from pg_catalog.pg_roles as role
            where role.rolname = session_user
              and role.rolsuper
        ) then
        raise exception
            'YO4X role deployment requires a direct offline PostgreSQL superuser session';
    end if;
end
$$;

do $$
declare
    required_role text;
begin
    foreach required_role in array array[
        'yo4x_migrator',
        'yo4x_context_authority',
        'yo4x_context_issuer',
        'yo4x_control_api',
        'yo4x_admin_bff',
        'yo4x_emergency',
        'yo4x_secret_ingestion',
        'yo4x_conversion_worker',
        'yo4x_strategy_verifier',
        'yo4x_runtime_evidence',
        'yo4x_worker',
        'yo4x_supervisor_runtime',
        'yo4x_trade_authorizer',
        'yo4x_gateway_runtime',
        'yo4x_credential_runtime'
    ]
    loop
        if not exists (select 1 from pg_catalog.pg_roles where rolname = required_role) then
            raise exception 'Required deployment role % does not exist', required_role;
        end if;
    end loop;

    if exists
    (
        select 1
        from pg_catalog.pg_roles
        where rolname = 'yo4x_migrator'
          and (rolcanlogin or rolinherit or rolsuper or rolbypassrls or rolcreatedb
               or rolcreaterole or rolreplication)
    ) then
        raise exception 'yo4x_migrator must be NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION CONNECTION LIMIT -1';
    end if;

    if exists
    (
        select 1
        from pg_catalog.pg_auth_members as membership
        join pg_catalog.pg_roles as member_role on member_role.oid = membership.member
        where member_role.rolname = 'yo4x_migrator'
    ) then
        raise exception 'yo4x_migrator must not inherit or SET ROLE into another database role';
    end if;

    if exists
    (
        select 1
        from pg_catalog.pg_auth_members as membership
        join pg_catalog.pg_roles as granted_role on granted_role.oid = membership.roleid
        where granted_role.rolname = 'yo4x_migrator'
    ) then
        raise exception 'yo4x_migrator must not be granted to any wrapper or member role';
    end if;

    if not exists
    (
        select 1
        from pg_catalog.pg_roles as role
        where role.rolname = 'yo4x_context_authority'
          and not role.rolcanlogin
          and not role.rolinherit
          and not role.rolsuper
          and not role.rolbypassrls
          and not role.rolcreatedb
          and not role.rolcreaterole
          and not role.rolreplication
          and not exists
          (
              select 1
              from pg_catalog.pg_auth_members as membership
              where membership.member = role.oid
                 or membership.roleid = role.oid
          )
    ) then
        raise exception 'yo4x_context_authority must be an isolated NOLOGIN NOINHERIT non-privileged owner role';
    end if;

    if exists
    (
        select 1
        from pg_catalog.pg_roles
        where rolname in
            ('yo4x_context_issuer', 'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency', 'yo4x_secret_ingestion', 'yo4x_conversion_worker', 'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker', 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer', 'yo4x_gateway_runtime', 'yo4x_credential_runtime')
          and (not rolcanlogin or rolinherit or rolsuper or rolbypassrls or rolcreatedb
               or rolcreaterole or rolreplication
               or (rolvaliduntil is not null
                   and rolvaliduntil <= statement_timestamp()))
    ) then
        raise exception 'YO4X runtime roles must be current LOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION identities';
    end if;

    if exists
    (
        select 1
        from pg_catalog.pg_auth_members as membership
        join pg_catalog.pg_roles as member_role on member_role.oid = membership.member
        where member_role.rolname in
            ('yo4x_context_issuer', 'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency', 'yo4x_secret_ingestion', 'yo4x_conversion_worker', 'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker', 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer', 'yo4x_gateway_runtime', 'yo4x_credential_runtime')
    ) then
        raise exception 'YO4X runtime roles must not inherit or SET ROLE into any other database role';
    end if;

    if exists
    (
        select 1
        from pg_catalog.pg_auth_members as membership
        join pg_catalog.pg_roles as granted_role on granted_role.oid = membership.roleid
        where granted_role.rolname in
            ('yo4x_context_issuer', 'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency', 'yo4x_secret_ingestion', 'yo4x_conversion_worker', 'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker', 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer', 'yo4x_gateway_runtime', 'yo4x_credential_runtime')
    ) then
        raise exception 'YO4X runtime roles must not be granted to wrapper or member roles';
    end if;
end
$$;

-- Raw credentials and one-time import capabilities cross PostgreSQL only as
-- transient bind parameters. Runtime roles must never serialize bind values in
-- ordinary statement logs or error diagnostics.
do $$
declare
    runtime_role text;
begin
    alter role yo4x_migrator reset all;
    alter role yo4x_migrator connection limit -1;
    alter role yo4x_context_authority reset all;
    alter role yo4x_context_authority connection limit -1;
    foreach runtime_role in array array[
        'yo4x_context_issuer',
        'yo4x_control_api',
        'yo4x_admin_bff',
        'yo4x_emergency',
        'yo4x_secret_ingestion',
        'yo4x_conversion_worker',
        'yo4x_strategy_verifier',
        'yo4x_runtime_evidence',
        'yo4x_worker',
        'yo4x_supervisor_runtime',
        'yo4x_trade_authorizer',
        'yo4x_gateway_runtime',
        'yo4x_credential_runtime'
    ]
    loop
        execute format('alter role %I reset all', runtime_role);
        execute format('alter role %I connection limit 32', runtime_role);
        execute format(
            'alter role %I in database %I reset all',
            runtime_role,
            current_database());
        execute format('alter role %I set log_parameter_max_length = 0', runtime_role);
        execute format('alter role %I set log_parameter_max_length_on_error = 0', runtime_role);
        execute format('alter role %I set search_path = %L', runtime_role, '');
        execute format('alter role %I set row_security = on', runtime_role);
        execute format('alter role %I set session_replication_role = origin', runtime_role);
        execute format('alter role %I set default_transaction_read_only = off', runtime_role);
        execute format('alter role %I set default_transaction_isolation = ''read committed''', runtime_role);
        execute format('alter role %I set transaction_timeout = ''2min''', runtime_role);
    end loop;
end
$$;

do $$
declare
    database_record record;
begin
    -- YO4X runtime identities are a dedicated-cluster security boundary. They
    -- may connect only to the database in which this role contract is applied;
    -- PUBLIC must not silently restore CONNECT/TEMPORARY/CREATE elsewhere.
    for database_record in
        select database.datname, owner.rolname as owner_name
        from pg_catalog.pg_database as database
        join pg_catalog.pg_roles as owner on owner.oid = database.datdba
        where database.datname <> current_database()
    loop
        execute format(
            'revoke connect, create, temporary on database %I from public, yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime',
            database_record.datname);
        if database_record.owner_name in
        (
            'yo4x_context_authority', 'yo4x_context_issuer',
            'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
            'yo4x_secret_ingestion', 'yo4x_conversion_worker',
            'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
            'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
            'yo4x_gateway_runtime', 'yo4x_credential_runtime'
        ) then
            execute format(
                'alter database %I owner to yo4x_migrator',
                database_record.datname);
        end if;
    end loop;

    execute format(
        'alter database %I reset all',
        current_database());
    execute format(
        'alter database %I allow_connections true is_template false connection limit -1',
        current_database());
    execute format(
        'alter database %I owner to yo4x_migrator',
        current_database());
    execute format(
        'revoke all privileges on database %I from public',
        current_database());
    execute format(
        'grant connect, create on database %I to yo4x_migrator',
        current_database());
    execute format(
        'revoke all privileges on database %I from yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime',
        current_database());
    execute format(
        'grant connect on database %I to yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime',
        current_database());
end
$$;

-- Remove explicit runtime grants outside the YO4X schemas as well. System
-- catalogs, public, languages, types, tablespaces, foreign-data boundaries,
-- large objects, and privileged GUCs are all independent PostgreSQL capability
-- surfaces and must not survive role-contract reapplication.
do $$
declare
    runtime_role_oids oid[];
    target record;
    runtime_roles constant text :=
        'yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, '
        || 'yo4x_secret_ingestion, yo4x_conversion_worker, '
        || 'yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, '
        || 'yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime';
begin
    select array_agg(role.oid order by role.oid)
    into strict runtime_role_oids
    from pg_catalog.pg_roles as role
    where role.rolname in
        ('yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
         'yo4x_secret_ingestion', 'yo4x_conversion_worker',
         'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
         'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
         'yo4x_gateway_runtime', 'yo4x_credential_runtime');

    for target in
        select namespace.nspname
        from pg_catalog.pg_namespace as namespace
        where exists
        (
            select 1
            from pg_catalog.aclexplode(namespace.nspacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on schema %I from %s',
            target.nspname,
            runtime_roles);
    end loop;

    for target in
        select namespace.nspname, relation.relname, relation.relkind
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
          and exists
          (
              select 1
              from pg_catalog.aclexplode(relation.relacl) as privilege
              where privilege.grantee = any(runtime_role_oids)
          )
    loop
        execute format(
            case when target.relkind = 'S'
                then 'revoke all privileges on sequence %I.%I from %s'
                else 'revoke all privileges on table %I.%I from %s' end,
            target.nspname,
            target.relname,
            runtime_roles);
    end loop;

    for target in
        select namespace.nspname,
            relation.relname,
            relation.relkind,
            string_agg(
                quote_ident(attribute.attname),
                ', ' order by attribute.attnum) as column_names
        from pg_catalog.pg_attribute as attribute
        join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where relation.relkind in ('r', 'p', 'v', 'm', 'f')
          and attribute.attnum > 0
          and not attribute.attisdropped
          and exists
          (
              select 1
              from pg_catalog.aclexplode(attribute.attacl) as privilege
              where privilege.grantee = any(runtime_role_oids)
          )
        group by namespace.nspname, relation.relname, relation.relkind
    loop
        if target.relkind = 'm' then
            execute format(
                'revoke select (%s) on %I.%I from %s',
                target.column_names,
                target.nspname,
                target.relname,
                runtime_roles);
        else
            execute format(
                'revoke select (%s), insert (%s), update (%s), references (%s) '
                || 'on %I.%I from %s',
                target.column_names,
                target.column_names,
                target.column_names,
                target.column_names,
                target.nspname,
                target.relname,
                runtime_roles);
        end if;
    end loop;

    for target in
        select function.oid::regprocedure::text as signature,
            function.prokind
        from pg_catalog.pg_proc as function
        where exists
        (
            select 1
            from pg_catalog.aclexplode(function.proacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            case when target.prokind = 'p'
                then 'revoke all privileges on procedure %s from %s'
                else 'revoke all privileges on function %s from %s' end,
            target.signature,
            runtime_roles);
    end loop;

    for target in
        select type_record.oid::regtype::text as signature
        from pg_catalog.pg_type as type_record
        where exists
        (
            select 1
            from pg_catalog.aclexplode(type_record.typacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on type %s from %s',
            target.signature,
            runtime_roles);
    end loop;

    for target in
        select language_record.lanname
        from pg_catalog.pg_language as language_record
        where exists
        (
            select 1
            from pg_catalog.aclexplode(language_record.lanacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on language %I from %s',
            target.lanname,
            runtime_roles);
    end loop;

    for target in
        select tablespace_record.spcname
        from pg_catalog.pg_tablespace as tablespace_record
        where exists
        (
            select 1
            from pg_catalog.aclexplode(tablespace_record.spcacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on tablespace %I from %s',
            target.spcname,
            runtime_roles);
    end loop;

    for target in
        select wrapper.fdwname
        from pg_catalog.pg_foreign_data_wrapper as wrapper
        where exists
        (
            select 1
            from pg_catalog.aclexplode(wrapper.fdwacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on foreign data wrapper %I from %s',
            target.fdwname,
            runtime_roles);
    end loop;

    for target in
        select foreign_server.srvname
        from pg_catalog.pg_foreign_server as foreign_server
        where exists
        (
            select 1
            from pg_catalog.aclexplode(foreign_server.srvacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on foreign server %I from %s',
            target.srvname,
            runtime_roles);
    end loop;

    for target in
        select large_object.oid
        from pg_catalog.pg_largeobject_metadata as large_object
        where exists
        (
            select 1
            from pg_catalog.aclexplode(large_object.lomacl) as privilege
            where privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on large object %s from %s',
            target.oid,
            runtime_roles);
    end loop;

    for target in
        select parameter_record.parname
        from pg_catalog.pg_parameter_acl as parameter_record
        where exists
        (
            select 1
            from pg_catalog.aclexplode(parameter_record.paracl) as privilege
            where privilege.grantee = 0
               or privilege.grantee = any(runtime_role_oids)
        )
    loop
        execute format(
            'revoke all privileges on parameter %I from public, %s',
            target.parname,
            runtime_roles);
    end loop;

    -- PUBLIC is an inherited capability. Strip it from every non-system
    -- object surface; pg_catalog/information_schema defaults are version-pinned
    -- by the external PG18 semantic manifest, with password-verifier catalogs
    -- explicitly denied below.
    for target in
        select namespace.nspname
        from pg_catalog.pg_namespace as namespace
        where namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
    loop
        execute format(
            'revoke all privileges on schema %I from public',
            target.nspname);
    end loop;

    for target in
        select namespace.nspname, relation.relname, relation.relkind
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
    loop
        execute format(
            case when target.relkind = 'S'
                then 'revoke all privileges on sequence %I.%I from public'
                else 'revoke all privileges on table %I.%I from public' end,
            target.nspname,
            target.relname);
    end loop;

    for target in
        select namespace.nspname,
            relation.relname,
            relation.relkind,
            string_agg(
                quote_ident(attribute.attname),
                ', ' order by attribute.attnum) as column_names
        from pg_catalog.pg_attribute as attribute
        join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and relation.relkind in ('r', 'p', 'v', 'm', 'f')
          and attribute.attnum > 0
          and not attribute.attisdropped
          and exists
          (
              select 1
              from pg_catalog.aclexplode(attribute.attacl) as privilege
              where privilege.grantee = 0
          )
        group by namespace.nspname, relation.relname, relation.relkind
    loop
        if target.relkind = 'm' then
            execute format(
                'revoke select (%s) on %I.%I from public',
                target.column_names,
                target.nspname,
                target.relname);
        else
            execute format(
                'revoke select (%s), insert (%s), update (%s), references (%s) '
                || 'on %I.%I from public',
                target.column_names,
                target.column_names,
                target.column_names,
                target.column_names,
                target.nspname,
                target.relname);
        end if;
    end loop;

    for target in
        select function.oid::regprocedure::text as signature,
            function.prokind
        from pg_catalog.pg_proc as function
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = function.pronamespace
        where namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
    loop
        execute format(
            case when target.prokind = 'p'
                then 'revoke all privileges on procedure %s from public'
                else 'revoke all privileges on function %s from public' end,
            target.signature);
    end loop;

    for target in
        select type_record.oid::regtype::text as signature
        from pg_catalog.pg_type as type_record
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = type_record.typnamespace
        where namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and type_record.typrelid = 0
          and type_record.typelem = 0
    loop
        execute format(
            'revoke all privileges on type %s from public',
            target.signature);
    end loop;

    for target in
        select language_record.lanname
        from pg_catalog.pg_language as language_record
        where language_record.lanpltrusted
    loop
        execute format(
            'revoke all privileges on language %I from public',
            target.lanname);
        execute format(
            'grant usage on language %I to yo4x_migrator',
            target.lanname);
    end loop;

    for target in
        select tablespace.spcname
        from pg_catalog.pg_tablespace as tablespace
        where tablespace.spcacl is not null
    loop
        execute format(
            'revoke all privileges on tablespace %I from public',
            target.spcname);
    end loop;

    for target in
        select wrapper.fdwname
        from pg_catalog.pg_foreign_data_wrapper as wrapper
        where wrapper.fdwacl is not null
    loop
        execute format(
            'revoke all privileges on foreign data wrapper %I from public',
            target.fdwname);
    end loop;

    for target in
        select foreign_server.srvname
        from pg_catalog.pg_foreign_server as foreign_server
        where foreign_server.srvacl is not null
    loop
        execute format(
            'revoke all privileges on foreign server %I from public',
            target.srvname);
    end loop;

    for target in
        select large_object.oid
        from pg_catalog.pg_largeobject_metadata as large_object
        where large_object.lomacl is not null
    loop
        execute format(
            'revoke all privileges on large object %s from public',
            target.oid);
    end loop;
end
$$;

revoke all privileges on table pg_catalog.pg_authid from public;
revoke all privileges on schema pg_toast from public,
    yo4x_context_authority, yo4x_context_issuer, yo4x_control_api,
    yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion,
    yo4x_conversion_worker, yo4x_strategy_verifier,
    yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
    yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime;
revoke all privileges on all tables in schema pg_toast from public,
    yo4x_context_authority, yo4x_context_issuer, yo4x_control_api,
    yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion,
    yo4x_conversion_worker, yo4x_strategy_verifier,
    yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
    yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime;
do $$
declare
    target record;
begin
    -- PostgreSQL's ALL TABLES expansion does not include TOAST relkind rows.
    -- Revoke their explicit ACLs by structural catalog identity as well.
    for target in
        select relation.oid::regclass::text as signature
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where namespace.nspname = 'pg_toast'
          and relation.relkind = 't'
    loop
        execute format(
            'revoke all privileges on table %s from public, '
            || 'yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, '
            || 'yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion, '
            || 'yo4x_conversion_worker, yo4x_strategy_verifier, '
            || 'yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime, '
            || 'yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime',
            target.signature);
    end loop;
end
$$;

-- Shared runtime credentials have no use for server-side large objects, raw
-- advisory locks, or caller-authored logical WAL messages. These APIs are
-- otherwise PUBLIC by default: large objects/WAL can consume unbounded storage
-- and advisory locks can exhaust the shared lock table. Only the NOLOGIN
-- migrator receives the transaction-lock primitives used behind YO4X
-- SECURITY DEFINER boundaries.
do $$
declare
    target record;
    runtime_roles constant text :=
        'yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, '
        || 'yo4x_secret_ingestion, yo4x_conversion_worker, '
        || 'yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, '
        || 'yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime';
begin
    for target in
        select function.oid::regprocedure::text as signature
        from pg_catalog.pg_proc as function
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = function.pronamespace
        where namespace.nspname = 'pg_catalog'
          and
          (
              left(function.proname, 3) = 'lo_'
              or function.proname in ('loread', 'lowrite')
              or function.proname = 'pg_logical_emit_message'
          )
    loop
        execute format(
            'revoke all privileges on function %s from public, %s',
            target.signature,
            runtime_roles);
    end loop;

    for target in
        select function.oid::regprocedure::text as signature
        from pg_catalog.pg_proc as function
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = function.pronamespace
        where namespace.nspname = 'pg_catalog'
          and position('advisory' in function.proname) > 0
    loop
        execute format(
            'revoke all privileges on function %s from public, %s',
            target.signature,
            runtime_roles);
    end loop;
end
$$;

grant execute on function pg_catalog.pg_advisory_xact_lock(bigint),
    pg_catalog.pg_advisory_xact_lock(integer, integer),
    pg_catalog.pg_advisory_xact_lock_shared(integer, integer)
    to yo4x_migrator;

-- REVOKE cannot remove implicit owner rights. Repair ownership drift outside
-- the protected schemas for every runtime identity. A runtime-owned system
-- catalog is not safely repairable here and aborts the deployment instead.
do $$
declare
    runtime_role_oids oid[];
    target record;
begin
    select array_agg(role.oid order by role.oid)
    into strict runtime_role_oids
    from pg_catalog.pg_roles as role
    where role.rolname in
        ('yo4x_context_authority', 'yo4x_context_issuer',
         'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
         'yo4x_secret_ingestion', 'yo4x_conversion_worker',
         'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
         'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
         'yo4x_gateway_runtime', 'yo4x_credential_runtime');

    if exists
    (
        select 1
        from pg_catalog.pg_namespace as namespace
        where namespace.nspowner = any(runtime_role_oids)
          and (namespace.nspname = 'information_schema'
               or namespace.nspname ~ '^pg_')
    ) or exists
    (
        select 1
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where relation.relowner = any(runtime_role_oids)
          and (namespace.nspname = 'information_schema'
               or namespace.nspname ~ '^pg_')
          and not
          (
              relation.relowner =
                  (select role.oid
                   from pg_catalog.pg_roles as role
                   where role.rolname = 'yo4x_context_authority')
              and namespace.nspname = 'pg_toast'
              and
              (
                  relation.oid =
                      (select base.reltoastrelid
                       from pg_catalog.pg_class as base
                       join pg_catalog.pg_namespace as base_namespace
                         on base_namespace.oid = base.relnamespace
                       where base_namespace.nspname = 'control'
                         and base.relname = 'tenant_context_capabilities')
                  or exists
                  (
                      select 1
                      from pg_catalog.pg_index as toast_index
                      where toast_index.indexrelid = relation.oid
                        and toast_index.indrelid =
                            (select base.reltoastrelid
                             from pg_catalog.pg_class as base
                             join pg_catalog.pg_namespace as base_namespace
                               on base_namespace.oid = base.relnamespace
                             where base_namespace.nspname = 'control'
                               and base.relname = 'tenant_context_capabilities')
                  )
              )
          )
    ) or exists
    (
        select 1
        from pg_catalog.pg_proc as function
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = function.pronamespace
        where function.proowner = any(runtime_role_oids)
          and (namespace.nspname = 'information_schema'
               or namespace.nspname ~ '^pg_')
    ) then
        raise exception 'A YO4X runtime identity owns a system catalog object';
    end if;

    for target in
        select namespace.nspname
        from pg_catalog.pg_namespace as namespace
        where namespace.nspowner = any(runtime_role_oids)
          and namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and namespace.nspname not in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
    loop
        execute format('alter schema %I owner to yo4x_migrator', target.nspname);
    end loop;

    for target in
        select namespace.nspname, relation.relname, relation.relkind
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where relation.relowner = any(runtime_role_oids)
          and namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and namespace.nspname not in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
          and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
    loop
        case target.relkind
            when 'v' then execute format(
                'alter view %I.%I owner to yo4x_migrator',
                target.nspname, target.relname);
            when 'm' then execute format(
                'alter materialized view %I.%I owner to yo4x_migrator',
                target.nspname, target.relname);
            when 'S' then execute format(
                'alter sequence %I.%I owner to yo4x_migrator',
                target.nspname, target.relname);
            when 'f' then execute format(
                'alter foreign table %I.%I owner to yo4x_migrator',
                target.nspname, target.relname);
            else execute format(
                'alter table %I.%I owner to yo4x_migrator',
                target.nspname, target.relname);
        end case;
    end loop;

    for target in
        select function.oid::regprocedure::text as signature,
            function.prokind
        from pg_catalog.pg_proc as function
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = function.pronamespace
        where function.proowner = any(runtime_role_oids)
          and namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and namespace.nspname not in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
    loop
        execute format(
            case target.prokind
                when 'p' then 'alter procedure %s owner to yo4x_migrator'
                when 'a' then 'alter aggregate %s owner to yo4x_migrator'
                else 'alter function %s owner to yo4x_migrator' end,
            target.signature);
    end loop;

    for target in
        select type_record.oid::regtype::text as signature
        from pg_catalog.pg_type as type_record
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = type_record.typnamespace
        where type_record.typowner = any(runtime_role_oids)
          and type_record.typrelid = 0
          and type_record.typelem = 0
          and namespace.nspname <> 'information_schema'
          and namespace.nspname !~ '^pg_'
          and namespace.nspname not in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
    loop
        execute format(
            'alter type %s owner to yo4x_migrator',
            target.signature);
    end loop;

    for target in
        select language_record.lanname
        from pg_catalog.pg_language as language_record
        where language_record.lanowner = any(runtime_role_oids)
    loop
        execute format(
            'alter language %I owner to yo4x_migrator',
            target.lanname);
    end loop;

    for target in
        select tablespace.spcname
        from pg_catalog.pg_tablespace as tablespace
        where tablespace.spcowner = any(runtime_role_oids)
    loop
        execute format(
            'alter tablespace %I owner to yo4x_migrator',
            target.spcname);
    end loop;

    for target in
        select wrapper.fdwname
        from pg_catalog.pg_foreign_data_wrapper as wrapper
        where wrapper.fdwowner = any(runtime_role_oids)
    loop
        execute format(
            'alter foreign data wrapper %I owner to yo4x_migrator',
            target.fdwname);
    end loop;

    for target in
        select foreign_server.srvname
        from pg_catalog.pg_foreign_server as foreign_server
        where foreign_server.srvowner = any(runtime_role_oids)
    loop
        execute format(
            'alter server %I owner to yo4x_migrator',
            target.srvname);
    end loop;

    for target in
        select large_object.oid
        from pg_catalog.pg_largeobject_metadata as large_object
        where large_object.lomowner = any(runtime_role_oids)
    loop
        execute format(
            'alter large object %s owner to yo4x_migrator',
            target.oid);
    end loop;
end
$$;

grant all privileges on schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;
grant all privileges on all tables in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;
grant all privileges on all sequences in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;
grant all privileges on all functions in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;

-- Reapplication is globally subtractive for every direct runtime identity.
-- A stale grant from an earlier deployment can never survive merely because
-- the current explicit grant list no longer mentions it.
revoke all privileges on schema identity, "authorization", control, operations,
    governance, audit, messaging, readmodel
    from yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
    yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier,
    yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
    yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime;
revoke all privileges on all tables in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel
    from yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
    yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier,
    yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
    yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime;
do $$
declare
    relation record;
begin
    for relation in
        select namespace.nspname as schema_name,
            class.relname as relation_name,
            class.relkind,
            string_agg(quote_ident(attribute.attname), ', ' order by attribute.attnum)
                as column_names
        from pg_catalog.pg_class as class
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = class.relnamespace
        join pg_catalog.pg_attribute as attribute
          on attribute.attrelid = class.oid
         and attribute.attnum > 0
         and not attribute.attisdropped
        where namespace.nspname in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
          and class.relkind in ('r', 'p', 'v', 'm', 'f')
        group by namespace.nspname, class.relname, class.relkind
    loop
        if relation.relkind = 'm' then
            execute format(
                'revoke select (%s) on %I.%I from '
                || 'yo4x_migrator, yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, '
                || 'yo4x_secret_ingestion, yo4x_conversion_worker, '
                || 'yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, '
                || 'yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime',
                relation.column_names, relation.schema_name, relation.relation_name);
        else
            execute format(
                'revoke select (%1$s), insert (%1$s), update (%1$s), references (%1$s) '
                || 'on %2$I.%3$I from '
                || 'yo4x_migrator, yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency, '
                || 'yo4x_secret_ingestion, yo4x_conversion_worker, '
                || 'yo4x_strategy_verifier, yo4x_runtime_evidence, yo4x_worker, '
                || 'yo4x_supervisor_runtime, yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime',
                relation.column_names, relation.schema_name, relation.relation_name);
        end if;
    end loop;
end
$$;

revoke all privileges on all sequences in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel
    from yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
    yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier,
    yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
    yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime;
revoke all privileges on all functions in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel
    from yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
    yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier,
    yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
    yo4x_trade_authorizer, yo4x_gateway_runtime, yo4x_credential_runtime;

-- Every protected object belongs to the dedicated non-login migration boundary,
-- never to the bootstrap superuser or a runtime login. Owner rights are implicit
-- and cannot be made least-privilege with REVOKE, so ownership is part of the
-- deployment contract rather than an ACL implementation detail.
do $$
declare
    protected_schema record;
    protected_relation record;
    owned_function record;
    target_owner text;
begin
    for protected_schema in
        select namespace.nspname as schema_name
        from pg_catalog.pg_namespace as namespace
        where namespace.nspname in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
    loop
        execute format(
            'alter schema %I owner to yo4x_migrator',
            protected_schema.schema_name);
    end loop;

    for protected_relation in
        select namespace.nspname as schema_name,
            relation.relname as relation_name,
            relation.relkind
        from pg_catalog.pg_class as relation
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = relation.relnamespace
        where namespace.nspname in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
          and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
    loop
        target_owner := case
            when protected_relation.schema_name = 'control'
             and protected_relation.relation_name = 'tenant_context_capabilities'
                then 'yo4x_context_authority'
            else 'yo4x_migrator'
        end;

        case protected_relation.relkind
            when 'v' then
                execute format(
                    'alter view %I.%I owner to %I',
                    protected_relation.schema_name,
                    protected_relation.relation_name,
                    target_owner);
            when 'm' then
                execute format(
                    'alter materialized view %I.%I owner to %I',
                    protected_relation.schema_name,
                    protected_relation.relation_name,
                    target_owner);
            when 'S' then
                execute format(
                    'alter sequence %I.%I owner to %I',
                    protected_relation.schema_name,
                    protected_relation.relation_name,
                    target_owner);
            when 'f' then
                execute format(
                    'alter foreign table %I.%I owner to %I',
                    protected_relation.schema_name,
                    protected_relation.relation_name,
                    target_owner);
            else
                execute format(
                    'alter table %I.%I owner to %I',
                    protected_relation.schema_name,
                    protected_relation.relation_name,
                    target_owner);
        end case;
    end loop;

    for owned_function in
        select function.oid::regprocedure::text as signature,
            function.prokind
        from pg_catalog.pg_proc as function
        join pg_catalog.pg_namespace as namespace
          on namespace.oid = function.pronamespace
        where namespace.nspname in
            ('identity', 'authorization', 'control', 'operations',
             'governance', 'audit', 'messaging', 'readmodel')
    loop
        target_owner := case
            when owned_function.signature in
            (
                'control.reject_tenant_context_capability_rewrite()',
                'control.current_tenant_id()',
                'control.current_actor_id()',
                'control.current_correlation_id()',
                'control.current_session_id()',
                'control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid)',
                'control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                'control.issue_credential_runtime_tenant_context_capability(bytea,text,integer,text,uuid,uuid,uuid,uuid)',
                'control.activate_credential_runtime_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                'control.cleanup_tenant_context_capabilities(integer)',
                'control.bind_verified_strategy_import_tenant_context(bytea,uuid,uuid,uuid,uuid)'
            ) then 'yo4x_context_authority'
            else 'yo4x_migrator'
        end;

        case owned_function.prokind
            when 'p' then
                execute format(
                    'alter procedure %s owner to %I',
                    owned_function.signature,
                    target_owner);
            when 'a' then
                execute format(
                    'alter aggregate %s owner to %I',
                    owned_function.signature,
                    target_owner);
            else
                execute format(
                    'alter function %s owner to %I',
                    owned_function.signature,
                    target_owner);
        end case;
    end loop;
end
$$;

-- Reapplying the subtractive grants above can remove an existing owner's own
-- ACL entries before the idempotent owner handoff. Restore only the authority
-- trust root's exact self-capabilities; the generic migrator must not retain a
-- raw-table path into transaction context evidence.
revoke all privileges on table control.tenant_context_capabilities
    from yo4x_migrator;
grant all privileges on table control.tenant_context_capabilities
    to yo4x_context_authority;
revoke all privileges on function
    control.reject_tenant_context_capability_rewrite(),
    control.current_tenant_id(),
    control.current_actor_id(),
    control.current_correlation_id(),
    control.current_session_id(),
    control.issue_tenant_context_capability(
        bytea, text, text, integer, text, uuid, uuid, uuid, uuid),
    control.activate_tenant_context(bytea, uuid, uuid, uuid, uuid),
    control.issue_credential_runtime_tenant_context_capability(
        bytea, text, integer, text, uuid, uuid, uuid, uuid),
    control.activate_credential_runtime_tenant_context(
        bytea, uuid, uuid, uuid, uuid),
    control.cleanup_tenant_context_capabilities(integer),
    control.bind_verified_strategy_import_tenant_context(
        bytea, uuid, uuid, uuid, uuid)
    from yo4x_migrator;
grant execute on function
    control.reject_tenant_context_capability_rewrite(),
    control.current_tenant_id(),
    control.current_actor_id(),
    control.current_correlation_id(),
    control.current_session_id(),
    control.issue_tenant_context_capability(
        bytea, text, text, integer, text, uuid, uuid, uuid, uuid),
    control.activate_tenant_context(bytea, uuid, uuid, uuid, uuid),
    control.issue_credential_runtime_tenant_context_capability(
        bytea, text, integer, text, uuid, uuid, uuid, uuid),
    control.activate_credential_runtime_tenant_context(
        bytea, uuid, uuid, uuid, uuid),
    control.cleanup_tenant_context_capabilities(integer),
    control.bind_verified_strategy_import_tenant_context(
        bytea, uuid, uuid, uuid, uuid)
    to yo4x_context_authority;

-- The migrator-owned definer graph calls these authority-owned private
-- boundaries. Grant only those internal call edges after the owner handoff;
-- no runtime login receives the private import binding function.
grant execute on function control.current_tenant_id(),
    control.current_actor_id(), control.current_correlation_id(),
    control.current_session_id(),
    control.bind_verified_strategy_import_tenant_context(
        bytea, uuid, uuid, uuid, uuid)
    to yo4x_migrator;

-- PUBLIC must not recover implicit object or database capabilities from
-- PostgreSQL defaults. Future objects created by the non-login owner inherit
-- the same fail-closed boundary.
revoke all privileges on schema identity, "authorization", control, operations,
    governance, audit, messaging, readmodel from public;
revoke all privileges on all tables in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel from public;
revoke all privileges on all sequences in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel from public;
revoke all privileges on all functions in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel from public;
alter default privileges for role yo4x_migrator
    revoke all privileges on tables from public;
alter default privileges for role yo4x_migrator
    revoke all privileges on sequences from public;
alter default privileges for role yo4x_migrator
    revoke all privileges on functions from public;
alter default privileges for role yo4x_context_authority
    revoke all privileges on tables from public;
alter default privileges for role yo4x_context_authority
    revoke all privileges on sequences from public;
alter default privileges for role yo4x_context_authority
    revoke all privileges on functions from public;

grant usage on schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency;
grant usage on schema control to yo4x_context_authority, yo4x_context_issuer;
revoke usage on schema identity, operations, audit, messaging from yo4x_secret_ingestion;
grant usage on schema control to yo4x_secret_ingestion;
grant usage on schema control, governance
    to yo4x_conversion_worker;
grant usage on schema control to yo4x_strategy_verifier;
revoke usage on schema operations, audit, messaging from yo4x_runtime_evidence;
grant usage on schema control to yo4x_runtime_evidence;
grant usage on schema identity, control, operations, governance, audit, messaging, readmodel
    to yo4x_worker;
grant usage on schema control to yo4x_supervisor_runtime;
grant usage on schema control
    to yo4x_trade_authorizer, yo4x_gateway_runtime,
       yo4x_credential_runtime;

grant execute on function control.current_tenant_id(), control.current_actor_id(),
    control.current_correlation_id(), control.current_session_id(), control.assert_safe_runtime_role()
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
       yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_runtime_evidence, yo4x_worker,
       yo4x_strategy_verifier, yo4x_supervisor_runtime,
       yo4x_trade_authorizer, yo4x_gateway_runtime,
       yo4x_credential_runtime;
grant execute on function control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
       yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_strategy_verifier,
       yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime,
       yo4x_trade_authorizer, yo4x_gateway_runtime;
grant execute on function control.assert_safe_runtime_role(),
    control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid),
    control.cleanup_tenant_context_capabilities(integer)
    to yo4x_context_issuer;
grant execute on function
    control.issue_credential_runtime_tenant_context_capability(
        bytea, text, integer, text, uuid, uuid, uuid, uuid)
    to yo4x_context_issuer;
grant execute on function
    control.activate_credential_runtime_tenant_context(
        bytea, uuid, uuid, uuid, uuid)
    to yo4x_credential_runtime;
grant select (migration_id, sha256) on control.schema_migrations
    to yo4x_context_issuer, yo4x_credential_runtime;

-- Tenant control API: ordinary user identity/session reads and user-operation
-- orchestration. No privileged-admin identity or approval mutation rights.
revoke all privileges on control.schema_migrations from yo4x_control_api;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_control_api;
revoke all on function control.acquire_u0_authority_lock() from public;
revoke all on function control.acquire_u0_tenant_authority_lock(uuid) from public;
grant execute on function control.acquire_u0_authority_lock()
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency, yo4x_worker;
revoke execute on function control.acquire_u0_authority_lock()
    from yo4x_secret_ingestion, yo4x_runtime_evidence;
grant select on identity.tenants, identity.user_identities, identity.user_session_families,
    identity.invalidated_session_tokens, operations.deployments,
    governance.broker_profiles, governance.risk_policy_versions,
    control.idempotency_records, control.user_operations, readmodel.deployment_health
    to yo4x_control_api;
grant select (id, sha256, state, signature_state, licence_evidence, network_evidence)
    on governance.gateway_artifacts to yo4x_control_api;
grant select (id, strategy_id, package_sha256, state)
    on governance.strategy_versions to yo4x_control_api;
grant select
(
    id, tenant_id, strategy_version_id, strategy_package_sha256,
    verification_evidence_sha256, verification_signature_algorithm,
    verification_signature_sha256, verification_signing_key_id,
    parsed_and_type_checked, metaeditor_compile_proven,
    semantic_conversion_proven, reference_parity_proven, demo_runtime_proven
) on governance.strategy_version_source_bindings to yo4x_control_api;
grant select (id, broker_profile_id, gateway_artifact_id, state, evidence_sha256, completed_at)
    on governance.compatibility_test_runs to yo4x_control_api;
grant select (id, tenant_id, policy_version, scope_type, scope_id,
    allow_new_deployment, allow_strategy_signals, allow_exposure_increase,
    allow_exposure_reduction, allow_protection, allow_pending_order_cancellation,
    allow_emergency_close, lease_mode, worker_actions, credential_mode,
    package_eligibility, state, policy_digest, signature_algorithm,
    signature_bytes, signature_sha256, signing_key_id)
    on control.execution_safety_policies to yo4x_control_api;
grant select (id, tenant_id, broker_account_id, masked_account_binding,
    credential_exists, credential_state, last_authorized_worker_use_at, deletion_state,
    source_version, projected_at)
    on readmodel.secret_metadata to yo4x_control_api;
grant update (state, revoked_at, row_version, updated_at)
    on identity.user_session_families to yo4x_control_api;
grant insert (id, tenant_id, user_id, broker_account_id, strategy_version_id,
    strategy_source_binding_id, strategy_verification_evidence_sha256,
    strategy_verification_signature_sha256, strategy_verification_signing_key_id,
    risk_policy_version_id, risk_policy_digest, gateway_artifact_id,
    gateway_digest, runtime_digest, strategy_package_digest, region, dedicated_account,
    hedging_account, broker_hosted_stop_loss, broker_hosted_take_profit,
    manual_or_external_trading_detected, binding_evidence,
    binding_evidence_sha256, creation_effective_policy_digest,
    creation_policy_version_watermark, creation_policy_input_sha256,
    configuration_sha256, environment, deployment_mode, desired_state,
    observed_state, fence_generation, row_version, created_at, updated_at)
    on operations.deployments to yo4x_control_api;
grant update (desired_state, fence_generation, row_version, updated_at)
    on operations.deployments to yo4x_control_api;
grant insert (id, tenant_id, actor_id, operation, idempotency_key,
    request_sha256, created_at, expires_at)
    on control.idempotency_records to yo4x_control_api;
grant update (state, response_status, response_body, response_sha256, completed_at, retired_at)
    on control.idempotency_records to yo4x_control_api;
grant insert (id, tenant_id, user_id, session_family_id, operation_type,
    target_type, target_id, state, idempotency_record_id,
    expected_resource_version, submitted_resource_version, requested_target_state,
    reason, correlation_id,
    effective_policy_digest, policy_version_watermark, policy_input_sha256,
    row_version, created_at, updated_at)
    on control.user_operations to yo4x_control_api;
grant insert (id, tenant_id, user_id, idempotency_record_id, decision_type,
    target_type, target_id, input_snapshot, applicable_policies, effective_vector,
    rule_results, decision, effective_policy_digest, policy_version_watermark,
    input_sha256, evidence_sha256, evaluated_at)
    on control.user_policy_evaluations to yo4x_control_api;
grant update (credential_state, state, row_version, updated_at)
    on operations.broker_accounts to yo4x_control_api;
grant select (id, tenant_id, broker_account_id, operation, allowed_origin, state,
    reservation_id, reserved_at, reservation_expires_at, expires_at, consumed_at,
    completion_digest, row_version, created_at, updated_at)
    on control.credential_ingestion_grants to yo4x_control_api;
grant select (id, tenant_id, user_id, broker_id, broker_profile_id, server,
    masked_login, binding_fingerprint, environment, account_mode, dedicated_cloud_use,
    manual_or_external_trading_detected, trading_allowed,
    broker_hosted_stop_loss, broker_hosted_take_profit, supports_position_query,
    supports_order_query, supports_deal_history, capability_observed_at,
    capability_valid_until, capability_evidence_sha256, credential_state, state,
    row_version, created_at, updated_at)
    on operations.broker_accounts to yo4x_control_api;
grant insert (id, tenant_id, broker_account_id, operation, allowed_origin,
    bearer_hash, nonce_hash, proof_key_id, expires_at)
    on control.credential_ingestion_grants to yo4x_control_api;
grant insert (id, tenant_id, user_id, correlation_id, source_label, capability_sha256,
    proof_key_id, expires_at)
    on control.strategy_import_jobs to yo4x_control_api;
grant select (id, tenant_id, user_id, state, row_version, expires_at, updated_at)
    on control.strategy_import_jobs to yo4x_control_api;
-- Tenant compatibility reads expose identifiers and static classifications
-- only. Source bytes, findings, and conversion evidence documents remain
-- outside the Control API surface.
revoke all privileges on governance.strategy_source_corpora,
    governance.strategy_source_files,
    governance.strategy_conversion_classifications
    from yo4x_control_api;
grant select (id, tenant_id, user_id, file_count, state)
    on governance.strategy_source_corpora to yo4x_control_api;
grant select (tenant_id, corpus_id, user_id)
    on governance.strategy_conversion_classifications to yo4x_control_api;
grant select (id, tenant_id, corpus_id, user_id, manifest_order, relative_path,
    source_kind, features, disposition)
    on governance.strategy_source_files to yo4x_control_api;
grant update (state, reservation_id, reservation_expires_at, row_version, updated_at)
    on control.strategy_import_jobs to yo4x_control_api;
grant update (state, reservation_id, reserved_at, reservation_expires_at,
    cleanup_claim_token, cleanup_claimed_by, cleanup_claim_expires_at,
    row_version, updated_at)
    on control.credential_ingestion_grants to yo4x_control_api;
grant insert, select on identity.invalidated_session_tokens, control.tenant_contexts,
    audit.audit_events
    to yo4x_control_api;
grant insert on messaging.outbox_messages to yo4x_control_api;

-- Admin BFF: privileged command workflow and tenant-scoped operational views.
revoke all privileges on control.schema_migrations from yo4x_admin_bff;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_admin_bff;
revoke all privileges on control.credential_ingestion_grants,
    control.strategy_import_jobs,
    governance.strategy_source_corpora,
    governance.strategy_source_files,
    governance.strategy_conversion_classifications,
    operations.broker_exposure_snapshots,
    operations.broker_command_risk_decisions,
    operations.broker_commands,
    operations.broker_command_reconciliations,
    operations.broker_accounts,
    readmodel.secret_metadata
    from yo4x_admin_bff;
revoke insert, update, delete, truncate, references, trigger
    on operations.execution_leases from yo4x_admin_bff;
revoke all privileges on audit.audit_events, messaging.outbox_messages
    from yo4x_admin_bff;
grant select on identity.tenants, identity.user_identities, identity.admin_identities,
    identity.admin_sessions, "authorization".permissions, "authorization".roles,
    "authorization".role_permissions, "authorization".role_assignments,
    "authorization".access_reviews, "authorization".privileged_infrastructure_grants,
    control.tenant_contexts, control.idempotency_records, control.impact_previews,
    control.admin_commands, control.user_operations, control.command_targets,
    control.policy_evaluations, control.approval_requests, control.approval_decisions,
    control.command_audit_intents, control.execution_safety_policies,
    control.emergency_safety_commands, operations.deployments,
    operations.worker_nodes, operations.worker_assignments, operations.execution_leases,
    operations.runtime_component_evidence, operations.runtime_event_cursors,
    operations.runtime_event_inbox, operations.deployment_reconciliations,
    operations.support_cases, operations.incidents, governance.broker_profiles,
    governance.gateway_artifacts, governance.compatibility_test_runs,
    governance.strategy_versions, governance.strategy_version_source_bindings,
    governance.risk_policy_versions, governance.release_records, audit.audit_events,
    audit.archive_deliveries, readmodel.deployment_health
    to yo4x_admin_bff;
grant select (id, tenant_id, broker_account_id, masked_account_binding,
    credential_exists, credential_state, last_authorized_worker_use_at, deletion_state,
    source_version, projected_at)
    on readmodel.secret_metadata to yo4x_admin_bff;
grant select on control.user_policy_evaluations to yo4x_admin_bff;
grant select (id, tenant_id, user_id, broker_id, broker_profile_id, server,
    masked_login, binding_fingerprint, environment, account_mode, dedicated_cloud_use,
    manual_or_external_trading_detected, trading_allowed,
    broker_hosted_stop_loss, broker_hosted_take_profit, supports_position_query,
    supports_order_query, supports_deal_history, capability_observed_at,
    capability_valid_until, capability_evidence_sha256, credential_state, state,
    row_version, created_at, updated_at)
    on operations.broker_accounts to yo4x_admin_bff;
grant insert, update on identity.admin_identities, identity.admin_sessions,
    "authorization".roles, "authorization".role_permissions, "authorization".role_assignments,
    "authorization".access_reviews, "authorization".privileged_infrastructure_grants,
    control.tenant_contexts, control.idempotency_records, control.impact_previews,
    control.admin_commands, control.command_targets, control.policy_evaluations,
    control.approval_requests, control.approval_decisions, control.command_audit_intents,
    operations.support_cases, operations.incidents,
    governance.risk_policy_versions, governance.release_records
    to yo4x_admin_bff;
grant insert
(
    id, tenant_id, strategy_id, version_number, package_sha256,
    manifest_sha256, schema_sha256, provenance, evidence,
    row_version, created_at, updated_at
) on governance.strategy_versions to yo4x_admin_bff;
grant update (evidence, state, row_version, updated_at)
    on governance.strategy_versions to yo4x_admin_bff;
grant execute on function control.promote_strategy_version_to_demo_approved(
    uuid, uuid, bigint, uuid) to yo4x_admin_bff;
grant insert on audit.audit_events, messaging.outbox_messages to yo4x_admin_bff;

-- Emergency plane: deliberately narrow containment and reconciliation surface.
grant select on identity.tenants, identity.admin_identities, identity.admin_sessions,
    operations.deployments, operations.worker_assignments, operations.incidents,
    control.idempotency_records, control.impact_previews, control.admin_commands,
    control.command_targets, control.execution_safety_policies,
    control.emergency_safety_commands, audit.audit_events
    to yo4x_emergency;
grant insert, update on control.idempotency_records, control.impact_previews,
    control.admin_commands, control.command_targets,
    control.emergency_safety_commands, operations.incidents
    to yo4x_emergency;
grant insert (id, tenant_id, policy_version, scope_type, scope_id,
    allow_new_deployment, allow_strategy_signals, allow_exposure_increase,
    allow_exposure_reduction, allow_protection, allow_pending_order_cancellation,
    allow_emergency_close, lease_mode, worker_actions, credential_mode,
    package_eligibility, reason, incident_id, state, owner_id,
    authority_expires_at, review_deadline, policy_digest, signature_algorithm,
    signature_bytes, signature_sha256, signing_key_id, row_version, created_at, updated_at)
    on control.execution_safety_policies to yo4x_emergency;
grant update (state, row_version, updated_at)
    on control.execution_safety_policies to yo4x_emergency;
grant insert on control.tenant_contexts, audit.audit_events, messaging.outbox_messages
    to yo4x_emergency;

-- Requested.v4/v3 outbox payloads contain raw one-use delivery/result
-- bearers. Only the worker/dispatcher can read payload bytes. Control,
-- administration, and emergency surfaces receive redacted delivery metadata.
grant select
(
    id, tenant_id, message_type, schema_version, aggregate_type, aggregate_id,
    payload_sha256, correlation_id, causation_id, occurred_at, available_at,
    state, attempts, locked_by, locked_until, published_at, last_error
)
on messaging.outbox_messages
to yo4x_control_api, yo4x_admin_bff, yo4x_emergency;

-- Secret ingestion: execute-only SECURITY DEFINER capabilities compare
-- proof hashes, serialize account/grant changes, and append redacted evidence;
-- the runtime role cannot inspect proof hashes or fabricate terminal state.
revoke all privileges on control.schema_migrations from yo4x_secret_ingestion;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_secret_ingestion;
revoke all privileges on control.credential_ingestion_grants,
    operations.broker_accounts, audit.audit_events, messaging.outbox_messages
    from yo4x_secret_ingestion;
grant execute on function control.reserve_credential_ingestion_grant(
        uuid, uuid, text, text, text, integer, uuid, uuid),
    control.release_credential_ingestion_grant(uuid, uuid, bigint, uuid, uuid),
    control.complete_credential_ingestion_grant(
        uuid, uuid, bigint, text, text, uuid, uuid)
    to yo4x_secret_ingestion;

-- Authenticated user-operation-result ingress is execute-only. Its exact
-- SECURITY DEFINER capability verifies the durable handoff and appends
-- redacted evidence; the login itself has no raw proof/audit/outbox writes.
-- Valid proof deliberately dominates fallible outbox acknowledgement state.
revoke all privileges on control.schema_migrations from yo4x_runtime_evidence;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_runtime_evidence;
revoke execute on function control.record_broker_user_operation_result(
    uuid, uuid, uuid, uuid, text, uuid, bigint, text, text, text,
    text, boolean, boolean, boolean, text, text, text, text, text,
    timestamptz)
    from yo4x_runtime_evidence;
revoke execute on function control.record_deployment_user_operation_result(
    uuid, uuid, uuid, uuid, text, uuid, bigint, text, text, text,
    text, boolean, boolean, text, text, text, boolean, text, text, text, text, text,
    timestamptz)
    from yo4x_runtime_evidence;
grant execute on function control.record_user_operation_result_v5(
    uuid, uuid, uuid, uuid, uuid, uuid, uuid, uuid, text,
    uuid, uuid, uuid, text, text, uuid, jsonb, bigint, text, text, text,
    text, text, timestamptz, text, uuid, uuid, uuid, bigint, text)
    to yo4x_runtime_evidence;
revoke all privileges on audit.audit_events, messaging.outbox_messages
    from yo4x_runtime_evidence;

-- Strategy verification is deliberately separate from source ingestion and
-- administration. It can only invoke the signed immutable-record capability.
revoke all privileges on governance.strategy_source_corpora,
    governance.strategy_source_files, governance.strategy_versions,
    governance.strategy_version_source_bindings, audit.audit_events
    from yo4x_strategy_verifier;
grant execute on function control.record_strategy_version_source_binding(
    uuid, uuid, uuid, text, text, text, text, text, text, text, text,
    text, text, text, bytea, bytea, text, timestamptz, uuid)
    to yo4x_strategy_verifier;
alter role yo4x_strategy_verifier set statement_timeout = '2min';
alter role yo4x_strategy_verifier set lock_timeout = '15s';
alter role yo4x_strategy_verifier set idle_in_transaction_session_timeout = '30s';

-- Conversion worker: tenant/user-bound, static-only source persistence. It can
-- neither create/promote strategy versions nor read broker/runtime credentials.
revoke all on function control.acquire_strategy_import_job(uuid, bytea),
    control.acquire_strategy_import_persistence_lock(uuid),
    control.persist_strategy_conversion_classification(
        uuid, text, text, text, text, text, text, text, text, text,
        integer, bigint, jsonb, bytea, bytea, uuid, uuid),
    control.complete_strategy_import_job(uuid, uuid, uuid)
    from public;
grant execute on function control.acquire_strategy_import_job(uuid, bytea),
    control.acquire_strategy_import_persistence_lock(uuid),
    control.persist_strategy_conversion_classification(
        uuid, text, text, text, text, text, text, text, text, text,
        integer, bigint, jsonb, bytea, bytea, uuid, uuid),
    control.complete_strategy_import_job(uuid, uuid, uuid)
    to yo4x_conversion_worker;

alter role yo4x_conversion_worker set statement_timeout = '2min';
alter role yo4x_conversion_worker set lock_timeout = '15s';
alter role yo4x_conversion_worker set idle_in_transaction_session_timeout = '30s';
grant insert (id, tenant_id, user_id, import_job_id, reservation_id,
    source_label, schema_version, analyzer_version, corpus_sha256,
    manifest_sha256, report_sha256, file_count, total_bytes,
    disposition_counts, manifest, manifest_content, report_content,
    state)
    on governance.strategy_source_corpora to yo4x_conversion_worker;
grant insert (id, tenant_id, corpus_id, user_id, import_job_id,
    reservation_id, manifest_order, relative_path, source_kind, byte_length, source_sha256,
    text_encoding, entrypoints, includes, features, findings, disposition,
    verification, source_content)
    on governance.strategy_source_files to yo4x_conversion_worker;
revoke all privileges on governance.strategy_conversion_classifications
    from yo4x_conversion_worker;

-- Supervisor runtime: execute-only strategy journal/transaction capability.
-- It cannot inspect or mutate authority, event, state, action, audit, or outbox
-- tables directly, and it cannot invoke the internal authority resolver.
revoke all privileges on all tables in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel
    from yo4x_supervisor_runtime;
revoke all privileges on all sequences in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel
    from yo4x_supervisor_runtime;
revoke all privileges on all functions in schema identity, "authorization", control,
    operations, governance, audit, messaging, readmodel
    from yo4x_supervisor_runtime;
grant execute on function control.current_tenant_id(), control.current_actor_id(),
    control.current_correlation_id(), control.current_session_id(),
    control.assert_safe_runtime_role(),
    control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)
    to yo4x_supervisor_runtime;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_supervisor_runtime;
grant execute on function control.persist_strategy_event(
        uuid, uuid, bigint, bigint, uuid, integer, integer, text,
        bigint, integer, text, bytea, bytea),
    control.claim_strategy_event(
        uuid, uuid, bigint, bigint, uuid, integer, integer, text,
        bigint, integer, text, uuid, integer),
    control.recover_expired_strategy_event_claim(
        uuid, uuid, bigint, bigint, uuid, uuid),
    control.commit_strategy_event(
        uuid, uuid, bigint, bigint, uuid, uuid, bytea, text),
    control.read_strategy_event_commit(uuid, uuid, bigint, bigint, uuid)
    to yo4x_supervisor_runtime;
grant execute on function control.claim_user_operation_delivery(
        uuid, text, uuid, text, interval, uuid, uuid, uuid, bigint, text),
    control.reject_user_operation_before_invocation(
        uuid, uuid, integer, text, uuid, text, uuid, uuid, uuid, bigint, text)
    to yo4x_supervisor_runtime;
alter role yo4x_supervisor_runtime set statement_timeout = '5s';
alter role yo4x_supervisor_runtime set lock_timeout = '2s';
alter role yo4x_supervisor_runtime set idle_in_transaction_session_timeout = '10s';

-- Worker: exact deployment/policy/package reads, assignment/reconciliation state,
-- command-target acknowledgement, and outbox delivery.
revoke all privileges on control.schema_migrations from yo4x_worker;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_worker;
revoke insert, update, delete on operations.execution_leases from yo4x_worker;
revoke insert, update, delete on operations.deployment_reconciliations from yo4x_worker;
grant execute on function control.apply_confirmed_broker_operation_result(uuid, uuid, uuid)
    to yo4x_worker;
grant execute on function control.persist_signed_execution_lease(bytea, bigint)
    to yo4x_worker;
grant execute on function control.claim_credential_grant_cleanup(
    uuid, uuid, bigint, text, integer)
    to yo4x_worker;
grant execute on function control.complete_credential_grant_cleanup(
    uuid, uuid, bigint, text, uuid, uuid)
    to yo4x_worker;
grant execute on function control.refresh_user_operation_backlog_observation()
    to yo4x_worker;
grant execute on function control.defer_user_operation(uuid, uuid, bigint, text, text)
    to yo4x_worker;
grant execute on function control.issue_user_operation_reconciliation_challenge(
    uuid, uuid, uuid, uuid, text, interval)
    to yo4x_worker;
grant execute on function control.create_user_operation_invocation_attempt(
        uuid, uuid, uuid, bigint, uuid, uuid, text, text,
        interval, interval, interval),
    control.reject_user_operation_before_invocation(
        uuid, uuid, integer, text, uuid, text, uuid, uuid, uuid, bigint, text),
    control.advance_user_operation_invocation_timeouts(integer),
    control.issue_user_operation_invocation_reconciliation_challenge_v3(
        uuid, uuid, bigint, uuid, uuid, uuid, text, interval),
    control.reconcile_user_operation_invocation_attempt(uuid, uuid, bigint)
    to yo4x_worker;
grant select on operations.deployments, operations.worker_nodes, operations.worker_assignments,
    operations.execution_leases, operations.runtime_component_evidence,
    operations.runtime_event_cursors, operations.runtime_event_inbox,
    operations.deployment_reconciliations, operations.user_operation_results,
    control.command_targets,
    control.execution_safety_policies, control.user_policy_evaluations,
    governance.gateway_artifacts,
    governance.strategy_versions, governance.strategy_version_source_bindings,
    governance.risk_policy_versions,
    messaging.outbox_messages, readmodel.deployment_health
    to yo4x_worker;
grant select (id) on identity.tenants to yo4x_worker;
grant select on control.worker_tenant_scan_cursors to yo4x_worker;
grant update (last_tenant_id)
    on control.worker_tenant_scan_cursors to yo4x_worker;
grant select (tenant_id, last_deployment_id, last_scan_at, last_advanced_at,
    last_rotation_completed_at, rotation_count, row_version)
    on control.deployment_scan_cursors to yo4x_worker;
grant insert (tenant_id)
    on control.deployment_scan_cursors to yo4x_worker;
grant update (last_deployment_id)
    on control.deployment_scan_cursors to yo4x_worker;
grant select (tenant_id, last_checked_at, oldest_open_created_at,
    refresh_count, row_version)
    on control.user_operation_backlog_observations to yo4x_worker;
grant select (tenant_id, id, operation_id, original_dispatch_message_id,
    route_deployment_id, fence_generation, worker_assignment_id,
    worker_instance_id)
    on control.user_operation_reconciliation_challenges to yo4x_worker;
grant select (tenant_id, challenge_id, target_type, result_record_id,
    result_id, request_sha256)
    on control.user_operation_reconciliation_challenge_consumptions
    to yo4x_worker;
grant select (id, tenant_id, user_id, operation_type, target_type, target_id,
    state, idempotency_record_id, expected_resource_version, correlation_id,
    submitted_resource_version, requested_target_state,
    last_error_code, result_reference, effective_policy_digest,
    policy_version_watermark, policy_input_sha256, dispatch_message_id,
    dispatch_route_deployment_id, dispatch_fence_generation, dispatch_worker_assignment_id,
    dispatch_worker_instance_id, dispatch_target_binding_sha256,
    dispatch_policy_snapshot_sha256, result_capability_sha256,
    result_capability_expires_at, dispatch_assignment_lease_expires_at,
    dispatch_execution_deadline,
    reconciliation_route_deployment_id, reconciliation_fence_generation,
    reconciliation_worker_assignment_id, reconciliation_worker_instance_id,
    dispatch_attempts, dispatched_at, claimed_by, claim_token, claim_expires_at,
    next_processing_at, processing_deferral_count, last_processing_error_code,
    row_version, created_at, updated_at, completed_at)
    on control.user_operations to yo4x_worker;
grant select (id, tenant_id, broker_account_id, operation, state,
    reservation_id, reservation_expires_at, expires_at, row_version,
    cleanup_claim_token, cleanup_claimed_by, cleanup_claim_expires_at,
    created_at, updated_at)
    on control.credential_ingestion_grants to yo4x_worker;
grant select (id, tenant_id, user_id, broker_id, binding_fingerprint, environment,
    account_mode, dedicated_cloud_use, manual_or_external_trading_detected,
    trading_allowed, broker_hosted_stop_loss, broker_hosted_take_profit,
    supports_position_query, supports_order_query, supports_deal_history,
    capability_observed_at, capability_valid_until, capability_evidence_sha256,
    credential_state, state, row_version, updated_at)
    on operations.broker_accounts to yo4x_worker;
grant insert on operations.worker_assignments,
    operations.runtime_event_cursors, operations.runtime_event_inbox,
    messaging.outbox_messages,
    readmodel.deployment_health to yo4x_worker;
grant update (state, lease_expires_at, revoked_at, row_version)
    on operations.worker_assignments to yo4x_worker;
grant update (last_accepted_sequence, last_event_id, row_version, updated_at)
    on operations.runtime_event_cursors to yo4x_worker;
grant update (processing_state, processed_at, result_code, row_version)
    on operations.runtime_event_inbox to yo4x_worker;
grant update (state, attempts, dispatched_at, delivered_at, acknowledged_at,
    applied_at, reconciled_at, observed_result, broker_evidence_reference,
    last_error_code, row_version, updated_at)
    on control.command_targets to yo4x_worker;
grant update (state, last_error_code, result_reference, dispatch_message_id,
    dispatch_route_deployment_id, dispatch_fence_generation, dispatch_worker_assignment_id,
    dispatch_worker_instance_id, dispatch_target_binding_sha256,
    dispatch_policy_snapshot_sha256, result_capability_sha256,
    result_capability_expires_at, dispatch_assignment_lease_expires_at,
    dispatch_execution_deadline,
    reconciliation_route_deployment_id, reconciliation_fence_generation,
    reconciliation_worker_assignment_id, reconciliation_worker_instance_id,
    dispatch_attempts, dispatched_at, claimed_by, claim_token, claim_expires_at,
    row_version, updated_at, completed_at)
    on control.user_operations to yo4x_worker;
revoke update on control.credential_ingestion_grants from yo4x_worker;
revoke update on operations.broker_accounts from yo4x_worker;
grant update (observed_state, lease_expires_at, last_reconciled_at,
    row_version, updated_at)
    on operations.deployments to yo4x_worker;
grant update (state, attempts, available_at, locked_by, locked_until, published_at, last_error)
    on messaging.outbox_messages to yo4x_worker;
grant update (desired_state, supervisor_state, strategy_host_state,
    gateway_host_state, lease_state, broker_state, reconciliation_state,
    fence_generation, last_heartbeat_at, last_reconciled_at, source_version,
    projected_at)
    on readmodel.deployment_health to yo4x_worker;
grant insert on operations.runtime_component_evidence to yo4x_worker;
grant insert on audit.audit_events to yo4x_worker;

-- Strategy-host authorization and gateway dispatch are separate execute-only
-- capabilities. Neither role can read or mutate raw authority/evidence tables.
revoke all privileges on governance.strategy_version_source_bindings,
    operations.broker_exposure_snapshots,
    operations.broker_command_risk_decisions,
    operations.broker_commands,
    operations.broker_command_reconciliations,
    operations.execution_leases,
    operations.deployments,
    operations.broker_accounts,
    audit.audit_events
    from yo4x_trade_authorizer, yo4x_gateway_runtime;

-- Reapplication is subtractive as well as additive: stale grants from an
-- earlier role layout must never collapse authorization and dispatch authority.
revoke execute on function control.authorize_broker_command(
    uuid, uuid, uuid, uuid, bigint, uuid, uuid, uuid, uuid,
    text, text, text, text, text, text, text, text, text, bigint,
    bytea, bytea, text, bigint, text,
    timestamptz, timestamptz, timestamptz, timestamptz, timestamptz,
    timestamptz, timestamptz, timestamptz, bytea, bytea, timestamptz,
    bytea, text, timestamptz, timestamptz, bytea, uuid)
    from yo4x_trade_authorizer, yo4x_gateway_runtime;

revoke execute on function control.claim_authorized_broker_command(
    uuid, text, text, uuid, uuid),
    control.record_broker_command_submission(
        uuid, text, uuid, text, boolean, text, text, text, text, bytea,
        timestamptz, uuid),
    control.recover_expired_broker_command_lifecycle(uuid, text, uuid),
    control.begin_broker_command_reconciliation(uuid, text, uuid, uuid),
    control.complete_broker_command_reconciliation(
        uuid, text, uuid, uuid, text, text, text, bytea, text, text,
        timestamptz, uuid)
    from yo4x_trade_authorizer;

grant execute on function control.claim_authorized_broker_command(
    uuid, text, text, uuid, uuid),
    control.record_broker_command_submission(
        uuid, text, uuid, text, boolean, text, text, text, text, bytea,
        timestamptz, uuid),
    control.recover_expired_broker_command_lifecycle(uuid, text, uuid),
    control.begin_broker_command_reconciliation(uuid, text, uuid, uuid),
    control.complete_broker_command_reconciliation(
        uuid, text, uuid, uuid, text, text, text, bytea, text, text,
        timestamptz, uuid)
    to yo4x_gateway_runtime;

grant execute on function control.begin_user_operation_gateway_invocation(
        uuid, uuid, integer, text, uuid, uuid, text, text, interval,
        uuid, uuid, uuid, bigint, text),
    control.record_user_operation_gateway_observation_v5(
        uuid, uuid, uuid, uuid, text, text, text, timestamptz, jsonb,
        uuid, uuid, uuid, bigint, text)
    to yo4x_gateway_runtime;
grant select (migration_id, sha256)
    on control.schema_migrations to yo4x_gateway_runtime;

-- The broker-account guard invokes this pure structural predicate in a WHEN
-- clause. These are the only direct broker-account mutators; the predicate
-- exposes no row data and grants no mutation authority by itself.
grant execute on function control.is_exact_v5_broker_projection(
        operations.broker_accounts, operations.broker_accounts)
    to yo4x_control_api, yo4x_secret_ingestion, yo4x_worker;

grant execute on function control.authorize_user_operation_provider_call(
        uuid, uuid, uuid, uuid, text, uuid, uuid, uuid, bigint, text),
    control.record_user_operation_provider_call_ambiguity(
        uuid, uuid, uuid, uuid, text, uuid, uuid, uuid, bigint, text)
    to yo4x_credential_runtime;

alter role yo4x_trade_authorizer set statement_timeout = '5s';
alter role yo4x_trade_authorizer set lock_timeout = '2s';
alter role yo4x_trade_authorizer set idle_in_transaction_session_timeout = '10s';
alter role yo4x_gateway_runtime set statement_timeout = '5s';
alter role yo4x_gateway_runtime set lock_timeout = '2s';
alter role yo4x_gateway_runtime set idle_in_transaction_session_timeout = '10s';
alter role yo4x_credential_runtime set statement_timeout = '5s';
alter role yo4x_credential_runtime set lock_timeout = '2s';
alter role yo4x_credential_runtime set idle_in_transaction_session_timeout = '10s';
alter role yo4x_context_issuer set statement_timeout = '5s';
alter role yo4x_context_issuer set lock_timeout = '2s';
alter role yo4x_context_issuer set idle_in_transaction_session_timeout = '10s';

commit;
