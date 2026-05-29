using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

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
            "pokemon.csv",
            "pokemon_forms.csv",
            "pokemon_types.csv",
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
            public ChineseOverride(string entity, int sourceId, string name, string description)
            {
                Entity = entity;
                SourceId = sourceId;
                Name = name;
                Description = description;
            }

            public string Entity { get; private set; }
            public int SourceId { get; private set; }
            public string Name { get; private set; }
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

                List<object> moves = RawList(raw, "moves");
                List<object> abilities = RawList(raw, "abilities");
                List<object> items = RawList(raw, "items");

                int maxMoveId = MaxRawId(moves, "id");
                int maxAbilityId = MaxRawId(abilities, "id");
                int maxItemId = MaxRawId(items, "id");

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

                Dictionary<int, Dictionary<string, string>> moveNameMap = BuildLocalizedTextMap(moveNames, "move_id", "name");
                Dictionary<int, Dictionary<string, string>> moveEffectMap = BuildLocalizedTextMap(moveEffects, "move_effect_id", "short_effect");
                Dictionary<int, Dictionary<string, string>> moveDescriptionOverrides = new Dictionary<int, Dictionary<string, string>>();
                Dictionary<int, Dictionary<string, string>> abilityNameMap = BuildLocalizedTextMap(abilityNames, "ability_id", "name");
                Dictionary<int, Dictionary<string, string>> abilityTextMap = BuildLocalizedTextMap(abilityProse, "ability_id", "short_effect");
                Dictionary<int, Dictionary<string, string>> itemNameMap = BuildLocalizedTextMap(itemNames, "item_id", "name");
                Dictionary<int, Dictionary<string, string>> itemTextMap = BuildLocalizedTextMap(itemProse, "item_id", "short_effect");
                List<ChineseOverride> overrides = LoadChineseOverrides(options.ChineseOverridePath);
                ApplyChineseOverrides(overrides, moveNameMap, moveDescriptionOverrides, abilityNameMap, abilityTextMap, itemNameMap, itemTextMap);
                Dictionary<int, List<int>> itemGenerations = BuildItemGenerationMap(itemGameIndices);
                Dictionary<int, Dictionary<string, object>> typeRefs = BuildTypeRefs(raw);

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

                raw["moves"] = moves;
                raw["abilities"] = abilities;
                raw["items"] = items;
                UpdateMetaCounts(raw, moves.Count, abilities.Count, items.Count);

                string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.PreviewDataPath));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                File.WriteAllText(options.PreviewDataPath, serializer.Serialize(raw), Encoding.UTF8);

                report.PreviewSummary["Preview data"] = Path.GetFullPath(options.PreviewDataPath);
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
                    string description = CsvValue(row, "zhCN_description");
                    if (string.IsNullOrWhiteSpace(entity) || (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))) continue;
                    overrides.Add(new ChineseOverride(entity, sourceId, name, description));
                }
                return overrides;
            }

            private static void ApplyChineseOverrides(
                List<ChineseOverride> overrides,
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

            private static Dictionary<int, List<int>> BuildItemGenerationMap(CsvTable table)
            {
                var result = new Dictionary<int, List<int>>();
                foreach (Dictionary<string, string> row in table.Rows)
                {
                    int itemId = CsvInt(row, "item_id");
                    int generationId = CsvInt(row, "generation_id");
                    if (itemId <= 0 || generationId <= 0) continue;
                    List<int> generations;
                    if (!result.TryGetValue(itemId, out generations))
                    {
                        generations = new List<int>();
                        result.Add(itemId, generations);
                    }
                    if (!generations.Contains(generationId)) generations.Add(generationId);
                }
                foreach (List<int> generations in result.Values)
                {
                    generations.Sort();
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
                var names = new Dictionary<string, object>();
                names["en"] = en;
                names["zhCN"] = zhCN;
                names["zhTW"] = zhCN;
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

            private static void UpdateMetaCounts(Dictionary<string, object> root, int moves, int abilities, int items)
            {
                Dictionary<string, object> meta = root.ContainsKey("meta") ? root["meta"] as Dictionary<string, object> : null;
                if (meta == null) return;
                Dictionary<string, object> counts = meta.ContainsKey("counts") ? meta["counts"] as Dictionary<string, object> : null;
                if (counts == null) return;
                counts["moves"] = moves;
                counts["abilities"] = abilities;
                counts["items"] = items;
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
