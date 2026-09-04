# YO4X Desktop Over-The-Air (OTA) Update & Distribution Specification

> **Notice:** The primary and complete OTA engineering specification, architecture, C# client implementation, PowerShell release automation, and deployment checklist is maintained in [OTA.md](../OTA.md) at the repository root.

Please refer to [OTA.md](../OTA.md) for:
1. Multi-Tier OTA Update Architecture (Hot Strategies, Hot Frontend, Binary Core).
2. The Quiescent Pre-flight Safety Gate (protecting in-flight trading orders from interruption).
3. Global CDN & Object Storage Directory Layout & Caching Rules.
4. Release Manifest Specification (`releases.json` schema).
5. Real-Time Push (SSE/WebSocket) & Background Polling Protocol.
6. Client-Side Atomic Update Coordinator (`apply-update.bat` / `YO4X.Updater`).
7. Complete C# `OtaUpdateManager` class for `YO4X.Desktop`.
8. Complete `Publish-OtaRelease.ps1` PowerShell automation script.
9. React 18 UI notification banner component (`OtaBanner.tsx`).
10. Authenticode Code Signing & Windows Defender SmartScreen reputation runbook.
11. End-to-end Pre-release and Deployment Checklist.
