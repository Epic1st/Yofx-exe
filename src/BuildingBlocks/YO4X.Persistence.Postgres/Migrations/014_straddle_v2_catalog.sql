-- Publish the tested Straddle package as the only UI-visible Straddle release.
-- The frontend projection independently requires both this v2 marker and the
-- .yo4x filename, so a raw source row or legacy container cannot reappear.

update catalog.strategies
set name = 'Straddle_1.1.36.yo4x',
    version = '1.0.0',
    category = 'Grid',
    symbol = 'XAUUSD',
    timeframe = 'M1',
    summary = 'Licensed v2 .yo4x package - runs locally',
    description = 'Straddle 1.1.36 packaged in the authenticated YO4X v2 container. The CLR assembly is decrypted only in memory after its signed licence and broker binding are validated.',
    is_drm_protected = true,
    package_format_version = 2,
    package_sha256 = 'd708b075f378979f242991003099f3101fa019cf1dad0ea34d17c0c40ed3b11f',
    package_size_bytes = 357829,
    drm_license_type = 'Lifetime',
    package_strategy_id = '2af1d0ae5dbd6527',
    package_entry_type = 'YO4X.Generated.Strategies.SStraddle1136',
    assembly_sha256 = '43d3675d3c1c807a0821bd1cccb231c41a13deda48fc1963f75d70a361899c6c',
    updated_at = clock_timestamp()
where lower(name) in ('straddle_1.1.36.mq5', 'straddle_1.1.36.yo4x');
