# Third-party notices

## PawnIO.Modules 0.2.11

- Upstream: https://github.com/namazso/PawnIO.Modules
- Fixed source commit: `52a7e536dff3e53c96917a28caac5e0fa6510696`
- Original release: https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11
- License: GNU Lesser General Public License v2.1, unmodified upstream `COPYING` included.
- Runtime modules: unmodified official signed `LpcIO.bin`, `AMDFamily17.bin` only.
- Both signed modules, the unmodified `COPYING` and exact corresponding `source.zip` are embedded in the standalone EXE. They are extracted to `%LOCALAPPDATA%/RedmiFanControl/runtime/pawnio-modules-0.2.11/` when connecting; “设置 / 关于 → 打开数据文件夹” also prepares this directory without accessing hardware. The ZIP additionally includes `licenses/pawnio-modules-0.2.11/{COPYING,source.zip}`. Extract source.zip for upstream sources, license headers, build instructions and build configuration. Immutable source URLs and SHA-256 hashes are recorded in `dependencies.lock.json`.
- These modules are not relicensed under this application's MIT license. No changes to their signatures, interpreter or permissions are made.

## PawnIO / PawnIOLib

External prerequisite, not bundled, installed, modified or relicensed by this project. Obtain it from https://pawnio.eu/ and review the official terms. See https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules for integration/licensing guidance. Do not substitute unsigned or vulnerable legacy drivers.

## Other references

The UI concept was informed by Macs Fan Control; no proprietary executable, code, brand image or asset is included. BIOS images, EC firmware dumps, datasheets and historical local research captures are not redistributed in the repository or release packages.
