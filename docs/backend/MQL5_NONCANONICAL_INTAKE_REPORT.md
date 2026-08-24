# MQL5 non-canonical quarantine intake report

This is deterministic metadata-only quarantine evidence for files that are not exact `.mq5` or `.mqh` inputs. It does not add any file to the canonical corpus, extract an archive to disk, load a compiled binary, parse a DOCX package, convert source, compile code, or run a strategy.

- Schema: `yo4x.mql5-quarantine-intake.v2`
- Analyzer: `1.1.0`
- Evidence SHA-256: `87b6811649b7ec859a61e8a7de702b85b36aeefc1259701cacc42ecb231bdd31`
- Canonical corpus binding: `9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47` (198 files, 12979438 bytes)
- Non-canonical files: 15 (1219643 bytes)
- Source-like text candidates: 4
- Legacy MQ4 sources: 2
- ZIP archives: 4; entries: 11; verified file-entry digests: 6; unavailable file-entry digests: 2
- Verified objects matching canonical content: 0; matched canonical paths: 0
- Conversion, compile, and runtime proofs: 0 / 0 / 0

## Non-canonical files

| File | Bytes | SHA-256 | Quarantine classification | Source signals | Exact intake duplicates | Archive state |
|---|---:|---|---|---:|---:|---|
| --+---------------------------.docx | 4528 | `98afcae7ce14cc10856ab5b3de840243f781cc6ad2bce07fddcdb712b560be32` | OfficeDocumentContainer | 0 | 0 | - |
| brains.mq5 kgaugelo | 7285 | `b85537ba7e3a6b63cb34bd73af20fe5b98d41f225e8566029087e33fe03c8435` | SourceLikeTextCandidate | 6 | 0 | - |
| Breakout Retest Pro 1.02a.mq4 | 73064 | `7d28c50e9a40c052177c8cd738f57b0302bcad34686ab601edf4029a22ddfcf4` | LegacyMql4Source | 7 | 0 | - |
| Crude oil scalper.zip | 76033 | `09c0d944b51b154599eaf39b4e7123be4aca88292035f5f8ff34f4cf8825ce0d` | ZipArchive | 0 | 0 | Inspected |
| DAY TRADING FOREX.txt | 8766 | `2312909220c07754328e50e37cfda7477e1e6db30436a8a5d97c7a201d1ffc30` | SourceLikeTextCandidate | 5 | 0 | - |
| FTR Reversals EA mq5 Modified.txt | 31912 | `a6bcfd7b6e92a434ca596e463d28cec0497b6cf510468d8879a9a4b73db13ce0` | SourceLikeTextCandidate | 7 | 0 | - |
| HyperGal Alpha EA.zip | 92934 | `d02237412614cc583c5b101fe98a6f7ec2f7aeaa482e9c6338e89cea04d0c957` | ZipArchive | 0 | 0 | Inspected |
| Multi Sniper mq No DLL&#95;fix by @ForexRobot5 &#40;2&#41;.zip | 111368 | `5fa95a8c828a5ac8ce088a507475974b2d8ca9ce0250c8cce39468918007c905` | ZipArchive | 0 | 0 | ContainsUnavailableEntryContent |
| Multi Sniper mq No DLL&#95;fix by @ForexRobot5.ex4 | 112584 | `861815e7b4e17e409962327990b2594a53e27e0662c392b2237675b683903a4d` | CompiledMql4Binary | 0 | 1 | - |
| Multi Sniper mq No DLL&#95;fix by @ForexRobot5.zip | 111368 | `d27e5d3b6e23443ab6a6218207b73676907119851ab16e92226162b46c78a8b0` | ZipArchive | 0 | 0 | ContainsUnavailableEntryContent |
| Multi Sniper mq No DLL&#95;fix.ex4 | 112584 | `861815e7b4e17e409962327990b2594a53e27e0662c392b2237675b683903a4d` | CompiledMql4Binary | 0 | 1 | - |
| Multi Sniper mq v24.16&#95;fix.ex4 | 112284 | `5d284b13023ec0f6f41d552c498e3195555977031e3ec50574d2af5f5ea7c175` | CompiledMql4Binary | 0 | 0 | - |
| SHK Professional Heikin Ashi 1.00.mq4 | 97378 | `6aada51b4e4e5c812520a49fc46c99e9511f282f4965880d97368b1bb477c3f5` | LegacyMql4Source | 4 | 0 | - |
| Simple Classic Trailing Stop Mq5.txt | 3577 | `16cb754b57087785cc04d529ababa3af54987f93bbd345fc1eee9db8d34631b0` | SourceLikeTextCandidate | 6 | 0 | - |
| The Gold Reaper 4.1 Enhanced.ex4 | 263978 | `4de27cc4d86ef9af7571a0b51f075b35e44a501040b0a3e6eb214a4695ed6a15` | CompiledMql4Binary | 0 | 0 | - |

## Archive entry metadata

Archive entries were streamed only to bounded hash/CRC verification; no entry was written to disk or loaded as code. Encrypted, unsupported, unsafe, oversized, or unreadable content remains unavailable and is never inferred from names or CRC values.

| Archive | Entry | Declared bytes | Compressed bytes | CRC-32 | Content state | SHA-256 | Canonical exact matches |
|---|---|---:|---:|---|---|---|---:|
| Crude oil scalper.zip | Crude oil scalper/CrudeOilScalpEA.ex5 | 56060 | 55598 | `8280d13a` | VerifiedDigest | `ea716d0182c36380414ef83e1c765c67cff173300fd70b2b78746c6ed78aa7dd` | 0 |
| Crude oil scalper.zip | Crude oil scalper/CrudeOilScalpEA.mq5 | 29125 | 6566 | `82f8593d` | VerifiedDigest | `6a203fb379e241a0bb52e848b6a4ee2d192c257512ea5614e9d52349c5e44c43` | 0 |
| Crude oil scalper.zip | Crude oil scalper/CrudeOilScalp.ex5 | 13904 | 13401 | `05e28631` | VerifiedDigest | `d4ae1e23ed42732a6a6bae12c3b59f837300230579eb19b4c6983dffae0e72fb` | 0 |
| HyperGal Alpha EA.zip | HyperGal Alpha EA/ | 0 | 2 | `00000000` | Directory | - | 0 |
| HyperGal Alpha EA.zip | HyperGal Alpha EA/Experts/ | 0 | 2 | `00000000` | Directory | - | 0 |
| HyperGal Alpha EA.zip | HyperGal Alpha EA/Experts/HyperGal Alpha EA.ex4 | 72222 | 70514 | `c2ea1a44` | VerifiedDigest | `3c2082cc164b1e24f5b110f6a9337658abf96d8ddc69d3a10515b191936a737c` | 0 |
| HyperGal Alpha EA.zip | HyperGal Alpha EA/Indicators/ | 0 | 2 | `00000000` | Directory | - | 0 |
| HyperGal Alpha EA.zip | HyperGal Alpha EA/Indicators/FOREEX SNIPER KILLER Entry Alert.ex4 | 13960 | 13492 | `a1ee535b` | VerifiedDigest | `88fd59faa9cec393fef696e02c47d09efd4f1797f976f8faa9a17cde70b454d7` | 0 |
| HyperGal Alpha EA.zip | HyperGal Alpha EA/Indicators/RSI&#95;Cust.ex4 | 8468 | 7896 | `571a8caa` | VerifiedDigest | `521d8f644ff344d0be283e916abf7d68bcc72e6550aa4ee42caf8962b8c75fd4` | 0 |
| Multi Sniper mq No DLL&#95;fix by @ForexRobot5 &#40;2&#41;.zip | Multi Sniper mq No DLL&#95;fix by @ForexRobot5.ex4 | 112584 | 111142 | `cb71f445` | Encrypted | - | 0 |
| Multi Sniper mq No DLL&#95;fix by @ForexRobot5.zip | Multi Sniper mq No DLL&#95;fix by @ForexRobot5.ex4 | 112584 | 111142 | `cb71f445` | Encrypted | - | 0 |

## Honest blockers

- Source-like text and legacy MQ4 files are quarantine candidates only. They require explicit provenance/licensing, a deliberate rename/import decision, and the same isolated parse/type-check/conversion gates as any other source.
- EX4/EX5 content is compiled code for a different trust lane. It is not source and is never loaded or treated as convertible.
- Encrypted or unreadable archive entries have no verified content digest and therefore no exact-duplicate or compatibility claim.
- DOCX containers are not inspected by this lane and cannot supply strategy source evidence.
- Archive entry names and CRC-32 values are metadata, not authenticity, provenance, source equivalence, or execution evidence.
