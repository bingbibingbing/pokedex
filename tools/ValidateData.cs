using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PodexTools
{
    internal static class ValidateData
    {
        private const int MaxSamplesPerRule = 12;

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

                if (!File.Exists(options.DataPath))
                {
                    Console.Error.WriteLine("Data file not found: " + options.DataPath);
                    return 2;
                }

                RootData root = LoadRoot(options.DataPath);
                Normalize(root);

                Validator validator = new Validator(root, options.DataPath, options.ImageRoot);
                ValidationReport report = validator.Run();
                string text = report.ToText();

                Console.OutputEncoding = Encoding.UTF8;
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

                if (report.ErrorCount > 0) return 1;
                if (options.FailOnWarnings && report.WarningCount > 0) return 1;
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

        private sealed class Validator
        {
            private readonly RootData root;
            private readonly string dataPath;
            private readonly string imageRoot;
            private readonly ValidationReport report;

            private Dictionary<int, PokemonEntry> pokemonById;
            private Dictionary<int, MoveEntry> movesById;
            private Dictionary<int, AbilityEntry> abilitiesById;
            private Dictionary<int, ItemEntry> itemsById;
            private Dictionary<int, NamedRef> gamesById;
            private Dictionary<int, NamedRef> levelsById;
            private Dictionary<int, TypeRef> typesById;

            public Validator(RootData root, string dataPath, string imageRoot)
            {
                this.root = root;
                this.dataPath = Path.GetFullPath(dataPath);
                this.imageRoot = string.IsNullOrWhiteSpace(imageRoot) ? "" : Path.GetFullPath(imageRoot);
                this.report = new ValidationReport(this.dataPath, this.imageRoot);
            }

            public ValidationReport Run()
            {
                BuildIndexes();
                ValidateMeta();
                ValidateNamesAndBasicFields();
                ValidatePokemonReferences();
                ValidateMoveReferences();
                ValidateItemFields();
                ValidateEvolutions();
                ValidateLearnsets();
                ValidateImages();
                FillSummary();
                return report;
            }

            private void BuildIndexes()
            {
                pokemonById = BuildIndex(root.pokemon, delegate(PokemonEntry p) { return p.legacyId; }, "pokemon.legacyId");
                movesById = BuildIndex(root.moves, delegate(MoveEntry m) { return m.id; }, "moves.id");
                abilitiesById = BuildIndex(root.abilities, delegate(AbilityEntry a) { return a.id; }, "abilities.id");
                itemsById = BuildIndex(root.items, delegate(ItemEntry i) { return i.id; }, "items.id");
                gamesById = BuildIndex(root.games, delegate(NamedRef g) { return g.id; }, "games.id");
                levelsById = BuildIndex(root.levels, delegate(NamedRef l) { return l.id; }, "levels.id");
                typesById = BuildIndex(root.types, delegate(TypeRef t) { return t.id; }, "types.id");
            }

            private Dictionary<int, T> BuildIndex<T>(IEnumerable<T> rows, Func<T, int> idSelector, string label)
            {
                var index = new Dictionary<int, T>();
                foreach (T row in rows)
                {
                    int id = idSelector(row);
                    if (id <= 0)
                    {
                        report.AddError("invalid-id", label + " has non-positive id: " + id);
                        continue;
                    }
                    if (index.ContainsKey(id))
                    {
                        report.AddError("duplicate-id", label + " duplicate id: " + id);
                        continue;
                    }
                    index.Add(id, row);
                }
                return index;
            }

            private void ValidateMeta()
            {
                if (root.meta == null)
                {
                    report.AddWarning("meta-missing", "meta object is missing.");
                    return;
                }

                if (root.meta.count != 0 && root.meta.count != root.pokemon.Count)
                {
                    report.AddWarning("meta-count-mismatch", "meta.count=" + root.meta.count + ", pokemon.Count=" + root.pokemon.Count);
                }

                if (root.meta.maxNationalDex != 0)
                {
                    int maxNationalDex = root.pokemon.Count == 0 ? 0 : root.pokemon.Max(delegate(PokemonEntry p) { return p.nationalDex; });
                    if (root.meta.maxNationalDex != maxNationalDex)
                    {
                        report.AddWarning("meta-max-dex-mismatch", "meta.maxNationalDex=" + root.meta.maxNationalDex + ", actual=" + maxNationalDex);
                    }
                }

                if (root.meta.counts != null)
                {
                    CheckCount("counts.pokemon", root.meta.counts.pokemon, root.pokemon.Count);
                    CheckCount("counts.moves", root.meta.counts.moves, root.moves.Count);
                    CheckCount("counts.abilities", root.meta.counts.abilities, root.abilities.Count);
                    CheckCount("counts.items", root.meta.counts.items, root.items.Count);
                    CheckCount("counts.natures", root.meta.counts.natures, root.natures.Count);
                    CheckCount("counts.types", root.meta.counts.types, root.types.Count);
                    CheckCount("counts.evolutions", root.meta.counts.evolutions, root.evolutions.Count);
                    CheckCount("counts.learnsets", root.meta.counts.learnsets, root.learnsets.Count);
                }
            }

            private void CheckCount(string name, int metaCount, int actualCount)
            {
                if (metaCount != 0 && metaCount != actualCount)
                {
                    report.AddWarning("meta-count-mismatch", name + "=" + metaCount + ", actual=" + actualCount);
                }
            }

            private void ValidateNamesAndBasicFields()
            {
                foreach (PokemonEntry p in root.pokemon)
                {
                    if (!HasAnyName(p.names)) report.AddError("pokemon-name-missing", PokemonLabel(p) + " has no names.");
                    else if (!HasChineseName(p.names)) report.AddWarning("pokemon-zh-name-missing", PokemonLabel(p) + " has no zh-CN name.");

                    if (p.nationalDex <= 0) report.AddError("pokemon-national-dex-invalid", PokemonLabel(p) + " has invalid nationalDex: " + p.nationalDex);
                    if (p.generation <= 0) report.AddWarning("pokemon-generation-missing", PokemonLabel(p) + " has invalid generation: " + p.generation);
                    if (p.types == null || p.types.Count == 0) report.AddError("pokemon-types-missing", PokemonLabel(p) + " has no types.");
                    if (p.stats == null) report.AddError("pokemon-stats-missing", PokemonLabel(p) + " has no stats.");
                    else
                    {
                        if (p.stats.hp <= 0 || p.stats.attack <= 0 || p.stats.defense <= 0 || p.stats.specialAttack <= 0 || p.stats.specialDefense <= 0 || p.stats.speed <= 0)
                        {
                            report.AddWarning("pokemon-stats-suspicious", PokemonLabel(p) + " has non-positive stat value.");
                        }
                    }
                }

                foreach (MoveEntry move in root.moves)
                {
                    if (!HasAnyName(move.names)) report.AddError("move-name-missing", MoveLabel(move) + " has no names.");
                    else if (!HasChineseName(move.names)) report.AddWarning("move-zh-name-missing", MoveLabel(move) + " has no zh-CN name.");
                    if (move.generation <= 0) report.AddWarning("move-generation-missing", MoveLabel(move) + " has invalid generation: " + move.generation);
                    if (!HasAnyName(move.descriptions)) report.AddWarning("move-description-missing", MoveLabel(move) + " has no description.");
                }

                foreach (AbilityEntry ability in root.abilities)
                {
                    if (!HasAnyName(ability.names)) report.AddError("ability-name-missing", AbilityLabel(ability) + " has no names.");
                    else if (!HasChineseName(ability.names)) report.AddWarning("ability-zh-name-missing", AbilityLabel(ability) + " has no zh-CN name.");
                    if (ability.generation <= 0) report.AddWarning("ability-generation-missing", AbilityLabel(ability) + " has invalid generation: " + ability.generation);
                    if (!HasAnyName(ability.descriptions)) report.AddWarning("ability-description-missing", AbilityLabel(ability) + " has no description.");
                }

                foreach (ItemEntry item in root.items)
                {
                    if (!HasAnyName(item.names)) report.AddError("item-name-missing", ItemLabel(item) + " has no names.");
                    else if (!HasChineseName(item.names)) report.AddWarning("item-zh-name-missing", ItemLabel(item) + " has no zh-CN name.");
                    if (!HasAnyName(item.descriptions)) report.AddWarning("item-description-missing", ItemLabel(item) + " has no description.");
                }
            }

            private void ValidatePokemonReferences()
            {
                foreach (PokemonEntry p in root.pokemon)
                {
                    if (p.types != null)
                    {
                        foreach (TypeRef type in p.types)
                        {
                            if (type == null || !typesById.ContainsKey(type.id))
                            {
                                report.AddError("pokemon-type-broken", PokemonLabel(p) + " references missing type id: " + (type == null ? "<null>" : type.id.ToString()));
                            }
                        }
                    }

                    if (p.abilities != null)
                    {
                        CheckAbilityRef(p.abilities.primary, p, "primary");
                        CheckAbilityRef(p.abilities.secondary, p, "secondary");
                        CheckAbilityRef(p.abilities.hidden, p, "hidden");
                    }
                    else
                    {
                        report.AddWarning("pokemon-abilities-missing", PokemonLabel(p) + " has no ability block.");
                    }
                }
            }

            private void CheckAbilityRef(NamedRef ability, PokemonEntry pokemon, string slot)
            {
                if (ability == null || ability.id <= 0) return;
                if (!abilitiesById.ContainsKey(ability.id))
                {
                    report.AddError("pokemon-ability-broken", PokemonLabel(pokemon) + " " + slot + " ability references missing id: " + ability.id);
                }
            }

            private void ValidateMoveReferences()
            {
                foreach (MoveEntry move in root.moves)
                {
                    if (move.type == null || !typesById.ContainsKey(move.type.id))
                    {
                        report.AddError("move-type-broken", MoveLabel(move) + " references missing type id: " + (move.type == null ? "<null>" : move.type.id.ToString()));
                    }

                    int categoryId = move.category == null ? 0 : move.category.id;
                    if (categoryId <= 0)
                    {
                        report.AddWarning("move-category-missing", MoveLabel(move) + " has no category.");
                    }
                }
            }

            private void ValidateItemFields()
            {
                foreach (ItemEntry item in root.items)
                {
                    bool hasLegacyGeneration = item.flags != null && (
                        item.flags.inGen1 || item.flags.inGen2 || item.flags.inGen3 || item.flags.inGen4 ||
                        item.flags.inGen5 || item.flags.inGen6 || item.flags.inGen7);
                    bool hasExpandableGeneration = (item.generations != null && item.generations.Count > 0) ||
                        (item.versionGroups != null && item.versionGroups.Count > 0);

                    if (!hasLegacyGeneration && !hasExpandableGeneration)
                    {
                        report.AddWarning("item-generation-missing", ItemLabel(item) + " has no generation/version availability.");
                    }

                    if (item.generations != null)
                    {
                        foreach (int generation in item.generations)
                        {
                            if (generation <= 0)
                            {
                                report.AddWarning("item-generation-invalid", ItemLabel(item) + " has invalid generation: " + generation);
                            }
                        }
                    }

                    if (item.versionGroups != null)
                    {
                        foreach (int versionGroup in item.versionGroups)
                        {
                            if (versionGroup <= 0)
                            {
                                report.AddWarning("item-version-group-invalid", ItemLabel(item) + " has invalid version group: " + versionGroup);
                            }
                        }
                    }

                    if (item.bagId == null)
                    {
                        report.AddWarning("item-bag-missing", ItemLabel(item) + " has no bagId.");
                    }
                }
            }

            private void ValidateEvolutions()
            {
                var seenPokemon = new HashSet<int>();
                foreach (EvolutionEntry evolution in root.evolutions)
                {
                    if (!pokemonById.ContainsKey(evolution.pokemonId))
                    {
                        report.AddError("evolution-pokemon-broken", "Evolution row references missing pokemonId: " + evolution.pokemonId);
                    }
                    else
                    {
                        seenPokemon.Add(evolution.pokemonId);
                    }

                    if (evolution.previousPokemonId > 0 && !pokemonById.ContainsKey(evolution.previousPokemonId))
                    {
                        report.AddError("evolution-previous-broken", "Evolution " + evolution.pokemonId + " references missing previousPokemonId: " + evolution.previousPokemonId);
                    }

                    if (evolution.familyId <= 0) report.AddWarning("evolution-family-missing", "Evolution " + evolution.pokemonId + " has invalid familyId: " + evolution.familyId);
                }

                foreach (PokemonEntry p in root.pokemon)
                {
                    if (!seenPokemon.Contains(p.legacyId))
                    {
                        report.AddWarning("evolution-row-missing", PokemonLabel(p) + " has no evolution row.");
                    }
                }
            }

            private void ValidateLearnsets()
            {
                foreach (LearnsetEntry entry in root.learnsets)
                {
                    if (entry.pokemonId <= 0 || !pokemonById.ContainsKey(entry.pokemonId))
                    {
                        report.AddError("learnset-pokemon-broken", "Learnset references missing pokemonId: " + entry.pokemonId + ", moveId=" + entry.moveId + ", gameId=" + entry.gameId);
                    }
                    if (entry.moveId <= 0)
                    {
                        report.AddWarning("learnset-move-placeholder", "Learnset uses placeholder moveId: " + entry.moveId + ", pokemonId=" + entry.pokemonId + ", gameId=" + entry.gameId);
                    }
                    else if (!movesById.ContainsKey(entry.moveId))
                    {
                        report.AddError("learnset-move-broken", "Learnset references missing moveId: " + entry.moveId + ", pokemonId=" + entry.pokemonId + ", gameId=" + entry.gameId);
                    }
                    if (entry.gameId <= 0 || !gamesById.ContainsKey(entry.gameId))
                    {
                        report.AddError("learnset-game-broken", "Learnset references missing gameId: " + entry.gameId + ", pokemonId=" + entry.pokemonId + ", moveId=" + entry.moveId);
                    }
                    if (entry.levelId <= 0)
                    {
                        report.AddWarning("learnset-level-placeholder", "Learnset uses placeholder levelId: " + entry.levelId + ", pokemonId=" + entry.pokemonId + ", moveId=" + entry.moveId);
                    }
                    else if (!levelsById.ContainsKey(entry.levelId))
                    {
                        report.AddError("learnset-level-broken", "Learnset references missing levelId: " + entry.levelId + ", pokemonId=" + entry.pokemonId + ", moveId=" + entry.moveId);
                    }
                }
            }

            private void ValidateImages()
            {
                if (string.IsNullOrWhiteSpace(imageRoot))
                {
                    report.AddWarning("image-root-missing", "Image root was not provided.");
                    return;
                }

                if (!Directory.Exists(imageRoot))
                {
                    report.AddWarning("image-root-missing", "Image root does not exist: " + imageRoot);
                    return;
                }

                foreach (PokemonEntry p in root.pokemon)
                {
                    CheckImage("pokemon-small-image-missing", Path.Combine("pokemon", "small", p.legacyId + ".png"), PokemonLabel(p));
                    CheckImage("pokemon-big-image-missing", Path.Combine("pokemon", "big", p.legacyId + ".png"), PokemonLabel(p));
                }

                foreach (TypeRef type in root.types)
                {
                    CheckImage("type-image-missing", Path.Combine("types", "zhCN", type.id + ".png"), "type #" + type.id);
                }

                foreach (MoveEntry move in root.moves)
                {
                    int categoryId = move.category == null ? 0 : move.category.id;
                    if (categoryId > 0)
                    {
                        CheckImage("move-category-image-missing", Path.Combine("moves", "category", categoryId + ".png"), MoveLabel(move));
                    }

                    int rangeId = ObjectInt(move.rangeId, -1);
                    if (rangeId >= 0)
                    {
                        CheckImage("move-range-image-missing", Path.Combine("moves", "range", rangeId + ".png"), MoveLabel(move));
                    }
                }

                foreach (ItemEntry item in root.items)
                {
                    CheckImage("item-small-image-missing", Path.Combine("items", "small", item.id + ".png"), ItemLabel(item));
                    CheckImage("item-big-image-missing", Path.Combine("items", "big", item.id + ".png"), ItemLabel(item));
                }
            }

            private void CheckImage(string rule, string relativePath, string owner)
            {
                string full = Path.Combine(imageRoot, relativePath);
                if (!File.Exists(full))
                {
                    report.AddWarning(rule, owner + " missing image: " + relativePath);
                }
            }

            private void FillSummary()
            {
                report.Summary.Add("Pokemon rows", root.pokemon.Count.ToString());
                report.Summary.Add("Max national dex", (root.pokemon.Count == 0 ? 0 : root.pokemon.Max(delegate(PokemonEntry p) { return p.nationalDex; })).ToString());
                report.Summary.Add("Moves", root.moves.Count.ToString());
                report.Summary.Add("Abilities", root.abilities.Count.ToString());
                report.Summary.Add("Items", root.items.Count.ToString());
                report.Summary.Add("Games", root.games.Count.ToString());
                report.Summary.Add("Learnsets", root.learnsets.Count.ToString());
                report.Summary.Add("Pokemon generations", FormatCounts(root.pokemon.Select(delegate(PokemonEntry p) { return p.generation; })));
                report.Summary.Add("Move generations", FormatCounts(root.moves.Select(delegate(MoveEntry m) { return m.generation; })));
                report.Summary.Add("Ability generations", FormatCounts(root.abilities.Select(delegate(AbilityEntry a) { return a.generation; })));
                report.Summary.Add("Item generations", FormatCounts(root.items.SelectMany(ItemGenerationIds)));
            }

            private static string FormatCounts(IEnumerable<int> values)
            {
                var counts = values.GroupBy(delegate(int value) { return value; })
                    .OrderBy(delegate(IGrouping<int, int> group) { return group.Key; })
                    .Select(delegate(IGrouping<int, int> group) { return "Gen " + group.Key + ": " + group.Count(); });
                return string.Join(", ", counts.ToArray());
            }

            private static int ObjectInt(object value, int fallback)
            {
                if (value == null) return fallback;
                int parsed;
                if (int.TryParse(value.ToString(), out parsed)) return parsed;
                return fallback;
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

            private static bool HasAnyName(Dictionary<string, string> names)
            {
                if (names == null) return false;
                foreach (string value in names.Values)
                {
                    if (!string.IsNullOrWhiteSpace(value)) return true;
                }
                return false;
            }

            private static bool HasChineseName(Dictionary<string, string> names)
            {
                return names != null && names.ContainsKey("zhCN") && !string.IsNullOrWhiteSpace(names["zhCN"]);
            }

            private static string LocalName(Dictionary<string, string> names)
            {
                if (names == null) return "";
                string value;
                if (names.TryGetValue("zhCN", out value) && !string.IsNullOrWhiteSpace(value)) return value;
                if (names.TryGetValue("en", out value) && !string.IsNullOrWhiteSpace(value)) return value;
                foreach (string candidate in names.Values)
                {
                    if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
                }
                return "";
            }

            private static string PokemonLabel(PokemonEntry p)
            {
                return "#" + p.legacyId + " " + LocalName(p.names);
            }

            private static string MoveLabel(MoveEntry move)
            {
                return "#" + move.id + " " + LocalName(move.names);
            }

            private static string AbilityLabel(AbilityEntry ability)
            {
                return "#" + ability.id + " " + LocalName(ability.names);
            }

            private static string ItemLabel(ItemEntry item)
            {
                return "#" + item.id + " " + LocalName(item.names);
            }
        }

        private sealed class ValidationReport
        {
            private readonly Dictionary<string, FindingGroup> errors = new Dictionary<string, FindingGroup>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, FindingGroup> warnings = new Dictionary<string, FindingGroup>(StringComparer.OrdinalIgnoreCase);
            private readonly string dataPath;
            private readonly string imageRoot;

            public ValidationReport(string dataPath, string imageRoot)
            {
                this.dataPath = dataPath;
                this.imageRoot = imageRoot;
                Summary = new Dictionary<string, string>();
            }

            public Dictionary<string, string> Summary { get; private set; }

            public int ErrorCount
            {
                get { return errors.Values.Sum(delegate(FindingGroup group) { return group.Count; }); }
            }

            public int WarningCount
            {
                get { return warnings.Values.Sum(delegate(FindingGroup group) { return group.Count; }); }
            }

            public void AddError(string rule, string message)
            {
                Add(errors, rule, message);
            }

            public void AddWarning(string rule, string message)
            {
                Add(warnings, rule, message);
            }

            private static void Add(Dictionary<string, FindingGroup> groups, string rule, string message)
            {
                FindingGroup group;
                if (!groups.TryGetValue(rule, out group))
                {
                    group = new FindingGroup(rule);
                    groups.Add(rule, group);
                }
                group.Count++;
                if (group.Samples.Count < MaxSamplesPerRule)
                {
                    group.Samples.Add(message);
                }
            }

            public string ToText()
            {
                var builder = new StringBuilder();
                builder.AppendLine("Podex Data Validation Report");
                builder.AppendLine("============================");
                builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine("Data: " + dataPath);
                builder.AppendLine("Images: " + (string.IsNullOrWhiteSpace(imageRoot) ? "<not provided>" : imageRoot));
                builder.AppendLine();
                builder.AppendLine("Summary");
                builder.AppendLine("-------");
                foreach (KeyValuePair<string, string> entry in Summary)
                {
                    builder.AppendLine("- " + entry.Key + ": " + entry.Value);
                }
                builder.AppendLine("- Errors: " + ErrorCount);
                builder.AppendLine("- Warnings: " + WarningCount);
                builder.AppendLine();

                AppendGroups(builder, "Errors", errors);
                AppendGroups(builder, "Warnings", warnings);
                return builder.ToString();
            }

            private static void AppendGroups(StringBuilder builder, string title, Dictionary<string, FindingGroup> groups)
            {
                builder.AppendLine(title);
                builder.AppendLine(new string('-', title.Length));
                if (groups.Count == 0)
                {
                    builder.AppendLine("- None");
                    builder.AppendLine();
                    return;
                }

                foreach (FindingGroup group in groups.Values.OrderByDescending(delegate(FindingGroup g) { return g.Count; }).ThenBy(delegate(FindingGroup g) { return g.Rule; }))
                {
                    builder.AppendLine("- " + group.Rule + ": " + group.Count);
                    foreach (string sample in group.Samples)
                    {
                        builder.AppendLine("  sample: " + sample);
                    }
                }
                builder.AppendLine();
            }
        }

        private sealed class FindingGroup
        {
            public FindingGroup(string rule)
            {
                Rule = rule;
                Samples = new List<string>();
            }

            public string Rule { get; private set; }
            public int Count { get; set; }
            public List<string> Samples { get; private set; }
        }

        private sealed class Options
        {
            public string DataPath { get; private set; }
            public string ImageRoot { get; private set; }
            public string ReportPath { get; private set; }
            public bool FailOnWarnings { get; private set; }
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
                    else if (arg == "--images" && i + 1 < args.Length)
                    {
                        options.ImageRoot = args[++i];
                    }
                    else if (arg == "--report" && i + 1 < args.Length)
                    {
                        options.ReportPath = args[++i];
                    }
                    else if (arg == "--fail-on-warnings")
                    {
                        options.FailOnWarnings = true;
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
                return "Usage: DataValidator.exe --data <pokemon.json> [--images <images-root>] [--report <path>] [--fail-on-warnings]";
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
        public string id { get; set; }
        public int legacyId { get; set; }
        public int nationalDex { get; set; }
        public int generation { get; set; }
        public int formId { get; set; }
        public Dictionary<string, string> names { get; set; }
        public Dictionary<string, string> formNames { get; set; }
        public Dictionary<string, string> speciesNames { get; set; }
        public List<TypeRef> types { get; set; }
        public List<NamedRef> eggGroups { get; set; }
        public NamedRef genderRatio { get; set; }
        public AbilitySet abilities { get; set; }
        public Stats stats { get; set; }
        public Measurements measurements { get; set; }
        public object breeding { get; set; }
        public object captureRate { get; set; }
        public Dictionary<string, object> typeDefense { get; set; }
        public Dictionary<string, string> descriptions { get; set; }
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

    public sealed class AbilitySet
    {
        public NamedRef primary { get; set; }
        public NamedRef secondary { get; set; }
        public NamedRef hidden { get; set; }
    }

    public sealed class Stats
    {
        public int hp { get; set; }
        public int attack { get; set; }
        public int defense { get; set; }
        public int specialAttack { get; set; }
        public int specialDefense { get; set; }
        public int speed { get; set; }
        public int total { get; set; }
    }

    public sealed class Measurements
    {
        public object height { get; set; }
        public object weight { get; set; }
    }

    public sealed class EvolutionEntry
    {
        public int pokemonId { get; set; }
        public int familyId { get; set; }
        public int stageId { get; set; }
        public object stageMax { get; set; }
        public int previousPokemonId { get; set; }
        public NamedRef method { get; set; }
        public NamedRef stage { get; set; }
        public string conditionKind { get; set; }
        public object conditionValue { get; set; }
        public NamedRef condition { get; set; }
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
        public TypeRef type { get; set; }
        public NamedRef category { get; set; }
        public object power { get; set; }
        public object accuracy { get; set; }
        public object pp { get; set; }
        public object priority { get; set; }
        public object rangeId { get; set; }
        public Dictionary<string, string> descriptions { get; set; }
    }

    public sealed class AbilityEntry
    {
        public int id { get; set; }
        public int generation { get; set; }
        public Dictionary<string, string> names { get; set; }
        public NamedRef trigger { get; set; }
        public NamedRef target { get; set; }
        public NamedRef effectOn { get; set; }
        public Dictionary<string, string> descriptions { get; set; }
    }

    public sealed class ItemEntry
    {
        public int id { get; set; }
        public Dictionary<string, string> names { get; set; }
        public Dictionary<string, string> descriptions { get; set; }
        public object price { get; set; }
        public object bagId { get; set; }
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
        public bool inBattle { get; set; }
        public bool outBattle { get; set; }
        public bool oneTime { get; set; }
        public bool heldEffect { get; set; }
        public bool evolveRelated { get; set; }
    }

    public sealed class NatureEntry
    {
        public int id { get; set; }
        public Dictionary<string, string> names { get; set; }
    }
}
