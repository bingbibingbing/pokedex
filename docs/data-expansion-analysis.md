# Data Expansion Analysis

## Scope

Expand the desktop PokeDex data beyond the legacy Gen 1-7 dataset, covering:

- Pokemon
- Moves
- Abilities
- Items
- Evolution chains
- Learnsets by game/version
- Images/icons where available

The UI goal stays unchanged: first keep the legacy layout and behavior, then let the same screens show newer data.

## Current Data Boundary

The current app reads one bundled file: `data/pokemon.json`.

Current migrated counts:

- Pokemon rows: 1051
- Max national dex: 807
- Moves: 728
- Abilities: 233
- Items: 728
- Games: 29
- Learnsets: 518041

Important detail: `pokemon` count is higher than `maxNationalDex` because legacy alternate forms preserve legacy `PM_ID` identifiers. This must be kept; do not collapse forms into national dex numbers.

Current game list ends at:

- Sun
- Moon
- Ultra Sun
- Ultra Moon

So the data boundary is effectively Gen 7.

## Feasibility

Expansion is feasible, but it should not be done by hand-editing `pokemon.json`.

The safe approach is:

1. Keep the existing JSON shape compatible for the desktop app.
2. Build an importer that reads newer source data.
3. Normalize source data into our local schema.
4. Generate a new `data/pokemon.json`.
5. Run validation checks before replacing the bundled data.

Manual edits are risky because learnsets, evolution chains, alternate forms, version-specific move availability, and item flags are dense relational data.

## Source Candidates

Use sources by responsibility, not one source for everything.

- Legacy database/export: keep as the trusted baseline for Gen 1-7 Chinese wording and original behavior.
- PokeAPI: good candidate for structured Pokemon/species/forms/moves/abilities/items/version-groups/evolution data, multilingual names, flavor text, and CSV-backed import data. Docs: https://pokeapi.co/docs/v2
- PokeAPI GitHub CSV data: better than live API for reproducible imports because the local generator can pin a commit. Repo: https://github.com/PokeAPI/pokeapi
- Pokemon Showdown data: useful for battle-accurate mechanics and generation-specific move/ability/item values. Dex API supports latest data and generation-specific lookup. Docs: https://www.smogon.com/dex/ and https://www.mintlify.com/smogon/pokemon-showdown/api/dex
- 52poke / 神奇宝贝百科: useful for zh-CN names and descriptions, especially newer move/ability/item pages that PokeAPI does not fully localize. Its content is CC BY-NC-SA, so importer output must preserve source attribution and license notes.

Chinese text is a hard requirement. Some public datasets have localized names, but detailed Chinese effect text may be incomplete or inconsistent. The importer must not ship English fallback in test builds. Missing zh-CN rows go to `artifacts/missing-chinese.csv` until a Chinese source or manual override fills them.

## Schema Gaps To Fix First

### Items

Current item flags are fixed booleans:

- `inGen1`
- `inGen2`
- `inGen3`
- `inGen4`
- `inGen5`
- `inGen6`
- `inGen7`

This does not scale. Add a new extensible representation while keeping old flags readable:

- `generations: [1, 2, 3, ...]`
- or `versionGroups: [id, id, ...]`

The UI can still render old filters, but data generation should stop depending on hard-coded `inGen7` as the last generation.

Implementation status:

- Desktop client now supports optional `items[].generations`.
- Desktop client still falls back to legacy `items[].flags.inGen1` through `inGen7` when `generations` is absent.
- Validator now accepts optional `items[].generations` and `items[].versionGroups`.
- Importers should emit `generations` for generation filtering; `versionGroups` can be added as extra source availability detail.

### Games / Version Groups

Current `games` ends at Ultra Sun / Ultra Moon.

Expansion needs new game/version records before learnsets can be imported:

- Sword / Shield
- Brilliant Diamond / Shining Pearl
- Legends: Arceus
- Scarlet / Violet
- DLC/version-group distinctions if learnsets differ

The existing UI already has a version filter pattern, so this is mostly data work plus larger combo-box labels if needed.

### Pokemon Forms

The existing model uses:

- `legacyId`
- `nationalDex`
- `generation`
- `formId`
- `formNames`

This can support newer forms, but the importer must preserve stable local IDs. Do not use national dex alone as the primary key.

Recommended local ID policy:

- Keep legacy IDs unchanged.
- Assign new base Pokemon by national dex when no conflict exists.
- Assign new forms into a high, deterministic range or introduce a source-key mapping table.

### Moves

Current move fields cover the visible legacy UI:

- type
- category
- power
- accuracy
- PP
- priority
- range
- descriptions

Expansion also needs:

- generation-specific changed values
- Z-Move / Max Move / special-case data if we decide to show them
- target/range mapping into existing range icons
- effect numeric parameters for accurate tooltips

### Abilities

Current ability fields are:

- trigger
- target
- effectOn
- descriptions

This is enough for the legacy list, but not enough for numeric/mechanics accuracy. Add optional structured effect data:

- `effectText`
- `shortEffectText`
- `mechanics`
- `changedByVersionGroup`

This helps answer cases like "Compound Eyes improves accuracy by how much" without burying the value only inside free text.

## Import Pipeline Proposal

Add a local tool under `tools/`, for example:

- `tools/import-data/`
- `tools/import-data/source-cache/`
- `tools/import-data/overrides/zh-cn.json`
- `tools/import-data/generate-pokemon-json.js`
- `tools/import-data/validate-data.js`

Pipeline:

1. Download or checkout pinned source data.
2. Read source Pokemon, species, forms, moves, abilities, items, versions, version groups, evolutions, and learnsets.
3. Map source IDs to our local IDs.
4. Merge with legacy Gen 1-7 data.
5. Apply Chinese/local override files.
6. Emit `data/pokemon.json`.
7. Validate counts, missing names, missing icons, broken move IDs, broken Pokemon IDs, broken evolution targets, and broken game IDs.

The desktop app remains portable; these import tools are development-only and are not shipped to users.

Implementation status:

- `tools/fetch-pokeapi-csv.ps1` can cache the required PokeAPI CSV files for local inspection.
- `tools/import-data.ps1` builds and runs a preflight importer report.
- The preflight report does not write `data/pokemon.json`; it only compares current data against source coverage and expansion candidates.
- The importer also writes `artifacts/import-id-map-preview.csv`, a reviewable source-to-local ID mapping preview.
- The importer can generate `artifacts/pokemon-catalog-preview.json` with added moves, abilities, and items only. Pokemon/forms/evolutions/learnsets remain out of preview until ID mapping is reviewed.
- Preview generation is strict Chinese by default: new rows without zh-CN names or descriptions are skipped and listed in `artifacts/missing-chinese.csv`.
- The importer can apply `tools/import-data/overrides/zh-cn.csv` for missing zh-CN names/descriptions. For moves, override descriptions are keyed by move ID so one move does not pollute another move that shares a PokeAPI effect ID.
- `tools/fetch-52poke-zh-cn.ps1` can generate small, reviewable zh-CN override batches from 52poke MediaWiki API results. It records source title, URL, and `CC BY-NC-SA 3.0`, uses serial requests with retry, and rejects dirty or incomplete text rather than allowing English fallback.

Current preview milestone:

- A conservative move override batch imports 28 additional moves into `artifacts/pokemon-catalog-preview.json`.
- The preview validates with `Errors: 0`.
- Existing warnings remain from legacy placeholder learnsets and missing item images; they are not introduced by the move preview.

## Validation Checklist

Before accepting expanded data:

- Every Pokemon has zh-CN name, type, stats, abilities, and description fallback.
- Every Pokemon image lookup either resolves or uses a clear placeholder.
- Every move has type, category, PP, power/accuracy fallback, range, and tooltip text.
- Every ability has name and tooltip text.
- Every item has name, description, pocket/category image, generation/version availability.
- Every learnset references existing Pokemon, move, game, and acquisition method.
- Every evolution target can be double-clicked and navigated.
- Default app startup stays fast with the larger JSON.

## Recommended Order

1. Add schema compatibility for expandable item generations and version groups.
2. Build a validator for the current `pokemon.json`.
3. Build a source-to-local ID mapping table.
4. Import Pokemon/forms for Gen 8 first, without learnsets.
5. Import moves, abilities, and items for Gen 8.
6. Import Gen 8 learnsets/evolutions.
7. Repeat for Gen 9.
8. Add missing images or placeholder policy.
9. Optimize startup if JSON size becomes a problem.

## Main Risks

- Chinese descriptions may not be complete from public structured sources.
- Generation-specific learnsets are the largest and easiest place to create wrong filters.
- Forms and regional variants can break navigation if IDs are not stable.
- Item generation availability needs a better model than the current `inGen1` to `inGen7` flags.
- Larger data may expose slow startup/list rendering issues, so validation should include performance.

## Decision

Do expansion through a reproducible importer and validator, not manual JSON edits.

Keep the app runtime unchanged: Windows desktop executable plus local bundled data and images only.
