param(
  [string]$OutDir,
  [string]$Ref = "master"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $OutDir) {
  $OutDir = Join-Path $Root "tools\import-data\source-cache\pokeapi-csv"
}

$Files = @(
  "generations.csv",
  "version_groups.csv",
  "versions.csv",
  "pokemon_species.csv",
  "pokemon_species_names.csv",
  "pokemon_species_flavor_text.csv",
  "pokemon.csv",
  "pokemon_forms.csv",
  "pokemon_types.csv",
  "pokemon_egg_groups.csv",
  "pokemon_abilities.csv",
  "pokemon_stats.csv",
  "pokemon_moves.csv",
  "moves.csv",
  "move_names.csv",
  "move_effect_prose.csv",
  "abilities.csv",
  "ability_names.csv",
  "ability_prose.csv",
  "items.csv",
  "item_names.csv",
  "item_prose.csv",
  "item_game_indices.csv",
  "machines.csv",
  "evolution_chains.csv",
  "pokemon_evolution.csv"
)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

foreach ($File in $Files) {
  $Url = "https://raw.githubusercontent.com/PokeAPI/pokeapi/$Ref/data/v2/csv/$File"
  $Target = Join-Path $OutDir $File
  Write-Output "Downloading $File"
  Invoke-WebRequest -Uri $Url -OutFile $Target
}

Write-Output "PokeAPI CSV cache: $OutDir"
