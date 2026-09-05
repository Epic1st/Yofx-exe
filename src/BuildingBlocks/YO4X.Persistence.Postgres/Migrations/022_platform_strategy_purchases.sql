-- Centrally administered strategies do not necessarily have a user-owned
-- marketplace listing. Their acquisition is still recorded as a purchase,
-- with the catalogue strategy supplying the product identity and title.

alter table marketplace.purchases
    alter column listing_id drop not null;

comment on column marketplace.purchases.listing_id is
    'Seller listing for user-listed products; null for centrally administered catalogue strategies.';
