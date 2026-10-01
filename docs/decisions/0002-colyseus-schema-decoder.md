# ADR 0002 — Colyseus schema v2 state decoder

- **Date:** 2026-10-01
- **Status:** Accepted
- **Phase:** 1 — Discovery (also the lowest layer of the Phase 2 adapter)

## Context

89% of Fourvale's WebSocket traffic is binary room state (`ROOM_STATE`, `ROOM_STATE_PATCH`). Class, level, battle HP and action readiness live there (discovery C2, K3). Without a decoder these fields stay at "Medium/Low" confidence and cannot be implemented (CLAUDE.md working method 5).

Investigation (2026-10-01, read-only review of the public bundle `main-DZfOvN-x.js`, client version 0.98):

- The client bundles **@colyseus/schema v2**: `SWITCH_TO_STRUCTURE = 255`, `TYPE_ID = 213`, operations `ADD 128 / REPLACE 0 / DELETE 64 / DELETE_AND_ADD 192 / CLEAR 10`, `ChangeTree` with `touchParents`, `_definition.fieldsByIndex`, and `Reflection` with `ReflectionType` / `ReflectionField`.
- Fixed-width numbers are **little-endian**. `number` uses MessagePack-style prefixes but little-endian payloads. Strings are MessagePack-style (`fixstr`, `str8/16/32`).
- The handshake in `JOIN_ROOM` is a `Reflection` instance encoded with the same format: `Reflection { types: ReflectionType[], rootType }`, `ReflectionType { id, fields: ReflectionField[] }`, `ReflectionField { name, type, referencedType }`.

## Options

1. **Port the decoder from the official C# client** (MIT). Reliable, but it targets Unity and carries callbacks and serializers we do not need. Its version must match v2 exactly.
2. **Write a minimal decoder that mirrors the bundled v2 client.** About 400 lines. No dependency, read-only, easy to test and to keep in step with what Fourvale actually ships.
3. Read state from the page with a script. This ranks below network observation in spec §7.1, so it is rejected while option 1 or 2 works.

## Decision

Option 2. `src/Fourvale.Adapter/Colyseus/` contains:

- `SchemaReader`: primitive decoding, matching the bundled `decode.*` functions.
- `SchemaContext`: type definitions built from the room's own handshake. It is bootstrapped with the three hard-coded Reflection types, so the handshake is decoded by the same code as the state.
- `StateDecoder`: applies full states and patches, tracks refIds, records changes with paths (`players.<key>.hp`) and serialises the tree to JSON.

It decodes only. It never encodes or sends anything (hard rule 5). Errors throw `FormatException`; callers catch, count and mark fields stale (CLAUDE.md code conventions).

`tools/Fourvale.Replay` is a console tool that runs a capture file through the decoder offline, for discovery and verification. It is not shipped.

## Consequences

- When Fourvale upgrades to @colyseus/schema v3, the format changes completely. Detection: handshake decoding fails or the bundle shows v3 markers (`$refId`, `Metadata`, `Decoder`). The adapter health indicator must surface this.
- Only the handshake fixture (type and field names, no player data) is committed. Real state fixtures need binary-level sanitising and are deferred.
