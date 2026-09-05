-- Migration 019 extended the constrained local-development identity provisioner
-- to create a user profile. user_profiles uses forced RLS, so the SECURITY
-- DEFINER function still needs a policy for the fixed development tenant.
-- The login retains no direct table privileges and can reach this policy only
-- through identity.provision_local_development_identity.

create policy local_identity_fixed_profile_provisioning
on identity.user_profiles for all to public
using
(
    session_user = 'yo4x_local_identity'
    and tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid
)
with check
(
    session_user = 'yo4x_local_identity'
    and tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid
);

comment on policy local_identity_fixed_profile_provisioning on identity.user_profiles is
    'Allows the execute-only local identity provisioner to maintain profiles for the fixed development tenant.';
