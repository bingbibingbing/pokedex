# Podex Desktop

Native Windows desktop client for the migrated Podex data.

This client does not use a browser, WebView, Node, npm, or a local web server.
It is a WinForms executable that reads the migrated local data file from
`data\pokemon.json`.

## Build

```powershell
.\build.ps1
```

To build a portable test package with the generated expanded catalog preview:

```powershell
.\tools\import-data.ps1 -RequireSource
.\tools\validate-data.ps1 -DataPath artifacts\pokemon-catalog-preview.json
.\build.ps1 -UsePreviewData
```

The output is:

```text
bin\PodexDesktop.exe
bin\data\pokemon.json
release\PodexDesktop\PodexDesktop.exe
release\PodexDesktop\data\pokemon.json
release\PodexDesktop\images\
release\PodexDesktop-catalog-preview\
```

## Run

```powershell
.\bin\PodexDesktop.exe
```

For another computer, copy the whole folder:

```text
release\PodexDesktop
```

Then run:

```text
PodexDesktop.exe
```

Runtime requirement is intentionally kept at the same level as the legacy app:
Windows with .NET Framework 4.x. No development tools are required for end users.

## Data

The migrated data is kept in `data\pokemon.json`.
The migrated sprites and UI icons are kept in `assets\images`.
Expanded catalog work is generated to `artifacts\pokemon-catalog-preview.json` first, then packaged with `.\build.ps1 -UsePreviewData` for testing.

The desktop app is read-only. It does not modify the original legacy package.

## Validate Data

Run this before and after data expansion work:

```powershell
.\tools\validate-data.ps1
```

The report is written to:

```text
artifacts\data-validation-report.txt
```

The validator uses the same Windows/.NET Framework toolchain as the app build.
It is a development tool only and is not required by end users.

## Import Preflight

Generate a report for future data expansion without modifying `data\pokemon.json`:

```powershell
.\tools\import-data.ps1
```

To inspect PokeAPI CSV source data first:

```powershell
.\tools\fetch-pokeapi-csv.ps1
.\tools\import-data.ps1 -RequireSource
```

The report is written to:

```text
artifacts\import-data-report.txt
artifacts\import-id-map-preview.csv
artifacts\pokemon-catalog-preview.json
artifacts\missing-chinese.csv
```

Downloaded CSV files are cached under `tools\import-data\source-cache` and are ignored by Git.
By default, the preview JSON refuses English fallback and skips new rows that lack zh-CN names or descriptions.
The preview JSON adds new moves, abilities, items, Gen 8/9 default Pokemon, their sprites, evolutions, and learnsets. Extra forms are still handled as a later import phase.

## Pokemon Sprites

Pokemon images are bundled so the desktop app remains portable. To rebuild the Gen 8/9 default Pokemon sprites from PokeAPI's sprite repository:

```powershell
.\tools\fetch-pokeapi-sprites.ps1 -From 808 -To 1025
.\tools\validate-data.ps1 -DataPath artifacts\pokemon-catalog-preview.json
```

The script writes `assets\images\pokemon\small\{id}.png` as `40x32` icons and `assets\images\pokemon\big\{id}.png` as `100x100` sprites, matching the legacy image layout used by the WinForms client.

## Chinese Overrides

New catalog rows must have zh-CN text. PokeAPI is used for structured data, but its newer Chinese effect text is incomplete, so the importer can apply a local override CSV:

```text
tools\import-data\overrides\zh-cn.csv
```

Generate a small, reviewable batch from 52poke through its MediaWiki API:

```powershell
.\tools\fetch-52poke-zh-cn.ps1 -Entity move -FromSourceId 729 -Limit 60
.\tools\import-data.ps1 -RequireSource
.\tools\validate-data.ps1 -DataPath artifacts\pokemon-catalog-preview.json
```

The fetcher is intentionally conservative: it uses serial requests, retries transient failures, stores source title/URL/license, and rejects low-quality text instead of importing English fallback or dirty wiki markup.
