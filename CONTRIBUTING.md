# Contributing

Read README and docs/SAFETY.md first. Restore pinned resources with scripts/Restore-Dependencies.ps1, then run scripts/Test.ps1 before proposing changes; CI never loads a driver or touches EC hardware. The test runner writes only to a fresh .cache test directory, not real user preferences.

Keep device/firmware/permission checks, strict returned-length handling, ownership-aware recovery and independent monitoring. Do not turn this into an arbitrary EC writer or relax guards merely to make tests pass. Add pure-policy and fake-transport tests for each behavior change.

Do not attach BIOS images, firmware dumps, private logs or user settings to public issues. New hardware support needs independent offset/protocol evidence and explicit bounded real-device testing, not a copied device ID.

Project-owned contributions are MIT licensed. Upstream PawnIO module terms remain separate.
