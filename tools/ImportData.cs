using System;
using System.Collections.Generic;
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
                CsvTable moves = CsvTable.Load(Path.Combine(options.SourcePath, "moves.csv"));
                CsvTable abilities = CsvTable.Load(Path.Combine(options.SourcePath, "abilities.csv"));
                CsvTable items = CsvTable.Load(Path.Combine(options.SourcePath, "items.csv"));
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
                report.Candidates.Add("New abilities by id", CountRowsGreaterThan(abilities, "id", currentMaxAbility).ToString());
                report.Candidates.Add("New abilities by generation > 7", CountRowsGreaterThan(abilities, "generation_id", 7).ToString());
                report.Candidates.Add("New items by id", CountRowsGreaterThan(items, "id", currentMaxItem).ToString());
                report.Candidates.Add("Items available in generation > 7", CountDistinctRowsGreaterThan(itemGameIndices, "item_id", "generation_id", 7).ToString());
                report.Candidates.Add("Version groups after generation 7", CountRowsGreaterThan(versionGroups, "generation_id", 7).ToString());

                report.Notes.Add("This preflight only compares source coverage. It does not merge or write data yet.");
                report.Notes.Add("Next importer step should build stable local ID mapping for Pokemon forms before writing any JSON.");
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

        private sealed class Options
        {
            public string DataPath { get; private set; }
            public string SourcePath { get; private set; }
            public string ReportPath { get; private set; }
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
                return "Usage: PodexDataImporter.exe --data <pokemon.json> --source <pokeapi-csv-dir> [--report <path>] [--require-source]";
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
    }

    public sealed class AbilityEntry
    {
        public int id { get; set; }
        public int generation { get; set; }
    }

    public sealed class ItemEntry
    {
        public int id { get; set; }
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
