# Podex Desktop

Native Windows desktop client for the migrated Podex data.

This client does not use a browser, WebView, Node, npm, or a local web server.
It is a WinForms executable that reads the migrated local data file from
`data\pokemon.json`.

## Build

```powershell
.\build.ps1
```

The output is:

```text
bin\PodexDesktop.exe
bin\data\pokemon.json
release\PodexDesktop\PodexDesktop.exe
release\PodexDesktop\data\pokemon.json
release\PodexDesktop\images\
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

The desktop app is read-only. It does not modify the original legacy package.
