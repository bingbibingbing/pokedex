using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.VisualBasic;

namespace PodexTools
{
    internal static class ImportData
    {
        private static readonly string[] RequiredPokeApiCsvFiles =
        {
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
        };

        private static int Main(string[] args)
        {
            try
            {
                Options options = Options.Parse(args);
                if (options.ShowHelp)
                {
                    Console.WriteLine(Options.HelpText());
                    return 0;
                }

                Console.OutputEncoding = Encoding.UTF8;

                if (!File.Exists(options.DataPath))
                {
                    Console.Error.WriteLine("Data file not found: " + options.DataPath);
                    return 2;
                }

                RootData root = LoadRoot(options.DataPath);
                Normalize(root);

                ImportPreflight preflight = new ImportPreflight(root, options);
                ImportReport report = preflight.Run();
                string text = report.ToText();
                Console.Write(text);

                if (!string.IsNullOrWhiteSpace(options.ReportPath))
                {
                    string reportDirectory = Path.GetDirectoryName(Path.GetFullPath(options.ReportPath));
                    if (!string.IsNullOrWhiteSpace(reportDirectory))
                    {
                        Directory.CreateDirectory(reportDirectory);
                    }
                    File.WriteAllText(options.ReportPath, text, Encoding.UTF8);
                }

                if (!string.IsNullOrWhiteSpace(options.MapPath) && report.MappingRows.Count > 0)
                {
                    string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(options.MapPath));
                    if (!string.IsNullOrWhiteSpace(mapDirectory))
                    {
                        Directory.CreateDirectory(mapDirectory);
                    }
                    File.WriteAllText(options.MapPath, MappingRow.ToCsv(report.MappingRows), Encoding.UTF8);
                }

                if (!string.IsNullOrWhiteSpace(options.PreviewDataPath) && report.MissingSourceFiles.Count == 0)
                {
                    CatalogPreviewGenerator.Generate(options, report);
                    if (!string.IsNullOrWhiteSpace(options.MissingChinesePath))
                    {
                        string missingChineseDirectory = Path.GetDirectoryName(Path.GetFullPath(options.MissingChinesePath));
                        if (!string.IsNullOrWhiteSpace(missingChineseDirectory))
                        {
                            Directory.CreateDirectory(missingChineseDirectory);
                        }
                        File.WriteAllText(options.MissingChinesePath, MissingChineseRow.ToCsv(report.MissingChineseRows), Encoding.UTF8);
                    }
                    text = report.ToText();
                    Console.WriteLine();
                    Console.WriteLine("Catalog preview generated: " + Path.GetFullPath(options.PreviewDataPath));
                    if (!string.IsNullOrWhiteSpace(options.ReportPath))
                    {
                        File.WriteAllText(options.ReportPath, text, Encoding.UTF8);
                    }
                }

                if (options.RequireSource && report.MissingSourceFiles.Count > 0) return 1;
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 2;
            }
        }

        private static RootData LoadRoot(string dataPath)
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 1000000
            };
            return serializer.Deserialize<RootData>(File.ReadAllText(dataPath, Encoding.UTF8));
        }

        private static void Normalize(RootData root)
        {
            if (root.games == null) root.games = new List<NamedRef>();
            if (root.levels == null) root.levels = new List<NamedRef>();
            if (root.types == null) root.types = new List<TypeRef>();
            if (root.pokemon == null) root.pokemon = new List<PokemonEntry>();
            if (root.evolutions == null) root.evolutions = new List<EvolutionEntry>();
            if (root.learnsets == null) root.learnsets = new List<LearnsetEntry>();
            if (root.moves == null) root.moves = new List<MoveEntry>();
            if (root.abilities == null) root.abilities = new List<AbilityEntry>();
            if (root.items == null) root.items = new List<ItemEntry>();
            if (root.natures == null) root.natures = new List<NatureEntry>();
        }

        private sealed class ImportPreflight
        {
            private readonly RootData root;
            private readonly Options options;
            private readonly ImportReport report;

            public ImportPreflight(RootData root, Options options)
            {
                this.root = root;
                this.options = options;
                this.report = new ImportReport(options.DataPath, options.SourcePath);
            }

            public ImportReport Run()
            {
                AddCurrentDataSummary();
                InspectPokeApiSource();
                AddImporterPlan();
                return report;
            }

            private void AddCurrentDataSummary()
            {
                report.Summary.Add("Current data", Path.GetFullPath(options.DataPath));
                report.Summary.Add("Current Pokemon rows", root.pokemon.Count.ToString());
                report.Summary.Add("Current max national dex", Max(root.pokemon.Select(delegate(PokemonEntry p) { return p.nationalDex; })).ToString());
                report.Summary.Add("Current moves", root.moves.Count.ToString());
                report.Summary.Add("Current abilities", root.abilities.Count.ToString());
                report.Summary.Add("Current items", root.items.Count.ToString());
                report.Summary.Add("Current games", root.games.Count.ToString());
                report.Summary.Add("Current learnsets", root.learnsets.Count.ToString());
                report.Summary.Add("Current Pokemon generations", FormatCounts(root.pokemon.Select(delegate(PokemonEntry p) { return p.generation; })));
                report.Summary.Add("Current move generations", FormatCounts(root.moves.Select(delegate(MoveEntry m) { return m.generation; })));
                report.Summary.Add("Current ability generations", FormatCounts(root.abilities.Select(delegate(AbilityEntry a) { return a.generation; })));
                report.Summary.Add("Current item generations", FormatCounts(root.items.SelectMany(ItemGenerationIds)));
            }

            private void InspectPokeApiSource()
            {
                report.Summary.Add("Source directory", Path.GetFullPath(options.SourcePath));

                if (!Directory.Exists(options.SourcePath))
                {
                    report.Warnings.Add("Source directory does not exist. Run tools\\fetch-pokeapi-csv.ps1 first or pass --source <csv-dir>.");
                    foreach (string fileName in RequiredPokeApiCsvFiles)
                    {
                        report.MissingSourceFiles.Add(fileName);
                    }
                    return;
                }

                foreach (string fileName in RequiredPokeApiCsvFiles)
                {
                    string path = Path.Combine(options.SourcePath, fileName);
                    if (!File.Exists(path))
                    {
                        report.MissingSourceFiles.Add(fileName);
                    }
                }

                foreach (string fileName in RequiredPokeApiCsvFiles)
                {
                    string path = Path.Combine(options.SourcePath, fileName);
                    if (!File.Exists(path)) continue;
                    CsvTable table = CsvTable.Load(path);
                    report.SourceTables.Add(new SourceTableInfo(fileName, table.Rows.Count, table.Headers));
                }

                if (report.MissingSourceFiles.Count > 0)
                {
                    report.Warnings.Add("Missing source CSV files: " + string.Join(", ", report.MissingSourceFiles.ToArray()));
                    return;
                }

                AddPokeApiCoverage();
            }

            private void AddPokeApiCoverage()
            {
                CsvTable species = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_species.csv"));
                CsvTable pokemon = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon.csv"));
                CsvTable pokemonForms = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_forms.csv"));
                CsvTable moves = CsvTable.Load(Path.Combine(options.SourcePath, "moves.csv"));
                CsvTable abilities = CsvTable.Load(Path.Combine(options.SourcePath, "abilities.csv"));
                CsvTable items = CsvTable.Load(Path.Combine(options.SourcePath, "items.csv"));
                CsvTable itemNames = CsvTable.Load(Path.Combine(options.SourcePath, "item_names.csv"));
                CsvTable itemGameIndices = CsvTable.Load(Path.Combine(options.SourcePath, "item_game_indices.csv"));
                CsvTable versionGroups = CsvTable.Load(Path.Combine(options.SourcePath, "version_groups.csv"));
                CsvTable pokemonMoves = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_moves.csv"));
                CsvTable evolutions = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_evolution.csv"));

                int currentMaxDex = Max(root.pokemon.Select(delegate(PokemonEntry p) { return p.nationalDex; }));
                int currentMaxMove = Max(root.moves.Select(delegate(MoveEntry m) { return m.id; }));
                int currentMaxAbility = Max(root.abilities.Select(delegate(AbilityEntry a) { return a.id; }));
                int currentMaxItem = Max(root.items.Select(delegate(ItemEntry i) { return i.id; }));

                report.SourceCoverage.Add("PokeAPI species rows", species.Rows.Count.ToString());
                report.SourceCoverage.Add("PokeAPI Pokemon/form rows", pokemon.Rows.Count.ToString());
                report.SourceCoverage.Add("PokeAPI moves rows", moves.Rows.Count.ToString());
                report.SourceCoverage.Add("PokeAPI abilities rows", abilities.Rows.Count.ToString());
                report.SourceCoverage.Add("PokeAPI items rows", items.Rows.Count.ToString());
                report.SourceCoverage.Add("PokeAPI Pokemon move rows", pokemonMoves.Rows.Count.ToString());
                report.SourceCoverage.Add("PokeAPI evolution rows", evolutions.Rows.Count.ToString());

                report.SourceCoverage.Add("PokeAPI species generations", FormatCsvCounts(species, "generation_id"));
                report.SourceCoverage.Add("PokeAPI move generations", FormatCsvCounts(moves, "generation_id"));
                report.SourceCoverage.Add("PokeAPI ability generations", FormatCsvCounts(abilities, "generation_id"));
                report.SourceCoverage.Add("PokeAPI version group generations", FormatCsvCounts(versionGroups, "generation_id"));
                report.SourceCoverage.Add("PokeAPI item availability generations", FormatDistinctItemGenerationCounts(itemGameIndices));

                report.Candidates.Add("New species by dex id", CountRowsGreaterThan(species, "id", currentMaxDex).ToString());
                report.Candidates.Add("New species by generation > 7", CountRowsGreaterThan(species, "generation_id", 7).ToString());
                report.Candidates.Add("New Pokemon/form rows by species id", CountRowsGreaterThan(pokemon, "species_id", currentMaxDex).ToString());
                report.Candidates.Add("New moves by id", CountRowsGreaterThan(moves, "id", currentMaxMove).ToString());
                report.Candidates.Add("New moves by generation > 7", CountRowsGreaterThan(moves, "generation_id", 7).ToString());
                report.Candidates.Add("New mainline abilities by id", CountMainlineAbilitiesGreaterThan(abilities, "id", currentMaxAbility).ToString());
                report.Candidates.Add("New mainline abilities by generation > 7", CountMainlineAbilitiesGreaterThan(abilities, "generation_id", 7).ToString());
                report.Candidates.Add("New items by id", CountRowsGreaterThan(items, "id", currentMaxItem).ToString());
                report.Candidates.Add("Items available in generation > 7", CountDistinctRowsGreaterThan(itemGameIndices, "item_id", "generation_id", 7).ToString());
                report.Candidates.Add("Version groups after generation 7", CountRowsGreaterThan(versionGroups, "generation_id", 7).ToString());

                BuildMappingPreview(species, pokemon, pokemonForms, moves, abilities, items, itemNames, currentMaxDex, currentMaxMove, currentMaxAbility, currentMaxItem);

                report.Notes.Add("This preflight only compares source coverage. It does not merge or write data yet.");
                if (!string.IsNullOrWhiteSpace(options.MapPath))
                {
                    report.Notes.Add("Mapping preview is written to: " + Path.GetFullPath(options.MapPath));
                }
                report.Notes.Add("Pokemon forms from existing Gen 1-7 are marked for manual matching because legacy form IDs are not PokeAPI IDs.");
            }

            private void BuildMappingPreview(
                CsvTable species,
                CsvTable pokemon,
                CsvTable pokemonForms,
                CsvTable moves,
                CsvTable abilities,
                CsvTable items,
                CsvTable itemNames,
                int currentMaxDex,
                int currentMaxMove,
                int currentMaxAbility,
                int currentMaxItem)
            {
                var existingBasePokemon = root.pokemon
                    .Where(delegate(PokemonEntry p) { return p.legacyId == p.nationalDex && p.nationalDex > 0; })
                    .GroupBy(delegate(PokemonEntry p) { return p.nationalDex; })
                    .ToDictionary(delegate(IGrouping<int, PokemonEntry> group) { return group.Key; }, delegate(IGrouping<int, PokemonEntry> group) { return group.First(); });

                foreach (Dictionary<string, string> row in species.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int sourceId = CsvInt(row, "id");
                    int generation = CsvInt(row, "generation_id");
                    PokemonEntry existing;
                    if (existingBasePokemon.TryGetValue(sourceId, out existing))
                    {
                        AddMapping("pokemon_species", sourceId.ToString(), existing.legacyId.ToString(), "existing_base", "nationalDex matched existing base Pokemon", CsvValue(row, "identifier"));
                    }
                    else if (sourceId > currentMaxDex)
                    {
                        AddMapping("pokemon_species", sourceId.ToString(), sourceId.ToString(), "add_base", "new species; generation " + generation, CsvValue(row, "identifier"));
                    }
                    else
                    {
                        AddMapping("pokemon_species", sourceId.ToString(), "", "needs_review", "source species is within current dex range but no base local row matched", CsvValue(row, "identifier"));
                    }
                }

                var formByPokemonId = pokemonForms.Rows
                    .GroupBy(delegate(Dictionary<string, string> row) { return CsvInt(row, "pokemon_id"); })
                    .ToDictionary(delegate(IGrouping<int, Dictionary<string, string>> group) { return group.Key; }, delegate(IGrouping<int, Dictionary<string, string>> group) { return group.First(); });

                int nextFormLocalId = Math.Max(5000, Max(root.pokemon.Select(delegate(PokemonEntry p) { return p.legacyId; })) + 1);
                foreach (Dictionary<string, string> row in pokemon.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int pokemonId = CsvInt(row, "id");
                    int speciesId = CsvInt(row, "species_id");
                    bool isDefault = CsvValue(row, "is_default") == "1";
                    string identifier = CsvValue(row, "identifier");
                    Dictionary<string, string> formRow;
                    int introducedVersionGroupId = formByPokemonId.TryGetValue(pokemonId, out formRow) ? CsvInt(formRow, "introduced_in_version_group_id") : 0;

                    if (speciesId > currentMaxDex)
                    {
                        if (isDefault)
                        {
                            AddMapping("pokemon_form", pokemonId.ToString(), speciesId.ToString(), "add_default_form", "new species default form; introduced version group " + introducedVersionGroupId, identifier);
                        }
                        else
                        {
                            AddMapping("pokemon_form", pokemonId.ToString(), nextFormLocalId.ToString(), "add_extra_form", "new species extra form; introduced version group " + introducedVersionGroupId, identifier);
                            nextFormLocalId++;
                        }
                    }
                    else if (isDefault)
                    {
                        AddMapping("pokemon_form", pokemonId.ToString(), speciesId.ToString(), "existing_default_form", "default form for existing species", identifier);
                    }
                    else
                    {
                        AddMapping("pokemon_form", pokemonId.ToString(), "", "manual_existing_form", "existing Gen 1-7 form requires legacy ID matching", identifier);
                    }
                }

                foreach (Dictionary<string, string> row in moves.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int sourceId = CsvInt(row, "id");
                    string localId = sourceId.ToString();
                    if (root.moves.Any(delegate(MoveEntry move) { return move.id == sourceId; }))
                    {
                        AddMapping("move", sourceId.ToString(), localId, "existing", "move id already present", CsvValue(row, "identifier"));
                    }
                    else if (sourceId > currentMaxMove)
                    {
                        AddMapping("move", sourceId.ToString(), localId, "add", "new move id after current max", CsvValue(row, "identifier"));
                    }
                    else
                    {
                        AddMapping("move", sourceId.ToString(), "", "needs_review", "move id is inside current range but missing locally", CsvValue(row, "identifier"));
                    }
                }

                foreach (Dictionary<string, string> row in abilities.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int sourceId = CsvInt(row, "id");
                    string localId = sourceId.ToString();
                    if (!IsMainlineAbility(row))
                    {
                        AddMapping("ability", sourceId.ToString(), "", "skip_non_mainline", "non-mainline ability", CsvValue(row, "identifier"));
                    }
                    else if (root.abilities.Any(delegate(AbilityEntry ability) { return ability.id == sourceId; }))
                    {
                        AddMapping("ability", sourceId.ToString(), localId, "existing", "ability id already present", CsvValue(row, "identifier"));
                    }
                    else if (sourceId > currentMaxAbility)
                    {
                        AddMapping("ability", sourceId.ToString(), localId, "add", "new ability id after current max", CsvValue(row, "identifier"));
                    }
                    else
                    {
                        AddMapping("ability", sourceId.ToString(), "", "needs_review", "ability id is inside current range but missing locally", CsvValue(row, "identifier"));
                    }
                }

                Dictionary<int, string> sourceItemEnglishNames = BuildEnglishNameMap(itemNames, "item_id");
                Dictionary<int, ItemEntry> currentItemsById = root.items
                    .GroupBy(delegate(ItemEntry item) { return item.id; })
                    .ToDictionary(delegate(IGrouping<int, ItemEntry> group) { return group.Key; }, delegate(IGrouping<int, ItemEntry> group) { return group.First(); });
                Dictionary<string, ItemEntry> currentItemsByName = new Dictionary<string, ItemEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (ItemEntry item in root.items)
                {
                    string normalized = NormalizeName(LocalName(item.names));
                    if (normalized.Length == 0 || currentItemsByName.ContainsKey(normalized)) continue;
                    currentItemsByName.Add(normalized, item);
                }

                foreach (Dictionary<string, string> row in items.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int sourceId = CsvInt(row, "id");
                    string sourceName;
                    if (!sourceItemEnglishNames.TryGetValue(sourceId, out sourceName) || sourceName.Length == 0)
                    {
                        sourceName = CsvValue(row, "identifier");
                    }

                    ItemEntry byId;
                    if (currentItemsById.TryGetValue(sourceId, out byId) && NormalizeName(LocalName(byId.names)) == NormalizeName(sourceName))
                    {
                        AddMapping("item", sourceId.ToString(), byId.id.ToString(), "existing_id_name", "item id and English name match", sourceName);
                        continue;
                    }

                    ItemEntry byName;
                    if (currentItemsByName.TryGetValue(NormalizeName(sourceName), out byName))
                    {
                        AddMapping("item", sourceId.ToString(), byName.id.ToString(), "existing_name", "English name matched legacy item id", sourceName);
                    }
                    else if (sourceId > currentMaxItem)
                    {
                        AddMapping("item", sourceId.ToString(), sourceId.ToString(), "add", "new PokeAPI item id after current max", sourceName);
                    }
                    else
                    {
                        AddMapping("item", sourceId.ToString(), "", "needs_review", "item id/name conflict inside current range", sourceName);
                    }
                }

                foreach (IGrouping<string, MappingRow> group in report.MappingRows.GroupBy(delegate(MappingRow row) { return row.Entity + ":" + row.Action; }).OrderBy(delegate(IGrouping<string, MappingRow> group) { return group.Key; }))
                {
                    report.MappingSummary.Add(group.Key, group.Count().ToString());
                }
            }

            private void AddMapping(string entity, string sourceKey, string localId, string action, string reason, string name)
            {
                report.MappingRows.Add(new MappingRow(entity, sourceKey, localId, action, reason, name));
            }

            private void AddImporterPlan()
            {
                report.Plan.Add("Pin or cache PokeAPI CSV files under tools\\import-data\\source-cache\\pokeapi-csv.");
                report.Plan.Add("Generate a stable source-to-local ID mapping for Pokemon/forms, moves, abilities, items, games, and learn methods.");
                report.Plan.Add("Merge moves, abilities, and items first because they have smaller dependency chains.");
                report.Plan.Add("Import Pokemon/forms after ID mapping is reviewed.");
                report.Plan.Add("Import evolutions and learnsets last, then run tools\\validate-data.ps1.");
            }

            private static int CountRowsGreaterThan(CsvTable table, string column, int threshold)
            {
                return table.Rows.Count(delegate(Dictionary<string, string> row) { return CsvInt(row, column) > threshold; });
            }

            private static int CountMainlineAbilitiesGreaterThan(CsvTable table, string column, int threshold)
            {
                return table.Rows.Count(delegate(Dictionary<string, string> row) { return IsMainlineAbility(row) && CsvInt(row, column) > threshold; });
            }

            private static bool IsMainlineAbility(Dictionary<string, string> row)
            {
                return CsvValue(row, "is_main_series") != "0";
            }

            private static int CountDistinctRowsGreaterThan(CsvTable table, string distinctColumn, string compareColumn, int threshold)
            {
                return table.Rows
                    .Where(delegate(Dictionary<string, string> row) { return CsvInt(row, compareColumn) > threshold; })
                    .Select(delegate(Dictionary<string, string> row) { return CsvInt(row, distinctColumn); })
                    .Where(delegate(int id) { return id > 0; })
                    .Distinct()
                    .Count();
            }

            private static string FormatCsvCounts(CsvTable table, string column)
            {
                return FormatCounts(table.Rows.Select(delegate(Dictionary<string, string> row) { return CsvInt(row, column); }));
            }

            private static string FormatDistinctItemGenerationCounts(CsvTable table)
            {
                var counts = table.Rows
                    .Select(delegate(Dictionary<string, string> row)
                    {
                        return new ItemGenerationPair(CsvInt(row, "item_id"), CsvInt(row, "generation_id"));
                    })
                    .Where(delegate(ItemGenerationPair pair) { return pair.ItemId > 0 && pair.GenerationId > 0; })
                    .GroupBy(delegate(ItemGenerationPair pair) { return pair.GenerationId; })
                    .OrderBy(delegate(IGrouping<int, ItemGenerationPair> group) { return group.Key; })
                    .Select(delegate(IGrouping<int, ItemGenerationPair> group) { return "Gen " + group.Key + ": " + group.Select(delegate(ItemGenerationPair pair) { return pair.ItemId; }).Distinct().Count(); });
                string text = string.Join(", ", counts.ToArray());
                return text.Length == 0 ? "--" : text;
            }

            private static int CsvInt(Dictionary<string, string> row, string column)
            {
                string value;
                if (!row.TryGetValue(column, out value)) return 0;
                int parsed;
                return int.TryParse(value, out parsed) ? parsed : 0;
            }

            private static string CsvValue(Dictionary<string, string> row, string column)
            {
                string value;
                return row.TryGetValue(column, out value) ? value : "";
            }

            private static Dictionary<int, string> BuildEnglishNameMap(CsvTable names, string idColumn)
            {
                var map = new Dictionary<int, string>();
                foreach (Dictionary<string, string> row in names.Rows)
                {
                    if (CsvInt(row, "local_language_id") != 9) continue;
                    int id = CsvInt(row, idColumn);
                    if (id <= 0 || map.ContainsKey(id)) continue;
                    map.Add(id, CsvValue(row, "name"));
                }
                return map;
            }

            private static string LocalName(Dictionary<string, string> names)
            {
                if (names == null) return "";
                string value;
                if (names.TryGetValue("en", out value) && !string.IsNullOrWhiteSpace(value)) return value;
                if (names.TryGetValue("zhCN", out value) && !string.IsNullOrWhiteSpace(value)) return value;
                foreach (string candidate in names.Values)
                {
                    if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
                }
                return "";
            }

            private static string NormalizeName(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return "";
                string decomposed = value.Normalize(NormalizationForm.FormD);
                var builder = new StringBuilder();
                foreach (char ch in decomposed)
                {
                    UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
                    if (category == UnicodeCategory.NonSpacingMark) continue;
                    if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
                }
                return builder.ToString();
            }

            private static int Max(IEnumerable<int> values)
            {
                int max = 0;
                foreach (int value in values)
                {
                    if (value > max) max = value;
                }
                return max;
            }

            private static string FormatCounts(IEnumerable<int> values)
            {
                var counts = values
                    .Where(delegate(int value) { return value > 0; })
                    .GroupBy(delegate(int value) { return value; })
                    .OrderBy(delegate(IGrouping<int, int> group) { return group.Key; })
                    .Select(delegate(IGrouping<int, int> group) { return "Gen " + group.Key + ": " + group.Count(); });
                string text = string.Join(", ", counts.ToArray());
                return text.Length == 0 ? "--" : text;
            }

            private static IEnumerable<int> ItemGenerationIds(ItemEntry item)
            {
                if (item == null) yield break;
                if (item.generations != null && item.generations.Any(delegate(int generation) { return generation > 0; }))
                {
                    foreach (int generation in item.generations)
                    {
                        if (generation > 0) yield return generation;
                    }
                    yield break;
                }

                if (item.flags == null) yield break;
                if (item.flags.inGen1) yield return 1;
                if (item.flags.inGen2) yield return 2;
                if (item.flags.inGen3) yield return 3;
                if (item.flags.inGen4) yield return 4;
                if (item.flags.inGen5) yield return 5;
                if (item.flags.inGen6) yield return 6;
                if (item.flags.inGen7) yield return 7;
            }
        }

        private sealed class ImportReport
        {
            public ImportReport(string dataPath, string sourcePath)
            {
                DataPath = Path.GetFullPath(dataPath);
                SourcePath = Path.GetFullPath(sourcePath);
                Summary = new Dictionary<string, string>();
                SourceCoverage = new Dictionary<string, string>();
                Candidates = new Dictionary<string, string>();
                MappingSummary = new Dictionary<string, string>();
                PreviewSummary = new Dictionary<string, string>();
                MappingRows = new List<MappingRow>();
                MissingChineseRows = new List<MissingChineseRow>();
                SourceTables = new List<SourceTableInfo>();
                MissingSourceFiles = new List<string>();
                Warnings = new List<string>();
                Notes = new List<string>();
                Plan = new List<string>();
            }

            public string DataPath { get; private set; }
            public string SourcePath { get; private set; }
            public Dictionary<string, string> Summary { get; private set; }
            public Dictionary<string, string> SourceCoverage { get; private set; }
            public Dictionary<string, string> Candidates { get; private set; }
            public Dictionary<string, string> MappingSummary { get; private set; }
            public Dictionary<string, string> PreviewSummary { get; private set; }
            public List<MappingRow> MappingRows { get; private set; }
            public List<MissingChineseRow> MissingChineseRows { get; private set; }
            public List<SourceTableInfo> SourceTables { get; private set; }
            public List<string> MissingSourceFiles { get; private set; }
            public List<string> Warnings { get; private set; }
            public List<string> Notes { get; private set; }
            public List<string> Plan { get; private set; }

            public string ToText()
            {
                var builder = new StringBuilder();
                builder.AppendLine("Podex Data Import Preflight");
                builder.AppendLine("===========================");
                builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine("Data: " + DataPath);
                builder.AppendLine("Source: " + SourcePath);
                builder.AppendLine();

                AppendDictionary(builder, "Current Data", Summary);
                AppendSourceTables(builder);
                AppendDictionary(builder, "Source Coverage", SourceCoverage);
                AppendDictionary(builder, "Expansion Candidates", Candidates);
                AppendDictionary(builder, "ID Mapping Summary", MappingSummary);
                AppendDictionary(builder, "Catalog Preview", PreviewSummary);
                AppendList(builder, "Warnings", Warnings);
                AppendList(builder, "Notes", Notes);
                AppendList(builder, "Next Plan", Plan);
                return builder.ToString();
            }

            private static void AppendDictionary(StringBuilder builder, string title, Dictionary<string, string> values)
            {
                builder.AppendLine(title);
                builder.AppendLine(new string('-', title.Length));
                if (values.Count == 0)
                {
                    builder.AppendLine("- None");
                    builder.AppendLine();
                    return;
                }
                foreach (KeyValuePair<string, string> entry in values)
                {
                    builder.AppendLine("- " + entry.Key + ": " + entry.Value);
                }
                builder.AppendLine();
            }

            private void AppendSourceTables(StringBuilder builder)
            {
                builder.AppendLine("Source Tables");
                builder.AppendLine("-------------");
                if (SourceTables.Count == 0)
                {
                    builder.AppendLine("- None");
                }
                else
                {
                    foreach (SourceTableInfo table in SourceTables.OrderBy(delegate(SourceTableInfo t) { return t.Name; }))
                    {
                        builder.AppendLine("- " + table.Name + ": " + table.RowCount + " rows; columns: " + string.Join(", ", table.Headers.ToArray()));
                    }
                }
                if (MissingSourceFiles.Count > 0)
                {
                    builder.AppendLine("- Missing: " + string.Join(", ", MissingSourceFiles.ToArray()));
                }
                builder.AppendLine();
            }

            private static void AppendList(StringBuilder builder, string title, List<string> values)
            {
                builder.AppendLine(title);
                builder.AppendLine(new string('-', title.Length));
                if (values.Count == 0)
                {
                    builder.AppendLine("- None");
                }
                else
                {
                    foreach (string value in values)
                    {
                        builder.AppendLine("- " + value);
                    }
                }
                builder.AppendLine();
            }
        }

        private sealed class SourceTableInfo
        {
            public SourceTableInfo(string name, int rowCount, List<string> headers)
            {
                Name = name;
                RowCount = rowCount;
                Headers = headers;
            }

            public string Name { get; private set; }
            public int RowCount { get; private set; }
            public List<string> Headers { get; private set; }
        }

        private sealed class MappingRow
        {
            public MappingRow(string entity, string sourceKey, string localId, string action, string reason, string name)
            {
                Entity = entity;
                SourceKey = sourceKey;
                LocalId = localId;
                Action = action;
                Reason = reason;
                Name = name;
            }

            public string Entity { get; private set; }
            public string SourceKey { get; private set; }
            public string LocalId { get; private set; }
            public string Action { get; private set; }
            public string Reason { get; private set; }
            public string Name { get; private set; }

            public static string ToCsv(List<MappingRow> rows)
            {
                var builder = new StringBuilder();
                builder.AppendLine("entity,source_key,local_id,action,reason,name");
                foreach (MappingRow row in rows)
                {
                    builder.Append(CsvEscape(row.Entity));
                    builder.Append(",");
                    builder.Append(CsvEscape(row.SourceKey));
                    builder.Append(",");
                    builder.Append(CsvEscape(row.LocalId));
                    builder.Append(",");
                    builder.Append(CsvEscape(row.Action));
                    builder.Append(",");
                    builder.Append(CsvEscape(row.Reason));
                    builder.Append(",");
                    builder.Append(CsvEscape(row.Name));
                    builder.AppendLine();
                }
                return builder.ToString();
            }

            private static string CsvEscape(string value)
            {
                if (value == null) value = "";
                bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
                value = value.Replace("\"", "\"\"");
                return quote ? "\"" + value + "\"" : value;
            }
        }

        private sealed class MissingChineseRow
        {
            public MissingChineseRow(string entity, int sourceId, string identifier, bool missingName, bool missingDescription)
            {
                Entity = entity;
                SourceId = sourceId;
                Identifier = identifier;
                MissingName = missingName;
                MissingDescription = missingDescription;
            }

            public string Entity { get; private set; }
            public int SourceId { get; private set; }
            public string Identifier { get; private set; }
            public bool MissingName { get; private set; }
            public bool MissingDescription { get; private set; }

            public static string ToCsv(List<MissingChineseRow> rows)
            {
                var builder = new StringBuilder();
                builder.AppendLine("entity,source_id,identifier,missing_name,missing_description");
                foreach (MissingChineseRow row in rows)
                {
                    builder.Append(CsvEscape(row.Entity));
                    builder.Append(",");
                    builder.Append(row.SourceId);
                    builder.Append(",");
                    builder.Append(CsvEscape(row.Identifier));
                    builder.Append(",");
                    builder.Append(row.MissingName ? "1" : "0");
                    builder.Append(",");
                    builder.Append(row.MissingDescription ? "1" : "0");
                    builder.AppendLine();
                }
                return builder.ToString();
            }

            private static string CsvEscape(string value)
            {
                if (value == null) value = "";
                bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
                value = value.Replace("\"", "\"\"");
                return quote ? "\"" + value + "\"" : value;
            }
        }

        private sealed class ChineseOverride
        {
            public ChineseOverride(string entity, int sourceId, string name, string genus, string description)
            {
                Entity = entity;
                SourceId = sourceId;
                Name = name;
                Genus = genus;
                Description = description;
            }

            public string Entity { get; private set; }
            public int SourceId { get; private set; }
            public string Name { get; private set; }
            public string Genus { get; private set; }
            public string Description { get; private set; }
        }

        private sealed class ItemGenerationPair
        {
            public ItemGenerationPair(int itemId, int generationId)
            {
                ItemId = itemId;
                GenerationId = generationId;
            }

            public int ItemId { get; private set; }
            public int GenerationId { get; private set; }
        }

        private sealed class CsvTable
        {
            public CsvTable()
            {
                Headers = new List<string>();
                Rows = new List<Dictionary<string, string>>();
            }

            public List<string> Headers { get; private set; }
            public List<Dictionary<string, string>> Rows { get; private set; }

            public static CsvTable Load(string path)
            {
                var table = new CsvTable();
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    string headerLine = reader.ReadLine();
                    if (headerLine == null) return table;
                    table.Headers.AddRange(ParseLine(headerLine));

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        List<string> values = ParseLine(line);
                        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < table.Headers.Count; i++)
                        {
                            string value = i < values.Count ? values[i] : "";
                            row[table.Headers[i]] = value;
                        }
                        table.Rows.Add(row);
                    }
                }
                return table;
            }

            private static List<string> ParseLine(string line)
            {
                var values = new List<string>();
                var current = new StringBuilder();
                bool quoted = false;

                for (int i = 0; i < line.Length; i++)
                {
                    char ch = line[i];
                    if (quoted)
                    {
                        if (ch == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"')
                            {
                                current.Append('"');
                                i++;
                            }
                            else
                            {
                                quoted = false;
                            }
                        }
                        else
                        {
                            current.Append(ch);
                        }
                    }
                    else
                    {
                        if (ch == ',')
                        {
                            values.Add(current.ToString());
                            current.Length = 0;
                        }
                        else if (ch == '"')
                        {
                            quoted = true;
                        }
                        else
                        {
                            current.Append(ch);
                        }
                    }
                }

                values.Add(current.ToString());
                return values;
            }
        }

        private static class CatalogPreviewGenerator
        {
            private static readonly Dictionary<int, string> LanguageKeys = new Dictionary<int, string>
            {
                { 9, "en" },
                { 12, "zhCN" },
                { 4, "zhTW" },
                { 11, "ja" },
                { 3, "ko" },
                { 6, "de" },
                { 5, "fr" },
                { 8, "it" },
                { 7, "es" }
            };

            public static void Generate(Options options, ImportReport report)
            {
                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = int.MaxValue,
                    RecursionLimit = 1000000
                };
                var raw = serializer.DeserializeObject(File.ReadAllText(options.DataPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (raw == null) throw new InvalidDataException("Root JSON must be an object.");

                List<object> pokemon = RawList(raw, "pokemon");
                List<object> moves = RawList(raw, "moves");
                List<object> abilities = RawList(raw, "abilities");
                List<object> items = RawList(raw, "items");
                List<object> evolutions = RawList(raw, "evolutions");
                List<object> games = RawList(raw, "games");
                List<object> levels = RawList(raw, "levels");
                List<object> learnsets = RawList(raw, "learnsets");

                int maxPokemonDex = MaxRawId(pokemon, "nationalDex");
                int maxMoveId = MaxRawId(moves, "id");
                int maxAbilityId = MaxRawId(abilities, "id");
                int maxItemId = MaxRawId(items, "id");

                CsvTable sourceSpecies = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_species.csv"));
                CsvTable sourcePokemon = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon.csv"));
                CsvTable pokemonSpeciesNames = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_species_names.csv"));
                CsvTable pokemonSpeciesFlavorText = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_species_flavor_text.csv"));
                CsvTable pokemonTypes = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_types.csv"));
                CsvTable pokemonStats = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_stats.csv"));
                CsvTable pokemonAbilities = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_abilities.csv"));
                CsvTable pokemonEggGroups = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_egg_groups.csv"));
                CsvTable pokemonEvolutions = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_evolution.csv"));
                CsvTable pokemonMoves = CsvTable.Load(Path.Combine(options.SourcePath, "pokemon_moves.csv"));
                CsvTable versionGroups = CsvTable.Load(Path.Combine(options.SourcePath, "version_groups.csv"));
                CsvTable sourceMoves = CsvTable.Load(Path.Combine(options.SourcePath, "moves.csv"));
                CsvTable moveNames = CsvTable.Load(Path.Combine(options.SourcePath, "move_names.csv"));
                CsvTable moveEffects = CsvTable.Load(Path.Combine(options.SourcePath, "move_effect_prose.csv"));
                CsvTable sourceAbilities = CsvTable.Load(Path.Combine(options.SourcePath, "abilities.csv"));
                CsvTable abilityNames = CsvTable.Load(Path.Combine(options.SourcePath, "ability_names.csv"));
                CsvTable abilityProse = CsvTable.Load(Path.Combine(options.SourcePath, "ability_prose.csv"));
                CsvTable sourceItems = CsvTable.Load(Path.Combine(options.SourcePath, "items.csv"));
                CsvTable itemNames = CsvTable.Load(Path.Combine(options.SourcePath, "item_names.csv"));
                CsvTable itemProse = CsvTable.Load(Path.Combine(options.SourcePath, "item_prose.csv"));
                CsvTable itemGameIndices = CsvTable.Load(Path.Combine(options.SourcePath, "item_game_indices.csv"));
                CsvTable machines = CsvTable.Load(Path.Combine(options.SourcePath, "machines.csv"));

                Dictionary<int, Dictionary<string, string>> pokemonNameMap = BuildLocalizedTextMap(pokemonSpeciesNames, "pokemon_species_id", "name");
                Dictionary<int, Dictionary<string, string>> pokemonGenusMap = BuildLocalizedTextMap(pokemonSpeciesNames, "pokemon_species_id", "genus");
                Dictionary<int, Dictionary<string, string>> pokemonFlavorMap = BuildLatestFlavorTextMap(pokemonSpeciesFlavorText);
                Dictionary<int, Dictionary<string, string>> pokemonDescriptionOverrides = new Dictionary<int, Dictionary<string, string>>();
                Dictionary<int, Dictionary<string, string>> moveNameMap = BuildLocalizedTextMap(moveNames, "move_id", "name");
                Dictionary<int, Dictionary<string, string>> moveEffectMap = BuildLocalizedTextMap(moveEffects, "move_effect_id", "short_effect");
                Dictionary<int, Dictionary<string, string>> moveDescriptionOverrides = new Dictionary<int, Dictionary<string, string>>();
                Dictionary<int, Dictionary<string, string>> abilityNameMap = BuildLocalizedTextMap(abilityNames, "ability_id", "name");
                Dictionary<int, Dictionary<string, string>> abilityTextMap = BuildLocalizedTextMap(abilityProse, "ability_id", "short_effect");
                Dictionary<int, Dictionary<string, string>> itemNameMap = BuildLocalizedTextMap(itemNames, "item_id", "name");
                Dictionary<int, Dictionary<string, string>> itemTextMap = BuildLocalizedTextMap(itemProse, "item_id", "short_effect");
                List<ChineseOverride> overrides = LoadChineseOverrides(options.ChineseOverridePath);
                ApplyChineseOverrides(overrides, pokemonNameMap, pokemonGenusMap, pokemonDescriptionOverrides, moveNameMap, moveDescriptionOverrides, abilityNameMap, abilityTextMap, itemNameMap, itemTextMap);
                FillSimplifiedChineseFallbacks(itemNameMap);
                FillSimplifiedChineseFallbacks(itemTextMap);
                Dictionary<int, List<int>> itemGenerations = BuildItemGenerationMap(itemGameIndices, sourceItems);
                FillInferredItemChineseDescriptions(sourceItems, itemGenerations, itemTextMap);
                Dictionary<int, Dictionary<string, object>> typeRefs = BuildTypeRefs(raw);
                Dictionary<int, Dictionary<string, object>> abilityRefs = BuildNamedRefs(abilityNameMap);
                Dictionary<int, Dictionary<string, object>> moveRefs = BuildNamedRefs(moveNameMap);
                Dictionary<int, Dictionary<string, object>> itemRefs = BuildNamedRefs(itemNameMap);
                Dictionary<int, Dictionary<string, object>> eggGroupRefs = BuildNestedNamedRefs(pokemon, "eggGroups");
                Dictionary<int, Dictionary<string, object>> genderRatioRefs = BuildNestedNamedRefs(pokemon, "genderRatio");
                Dictionary<int, Dictionary<string, object>> evolutionMethodRefs = BuildSingleNamedRefs(evolutions, "method");
                Dictionary<int, Dictionary<string, object>> evolutionStageRefs = BuildSingleNamedRefs(evolutions, "stage");
                Dictionary<int, Dictionary<string, object>> singleTypeDefense = BuildSingleTypeDefense(pokemon);
                Dictionary<int, Dictionary<string, string>> speciesRows = BuildRowMap(sourceSpecies, "id");
                Dictionary<int, List<Dictionary<string, string>>> typeRowsByPokemon = BuildRowsByInt(pokemonTypes, "pokemon_id");
                Dictionary<int, List<Dictionary<string, string>>> statRowsByPokemon = BuildRowsByInt(pokemonStats, "pokemon_id");
                Dictionary<int, List<Dictionary<string, string>>> abilityRowsByPokemon = BuildRowsByInt(pokemonAbilities, "pokemon_id");
                Dictionary<int, List<Dictionary<string, string>>> eggGroupRowsBySpecies = BuildRowsByInt(pokemonEggGroups, "species_id");
                Dictionary<int, List<Dictionary<string, string>>> evolutionRowsBySpecies = BuildRowsByInt(pokemonEvolutions, "evolved_species_id");
                Dictionary<int, int> versionGroupGameIds = BuildVersionGroupGameIds(versionGroups);
                Dictionary<string, int> machineLevelIds = BuildMachineLevelIds(machines, sourceItems);
                var newPokemonIdToLocalId = new Dictionary<int, int>();

                int addedPokemon = 0;
                int skippedPokemon = 0;
                int skippedPokemonMissingChinese = 0;
                foreach (Dictionary<string, string> row in sourcePokemon.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "species_id"); }).ThenBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int pokemonId = CsvInt(row, "id");
                    int speciesId = CsvInt(row, "species_id");
                    if (speciesId <= maxPokemonDex || CsvValue(row, "is_default") != "1") continue;
                    Dictionary<string, string> speciesRow;
                    if (!speciesRows.TryGetValue(speciesId, out speciesRow))
                    {
                        skippedPokemon++;
                        continue;
                    }
                    if (!options.AllowEnglishFallback && !HasCompleteChinese(pokemonNameMap, speciesId, pokemonDescriptionOverrides, speciesId))
                    {
                        report.MissingChineseRows.Add(new MissingChineseRow("pokemon", speciesId, CsvValue(row, "identifier"), !HasChinese(pokemonNameMap, speciesId), !HasChinese(pokemonDescriptionOverrides, speciesId)));
                        skippedPokemonMissingChinese++;
                        continue;
                    }
                    pokemon.Add(BuildPokemon(row, speciesRow, pokemonNameMap, pokemonGenusMap, pokemonFlavorMap, pokemonDescriptionOverrides, typeRowsByPokemon, statRowsByPokemon, abilityRowsByPokemon, eggGroupRowsBySpecies, typeRefs, abilityRefs, eggGroupRefs, genderRatioRefs, singleTypeDefense));
                    evolutions.Add(BuildPokemonEvolution(row, speciesRow, speciesRows, evolutionRowsBySpecies, evolutionMethodRefs, evolutionStageRefs, itemRefs, moveRefs));
                    newPokemonIdToLocalId[pokemonId] = speciesId;
                    addedPokemon++;
                }

                int addedMoves = 0;
                int skippedMoves = 0;
                int skippedMovesMissingChinese = 0;
                foreach (Dictionary<string, string> row in sourceMoves.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int id = CsvInt(row, "id");
                    if (id <= maxMoveId) continue;
                    int typeId = CsvInt(row, "type_id");
                    if (id >= 10000 || !typeRefs.ContainsKey(typeId))
                    {
                        skippedMoves++;
                        continue;
                    }
                    int effectId = CsvInt(row, "effect_id");
                    if (!options.AllowEnglishFallback && !HasCompleteMoveChinese(moveNameMap, id, moveDescriptionOverrides, moveEffectMap, effectId))
                    {
                        report.MissingChineseRows.Add(new MissingChineseRow("move", id, CsvValue(row, "identifier"), !HasChinese(moveNameMap, id), !HasMoveChineseDescription(moveDescriptionOverrides, id, moveEffectMap, effectId)));
                        skippedMovesMissingChinese++;
                        continue;
                    }
                    moves.Add(BuildMove(row, moveNameMap, moveEffectMap, moveDescriptionOverrides, typeRefs));
                    addedMoves++;
                }

                int addedAbilities = 0;
                int skippedAbilities = 0;
                int skippedAbilitiesMissingChinese = 0;
                foreach (Dictionary<string, string> row in sourceAbilities.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int id = CsvInt(row, "id");
                    if (id <= maxAbilityId) continue;
                    if (!IsMainlineAbility(row))
                    {
                        skippedAbilities++;
                        continue;
                    }
                    if (!options.AllowEnglishFallback && !HasCompleteChinese(abilityNameMap, id, abilityTextMap, id))
                    {
                        report.MissingChineseRows.Add(new MissingChineseRow("ability", id, CsvValue(row, "identifier"), !HasChinese(abilityNameMap, id), !HasChinese(abilityTextMap, id)));
                        skippedAbilitiesMissingChinese++;
                        continue;
                    }
                    abilities.Add(BuildAbility(row, abilityNameMap, abilityTextMap));
                    addedAbilities++;
                }

                int addedItems = 0;
                int skippedItems = 0;
                int skippedInternalItems = 0;
                int skippedPlaceholderItems = 0;
                int skippedItemsMissingChinese = 0;
                foreach (Dictionary<string, string> row in sourceItems.Rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "id"); }))
                {
                    int id = CsvInt(row, "id");
                    if (id <= maxItemId) continue;
                    if (!itemGenerations.ContainsKey(id))
                    {
                        skippedItems++;
                        continue;
                    }
                    if (IsUnresolvedDynamaxCrystal(row, id, itemTextMap))
                    {
                        skippedPlaceholderItems++;
                        continue;
                    }
                    if (IsInternalLetsGoPocket(row))
                    {
                        skippedInternalItems++;
                        continue;
                    }
                    if (!options.AllowEnglishFallback && !HasCompleteChinese(itemNameMap, id, itemTextMap, id))
                    {
                        report.MissingChineseRows.Add(new MissingChineseRow("item", id, CsvValue(row, "identifier"), !HasChinese(itemNameMap, id), !HasChinese(itemTextMap, id)));
                        skippedItemsMissingChinese++;
                        continue;
                    }
                    items.Add(BuildItem(row, itemNameMap, itemTextMap, itemGenerations));
                    addedItems++;
                }

                var usedVersionGroups = new HashSet<int>();
                var usedLevelIds = new HashSet<int>();
                int skippedLearnsets = 0;
                int addedLearnsets = AddPreviewLearnsets(learnsets, pokemonMoves, newPokemonIdToLocalId, moves, versionGroupGameIds, machineLevelIds, usedVersionGroups, usedLevelIds, ref skippedLearnsets);
                int addedGames = EnsurePreviewGames(games, usedVersionGroups, versionGroupGameIds);
                int addedLevels = EnsurePreviewLevels(levels, usedLevelIds);

                raw["pokemon"] = pokemon;
                raw["evolutions"] = evolutions;
                raw["moves"] = moves;
                raw["abilities"] = abilities;
                raw["items"] = items;
                raw["games"] = games;
                raw["levels"] = levels;
                raw["learnsets"] = learnsets;
                UpdateEvolutionStageMax(evolutions);
                UpdateMetaCounts(raw, pokemon.Count, MaxRawId(pokemon, "nationalDex"), moves.Count, abilities.Count, items.Count, evolutions.Count, learnsets.Count);

                string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.PreviewDataPath));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                File.WriteAllText(options.PreviewDataPath, serializer.Serialize(raw), Encoding.UTF8);

                report.PreviewSummary["Preview data"] = Path.GetFullPath(options.PreviewDataPath);
                report.PreviewSummary["Added Pokemon"] = addedPokemon.ToString();
                report.PreviewSummary["Skipped incomplete Pokemon"] = skippedPokemon.ToString();
                report.PreviewSummary["Skipped Pokemon missing Chinese"] = skippedPokemonMissingChinese.ToString();
                report.PreviewSummary["Added games"] = addedGames.ToString();
                report.PreviewSummary["Added levels"] = addedLevels.ToString();
                report.PreviewSummary["Added learnsets"] = addedLearnsets.ToString();
                report.PreviewSummary["Skipped learnsets"] = skippedLearnsets.ToString();
                report.PreviewSummary["Added moves"] = addedMoves.ToString();
                report.PreviewSummary["Skipped non-mainline/incomplete moves"] = skippedMoves.ToString();
                report.PreviewSummary["Skipped moves missing Chinese"] = skippedMovesMissingChinese.ToString();
                report.PreviewSummary["Added abilities"] = addedAbilities.ToString();
                report.PreviewSummary["Skipped non-mainline abilities"] = skippedAbilities.ToString();
                report.PreviewSummary["Skipped abilities missing Chinese"] = skippedAbilitiesMissingChinese.ToString();
                report.PreviewSummary["Added items"] = addedItems.ToString();
                report.PreviewSummary["Skipped items without generation availability"] = skippedItems.ToString();
                report.PreviewSummary["Skipped internal item pocket entries"] = skippedInternalItems.ToString();
                report.PreviewSummary["Skipped placeholder Dynamax crystals"] = skippedPlaceholderItems.ToString();
                report.PreviewSummary["Skipped items missing Chinese"] = skippedItemsMissingChinese.ToString();
                report.PreviewSummary["Missing Chinese report"] = string.IsNullOrWhiteSpace(options.MissingChinesePath) ? "--" : Path.GetFullPath(options.MissingChinesePath);
                report.PreviewSummary["Chinese overrides loaded"] = overrides.Count.ToString();
                report.PreviewSummary["Preview moves total"] = moves.Count.ToString();
                report.PreviewSummary["Preview abilities total"] = abilities.Count.ToString();
                report.PreviewSummary["Preview items total"] = items.Count.ToString();
                report.PreviewSummary["Preview Pokemon total"] = pokemon.Count.ToString();
                report.PreviewSummary["Preview games total"] = games.Count.ToString();
                report.PreviewSummary["Preview learnsets total"] = learnsets.Count.ToString();
            }

            private static Dictionary<string, object> BuildMove(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> effects,
                Dictionary<int, Dictionary<string, string>> descriptionOverrides,
                Dictionary<int, Dictionary<string, object>> typeRefs)
            {
                int id = CsvInt(row, "id");
                int typeId = CsvInt(row, "type_id");
                int damageClassId = CsvInt(row, "damage_class_id");
                int effectId = CsvInt(row, "effect_id");
                var move = new Dictionary<string, object>();
                move["id"] = id;
                move["generation"] = CsvInt(row, "generation_id");
                move["names"] = TextOrIdentifier(names, id, CsvValue(row, "identifier"));
                move["type"] = typeRefs.ContainsKey(typeId) ? typeRefs[typeId] : MakeNamedRef(typeId, CsvValue(row, "type_id"));
                move["category"] = MoveCategoryRef(damageClassId);
                move["power"] = NullableInt(row, "power");
                move["accuracy"] = NullableInt(row, "accuracy");
                move["pp"] = NullableInt(row, "pp");
                move["priority"] = CsvInt(row, "priority");
                move["rangeId"] = MoveRangeId(CsvInt(row, "target_id"));
                move["descriptions"] = MoveDescriptionText(effects, effectId, descriptionOverrides, id, CsvValue(row, "identifier"));
                return move;
            }

            private static Dictionary<string, object> BuildAbility(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> effects)
            {
                int id = CsvInt(row, "id");
                var ability = new Dictionary<string, object>();
                ability["id"] = id;
                ability["generation"] = CsvInt(row, "generation_id");
                ability["names"] = TextOrIdentifier(names, id, CsvValue(row, "identifier"));
                ability["trigger"] = null;
                ability["target"] = null;
                ability["effectOn"] = null;
                ability["descriptions"] = TextOrIdentifier(effects, id, CsvValue(row, "identifier"));
                return ability;
            }

            private static Dictionary<string, object> BuildItem(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> effects,
                Dictionary<int, List<int>> generationMap)
            {
                int id = CsvInt(row, "id");
                List<int> generations;
                if (!generationMap.TryGetValue(id, out generations)) generations = new List<int>();
                var item = new Dictionary<string, object>();
                item["id"] = id;
                item["names"] = TextOrIdentifier(names, id, CsvValue(row, "identifier"));
                item["descriptions"] = TextOrIdentifier(effects, id, CsvValue(row, "identifier"));
                item["price"] = CsvInt(row, "cost");
                item["bagId"] = CsvInt(row, "category_id");
                item["flags"] = new Dictionary<string, object>
                {
                    { "inGen1", false },
                    { "inGen2", false },
                    { "inGen3", false },
                    { "inGen4", false },
                    { "inGen5", false },
                    { "inGen6", false },
                    { "inGen7", false },
                    { "inBattle", false },
                    { "outBattle", false },
                    { "oneTime", false },
                    { "heldEffect", false },
                    { "evolveRelated", false }
                };
                item["generations"] = generations;
                item["versionGroups"] = new List<int>();
                return item;
            }

            private static Dictionary<string, object> BuildPokemon(
                Dictionary<string, string> pokemonRow,
                Dictionary<string, string> speciesRow,
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> genera,
                Dictionary<int, Dictionary<string, string>> flavorTexts,
                Dictionary<int, Dictionary<string, string>> descriptionOverrides,
                Dictionary<int, List<Dictionary<string, string>>> typeRowsByPokemon,
                Dictionary<int, List<Dictionary<string, string>>> statRowsByPokemon,
                Dictionary<int, List<Dictionary<string, string>>> abilityRowsByPokemon,
                Dictionary<int, List<Dictionary<string, string>>> eggGroupRowsBySpecies,
                Dictionary<int, Dictionary<string, object>> typeRefs,
                Dictionary<int, Dictionary<string, object>> abilityRefs,
                Dictionary<int, Dictionary<string, object>> eggGroupRefs,
                Dictionary<int, Dictionary<string, object>> genderRatioRefs,
                Dictionary<int, Dictionary<string, object>> singleTypeDefense)
            {
                int pokemonId = CsvInt(pokemonRow, "id");
                int speciesId = CsvInt(pokemonRow, "species_id");
                var pokemon = new Dictionary<string, object>();
                pokemon["id"] = "pokeapi:" + pokemonId.ToString(CultureInfo.InvariantCulture);
                pokemon["legacyId"] = speciesId;
                pokemon["nationalDex"] = speciesId;
                pokemon["generation"] = CsvInt(speciesRow, "generation_id");
                pokemon["formId"] = -1;
                pokemon["names"] = TextOrIdentifier(names, speciesId, CsvValue(pokemonRow, "identifier"));
                pokemon["formNames"] = DashNames();
                pokemon["speciesNames"] = TextOrIdentifier(genera, speciesId, "");

                List<int> typeIds = new List<int>();
                var typeRefsForPokemon = new List<object>();
                List<Dictionary<string, string>> typeRows;
                if (typeRowsByPokemon.TryGetValue(pokemonId, out typeRows))
                {
                    foreach (Dictionary<string, string> typeRow in typeRows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "slot"); }))
                    {
                        int typeId = CsvInt(typeRow, "type_id");
                        Dictionary<string, object> typeRef;
                        if (typeRefs.TryGetValue(typeId, out typeRef))
                        {
                            typeIds.Add(typeId);
                            typeRefsForPokemon.Add(typeRef);
                        }
                    }
                }
                pokemon["types"] = typeRefsForPokemon;

                pokemon["eggGroups"] = BuildEggGroups(speciesId, eggGroupRowsBySpecies, eggGroupRefs);
                pokemon["genderRatio"] = GenderRatioRef(CsvInt(speciesRow, "gender_rate"), genderRatioRefs);
                pokemon["abilities"] = BuildPokemonAbilities(pokemonId, abilityRowsByPokemon, abilityRefs);
                pokemon["stats"] = BuildPokemonStats(pokemonId, statRowsByPokemon, false);
                pokemon["effortYield"] = BuildPokemonStats(pokemonId, statRowsByPokemon, true);
                pokemon["measurements"] = BuildMeasurements(CsvInt(pokemonRow, "height"), CsvInt(pokemonRow, "weight"));
                pokemon["breeding"] = BuildBreedingInfo(speciesRow, pokemonRow);
                pokemon["captureRate"] = CsvInt(speciesRow, "capture_rate");
                pokemon["genderRatioId"] = GenderRatioId(CsvInt(speciesRow, "gender_rate"));
                pokemon["colorId"] = CsvInt(speciesRow, "color_id");
                pokemon["shapeId"] = CsvInt(speciesRow, "shape_id");
                pokemon["typeDefense"] = BuildPokemonTypeDefense(typeIds, singleTypeDefense);
                pokemon["descriptions"] = PokemonDescriptionText(flavorTexts, descriptionOverrides, speciesId, CsvValue(pokemonRow, "identifier"));
                return pokemon;
            }

            private static Dictionary<string, object> BuildPokemonEvolution(
                Dictionary<string, string> pokemonRow,
                Dictionary<string, string> speciesRow,
                Dictionary<int, Dictionary<string, string>> speciesRows,
                Dictionary<int, List<Dictionary<string, string>>> evolutionRowsBySpecies,
                Dictionary<int, Dictionary<string, object>> methodRefs,
                Dictionary<int, Dictionary<string, object>> stageRefs,
                Dictionary<int, Dictionary<string, object>> itemRefs,
                Dictionary<int, Dictionary<string, object>> moveRefs)
            {
                int speciesId = CsvInt(speciesRow, "id");
                int previousId = CsvInt(speciesRow, "evolves_from_species_id");
                int rootId = EvolutionRootId(speciesId, speciesRows);
                int stageId = EvolutionStageId(speciesId, speciesRows);
                Dictionary<string, string> evolutionRow = BestEvolutionRow(speciesId, evolutionRowsBySpecies);
                EvolutionDisplay display = BuildEvolutionDisplay(evolutionRow, methodRefs, itemRefs, moveRefs);

                var evolution = new Dictionary<string, object>();
                evolution["pokemonId"] = speciesId;
                evolution["familyId"] = rootId > 0 ? rootId : CsvInt(speciesRow, "evolution_chain_id");
                evolution["stageId"] = stageId;
                evolution["stageMax"] = EvolutionStageMax(rootId, speciesRows);
                evolution["previousPokemonId"] = previousId > 0 ? previousId : -1;
                evolution["method"] = previousId > 0 ? display.Method : null;
                evolution["stage"] = EvolutionStageRef(stageId, stageRefs);
                evolution["conditionKind"] = previousId > 0 ? display.ConditionKind : "none";
                evolution["conditionValue"] = previousId > 0 ? display.ConditionValue : (object)(-1);
                evolution["condition"] = previousId > 0 ? display.Condition : null;
                return evolution;
            }

            private static Dictionary<int, Dictionary<string, string>> BuildLocalizedTextMap(CsvTable table, string idColumn, string textColumn)
            {
                var result = new Dictionary<int, Dictionary<string, string>>();
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int id = CsvInt(row, idColumn);
                    int languageId = CsvInt(row, "local_language_id");
                    string languageKey;
                    if (id <= 0 || !LanguageKeys.TryGetValue(languageId, out languageKey)) continue;
                    Dictionary<string, string> values;
                    if (!result.TryGetValue(id, out values))
                    {
                        values = new Dictionary<string, string>();
                        result.Add(id, values);
                    }
                    values[languageKey] = CsvValue(row, textColumn);
                }
                return result;
            }

            private static Dictionary<int, Dictionary<string, string>> BuildLatestFlavorTextMap(CsvTable table)
            {
                var result = new Dictionary<int, Dictionary<string, string>>();
                var latestVersion = new Dictionary<string, int>();
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int id = CsvInt(row, "species_id");
                    int languageId = CsvInt(row, "language_id");
                    int versionId = CsvInt(row, "version_id");
                    string languageKey;
                    if (id <= 0 || !LanguageKeys.TryGetValue(languageId, out languageKey)) continue;
                    string text = NormalizeFlavorText(CsvValue(row, "flavor_text"));
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    string key = id.ToString(CultureInfo.InvariantCulture) + "|" + languageKey;
                    int previousVersion;
                    if (latestVersion.TryGetValue(key, out previousVersion) && previousVersion > versionId) continue;
                    latestVersion[key] = versionId;
                    Dictionary<string, string> values;
                    if (!result.TryGetValue(id, out values))
                    {
                        values = new Dictionary<string, string>();
                        result.Add(id, values);
                    }
                    values[languageKey] = text;
                }
                return result;
            }

            private static string NormalizeFlavorText(string value)
            {
                if (value == null) return "";
                return value.Replace("\r", " ").Replace("\n", " ").Replace("\f", " ").Trim();
            }

            private static bool HasCompleteChinese(
                Dictionary<int, Dictionary<string, string>> names,
                int nameId,
                Dictionary<int, Dictionary<string, string>> descriptions,
                int descriptionId)
            {
                return HasChinese(names, nameId) && HasChinese(descriptions, descriptionId);
            }

            private static bool HasCompleteMoveChinese(
                Dictionary<int, Dictionary<string, string>> names,
                int moveId,
                Dictionary<int, Dictionary<string, string>> descriptionOverrides,
                Dictionary<int, Dictionary<string, string>> effectDescriptions,
                int effectId)
            {
                return HasChinese(names, moveId) && HasMoveChineseDescription(descriptionOverrides, moveId, effectDescriptions, effectId);
            }

            private static bool HasMoveChineseDescription(
                Dictionary<int, Dictionary<string, string>> descriptionOverrides,
                int moveId,
                Dictionary<int, Dictionary<string, string>> effectDescriptions,
                int effectId)
            {
                return HasChinese(descriptionOverrides, moveId) || HasChinese(effectDescriptions, effectId);
            }

            private static bool IsUnresolvedDynamaxCrystal(
                Dictionary<string, string> row,
                int id,
                Dictionary<int, Dictionary<string, string>> itemTextMap)
            {
                string identifier = CsvValue(row, "identifier");
                return identifier.StartsWith("dynamax-crystal-", StringComparison.OrdinalIgnoreCase) &&
                    !HasChinese(itemTextMap, id);
            }

            private static bool IsInternalLetsGoPocket(Dictionary<string, string> row)
            {
                string identifier = CsvValue(row, "identifier");
                return identifier == "medicine-pocket" ||
                    identifier == "candy-jar" ||
                    identifier == "power-up-pocket" ||
                    identifier == "catching-pocket" ||
                    identifier == "battle-pocket";
            }

            private static bool HasChinese(Dictionary<int, Dictionary<string, string>> valuesById, int id)
            {
                Dictionary<string, string> values;
                string value;
                return valuesById.TryGetValue(id, out values) &&
                    values.TryGetValue("zhCN", out value) &&
                    !string.IsNullOrWhiteSpace(value);
            }

            private static List<ChineseOverride> LoadChineseOverrides(string path)
            {
                var overrides = new List<ChineseOverride>();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return overrides;
                CsvTable table = CsvTable.Load(path);
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int sourceId = CsvInt(row, "source_id");
                    if (sourceId <= 0) continue;
                    string entity = CsvValue(row, "entity");
                    string name = CsvValue(row, "zhCN_name");
                    string genus = CsvValue(row, "zhCN_genus");
                    string description = CsvValue(row, "zhCN_description");
                    if (string.IsNullOrWhiteSpace(entity) || (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(genus) && string.IsNullOrWhiteSpace(description))) continue;
                    overrides.Add(new ChineseOverride(entity, sourceId, name, genus, description));
                }
                return overrides;
            }

            private static void ApplyChineseOverrides(
                List<ChineseOverride> overrides,
                Dictionary<int, Dictionary<string, string>> pokemonNameMap,
                Dictionary<int, Dictionary<string, string>> pokemonGenusMap,
                Dictionary<int, Dictionary<string, string>> pokemonDescriptionOverrides,
                Dictionary<int, Dictionary<string, string>> moveNameMap,
                Dictionary<int, Dictionary<string, string>> moveDescriptionOverrides,
                Dictionary<int, Dictionary<string, string>> abilityNameMap,
                Dictionary<int, Dictionary<string, string>> abilityTextMap,
                Dictionary<int, Dictionary<string, string>> itemNameMap,
                Dictionary<int, Dictionary<string, string>> itemTextMap)
            {
                foreach (ChineseOverride row in overrides)
                {
                    if (row.Entity.Equals("move", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyChineseMoveOverride(moveNameMap, moveDescriptionOverrides, row);
                    }
                    else if (row.Entity.Equals("pokemon", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyChinesePokemonOverride(pokemonNameMap, pokemonGenusMap, pokemonDescriptionOverrides, row);
                    }
                    else if (row.Entity.Equals("ability", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyChineseOverride(abilityNameMap, abilityTextMap, row);
                    }
                    else if (row.Entity.Equals("item", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyChineseOverride(itemNameMap, itemTextMap, row);
                    }
                }
            }

            private static void ApplyChinesePokemonOverride(
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> genera,
                Dictionary<int, Dictionary<string, string>> descriptions,
                ChineseOverride row)
            {
                if (!string.IsNullOrWhiteSpace(row.Name))
                {
                    EnsureTextMap(names, row.SourceId)["zhCN"] = row.Name;
                }
                if (!string.IsNullOrWhiteSpace(row.Genus))
                {
                    EnsureTextMap(genera, row.SourceId)["zhCN"] = row.Genus;
                }
                if (!string.IsNullOrWhiteSpace(row.Description))
                {
                    EnsureTextMap(descriptions, row.SourceId)["zhCN"] = row.Description;
                }
            }

            private static void ApplyChineseMoveOverride(
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> descriptionsByMoveId,
                ChineseOverride row)
            {
                if (!string.IsNullOrWhiteSpace(row.Name))
                {
                    EnsureTextMap(names, row.SourceId)["zhCN"] = row.Name;
                }
                if (!string.IsNullOrWhiteSpace(row.Description))
                {
                    EnsureTextMap(descriptionsByMoveId, row.SourceId)["zhCN"] = row.Description;
                }
            }

            private static void ApplyChineseOverride(
                Dictionary<int, Dictionary<string, string>> names,
                Dictionary<int, Dictionary<string, string>> descriptions,
                ChineseOverride row)
            {
                if (!string.IsNullOrWhiteSpace(row.Name))
                {
                    EnsureTextMap(names, row.SourceId)["zhCN"] = row.Name;
                }
                if (!string.IsNullOrWhiteSpace(row.Description))
                {
                    EnsureTextMap(descriptions, row.SourceId)["zhCN"] = row.Description;
                }
            }

            private static Dictionary<string, string> EnsureTextMap(Dictionary<int, Dictionary<string, string>> map, int id)
            {
                Dictionary<string, string> values;
                if (!map.TryGetValue(id, out values))
                {
                    values = new Dictionary<string, string>();
                    map.Add(id, values);
                }
                return values;
            }

            private static void FillSimplifiedChineseFallbacks(Dictionary<int, Dictionary<string, string>> textMap)
            {
                foreach (Dictionary<string, string> values in textMap.Values)
                {
                    string zhCN;
                    string zhTW;
                    if (values.TryGetValue("zhCN", out zhCN) && !string.IsNullOrWhiteSpace(zhCN)) continue;
                    if (!values.TryGetValue("zhTW", out zhTW) || string.IsNullOrWhiteSpace(zhTW)) continue;
                    values["zhCN"] = Strings.StrConv(zhTW, VbStrConv.SimplifiedChinese, 0);
                }
            }

            private static void FillInferredItemChineseDescriptions(
                CsvTable items,
                Dictionary<int, List<int>> generationMap,
                Dictionary<int, Dictionary<string, string>> descriptions)
            {
                foreach (Dictionary<string, string> row in items.Rows)
                {
                    int itemId = CsvInt(row, "id");
                    List<int> generations;
                    if (itemId <= 0 || !generationMap.TryGetValue(itemId, out generations) || !generations.Any(delegate(int generation) { return generation >= 8; })) continue;
                    if (HasChinese(descriptions, itemId)) continue;

                    string text = InferredItemDescription(CsvInt(row, "category_id"), generations);
                    Dictionary<string, string> values = EnsureTextMap(descriptions, itemId);
                    values["zhCN"] = text;
                    values["zhTW"] = text;
                    values["en"] = text;
                }
            }

            private static string InferredItemDescription(int categoryId, List<int> generations)
            {
                switch (categoryId)
                {
                    case 10: return "进化相关道具。";
                    case 12: return "可携带道具。";
                    case 20:
                    case 21:
                    case 22: return "重要物品。";
                    case 26: return "培养用道具。";
                    case 37: return "招式学习器。可让宝可梦学习对应招式。";
                    case 52: return "太晶碎块。可用于改变宝可梦的太晶属性。";
                    case 53: return "野餐食材。可用于制作三明治。";
                    case 54: return "宝可梦素材。可用于制作招式学习器。";
                    case 55: return "重要物品。";
                    default:
                        int generation = generations.Where(delegate(int value) { return value > 0; }).DefaultIfEmpty(0).Min();
                        return generation > 0 ? "第" + generation.ToString(CultureInfo.InvariantCulture) + "世代道具。" : "道具。";
                }
            }

            private static Dictionary<int, List<int>> BuildItemGenerationMap(CsvTable table, CsvTable items)
            {
                var result = new Dictionary<int, List<int>>();
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int itemId = CsvInt(row, "item_id");
                    int generationId = CsvInt(row, "generation_id");
                    if (itemId <= 0 || generationId <= 0) continue;
                    AddItemGeneration(result, itemId, generationId);
                }
                foreach (Dictionary<string, string> row in items.Rows)
                {
                    int itemId = CsvInt(row, "id");
                    int inferredGeneration = InferItemGeneration(row);
                    if (itemId > 0 && inferredGeneration > 0) AddItemGeneration(result, itemId, inferredGeneration);
                }
                foreach (List<int> generations in result.Values)
                {
                    generations.Sort();
                }
                return result;
            }

            private static void AddItemGeneration(Dictionary<int, List<int>> result, int itemId, int generationId)
            {
                List<int> generations;
                if (!result.TryGetValue(itemId, out generations))
                {
                    generations = new List<int>();
                    result.Add(itemId, generations);
                }
                if (!generations.Contains(generationId)) generations.Add(generationId);
            }

            private static int InferItemGeneration(Dictionary<string, string> row)
            {
                int itemId = CsvInt(row, "id");
                if (itemId >= 1659 && itemId <= 1664) return 8;
                if (itemId == 1675 || itemId == 1676) return 8;
                if (itemId == 2160) return 8;
                if (itemId >= 1665) return 9;
                return 0;
            }

            private static Dictionary<int, Dictionary<string, string>> BuildRowMap(CsvTable table, string idColumn)
            {
                var result = new Dictionary<int, Dictionary<string, string>>();
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int id = CsvInt(row, idColumn);
                    if (id > 0 && !result.ContainsKey(id)) result.Add(id, row);
                }
                return result;
            }

            private static Dictionary<int, List<Dictionary<string, string>>> BuildRowsByInt(CsvTable table, string idColumn)
            {
                var result = new Dictionary<int, List<Dictionary<string, string>>>();
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int id = CsvInt(row, idColumn);
                    if (id <= 0) continue;
                    List<Dictionary<string, string>> rows;
                    if (!result.TryGetValue(id, out rows))
                    {
                        rows = new List<Dictionary<string, string>>();
                        result.Add(id, rows);
                    }
                    rows.Add(row);
                }
                return result;
            }

            private static Dictionary<int, int> BuildVersionGroupGameIds(CsvTable versionGroups)
            {
                var available = new HashSet<int>();
                foreach (Dictionary<string, string> row in versionGroups.Rows)
                {
                    int versionGroupId = CsvInt(row, "id");
                    if (versionGroupId > 0) available.Add(versionGroupId);
                }

                var result = new Dictionary<int, int>();
                AddVersionGroupGameId(result, available, 19, 30);
                AddVersionGroupGameId(result, available, 20, 31);
                AddVersionGroupGameId(result, available, 21, 32);
                AddVersionGroupGameId(result, available, 22, 33);
                AddVersionGroupGameId(result, available, 23, 34);
                AddVersionGroupGameId(result, available, 24, 35);
                AddVersionGroupGameId(result, available, 25, 36);
                AddVersionGroupGameId(result, available, 26, 37);
                AddVersionGroupGameId(result, available, 27, 38);
                return result;
            }

            private static void AddVersionGroupGameId(Dictionary<int, int> result, HashSet<int> available, int versionGroupId, int gameId)
            {
                if (available.Contains(versionGroupId)) result[versionGroupId] = gameId;
            }

            private static Dictionary<string, int> BuildMachineLevelIds(CsvTable machines, CsvTable items)
            {
                var itemIdentifiers = BuildItemIdentifierMap(items);
                var result = new Dictionary<string, int>();
                foreach (Dictionary<string, string> row in machines.Rows)
                {
                    int versionGroupId = CsvInt(row, "version_group_id");
                    int moveId = CsvInt(row, "move_id");
                    int itemId = CsvInt(row, "item_id");
                    if (versionGroupId <= 0 || moveId <= 0 || itemId <= 0) continue;

                    string identifier;
                    int levelId;
                    if (!itemIdentifiers.TryGetValue(itemId, out identifier) || !TryMachineLevelId(identifier, out levelId)) continue;

                    string key = MachineKey(versionGroupId, moveId);
                    if (!result.ContainsKey(key)) result.Add(key, levelId);
                }
                return result;
            }

            private static Dictionary<int, string> BuildItemIdentifierMap(CsvTable items)
            {
                var result = new Dictionary<int, string>();
                foreach (Dictionary<string, string> row in items.Rows)
                {
                    int itemId = CsvInt(row, "id");
                    string identifier = CsvValue(row, "identifier");
                    if (itemId > 0 && !string.IsNullOrWhiteSpace(identifier) && !result.ContainsKey(itemId))
                    {
                        result.Add(itemId, identifier);
                    }
                }
                return result;
            }

            private static bool TryMachineLevelId(string itemIdentifier, out int levelId)
            {
                levelId = 0;
                int machineNumber;
                if (itemIdentifier.StartsWith("tm", StringComparison.OrdinalIgnoreCase) && TryParseMachineNumber(itemIdentifier, 2, out machineNumber))
                {
                    levelId = TechnicalMachineLevelId(machineNumber);
                    return levelId > 0;
                }
                if (itemIdentifier.StartsWith("tr", StringComparison.OrdinalIgnoreCase) && TryParseMachineNumber(itemIdentifier, 2, out machineNumber))
                {
                    levelId = TechnicalRecordLevelId(machineNumber);
                    return levelId > 0;
                }
                if (itemIdentifier.StartsWith("hm", StringComparison.OrdinalIgnoreCase) && TryParseMachineNumber(itemIdentifier, 2, out machineNumber))
                {
                    levelId = HiddenMachineLevelId(machineNumber);
                    return levelId > 0;
                }
                return false;
            }

            private static bool TryParseMachineNumber(string itemIdentifier, int prefixLength, out int machineNumber)
            {
                machineNumber = 0;
                if (itemIdentifier.Length <= prefixLength) return false;
                return int.TryParse(itemIdentifier.Substring(prefixLength), NumberStyles.Integer, CultureInfo.InvariantCulture, out machineNumber);
            }

            private static int TechnicalMachineLevelId(int machineNumber)
            {
                if (machineNumber == 0) return 500;
                if (machineNumber > 0 && machineNumber <= 100) return 100 + machineNumber;
                if (machineNumber > 100 && machineNumber <= 499) return 500 + machineNumber;
                return 0;
            }

            private static int TechnicalRecordLevelId(int machineNumber)
            {
                return machineNumber >= 0 && machineNumber <= 99 ? 400 + machineNumber : 0;
            }

            private static int HiddenMachineLevelId(int machineNumber)
            {
                return machineNumber > 0 && machineNumber <= 50 ? 200 + machineNumber : 0;
            }

            private static int AddPreviewLearnsets(
                List<object> learnsets,
                CsvTable pokemonMoves,
                Dictionary<int, int> pokemonIdToLocalId,
                List<object> moves,
                Dictionary<int, int> versionGroupGameIds,
                Dictionary<string, int> machineLevelIds,
                HashSet<int> usedVersionGroups,
                HashSet<int> usedLevelIds,
                ref int skippedLearnsets)
            {
                HashSet<int> moveIds = BuildRawIdSet(moves, "id");
                var existing = new HashSet<string>();
                foreach (object value in learnsets)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null) continue;
                    int pokemonId = ObjectInt(row.ContainsKey("pokemonId") ? row["pokemonId"] : null);
                    int gameId = ObjectInt(row.ContainsKey("gameId") ? row["gameId"] : null);
                    int levelId = ObjectInt(row.ContainsKey("levelId") ? row["levelId"] : null);
                    int moveId = ObjectInt(row.ContainsKey("moveId") ? row["moveId"] : null);
                    if (pokemonId > 0 && gameId > 0 && levelId > 0 && moveId > 0) existing.Add(LearnsetKey(pokemonId, gameId, levelId, moveId));
                }

                int added = 0;
                foreach (Dictionary<string, string> row in pokemonMoves.Rows
                    .OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "pokemon_id"); })
                    .ThenBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "version_group_id"); })
                    .ThenBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "pokemon_move_method_id"); })
                    .ThenBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "level"); })
                    .ThenBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "move_id"); }))
                {
                    int sourcePokemonId = CsvInt(row, "pokemon_id");
                    int pokemonId;
                    if (!pokemonIdToLocalId.TryGetValue(sourcePokemonId, out pokemonId)) continue;

                    int versionGroupId = CsvInt(row, "version_group_id");
                    int gameId;
                    if (!versionGroupGameIds.TryGetValue(versionGroupId, out gameId))
                    {
                        skippedLearnsets++;
                        continue;
                    }

                    int moveId = CsvInt(row, "move_id");
                    if (!moveIds.Contains(moveId))
                    {
                        skippedLearnsets++;
                        continue;
                    }

                    int methodId = CsvInt(row, "pokemon_move_method_id");
                    int levelId = LearnsetLevelId(methodId, CsvInt(row, "level"), versionGroupId, moveId, machineLevelIds);
                    if (levelId <= 0)
                    {
                        skippedLearnsets++;
                        continue;
                    }

                    string key = LearnsetKey(pokemonId, gameId, levelId, moveId);
                    if (existing.Contains(key)) continue;

                    learnsets.Add(new Dictionary<string, object>
                    {
                        { "pokemonId", pokemonId },
                        { "gameId", gameId },
                        { "levelId", levelId },
                        { "moveId", moveId }
                    });
                    existing.Add(key);
                    usedVersionGroups.Add(versionGroupId);
                    usedLevelIds.Add(levelId);
                    added++;
                }

                return added;
            }

            private static int LearnsetLevelId(int methodId, int level, int versionGroupId, int moveId, Dictionary<string, int> machineLevelIds)
            {
                switch (methodId)
                {
                    case 1: return level > 0 ? level : 1;
                    case 2: return 301;
                    case 3: return 302;
                    case 4:
                        int machineLevelId;
                        return machineLevelIds.TryGetValue(MachineKey(versionGroupId, moveId), out machineLevelId) ? machineLevelId : 306;
                    default: return 0;
                }
            }

            private static int EnsurePreviewGames(List<object> games, HashSet<int> usedVersionGroups, Dictionary<int, int> versionGroupGameIds)
            {
                HashSet<int> existingIds = BuildRawIdSet(games, "id");
                int added = 0;
                foreach (int versionGroupId in usedVersionGroups.OrderBy(delegate(int id) { return versionGroupGameIds.ContainsKey(id) ? versionGroupGameIds[id] : id; }))
                {
                    int gameId;
                    if (!versionGroupGameIds.TryGetValue(versionGroupId, out gameId) || existingIds.Contains(gameId)) continue;
                    games.Add(BuildPreviewGame(versionGroupId, gameId));
                    existingIds.Add(gameId);
                    added++;
                }
                return added;
            }

            private static int EnsurePreviewLevels(List<object> levels, HashSet<int> usedLevelIds)
            {
                HashSet<int> existingIds = BuildRawIdSet(levels, "id");
                int added = 0;
                foreach (int levelId in usedLevelIds.OrderBy(delegate(int id) { return id; }))
                {
                    if (existingIds.Contains(levelId)) continue;
                    levels.Add(BuildPreviewLevel(levelId));
                    existingIds.Add(levelId);
                    added++;
                }
                return added;
            }

            private static Dictionary<string, object> BuildPreviewGame(int versionGroupId, int gameId)
            {
                switch (versionGroupId)
                {
                    case 19: return MakeNamedRef(gameId, "Let's Go! 皮卡丘／Let's Go! 伊布", "Let's Go! 皮卡丘／Let's Go! 伊布", "Let's Go Pikachu/Let's Go Eevee");
                    case 20: return MakeNamedRef(gameId, "剑／盾", "劍／盾", "Sword/Shield");
                    case 21: return MakeNamedRef(gameId, "铠之孤岛", "鎧之孤島", "The Isle of Armor");
                    case 22: return MakeNamedRef(gameId, "冠之雪原", "冠之雪原", "The Crown Tundra");
                    case 23: return MakeNamedRef(gameId, "晶灿钻石／明亮珍珠", "晶燦鑽石／明亮珍珠", "Brilliant Diamond/Shining Pearl");
                    case 24: return MakeNamedRef(gameId, "传说 阿尔宙斯", "傳說 阿爾宙斯", "Legends Arceus");
                    case 25: return MakeNamedRef(gameId, "朱／紫", "朱／紫", "Scarlet/Violet");
                    case 26: return MakeNamedRef(gameId, "碧之假面", "碧之假面", "The Teal Mask");
                    case 27: return MakeNamedRef(gameId, "蓝之圆盘", "藍之圓盤", "The Indigo Disk");
                    default: return MakeNamedRef(gameId, "版本组" + versionGroupId.ToString(CultureInfo.InvariantCulture), "版本組" + versionGroupId.ToString(CultureInfo.InvariantCulture), "Version group " + versionGroupId.ToString(CultureInfo.InvariantCulture));
                }
            }

            private static Dictionary<string, object> BuildPreviewLevel(int levelId)
            {
                if (levelId == 306) return MakeNamedRef(levelId, "招式学习器", "招式學習器", "Move Machine");
                if (levelId >= 400 && levelId <= 499)
                {
                    int number = levelId - 400;
                    string label = MachineNumberText(number);
                    return MakeNamedRef(levelId, "招式记录" + label, "招式記錄" + label, "TR" + label);
                }
                if (levelId == 500) return MakeNamedRef(levelId, "招式00", "招式00", "TM00");
                if (levelId >= 601 && levelId <= 999)
                {
                    int number = levelId - 500;
                    string label = MachineNumberText(number);
                    return MakeNamedRef(levelId, "招式" + label, "招式" + label, "TM" + label);
                }
                if (levelId >= 101 && levelId <= 200)
                {
                    int number = levelId - 100;
                    string label = MachineNumberText(number);
                    return MakeNamedRef(levelId, "招式" + label, "招式" + label, "TM" + label);
                }
                if (levelId >= 201 && levelId <= 250)
                {
                    int number = levelId - 200;
                    string label = MachineNumberText(number);
                    return MakeNamedRef(levelId, "秘传" + label, "秘傳" + label, "HM" + label);
                }
                return MakeNamedRef(levelId, "其他", "其他", "Other");
            }

            private static string MachineNumberText(int number)
            {
                return number >= 0 && number < 100 ? number.ToString("00", CultureInfo.InvariantCulture) : number.ToString(CultureInfo.InvariantCulture);
            }

            private static string MachineKey(int versionGroupId, int moveId)
            {
                return versionGroupId.ToString(CultureInfo.InvariantCulture) + "|" + moveId.ToString(CultureInfo.InvariantCulture);
            }

            private static string LearnsetKey(int pokemonId, int gameId, int levelId, int moveId)
            {
                return pokemonId.ToString(CultureInfo.InvariantCulture) + "|" +
                    gameId.ToString(CultureInfo.InvariantCulture) + "|" +
                    levelId.ToString(CultureInfo.InvariantCulture) + "|" +
                    moveId.ToString(CultureInfo.InvariantCulture);
            }

            private static HashSet<int> BuildRawIdSet(List<object> rows, string key)
            {
                var result = new HashSet<int>();
                foreach (object value in rows)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null || !row.ContainsKey(key)) continue;
                    int id = ObjectInt(row[key]);
                    if (id > 0) result.Add(id);
                }
                return result;
            }

            private static Dictionary<int, Dictionary<string, object>> BuildTypeRefs(Dictionary<string, object> raw)
            {
                var result = new Dictionary<int, Dictionary<string, object>>();
                foreach (object value in RawList(raw, "types"))
                {
                    var type = value as Dictionary<string, object>;
                    if (type == null) continue;
                    int id = ObjectInt(type.ContainsKey("id") ? type["id"] : null);
                    if (id > 0 && !result.ContainsKey(id)) result.Add(id, type);
                }
                return result;
            }

            private static Dictionary<int, Dictionary<string, object>> BuildNamedRefs(Dictionary<int, Dictionary<string, string>> nameMap)
            {
                var result = new Dictionary<int, Dictionary<string, object>>();
                foreach (KeyValuePair<int, Dictionary<string, string>> entry in nameMap)
                {
                    var refObject = new Dictionary<string, object>();
                    refObject["id"] = entry.Key;
                    refObject["names"] = entry.Value;
                    result[entry.Key] = refObject;
                }
                return result;
            }

            private static Dictionary<int, Dictionary<string, object>> BuildSingleNamedRefs(List<object> rows, string property)
            {
                var result = new Dictionary<int, Dictionary<string, object>>();
                foreach (object value in rows)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null || !row.ContainsKey(property) || row[property] == null) continue;
                    AddNestedNamedRef(result, row[property]);
                }
                return result;
            }

            private static Dictionary<int, Dictionary<string, object>> BuildNestedNamedRefs(List<object> pokemon, string property)
            {
                var result = new Dictionary<int, Dictionary<string, object>>();
                foreach (object value in pokemon)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null || !row.ContainsKey(property) || row[property] == null) continue;
                    if (property == "genderRatio")
                    {
                        AddNestedNamedRef(result, row[property]);
                    }
                    else
                    {
                        foreach (object refValue in RawObjectList(row[property]))
                        {
                            AddNestedNamedRef(result, refValue);
                        }
                    }
                }
                return result;
            }

            private static void AddNestedNamedRef(Dictionary<int, Dictionary<string, object>> result, object value)
            {
                var refObject = value as Dictionary<string, object>;
                if (refObject == null) return;
                int id = ObjectInt(refObject.ContainsKey("id") ? refObject["id"] : null);
                if (id > 0 && !result.ContainsKey(id)) result.Add(id, refObject);
            }

            private static List<object> RawObjectList(object value)
            {
                if (value == null) return new List<object>();
                object[] array = value as object[];
                if (array != null) return array.ToList();
                List<object> list = value as List<object>;
                if (list != null) return list;
                return new List<object>();
            }

            private static Dictionary<int, Dictionary<string, object>> BuildSingleTypeDefense(List<object> pokemon)
            {
                var result = new Dictionary<int, Dictionary<string, object>>();
                foreach (object value in pokemon)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null || !row.ContainsKey("types") || !row.ContainsKey("typeDefense")) continue;
                    List<object> types = RawObjectList(row["types"]);
                    if (types.Count != 1) continue;
                    var typeRef = types[0] as Dictionary<string, object>;
                    if (typeRef == null) continue;
                    int typeId = ObjectInt(typeRef.ContainsKey("id") ? typeRef["id"] : null);
                    var defense = row["typeDefense"] as Dictionary<string, object>;
                    if (typeId > 0 && defense != null && !result.ContainsKey(typeId)) result.Add(typeId, defense);
                }
                return result;
            }

            private static Dictionary<string, string> BestEvolutionRow(
                int speciesId,
                Dictionary<int, List<Dictionary<string, string>>> evolutionRowsBySpecies)
            {
                List<Dictionary<string, string>> rows;
                if (!evolutionRowsBySpecies.TryGetValue(speciesId, out rows) || rows.Count == 0) return null;

                Dictionary<string, string> best = null;
                int bestScore = -1;
                foreach (Dictionary<string, string> row in rows)
                {
                    int score = EvolutionRowScore(row);
                    if (score >= bestScore)
                    {
                        best = row;
                        bestScore = score;
                    }
                }
                return best;
            }

            private static int EvolutionRowScore(Dictionary<string, string> row)
            {
                int score = 0;
                foreach (string column in new[]
                {
                    "trigger_item_id", "minimum_level", "gender_id", "held_item_id", "time_of_day",
                    "known_move_id", "known_move_type_id", "minimum_happiness", "minimum_affection",
                    "relative_physical_stats", "party_species_id", "party_type_id", "trade_species_id",
                    "region_id", "used_move_id", "minimum_move_count", "minimum_steps", "minimum_damage_taken"
                })
                {
                    if (!string.IsNullOrWhiteSpace(CsvValue(row, column))) score++;
                }
                if (CsvValue(row, "needs_overworld_rain") == "1") score++;
                if (CsvValue(row, "turn_upside_down") == "1") score++;
                if (CsvValue(row, "needs_multiplayer") == "1") score++;
                return score;
            }

            private static int EvolutionRootId(int speciesId, Dictionary<int, Dictionary<string, string>> speciesRows)
            {
                int currentId = speciesId;
                var visited = new HashSet<int>();
                while (currentId > 0 && visited.Add(currentId))
                {
                    Dictionary<string, string> row;
                    if (!speciesRows.TryGetValue(currentId, out row)) break;
                    int previousId = CsvInt(row, "evolves_from_species_id");
                    if (previousId <= 0) return currentId;
                    currentId = previousId;
                }
                return speciesId;
            }

            private static int EvolutionStageId(int speciesId, Dictionary<int, Dictionary<string, string>> speciesRows)
            {
                int stage = 1;
                int currentId = speciesId;
                var visited = new HashSet<int>();
                while (currentId > 0 && visited.Add(currentId))
                {
                    Dictionary<string, string> row;
                    if (!speciesRows.TryGetValue(currentId, out row)) break;
                    int previousId = CsvInt(row, "evolves_from_species_id");
                    if (previousId <= 0) break;
                    stage++;
                    currentId = previousId;
                }
                return stage;
            }

            private static int EvolutionStageMax(int rootId, Dictionary<int, Dictionary<string, string>> speciesRows)
            {
                int max = 1;
                foreach (int speciesId in speciesRows.Keys)
                {
                    if (EvolutionRootId(speciesId, speciesRows) != rootId) continue;
                    int stage = EvolutionStageId(speciesId, speciesRows);
                    if (stage > max) max = stage;
                }
                return max;
            }

            private static Dictionary<string, object> EvolutionStageRef(int stageId, Dictionary<int, Dictionary<string, object>> stageRefs)
            {
                Dictionary<string, object> refObject;
                if (stageRefs.TryGetValue(stageId, out refObject)) return refObject;
                return MakeNamedRef(stageId, stageId.ToString(CultureInfo.InvariantCulture));
            }

            private static void UpdateEvolutionStageMax(List<object> evolutions)
            {
                var maxByFamily = new Dictionary<int, int>();
                foreach (object value in evolutions)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null) continue;
                    int familyId = ObjectInt(row.ContainsKey("familyId") ? row["familyId"] : null);
                    int stageId = ObjectInt(row.ContainsKey("stageId") ? row["stageId"] : null);
                    if (familyId <= 0 || stageId <= 0) continue;
                    int current;
                    if (!maxByFamily.TryGetValue(familyId, out current) || stageId > current) maxByFamily[familyId] = stageId;
                }

                foreach (object value in evolutions)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null) continue;
                    int familyId = ObjectInt(row.ContainsKey("familyId") ? row["familyId"] : null);
                    int max;
                    if (familyId > 0 && maxByFamily.TryGetValue(familyId, out max)) row["stageMax"] = max;
                }
            }

            private sealed class EvolutionDisplay
            {
                public EvolutionDisplay(Dictionary<string, object> method, string conditionKind, object conditionValue, Dictionary<string, object> condition)
                {
                    Method = method;
                    ConditionKind = conditionKind;
                    ConditionValue = conditionValue;
                    Condition = condition;
                }

                public Dictionary<string, object> Method { get; private set; }
                public string ConditionKind { get; private set; }
                public object ConditionValue { get; private set; }
                public Dictionary<string, object> Condition { get; private set; }
            }

            private static EvolutionDisplay BuildEvolutionDisplay(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, object>> methodRefs,
                Dictionary<int, Dictionary<string, object>> itemRefs,
                Dictionary<int, Dictionary<string, object>> moveRefs)
            {
                if (row == null) return new EvolutionDisplay(null, "none", -1, null);

                int triggerId = CsvInt(row, "evolution_trigger_id");
                switch (triggerId)
                {
                    case 1: return BuildLevelEvolutionDisplay(row, methodRefs, itemRefs, moveRefs);
                    case 2: return BuildTradeEvolutionDisplay(row, methodRefs, itemRefs);
                    case 3: return BuildItemEvolutionDisplay(row, methodRefs, itemRefs);
                    case 4: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 52, "脱壳进化", "Shed evolution"), "none", -1, null);
                    case 5: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 41, "旋转进化", "Spin evolution"), "none", -1, null);
                    case 6: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 42, "恶之塔进化", "Tower of Darkness evolution"), "none", -1, null);
                    case 7: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 43, "水之塔进化", "Tower of Waters evolution"), "none", -1, null);
                    case 8: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 44, "单场三次击中要害后进化", "Three critical hits evolution"), "value", "单场战斗击中要害3次", null);
                    case 9: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 45, "受伤后前往特定地点进化", "Take damage evolution"), "value", DamageCondition(row, "累计受到至少", "伤害后前往特定地点"), null);
                    case 10: return BuildOtherEvolutionDisplay(row, methodRefs);
                    case 11: return BuildUsedMoveEvolutionDisplay(row, methodRefs, moveRefs, 46, "迅疾使出特定招式后进化", "Agile style move evolution");
                    case 12: return BuildUsedMoveEvolutionDisplay(row, methodRefs, moveRefs, 47, "刚猛使出特定招式后进化", "Strong style move evolution");
                    case 13: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 48, "累计反作用力伤害后进化", "Recoil damage evolution"), "value", DamageCondition(row, "累计承受至少", "反作用力伤害"), null);
                    case 14: return BuildUsedMoveEvolutionDisplay(row, methodRefs, moveRefs, 49, "使用特定招式后进化", "Use move evolution");
                    case 15: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 50, "击败特定宝可梦后进化", "Defeat special Pokemon evolution"), "value", "击败3只携带头领凭证的劈斩司令", null);
                    case 16: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 51, "收集硬币后进化", "Collect coins evolution"), "value", "收集999枚索财灵的硬币", null);
                    default: return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 1, "升级进化", "Level up"), "none", -1, null);
                }
            }

            private static EvolutionDisplay BuildLevelEvolutionDisplay(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, object>> methodRefs,
                Dictionary<int, Dictionary<string, object>> itemRefs,
                Dictionary<int, Dictionary<string, object>> moveRefs)
            {
                int minimumLevel = CsvInt(row, "minimum_level");
                int knownMoveId = CsvInt(row, "known_move_id");
                int knownMoveTypeId = CsvInt(row, "known_move_type_id");
                int heldItemId = CsvInt(row, "held_item_id");
                int partySpeciesId = CsvInt(row, "party_species_id");
                int partyTypeId = CsvInt(row, "party_type_id");
                int minimumSteps = CsvInt(row, "minimum_steps");
                string timeOfDay = CsvValue(row, "time_of_day");

                if (minimumSteps > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 53, "同行步数后升级进化", "Walk together evolution"), "value", "同行" + minimumSteps.ToString(CultureInfo.InvariantCulture) + "步后升级", null);
                }
                if (knownMoveId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 22, "习得特定招式进化", "Level up while knowing a certain move"), "move", knownMoveId, RefOrFallback(moveRefs, knownMoveId, "招式#" + knownMoveId.ToString(CultureInfo.InvariantCulture)));
                }
                if (knownMoveTypeId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 33, "习得特定属性招式后升级进化", "Level up with a move type"), "value", "掌握指定属性招式", null);
                }
                if (heldItemId > 0)
                {
                    int methodId = timeOfDay == "day" ? 20 : (timeOfDay == "night" ? 21 : 1);
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, TimeText(timeOfDay) + "持有道具升级进化", "Level up while holding item"), "item", heldItemId, RefOrFallback(itemRefs, heldItemId, "道具#" + heldItemId.ToString(CultureInfo.InvariantCulture)));
                }
                if (partySpeciesId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 23, "队伍里有特定宝可梦进化", "Level up with certain Pokemon in party"), "pokemon", partySpeciesId, null);
                }
                if (partyTypeId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 29, "升级时队伍中有指定属性宝可梦", "Level up with certain type in party"), "value", "队伍中有指定属性宝可梦", null);
                }
                if (CsvValue(row, "needs_overworld_rain") == "1")
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 34, "下雨时升级进化", "Level up while raining"), minimumLevel > 0 ? "level" : "none", minimumLevel > 0 ? (object)minimumLevel : -1, null);
                }
                if (CsvValue(row, "turn_upside_down") == "1")
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 30, "升级时把主机反过来", "Level up while upside down"), minimumLevel > 0 ? "level" : "none", minimumLevel > 0 ? (object)minimumLevel : -1, null);
                }
                if (CsvInt(row, "minimum_happiness") > 0 || CsvInt(row, "minimum_affection") > 0)
                {
                    int methodId = timeOfDay == "day" ? 5 : (timeOfDay == "night" ? 6 : 4);
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, TimeText(timeOfDay) + "亲密度进化", "Happiness evolution"), minimumLevel > 0 ? "level" : "none", minimumLevel > 0 ? (object)minimumLevel : -1, null);
                }
                if (timeOfDay == "day" || timeOfDay == "night")
                {
                    int methodId = timeOfDay == "day" ? 31 : 32;
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, TimeText(timeOfDay) + "升级进化", "Timed level up"), minimumLevel > 0 ? "level" : "none", minimumLevel > 0 ? (object)minimumLevel : -1, null);
                }
                return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 1, "升级进化", "Level up"), minimumLevel > 0 ? "level" : "none", minimumLevel > 0 ? (object)minimumLevel : -1, null);
            }

            private static EvolutionDisplay BuildTradeEvolutionDisplay(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, object>> methodRefs,
                Dictionary<int, Dictionary<string, object>> itemRefs)
            {
                int heldItemId = CsvInt(row, "held_item_id");
                int tradeSpeciesId = CsvInt(row, "trade_species_id");
                if (heldItemId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 7, "携带道具通信进化", "Trade while holding item"), "item", heldItemId, RefOrFallback(itemRefs, heldItemId, "道具#" + heldItemId.ToString(CultureInfo.InvariantCulture)));
                }
                if (tradeSpeciesId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 27, "与特定宝可梦通信交换进化", "Trade for a certain Pokemon"), "pokemon", tradeSpeciesId, null);
                }
                return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 2, "通信进化", "Trade"), "none", -1, null);
            }

            private static EvolutionDisplay BuildItemEvolutionDisplay(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, object>> methodRefs,
                Dictionary<int, Dictionary<string, object>> itemRefs)
            {
                int itemId = CsvInt(row, "trigger_item_id");
                int genderId = CsvInt(row, "gender_id");
                string timeOfDay = CsvValue(row, "time_of_day");
                int methodId = genderId == 2 ? 18 : (genderId == 1 ? 19 : 3);
                Dictionary<string, object> itemRef = RefOrFallback(itemRefs, itemId, "道具#" + itemId.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(timeOfDay))
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, "使用道具进化", "Use item"), "value", TimeText(timeOfDay) + "时使用" + NamedRefLabel(itemRef), null);
                }
                return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, "使用道具进化", "Use item"), "item", itemId, itemRef);
            }

            private static EvolutionDisplay BuildOtherEvolutionDisplay(Dictionary<string, string> row, Dictionary<int, Dictionary<string, object>> methodRefs)
            {
                int minimumLevel = CsvInt(row, "minimum_level");
                return new EvolutionDisplay(EvolutionMethodRef(methodRefs, 54, "特殊条件进化", "Special condition evolution"), minimumLevel > 0 ? "level" : "none", minimumLevel > 0 ? (object)minimumLevel : -1, null);
            }

            private static EvolutionDisplay BuildUsedMoveEvolutionDisplay(
                Dictionary<string, string> row,
                Dictionary<int, Dictionary<string, object>> methodRefs,
                Dictionary<int, Dictionary<string, object>> moveRefs,
                int methodId,
                string zhCN,
                string en)
            {
                int moveId = CsvInt(row, "used_move_id");
                if (moveId <= 0) moveId = CsvInt(row, "known_move_id");
                int count = CsvInt(row, "minimum_move_count");
                Dictionary<string, object> moveRef = RefOrFallback(moveRefs, moveId, "招式#" + moveId.ToString(CultureInfo.InvariantCulture));
                string label = NamedRefLabel(moveRef);
                if (count > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, zhCN, en), "value", "使用" + label + count.ToString(CultureInfo.InvariantCulture) + "次", null);
                }
                if (moveId > 0)
                {
                    return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, zhCN, en), "move", moveId, moveRef);
                }
                return new EvolutionDisplay(EvolutionMethodRef(methodRefs, methodId, zhCN, en), "none", -1, null);
            }

            private static string DamageCondition(Dictionary<string, string> row, string prefix, string suffix)
            {
                int damage = CsvInt(row, "minimum_damage_taken");
                if (damage <= 0) return prefix + suffix;
                return prefix + damage.ToString(CultureInfo.InvariantCulture) + suffix;
            }

            private static Dictionary<string, object> EvolutionMethodRef(Dictionary<int, Dictionary<string, object>> methodRefs, int id, string zhCN, string en)
            {
                Dictionary<string, object> refObject;
                if (methodRefs.TryGetValue(id, out refObject)) return refObject;
                return MakeNamedRef(id, zhCN, en);
            }

            private static Dictionary<string, object> RefOrFallback(Dictionary<int, Dictionary<string, object>> refs, int id, string fallback)
            {
                Dictionary<string, object> refObject;
                if (id > 0 && refs.TryGetValue(id, out refObject)) return refObject;
                return MakeNamedRef(id, fallback, fallback);
            }

            private static string NamedRefLabel(Dictionary<string, object> refObject)
            {
                if (refObject == null || !refObject.ContainsKey("names")) return "";
                var objectNames = refObject["names"] as Dictionary<string, object>;
                if (objectNames != null)
                {
                    object value;
                    if (objectNames.TryGetValue("zhCN", out value) && value != null) return value.ToString();
                    if (objectNames.TryGetValue("en", out value) && value != null) return value.ToString();
                }
                var stringNames = refObject["names"] as Dictionary<string, string>;
                string text;
                if (stringNames != null && stringNames.TryGetValue("zhCN", out text)) return text;
                if (stringNames != null && stringNames.TryGetValue("en", out text)) return text;
                return "";
            }

            private static string TimeText(string timeOfDay)
            {
                if (timeOfDay == "day") return "白天";
                if (timeOfDay == "night") return "夜晚";
                if (timeOfDay == "full-moon") return "满月";
                return string.IsNullOrWhiteSpace(timeOfDay) ? "" : timeOfDay;
            }

            private static Dictionary<string, object> MoveCategoryRef(int damageClassId)
            {
                switch (damageClassId)
                {
                    case 2: return MakeNamedRef(1, "物理", "Physical");
                    case 3: return MakeNamedRef(2, "特殊", "Special");
                    default: return MakeNamedRef(3, "变化", "Status");
                }
            }

            private static int MoveRangeId(int targetId)
            {
                switch (targetId)
                {
                    case 3: return 2;
                    case 4:
                    case 6:
                    case 12: return 9;
                    case 7: return 8;
                    case 9:
                    case 14: return 5;
                    case 11: return 6;
                    case 13:
                    case 15: return 4;
                    default: return 1;
                }
            }

            private static Dictionary<string, object> MakeNamedRef(int id, string zhCN)
            {
                return MakeNamedRef(id, zhCN, zhCN);
            }

            private static Dictionary<string, object> MakeNamedRef(int id, string zhCN, string en)
            {
                return MakeNamedRef(id, zhCN, zhCN, en);
            }

            private static Dictionary<string, object> MakeNamedRef(int id, string zhCN, string zhTW, string en)
            {
                var names = new Dictionary<string, object>();
                names["en"] = en;
                names["zhCN"] = zhCN;
                names["zhTW"] = zhTW;
                var result = new Dictionary<string, object>();
                result["id"] = id;
                result["names"] = names;
                return result;
            }

            private static Dictionary<string, string> TextOrIdentifier(Dictionary<int, Dictionary<string, string>> map, int id, string identifier)
            {
                Dictionary<string, string> values;
                if (map.TryGetValue(id, out values) && values.Count > 0) return values;
                return new Dictionary<string, string> { { "en", identifier }, { "zhCN", identifier } };
            }

            private static Dictionary<string, string> MoveDescriptionText(
                Dictionary<int, Dictionary<string, string>> effects,
                int effectId,
                Dictionary<int, Dictionary<string, string>> descriptionOverrides,
                int moveId,
                string identifier)
            {
                var result = new Dictionary<string, string>();
                Dictionary<string, string> values;
                if (effects.TryGetValue(effectId, out values))
                {
                    foreach (KeyValuePair<string, string> value in values)
                    {
                        if (!string.IsNullOrWhiteSpace(value.Value)) result[value.Key] = value.Value;
                    }
                }
                if (descriptionOverrides.TryGetValue(moveId, out values))
                {
                    foreach (KeyValuePair<string, string> value in values)
                    {
                        if (!string.IsNullOrWhiteSpace(value.Value)) result[value.Key] = value.Value;
                    }
                }
                if (result.Count > 0) return result;
                return new Dictionary<string, string> { { "en", identifier }, { "zhCN", identifier } };
            }

            private static Dictionary<string, string> PokemonDescriptionText(
                Dictionary<int, Dictionary<string, string>> flavorTexts,
                Dictionary<int, Dictionary<string, string>> descriptionOverrides,
                int speciesId,
                string identifier)
            {
                var result = new Dictionary<string, string>();
                Dictionary<string, string> values;
                if (flavorTexts.TryGetValue(speciesId, out values))
                {
                    foreach (KeyValuePair<string, string> value in values)
                    {
                        if (!string.IsNullOrWhiteSpace(value.Value)) result[value.Key] = value.Value;
                    }
                }
                if (descriptionOverrides.TryGetValue(speciesId, out values))
                {
                    foreach (KeyValuePair<string, string> value in values)
                    {
                        if (!string.IsNullOrWhiteSpace(value.Value)) result[value.Key] = value.Value;
                    }
                }
                if (result.Count > 0) return result;
                return new Dictionary<string, string> { { "en", identifier }, { "zhCN", identifier } };
            }

            private static Dictionary<string, string> DashNames()
            {
                var result = new Dictionary<string, string>();
                foreach (string key in LanguageKeys.Values.Distinct())
                {
                    result[key] = "---";
                }
                return result;
            }

            private static List<object> BuildEggGroups(
                int speciesId,
                Dictionary<int, List<Dictionary<string, string>>> eggGroupRowsBySpecies,
                Dictionary<int, Dictionary<string, object>> eggGroupRefs)
            {
                var result = new List<object>();
                List<Dictionary<string, string>> rows;
                if (!eggGroupRowsBySpecies.TryGetValue(speciesId, out rows)) return result;
                foreach (Dictionary<string, string> row in rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "egg_group_id"); }))
                {
                    int eggGroupId = CsvInt(row, "egg_group_id");
                    Dictionary<string, object> refObject;
                    if (eggGroupRefs.TryGetValue(eggGroupId, out refObject)) result.Add(refObject);
                }
                return result;
            }

            private static Dictionary<string, object> GenderRatioRef(int genderRate, Dictionary<int, Dictionary<string, object>> genderRatioRefs)
            {
                int id = GenderRatioId(genderRate);
                Dictionary<string, object> refObject;
                if (genderRatioRefs.TryGetValue(id, out refObject)) return refObject;
                return MakeNamedRef(id, id == 8 ? "无性别" : "未知");
            }

            private static int GenderRatioId(int genderRate)
            {
                switch (genderRate)
                {
                    case 8: return 1;
                    case 7: return 2;
                    case 6: return 3;
                    case 4: return 4;
                    case 2: return 5;
                    case 1: return 6;
                    case 0: return 7;
                    default: return 8;
                }
            }

            private static Dictionary<string, object> BuildPokemonAbilities(
                int pokemonId,
                Dictionary<int, List<Dictionary<string, string>>> abilityRowsByPokemon,
                Dictionary<int, Dictionary<string, object>> abilityRefs)
            {
                var result = new Dictionary<string, object>();
                result["primary"] = null;
                result["secondary"] = null;
                result["hidden"] = null;
                List<Dictionary<string, string>> rows;
                if (!abilityRowsByPokemon.TryGetValue(pokemonId, out rows)) return result;
                foreach (Dictionary<string, string> row in rows.OrderBy(delegate(Dictionary<string, string> r) { return CsvInt(r, "slot"); }))
                {
                    int abilityId = CsvInt(row, "ability_id");
                    Dictionary<string, object> refObject;
                    if (!abilityRefs.TryGetValue(abilityId, out refObject)) refObject = MakeNamedRef(abilityId, abilityId.ToString(CultureInfo.InvariantCulture));
                    if (CsvValue(row, "is_hidden") == "1") result["hidden"] = refObject;
                    else if (CsvInt(row, "slot") == 2) result["secondary"] = refObject;
                    else result["primary"] = refObject;
                }
                return result;
            }

            private static Dictionary<string, object> BuildPokemonStats(
                int pokemonId,
                Dictionary<int, List<Dictionary<string, string>>> statRowsByPokemon,
                bool effort)
            {
                int hp = 0;
                int attack = 0;
                int defense = 0;
                int specialAttack = 0;
                int specialDefense = 0;
                int speed = 0;
                List<Dictionary<string, string>> rows;
                if (statRowsByPokemon.TryGetValue(pokemonId, out rows))
                {
                    foreach (Dictionary<string, string> row in rows)
                    {
                        int value = CsvInt(row, effort ? "effort" : "base_stat");
                        switch (CsvInt(row, "stat_id"))
                        {
                            case 1: hp = value; break;
                            case 2: attack = value; break;
                            case 3: defense = value; break;
                            case 4: specialAttack = value; break;
                            case 5: specialDefense = value; break;
                            case 6: speed = value; break;
                        }
                    }
                }
                var result = new Dictionary<string, object>();
                result["hp"] = hp;
                result["attack"] = attack;
                result["defense"] = defense;
                result["specialAttack"] = specialAttack;
                result["specialDefense"] = specialDefense;
                result["speed"] = speed;
                result["total"] = hp + attack + defense + specialAttack + specialDefense + speed;
                return result;
            }

            private static Dictionary<string, object> BuildMeasurements(int heightDecimeters, int weightHectograms)
            {
                var result = new Dictionary<string, object>();
                double meters = heightDecimeters / 10.0;
                double inches = meters * 39.3700787;
                double kilograms = weightHectograms / 10.0;
                double pounds = kilograms * 2.20462262;
                result["heightMetric"] = Math.Round(meters, 1);
                result["heightImperial"] = Math.Round(inches, 1);
                result["weightMetric"] = Math.Round(kilograms, 1);
                result["weightImperial"] = Math.Round(pounds, 1);
                return result;
            }

            private static Dictionary<string, object> BuildBreedingInfo(Dictionary<string, string> speciesRow, Dictionary<string, string> pokemonRow)
            {
                var result = new Dictionary<string, object>();
                result["hatchCycles"] = CsvInt(speciesRow, "hatch_counter");
                result["baseTameness"] = CsvInt(speciesRow, "base_happiness");
                result["exp"] = CsvInt(pokemonRow, "base_experience");
                result["exp100"] = null;
                return result;
            }

            private static Dictionary<string, object> BuildPokemonTypeDefense(List<int> typeIds, Dictionary<int, Dictionary<string, object>> singleTypeDefense)
            {
                var result = new Dictionary<string, object>();
                for (int attackTypeId = 1; attackTypeId <= 18; attackTypeId++)
                {
                    double multiplier = 1.0;
                    foreach (int defenseTypeId in typeIds)
                    {
                        Dictionary<string, object> defense;
                        object value;
                        if (singleTypeDefense.TryGetValue(defenseTypeId, out defense) && defense.TryGetValue(attackTypeId.ToString(CultureInfo.InvariantCulture), out value))
                        {
                            double parsed;
                            if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) multiplier *= parsed;
                        }
                    }
                    result[attackTypeId.ToString(CultureInfo.InvariantCulture)] = multiplier;
                }
                return result;
            }

            private static object NullableInt(Dictionary<string, string> row, string column)
            {
                string value = CsvValue(row, column);
                if (string.IsNullOrWhiteSpace(value)) return null;
                int parsed;
                return int.TryParse(value, out parsed) ? (object)parsed : null;
            }

            private static List<object> RawList(Dictionary<string, object> root, string key)
            {
                object value;
                if (!root.TryGetValue(key, out value) || value == null) return new List<object>();
                object[] array = value as object[];
                if (array != null) return array.ToList();
                List<object> list = value as List<object>;
                if (list != null) return list;
                return new List<object>();
            }

            private static int MaxRawId(List<object> rows, string key)
            {
                int max = 0;
                foreach (object value in rows)
                {
                    var row = value as Dictionary<string, object>;
                    if (row == null || !row.ContainsKey(key)) continue;
                    int id = ObjectInt(row[key]);
                    if (id > max) max = id;
                }
                return max;
            }

            private static int ObjectInt(object value)
            {
                if (value == null) return 0;
                int parsed;
                return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
            }

            private static int CsvInt(Dictionary<string, string> row, string column)
            {
                string value;
                if (!row.TryGetValue(column, out value)) return 0;
                int parsed;
                return int.TryParse(value, out parsed) ? parsed : 0;
            }

            private static bool IsMainlineAbility(Dictionary<string, string> row)
            {
                return CsvValue(row, "is_main_series") != "0";
            }

            private static string CsvValue(Dictionary<string, string> row, string column)
            {
                string value;
                return row.TryGetValue(column, out value) ? value : "";
            }

            private static void UpdateMetaCounts(Dictionary<string, object> root, int pokemon, int maxNationalDex, int moves, int abilities, int items, int evolutions, int learnsets)
            {
                Dictionary<string, object> meta = root.ContainsKey("meta") ? root["meta"] as Dictionary<string, object> : null;
                if (meta == null) return;
                meta["count"] = pokemon;
                meta["maxNationalDex"] = maxNationalDex;
                Dictionary<string, object> counts = meta.ContainsKey("counts") ? meta["counts"] as Dictionary<string, object> : null;
                if (counts == null) return;
                counts["pokemon"] = pokemon;
                counts["moves"] = moves;
                counts["abilities"] = abilities;
                counts["items"] = items;
                counts["evolutions"] = evolutions;
                counts["learnsets"] = learnsets;
            }
        }

        private sealed class Options
        {
            public string DataPath { get; private set; }
            public string SourcePath { get; private set; }
            public string ReportPath { get; private set; }
            public string MapPath { get; private set; }
            public string PreviewDataPath { get; private set; }
            public string MissingChinesePath { get; private set; }
            public string ChineseOverridePath { get; private set; }
            public bool AllowEnglishFallback { get; private set; }
            public bool RequireSource { get; private set; }
            public bool ShowHelp { get; private set; }

            public static Options Parse(string[] args)
            {
                var options = new Options();
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg == "--help" || arg == "-h" || arg == "/?")
                    {
                        options.ShowHelp = true;
                    }
                    else if (arg == "--data" && i + 1 < args.Length)
                    {
                        options.DataPath = args[++i];
                    }
                    else if (arg == "--source" && i + 1 < args.Length)
                    {
                        options.SourcePath = args[++i];
                    }
                    else if (arg == "--report" && i + 1 < args.Length)
                    {
                        options.ReportPath = args[++i];
                    }
                    else if (arg == "--map" && i + 1 < args.Length)
                    {
                        options.MapPath = args[++i];
                    }
                    else if (arg == "--preview-data" && i + 1 < args.Length)
                    {
                        options.PreviewDataPath = args[++i];
                    }
                    else if (arg == "--missing-chinese" && i + 1 < args.Length)
                    {
                        options.MissingChinesePath = args[++i];
                    }
                    else if (arg == "--chinese-overrides" && i + 1 < args.Length)
                    {
                        options.ChineseOverridePath = args[++i];
                    }
                    else if (arg == "--allow-english-fallback")
                    {
                        options.AllowEnglishFallback = true;
                    }
                    else if (arg == "--require-source")
                    {
                        options.RequireSource = true;
                    }
                    else
                    {
                        throw new ArgumentException("Unknown or incomplete argument: " + arg);
                    }
                }
                return options;
            }

            public static string HelpText()
            {
                return "Usage: PodexDataImporter.exe --data <pokemon.json> --source <pokeapi-csv-dir> [--report <path>] [--map <path>] [--preview-data <path>] [--missing-chinese <path>] [--chinese-overrides <path>] [--allow-english-fallback] [--require-source]";
            }
        }
    }

    public sealed class RootData
    {
        public Meta meta { get; set; }
        public List<NamedRef> games { get; set; }
        public List<NamedRef> levels { get; set; }
        public List<TypeRef> types { get; set; }
        public List<PokemonEntry> pokemon { get; set; }
        public List<EvolutionEntry> evolutions { get; set; }
        public List<LearnsetEntry> learnsets { get; set; }
        public List<MoveEntry> moves { get; set; }
        public List<AbilityEntry> abilities { get; set; }
        public List<ItemEntry> items { get; set; }
        public List<NatureEntry> natures { get; set; }
        public object typeChart { get; set; }
    }

    public sealed class Meta
    {
        public int count { get; set; }
        public int maxNationalDex { get; set; }
        public Counts counts { get; set; }
    }

    public sealed class Counts
    {
        public int pokemon { get; set; }
        public int moves { get; set; }
        public int abilities { get; set; }
        public int items { get; set; }
        public int natures { get; set; }
        public int types { get; set; }
        public int evolutions { get; set; }
        public int learnsets { get; set; }
    }

    public sealed class PokemonEntry
    {
        public int legacyId { get; set; }
        public int nationalDex { get; set; }
        public int generation { get; set; }
        public Dictionary<string, string> names { get; set; }
        public Dictionary<string, string> formNames { get; set; }
    }

    public sealed class TypeRef
    {
        public int id { get; set; }
        public Dictionary<string, string> names { get; set; }
    }

    public sealed class NamedRef
    {
        public int id { get; set; }
        public Dictionary<string, string> names { get; set; }
    }

    public sealed class EvolutionEntry
    {
        public int pokemonId { get; set; }
        public int familyId { get; set; }
        public int previousPokemonId { get; set; }
    }

    public sealed class LearnsetEntry
    {
        public int pokemonId { get; set; }
        public int gameId { get; set; }
        public int levelId { get; set; }
        public int moveId { get; set; }
    }

    public sealed class MoveEntry
    {
        public int id { get; set; }
        public int generation { get; set; }
        public Dictionary<string, string> names { get; set; }
    }

    public sealed class AbilityEntry
    {
        public int id { get; set; }
        public int generation { get; set; }
        public Dictionary<string, string> names { get; set; }
    }

    public sealed class ItemEntry
    {
        public int id { get; set; }
        public Dictionary<string, string> names { get; set; }
        public ItemFlags flags { get; set; }
        public List<int> generations { get; set; }
        public List<int> versionGroups { get; set; }
    }

    public sealed class ItemFlags
    {
        public bool inGen1 { get; set; }
        public bool inGen2 { get; set; }
        public bool inGen3 { get; set; }
        public bool inGen4 { get; set; }
        public bool inGen5 { get; set; }
        public bool inGen6 { get; set; }
        public bool inGen7 { get; set; }
    }

    public sealed class NatureEntry
    {
        public int id { get; set; }
    }
}
