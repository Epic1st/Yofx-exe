-- Listings published by the central YO4X administrator are platform-owned.
-- They do not impersonate an end user merely to satisfy seller attribution.

alter table marketplace.listings
    alter column seller_user_id drop not null;

comment on column marketplace.listings.seller_user_id is
    'End-user seller for user-owned listings; null for centrally administered platform listings.';
