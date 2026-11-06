# BENT and MTB inspection

Open a loose `.bent` or `.mtb` with **Open BENT / MTB**, drag it into the main window, or double-click its member inside an open archive. **View BENT / MTB** opens the selected archive entry without extracting it.

Both viewers are read-only. Trees start collapsed. Select a node for details, search with **Find next**, or inspect the first 64 KiB in the Hex tab.

## BENT

The reviewed little-endian tuple signature is supported. Shows hierarchy, names, values, record types and byte offsets, including authored animation aliases and filenames. Arrays display their first 128 elements. Unknown types or malformed records stop parsing with a visible partial-inspection message; no guessed tree is produced beyond that point. Other signatures/platform variants are not claimed supported. This is an inspector, not animation playback, an editor or a serializer.

Record grammar provenance: the project's `TupleDecoderResearch/decode_tup_safe.py`, researched from zzh8829/JiroTheOne's decode-tup.py in LucasLhomme/Disney-Infinity-Modding-Tools. Preserve existing repository licensing and attribution.

## MTB

Uses the existing AssetWiiUdio research readers and PC/Wii U shader metadata. Shows bundle sections, versions, offsets, sizes, texture IDs, materials, shader IDs, UUID/hash fields and reviewed sampler-to-texture bindings. Unknown shader layouts remain explicitly unresolved. Texture IDs preserve their original byte order. Images and external texture lookup are not implemented in this viewer.

The 64 MiB resource limit and bounds/count checks constrain parsing. Decoding one resource does not establish that every game/platform version is supported.
