# Security

Do not post exploitable privileged-backend issues, secrets or private hardware dumps publicly. Until the maintainer configures GitHub private vulnerability reporting, do not treat public issues as a confidential channel.

The UI requests normal UAC for hardware access. The worker is device-specific, but local IPC files and the writable portable app directory do not defend against an already-compromised current-user account or local administrator. Install only from a trusted build and do not run user-modified modules or binaries elevated.

This project cannot promise firmware-level reliability or guarantee thermal safety. Never disable platform thermal protection to use it. See docs/SAFETY.md for unsupported conditions.
