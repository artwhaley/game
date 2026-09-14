# Universal Animation Library control source

This folder contains the official current Standard-edition Unity export acquired for the Phase 00 Humanoid retargeting proof.

- Creator: Quaternius
- Work: Universal Animation Library, Standard edition
- Publisher page: https://quaternius.itch.io/universal-animation-library
- Publisher description/license: https://quaternius.com/packs/universalanimationlibrary.html — CC0 1.0
- Downloaded archive: `Universal Animation Library[Standard].zip`, 15,904,933 bytes, downloaded September 13, 2026 from the publisher itch.io page
- Imported file: the archive's non-root-motion `Unity/UAL1_Standard.fbx`, dated June 16, 2026
- SHA-256: `21B32D912DA3CB93426D974FB945E86F5B2E86970ACD2CE89905E0FBF9F1DCC2`
- Source contents: 65-bone humanoid source skeleton, mannequin mesh and 43 animation stacks; inspected in Blender 4.5 without modifying the FBX
- Unity role candidates: `Idle_Loop`, `Walk_Loop`, `Sitting_Enter`, `Sitting_Idle_Loop`, `Sitting_Exit`, `Idle_Talking_Loop`, `Interact`

`UAL1_Standard.fbx` is an acquisition/control asset, not an editable performance catalog. Keep the role IDs in later catalog work so replacing a motion source never affects Cards.

The imported source model and skeleton stay in the FBX so the same clip can be checked on its source character before it is retargeted to Lara or Luna.
