using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PodexDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        private readonly Panel navPanel = new Panel();
        private readonly Label titleLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly TextBox searchBox = new TextBox();
        private readonly ComboBox typeFilter = new ComboBox();
        private readonly ComboBox generationFilter = new ComboBox();
        private readonly ListView list = new ListView();
        private readonly Panel details = new Panel();
        private readonly ImageList pokemonSmallImages = new ImageList();
        private readonly ImageList itemSmallImages = new ImageList();
        private readonly ToolTip abilityToolTip = new ToolTip();
        private SplitContainer mainSplit;
        private TableLayoutPanel topFilters;

        private RootData root = new RootData();
        private string imageRoot = "";
        private string module = "pokemon";
        private bool rebuildingFilters;
        private int sortColumn = -1;
        private bool sortAscending = true;
        private bool suppressListSelectionChanged;
        private readonly Dictionary<int, ListViewItem> pokemonListItemsByLegacyId = new Dictionary<int, ListViewItem>();
        private readonly Dictionary<string, Image> cellImageCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, PokemonEntry> pokemonByLegacyId = new Dictionary<int, PokemonEntry>();
        private readonly Dictionary<int, MoveEntry> movesById = new Dictionary<int, MoveEntry>();
        private readonly Dictionary<int, AbilityEntry> abilitiesById = new Dictionary<int, AbilityEntry>();
        private readonly Dictionary<int, NamedRef> gamesById = new Dictionary<int, NamedRef>();
        private readonly Dictionary<int, NamedRef> levelsById = new Dictionary<int, NamedRef>();
        private readonly Dictionary<int, EvolutionEntry> evolutionByPokemonId = new Dictionary<int, EvolutionEntry>();
        private readonly Dictionary<int, List<EvolutionEntry>> evolutionsByFamilyId = new Dictionary<int, List<EvolutionEntry>>();
        private readonly Dictionary<int, List<LearnsetEntry>> learnsetsByPokemonId = new Dictionary<int, List<LearnsetEntry>>();
        private readonly Dictionary<int, List<LearnsetEntry>> learnsetsByMoveId = new Dictionary<int, List<LearnsetEntry>>();
        private readonly List<int> moveFilterMoveIds = new List<int>();
        private string moveFilterSearchText = "";
        private int pokemonDetailTabIndex;
        private int moveFilterCatalogSortColumn;
        private bool moveFilterCatalogSortAscending = true;
        private int moveFilterConditionSortColumn;
        private bool moveFilterConditionSortAscending = true;
        private int moveFilterGameId = -1;
        private string moveModuleFilterSearchText = "";
        private readonly MoveNumericFilter moveModulePowerFilter = new MoveNumericFilter();
        private readonly MoveNumericFilter moveModuleAccuracyFilter = new MoveNumericFilter();
        private readonly MoveNumericFilter moveModulePpFilter = new MoveNumericFilter();
        private readonly MoveNumericFilter moveModulePriorityFilter = new MoveNumericFilter();
        private string moveModuleMachineFilter;
        private int moveModuleTypeFilterId = -1;
        private int moveModuleCategoryFilterId = -1;
        private string moveModuleRangeFilter;
        private string abilityFilterSearchText = "";
        private int abilityFilterTriggerId = -1;
        private int abilityFilterTargetId = -1;
        private int abilityFilterEffectOnId = -1;
        private bool pokemonSmallImagesLoaded;
        private bool itemSmallImagesLoaded;

        public MainForm()
        {
            Text = "Podex Desktop";
            MinimumSize = new Size(1180, 740);
            Size = new Size(1320, 840);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(244, 234, 216);
            abilityToolTip.ShowAlways = true;
            abilityToolTip.InitialDelay = 200;
            abilityToolTip.ReshowDelay = 100;
            abilityToolTip.AutoPopDelay = 8000;
            BuildLayout();
            Load += delegate { LoadData(); };
            Shown += delegate { ApplyOriginalLikeSplitter(); };
        }

        private void BuildLayout()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(31, 111, 105),
                Padding = new Padding(18, 12, 18, 10)
            };

            titleLabel.Text = "Podex Desktop";
            titleLabel.AutoSize = true;
            titleLabel.ForeColor = Color.White;
            titleLabel.Font = new Font("Georgia", 25f, FontStyle.Bold);
            titleLabel.Location = new Point(18, 10);

            statusLabel.AutoSize = true;
            statusLabel.ForeColor = Color.FromArgb(231, 220, 195);
            statusLabel.Location = new Point(23, 56);
            statusLabel.Text = "Loading migrated data...";
            header.Controls.Add(titleLabel);
            header.Controls.Add(statusLabel);

            navPanel.Dock = DockStyle.Left;
            navPanel.Width = 190;
            navPanel.BackColor = Color.FromArgb(35, 45, 39);
            navPanel.Padding = new Padding(12);

            AddNavButton("宝可梦", "pokemon");
            AddNavButton("道具", "items");
            AddNavButton("特性", "abilities");
            AddNavButton("招式", "moves");
            AddNavButton("属性效果", "type-effect");
            AddNavButton("性格效果", "natures");

            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var filters = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 72,
                ColumnCount = 3,
                RowCount = 2
            };
            topFilters = filters;
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
            filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            AddFilterLabel(filters, "搜索", 0);
            AddFilterLabel(filters, "属性", 1);
            AddFilterLabel(filters, "世代", 2);

            searchBox.Dock = DockStyle.Fill;
            searchBox.Margin = new Padding(0, 0, 10, 0);
            searchBox.TextChanged += delegate { if (!rebuildingFilters) ApplyFilters(); };

            typeFilter.Dock = DockStyle.Fill;
            typeFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            typeFilter.Margin = new Padding(0, 0, 10, 0);
            typeFilter.SelectedIndexChanged += delegate { if (!rebuildingFilters) ApplyFilters(); };

            generationFilter.Dock = DockStyle.Fill;
            generationFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            generationFilter.SelectedIndexChanged += delegate { if (!rebuildingFilters) ApplyFilters(); };

            filters.Controls.Add(searchBox, 0, 1);
            filters.Controls.Add(typeFilter, 1, 1);
            filters.Controls.Add(generationFilter, 2, 1);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 640,
                BackColor = Color.FromArgb(244, 234, 216)
            };
            mainSplit = split;

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.GridLines = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.FromArgb(255, 250, 237);
            list.SelectedIndexChanged += delegate
            {
                if (!suppressListSelectionChanged && list.SelectedItems.Count > 0)
                {
                    ShowDetails(list.SelectedItems[0].Tag);
                }
            };
            list.ColumnClick += delegate(object sender, ColumnClickEventArgs e)
            {
                SortListByColumn(e.Column);
            };
            list.DrawColumnHeader += DrawListColumnHeader;
            list.DrawSubItem += DrawListSubItem;

            details.Dock = DockStyle.Fill;
            details.AutoScroll = true;
            details.Padding = new Padding(24);
            details.BackColor = Color.FromArgb(255, 250, 237);

            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(details);
            content.Controls.Add(split);
            content.Controls.Add(filters);

            Controls.Add(content);
            Controls.Add(navPanel);
            Controls.Add(header);
        }

        private void ApplyOriginalLikeSplitter()
        {
            if (mainSplit == null || mainSplit.Width <= 0) return;
            if (mainSplit.Panel1Collapsed || IsMatrixModule()) return;

            int total = Math.Max(0, mainSplit.Width - mainSplit.SplitterWidth);
            int panel1Min = module == "moves" ? 486 : 520;
            int panel2Min = module == "moves" ? 620 : 420;
            if (total < panel1Min + panel2Min)
            {
                panel2Min = Math.Max(300, total - panel1Min);
                panel1Min = Math.Max(260, total - panel2Min);
            }

            mainSplit.Panel1MinSize = panel1Min;
            mainSplit.Panel2MinSize = panel2Min;

            int target = module == "moves"
                ? Math.Min(500, Math.Max(panel1Min, total - 700))
                : Math.Min(640, Math.Max(panel1Min, mainSplit.Width - 560 - mainSplit.SplitterWidth));
            int maxTarget = mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth;
            if (target > maxTarget) target = maxTarget;
            if (target < mainSplit.Panel1MinSize) target = mainSplit.Panel1MinSize;
            if (target > 0 && target < mainSplit.Width) mainSplit.SplitterDistance = target;
        }

        private void AddNavButton(string text, string key)
        {
            var button = new Button
            {
                Text = text,
                Tag = key,
                Dock = DockStyle.Top,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(46, 60, 52),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { SelectModule((string)button.Tag); };
            navPanel.Controls.Add(button);
            button.BringToFront();
        }

        private void AddNavSeparator()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = navPanel.BackColor };
            navPanel.Controls.Add(panel);
            panel.BringToFront();
        }

        private static void AddFilterLabel(TableLayoutPanel panel, string text, int column)
        {
            panel.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(106, 91, 69),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            }, column, 0);
        }

        private void LoadData()
        {
            string dataPath = FindDataPath();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            root = serializer.Deserialize<RootData>(File.ReadAllText(dataPath, Encoding.UTF8));
            if (root.pokemon == null) root.pokemon = new List<PokemonEntry>();
            if (root.moves == null) root.moves = new List<MoveEntry>();
            if (root.abilities == null) root.abilities = new List<AbilityEntry>();
            if (root.items == null) root.items = new List<ItemEntry>();
            if (root.natures == null) root.natures = new List<NatureEntry>();
            if (root.types == null) root.types = new List<TypeRef>();
            if (root.games == null) root.games = new List<NamedRef>();
            if (root.levels == null) root.levels = new List<NamedRef>();
            if (root.evolutions == null) root.evolutions = new List<EvolutionEntry>();
            if (root.learnsets == null) root.learnsets = new List<LearnsetEntry>();
            IndexData();
            imageRoot = FindImageRoot();
            InitializeSmallImageLists();
            EnsurePokemonSmallImages();
            SelectModule("pokemon");
        }

        private void IndexData()
        {
            pokemonByLegacyId.Clear();
            movesById.Clear();
            abilitiesById.Clear();
            gamesById.Clear();
            levelsById.Clear();
            evolutionByPokemonId.Clear();
            evolutionsByFamilyId.Clear();
            learnsetsByPokemonId.Clear();
            learnsetsByMoveId.Clear();
            moveFilterGameId = -1;

            foreach (var p in root.pokemon)
            {
                if (!pokemonByLegacyId.ContainsKey(p.legacyId))
                {
                    pokemonByLegacyId.Add(p.legacyId, p);
                }
            }

            foreach (var move in root.moves)
            {
                if (!movesById.ContainsKey(move.id))
                {
                    movesById.Add(move.id, move);
                }
            }

            foreach (var ability in root.abilities)
            {
                if (!abilitiesById.ContainsKey(ability.id))
                {
                    abilitiesById.Add(ability.id, ability);
                }
            }

            foreach (var game in root.games)
            {
                if (!gamesById.ContainsKey(game.id))
                {
                    gamesById.Add(game.id, game);
                }
            }

            foreach (var level in root.levels)
            {
                if (!levelsById.ContainsKey(level.id))
                {
                    levelsById.Add(level.id, level);
                }
            }

            foreach (var evolution in root.evolutions)
            {
                evolutionByPokemonId[evolution.pokemonId] = evolution;
                List<EvolutionEntry> family;
                if (!evolutionsByFamilyId.TryGetValue(evolution.familyId, out family))
                {
                    family = new List<EvolutionEntry>();
                    evolutionsByFamilyId.Add(evolution.familyId, family);
                }
                family.Add(evolution);
            }

            foreach (var entry in root.learnsets)
            {
                AddIndexedLearnset(learnsetsByPokemonId, entry.pokemonId, entry);
                AddIndexedLearnset(learnsetsByMoveId, entry.moveId, entry);
                if (entry.gameId > moveFilterGameId) moveFilterGameId = entry.gameId;
            }

            foreach (var family in evolutionsByFamilyId.Values)
            {
                family.Sort(delegate(EvolutionEntry a, EvolutionEntry b)
                {
                    int stage = a.stageId.CompareTo(b.stageId);
                    return stage != 0 ? stage : a.pokemonId.CompareTo(b.pokemonId);
                });
            }
        }

        private static void AddIndexedLearnset(Dictionary<int, List<LearnsetEntry>> index, int key, LearnsetEntry entry)
        {
            List<LearnsetEntry> rows;
            if (!index.TryGetValue(key, out rows))
            {
                rows = new List<LearnsetEntry>();
                index.Add(key, rows);
            }
            rows.Add(entry);
        }

        private static string FindDataPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "data", "pokemon.json"),
                Path.Combine(baseDir, "..", "data", "pokemon.json"),
                Path.Combine(baseDir, "..", "..", "podex-next", "public", "data", "pokemon.json")
            };

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }

            throw new FileNotFoundException("pokemon.json not found. Run the legacy exporter first.");
        }

        private static string FindImageRoot()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "images"),
                Path.Combine(baseDir, "..", "images"),
                Path.Combine(baseDir, "..", "..", "assets", "images")
            };

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (Directory.Exists(full)) return full;
            }

            return "";
        }

        private void InitializeSmallImageLists()
        {
            pokemonSmallImages.Images.Clear();
            pokemonSmallImages.ImageSize = new Size(40, 32);
            pokemonSmallImages.ColorDepth = ColorDepth.Depth32Bit;
            itemSmallImages.Images.Clear();
            itemSmallImages.ImageSize = new Size(32, 32);
            itemSmallImages.ColorDepth = ColorDepth.Depth32Bit;
            pokemonSmallImagesLoaded = false;
            itemSmallImagesLoaded = false;
        }

        private void EnsurePokemonSmallImages()
        {
            if (pokemonSmallImagesLoaded) return;
            pokemonSmallImagesLoaded = true;
            if (string.IsNullOrWhiteSpace(imageRoot)) return;
            LoadImageList(pokemonSmallImages, Path.Combine(imageRoot, "pokemon", "small"));
        }

        private void EnsureItemSmallImages()
        {
            if (itemSmallImagesLoaded) return;
            itemSmallImagesLoaded = true;
            if (string.IsNullOrWhiteSpace(imageRoot)) return;
            LoadImageList(itemSmallImages, Path.Combine(imageRoot, "items", "small"));
        }

        private static void LoadImageList(ImageList target, string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*.png"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                if (target.Images.ContainsKey(key)) continue;
                using (Image image = Image.FromFile(file))
                {
                    target.Images.Add(key, new Bitmap(image));
                }
            }
        }

        private void SelectModule(string key)
        {
            module = key;
            sortColumn = -1;
            sortAscending = true;
            ConfigureModuleChrome();
            rebuildingFilters = true;
            searchBox.Clear();
            rebuildingFilters = false;
            ConfigureListColumns();
            BuildFilters();
            ApplyFilters();
            ApplyOriginalLikeSplitter();
        }

        private void ConfigureModuleChrome()
        {
            bool matrixModule = IsMatrixModule();
            if (topFilters != null) topFilters.Visible = !matrixModule;
            if (mainSplit != null)
            {
                mainSplit.Panel1Collapsed = matrixModule;
            }
            details.Padding = matrixModule ? new Padding(12) : new Padding(24);
            details.AutoScroll = false;
        }

        private bool IsMatrixModule()
        {
            return module == "type-effect" || module == "natures";
        }

        private void SortListByColumn(int column)
        {
            if (column < 0 || column >= list.Columns.Count) return;
            if (sortColumn == column)
            {
                sortAscending = !sortAscending;
            }
            else
            {
                sortColumn = column;
                sortAscending = true;
            }

            ApplyCurrentSort();
        }

        private void ApplyCurrentSort()
        {
            if (sortColumn < 0 || sortColumn >= list.Columns.Count)
            {
                list.ListViewItemSorter = null;
                return;
            }

            list.ListViewItemSorter = new ListViewColumnComparer(sortColumn, sortAscending);
            list.Sort();
        }

        private void DrawListColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void DrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var move = e.Item.Tag as MoveEntry;
            if (module != "moves" || move == null)
            {
                e.DrawDefault = true;
                return;
            }

            bool selected = e.Item.Selected;
            Color backColor = selected ? SystemColors.Highlight : MoveListCellBackColor(e.SubItem);
            Color foreColor = selected ? SystemColors.HighlightText : MoveListCellForeColor(e.SubItem, e.ColumnIndex);
            using (var brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            if (e.ColumnIndex == 7)
            {
                DrawCenteredCellImage(e.Graphics, e.Bounds, LoadCellImage(MoveRangeImagePath(move.rangeId)));
            }
            else
            {
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                Rectangle textBounds = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, textBounds, foreColor, flags);
            }
        }

        private static Color MoveListCellBackColor(ListViewItem.ListViewSubItem subItem)
        {
            return subItem.BackColor == Color.Empty ? Color.FromArgb(255, 250, 237) : subItem.BackColor;
        }

        private static Color MoveListCellForeColor(ListViewItem.ListViewSubItem subItem, int columnIndex)
        {
            if (columnIndex == 2 || columnIndex == 3) return Color.White;
            return subItem.ForeColor == Color.Empty ? Color.Black : subItem.ForeColor;
        }

        private static void DrawCenteredCellImage(Graphics graphics, Rectangle bounds, Image image)
        {
            if (image == null) return;
            int width = Math.Min(image.Width, Math.Max(4, bounds.Width - 6));
            int height = Math.Min(image.Height, Math.Max(4, bounds.Height - 4));
            int x = bounds.Left + (bounds.Width - width) / 2;
            int y = bounds.Top + (bounds.Height - height) / 2;
            graphics.DrawImage(image, x, y, width, height);
        }

        private void ConfigureListColumns()
        {
            list.BeginUpdate();
            list.Columns.Clear();
            if (module.StartsWith("pokemon"))
            {
                list.OwnerDraw = false;
                EnsurePokemonSmallImages();
                list.SmallImageList = pokemonSmallImages;
                titleLabel.Text = module == "pokemon-classic" ? "宝可梦 (经典版)" : "宝可梦";
                list.Columns.Add("#", 70);
                list.Columns.Add("名字", 104);
                list.Columns.Add("属性", 54);
                list.Columns.Add("属性", 54);
                list.Columns.Add("HP", 42);
                list.Columns.Add("攻击", 48);
                list.Columns.Add("防御", 48);
                list.Columns.Add("特攻", 48);
                list.Columns.Add("特防", 48);
                list.Columns.Add("速度", 48);
                list.Columns.Add("合计", 52);
            }
            else if (module == "moves")
            {
                list.OwnerDraw = true;
                list.SmallImageList = null;
                titleLabel.Text = "招式";
                list.Columns.Add("#", 36);
                list.Columns.Add("名字", 124);
                list.Columns.Add("属性", 50);
                list.Columns.Add("分类", 54);
                list.Columns.Add("威", 34);
                list.Columns.Add("命", 34);
                list.Columns.Add("PP", 32);
                list.Columns.Add("范围", 44);
                list.Columns.Add("优", 28);
            }
            else if (module == "abilities")
            {
                list.OwnerDraw = false;
                list.SmallImageList = null;
                titleLabel.Text = "特性";
                list.Columns.Add("#", 56);
                list.Columns.Add("名字", 138);
                list.Columns.Add("描述", 300);
            }
            else if (module == "items")
            {
                list.OwnerDraw = false;
                EnsureItemSmallImages();
                list.SmallImageList = itemSmallImages;
                titleLabel.Text = "道具";
                list.Columns.Add("#", 84);
                list.Columns.Add("名字", 150);
                list.Columns.Add("English", 150);
                list.Columns.Add("价格", 78);
                list.Columns.Add("背包", 72);
            }
            else if (module == "type-effect")
            {
                list.OwnerDraw = false;
                list.SmallImageList = null;
                titleLabel.Text = "属性效果";
                list.Columns.Add("#", 64);
                list.Columns.Add("攻击属性", 150);
                list.Columns.Add("English", 150);
            }
            else if (module == "natures")
            {
                list.OwnerDraw = false;
                list.SmallImageList = null;
                titleLabel.Text = "性格效果";
                list.Columns.Add("#", 64);
                list.Columns.Add("性格", 150);
                list.Columns.Add("English", 150);
                list.Columns.Add("提升", 96);
                list.Columns.Add("降低", 96);
            }
            else
            {
                list.OwnerDraw = false;
                list.SmallImageList = null;
                titleLabel.Text = GetModuleTitle(module);
                list.Columns.Add("功能", 220);
                list.Columns.Add("状态", 260);
            }
            list.EndUpdate();
        }

        private void BuildFilters()
        {
            rebuildingFilters = true;
            typeFilter.Items.Clear();
            generationFilter.Items.Clear();
            typeFilter.Items.Add(new FilterOption("", "全部"));
            generationFilter.Items.Add(new FilterOption("", "全部"));

            if (module == "pokemon" || module == "pokemon-classic" || module == "moves")
            {
                foreach (var type in root.types.OrderBy(t => t.id))
                {
                    typeFilter.Items.Add(new FilterOption(type.id.ToString(), LocalName(type.names)));
                }
            }

            IEnumerable<int> generations = Enumerable.Empty<int>();
            if (module.StartsWith("pokemon")) generations = root.pokemon.Select(p => p.generation);
            else if (module == "abilities") generations = root.abilities.Select(a => a.generation);
            else if (module == "items") generations = Enumerable.Range(1, 7);

            foreach (int gen in generations.Where(g => g > 0).Distinct().OrderBy(g => g))
            {
                generationFilter.Items.Add(new FilterOption(gen.ToString(), "第" + gen + "世代"));
            }

            typeFilter.Enabled = typeFilter.Items.Count > 1;
            generationFilter.Enabled = generationFilter.Items.Count > 1;
            typeFilter.SelectedIndex = 0;
            generationFilter.SelectedIndex = 0;
            rebuildingFilters = false;
        }

        private void ApplyFilters()
        {
            string query = (searchBox.Text ?? "").Trim().ToLowerInvariant();
            string typeId = SelectedValue(typeFilter);
            string generation = SelectedValue(generationFilter);
            if (module == "moves")
            {
                // The legacy move page uses the embedded filter panel, not the global generation selector.
                generation = "";
            }

            if (IsMatrixModule())
            {
                list.BeginUpdate();
                list.ListViewItemSorter = null;
                list.Items.Clear();
                pokemonListItemsByLegacyId.Clear();
                list.EndUpdate();
                ShowMatrixModule();
                UpdateModuleTitleWithCount();
                statusLabel.Text = BuildStatus();
                return;
            }

            list.BeginUpdate();
            list.ListViewItemSorter = null;
            list.Items.Clear();
            pokemonListItemsByLegacyId.Clear();

            if (module.StartsWith("pokemon"))
            {
                foreach (var p in root.pokemon.Where(p =>
                    Match(query, PokemonSearchText(p)) &&
                    (typeId.Length == 0 || (p.types != null && p.types.Any(t => t.id.ToString() == typeId))) &&
                    (generation.Length == 0 || p.generation.ToString() == generation) &&
                    PokemonMatchesMoveFilter(p)))
                {
                    AddPokemonRow(p);
                }
            }
            else if (module == "moves")
            {
                foreach (var m in root.moves.Where(m =>
                    Match(query, MoveSearchText(m)) &&
                    Match(moveModuleFilterSearchText.Trim().ToLowerInvariant(), MoveSearchText(m)) &&
                    (typeId.Length == 0 || (m.type != null && m.type.id.ToString() == typeId)) &&
                    (generation.Length == 0 || m.generation.ToString() == generation) &&
                    MoveMatchesDetailFilters(m)))
                {
                    AddMoveRow(m);
                }
            }
            else if (module == "abilities")
            {
                foreach (var a in root.abilities.Where(a =>
                    Match(query, AbilitySearchText(a)) &&
                    Match(abilityFilterSearchText.Trim().ToLowerInvariant(), AbilitySearchText(a)) &&
                    (generation.Length == 0 || a.generation.ToString() == generation) &&
                    AbilityMatchesDetailFilters(a)))
                {
                    AddAbilityRow(a);
                }
            }
            else if (module == "items")
            {
                foreach (var item in root.items.Where(i =>
                    Match(query, ItemSearchText(i)) &&
                    (generation.Length == 0 || ItemInGeneration(i, int.Parse(generation)))))
                {
                    AddItemRow(item);
                }
            }
            else if (module == "type-effect")
            {
                foreach (var type in root.types.Where(t => Match(query, Values(t.names))))
                {
                    AddTypeRow(type);
                }
            }
            else if (module == "natures")
            {
                foreach (var nature in root.natures.Where(n => Match(query, Values(n.names))))
                {
                    AddNatureRow(nature);
                }
            }
            else
            {
                AddPlaceholderRow(module);
            }

            ApplyCurrentSort();
            list.EndUpdate();
            UpdateModuleTitleWithCount();
            if (list.Items.Count > 0)
            {
                list.Items[0].Selected = true;
                list.Select();
            }
            else
            {
                details.Controls.Clear();
                if (module == "moves")
                {
                    details.Padding = new Padding(6);
                    details.AutoScroll = false;
                    details.Controls.Add(MakeMoveDetailPanel(null));
                }
                else
                {
                    details.Controls.Add(MakeBodyLabel("没有匹配条目。"));
                }
            }
            statusLabel.Text = BuildStatus();
        }

        private void UpdateModuleTitleWithCount()
        {
            string title = CurrentModuleTitle();
            if (IsMatrixModule())
            {
                titleLabel.Text = title;
                Text = title;
                return;
            }
            int total = CurrentModuleTotal();
            string text = total > 0
                ? string.Format("{0} ({1:N0} / {2:N0})", title, list.Items.Count, total)
                : title;
            titleLabel.Text = text;
            Text = text;
        }

        private void ShowMatrixModule()
        {
            details.SuspendLayout();
            details.Controls.Clear();
            details.Padding = new Padding(12);
            details.AutoScroll = false;
            if (module == "type-effect")
            {
                details.Controls.Add(MakeTypeEffectMatrix());
            }
            else if (module == "natures")
            {
                details.Controls.Add(MakeNatureEffectMatrix());
            }
            details.ResumeLayout();
        }

        private string CurrentModuleTitle()
        {
            if (module == "pokemon-classic") return "宝可梦 (经典版)";
            if (module == "pokemon") return "宝可梦";
            if (module == "moves") return "招式";
            if (module == "abilities") return "特性";
            if (module == "items") return "道具";
            if (module == "type-effect") return "属性效果";
            if (module == "natures") return "性格效果";
            return GetModuleTitle(module);
        }

        private int CurrentModuleTotal()
        {
            if (module.StartsWith("pokemon")) return root.pokemon.Count;
            if (module == "moves") return root.moves.Count;
            if (module == "abilities") return root.abilities.Count;
            if (module == "items") return root.items.Count;
            if (module == "type-effect") return root.types.Count;
            if (module == "natures") return root.natures.Count;
            return 0;
        }

        private bool AbilityMatchesDetailFilters(AbilityEntry ability)
        {
            if (abilityFilterTriggerId > 0 && (ability.trigger == null || ability.trigger.id != abilityFilterTriggerId)) return false;
            if (abilityFilterTargetId > 0 && (ability.target == null || ability.target.id != abilityFilterTargetId)) return false;
            if (abilityFilterEffectOnId > 0 && (ability.effectOn == null || ability.effectOn.id != abilityFilterEffectOnId)) return false;
            return true;
        }

        private bool MoveMatchesDetailFilters(MoveEntry move)
        {
            if (!MoveMatchesNumberFilter(move.power, moveModulePowerFilter, false)) return false;
            if (!MoveMatchesNumberFilter(move.accuracy, moveModuleAccuracyFilter, false)) return false;
            if (!MoveMatchesNumberFilter(move.pp, moveModulePpFilter, false)) return false;
            if (!MoveMatchesNumberFilter(move.priority, moveModulePriorityFilter, true)) return false;
            if (moveModuleTypeFilterId > 0 && (move.type == null || move.type.id != moveModuleTypeFilterId)) return false;
            if (moveModuleCategoryFilterId > 0 && (move.category == null || move.category.id != moveModuleCategoryFilterId)) return false;
            if (moveModuleRangeFilter != null && MoveValue(move.rangeId) != moveModuleRangeFilter) return false;
            if (moveModuleMachineFilter != null && !MoveMatchesMachineFilter(move)) return false;
            return true;
        }

        private static bool MoveMatchesNumberFilter(object value, MoveNumericFilter filter, bool preserveMinusOne)
        {
            if (filter == null || !filter.Enabled) return true;

            string filterText = string.IsNullOrWhiteSpace(filter.ValueText) ? filter.Value.ToString("0") : filter.ValueText;
            string moveText = MoveDisplayValue(value, preserveMinusOne);
            if (filter.Operator == "!=")
            {
                if (filterText == "--") return moveText != "--";

                decimal moveNumberForNotEquals;
                decimal filterNumberForNotEquals;
                if (!decimal.TryParse(filterText.Trim().TrimStart('#').Replace(",", ""), out filterNumberForNotEquals)) return true;
                if (!TryMoveFilterNumber(value, preserveMinusOne, out moveNumberForNotEquals)) return true;
                return moveNumberForNotEquals.CompareTo(filterNumberForNotEquals) != 0;
            }

            if (filterText == "--") return filter.Operator == "=" && moveText == "--";

            decimal number;
            if (!TryMoveFilterNumber(value, preserveMinusOne, out number)) return false;

            decimal filterNumber;
            if (!decimal.TryParse(filterText.Trim().TrimStart('#').Replace(",", ""), out filterNumber)) return false;

            int comparison = number.CompareTo(filterNumber);
            switch (filter.Operator)
            {
                case ">": return comparison > 0;
                case "<": return comparison < 0;
                case ">=": return comparison >= 0;
                case "<=": return comparison <= 0;
                default: return comparison == 0;
            }
        }

        private static bool TryMoveFilterNumber(object value, bool preserveMinusOne, out decimal number)
        {
            number = 0;
            string text = MoveDisplayValue(value, preserveMinusOne);
            if (string.IsNullOrWhiteSpace(text) || text == "--" || text == "-") return false;
            return decimal.TryParse(text.Trim().TrimStart('#').Replace(",", ""), out number);
        }

        private static string MoveDisplayValue(object value, bool preserveMinusOne)
        {
            return preserveMinusOne ? ValueOrDash(value) : MoveValue(value);
        }

        private bool MoveMatchesMachineFilter(MoveEntry move)
        {
            List<LearnsetEntry> rows;
            if (!learnsetsByMoveId.TryGetValue(move.id, out rows)) return false;

            bool hasMachine = false;
            bool hasHiddenMachine = false;
            foreach (var row in rows)
            {
                if (IsMoveMachineLevel(row.levelId)) hasMachine = true;
                if (IsHiddenMachineLevel(row.levelId)) hasHiddenMachine = true;
            }
            if (moveModuleMachineFilter == "招式") return hasMachine;
            if (moveModuleMachineFilter == "秘传") return hasHiddenMachine;
            return !hasMachine && !hasHiddenMachine;
        }

        private static bool IsMoveMachineLevel(int levelId)
        {
            return levelId >= 101 && levelId <= 200;
        }

        private static bool IsHiddenMachineLevel(int levelId)
        {
            return levelId >= 201 && levelId <= 250;
        }

        private string BuildStatus()
        {
            if (root.meta != null && root.meta.counts != null)
            {
                return string.Format("宝可梦 {0} / 招式 {1} / 特性 {2} / 道具 {3} / 性格 {4}",
                    root.meta.counts.pokemon,
                    root.meta.counts.moves,
                    root.meta.counts.abilities,
                    root.meta.counts.items,
                    root.meta.counts.natures);
            }
            return "Loaded migrated legacy data.";
        }

        private bool PokemonMatchesMoveFilter(PokemonEntry p)
        {
            if (moveFilterMoveIds.Count == 0) return true;

            int gameId = CurrentMoveFilterGameId();
            List<LearnsetEntry> rows;
            if (!learnsetsByPokemonId.TryGetValue(p.legacyId, out rows)) return false;

            foreach (int moveId in moveFilterMoveIds)
            {
                bool found = false;
                foreach (var row in rows)
                {
                    if (row.gameId == gameId && row.moveId == moveId)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }

            return true;
        }

        private int CurrentMoveFilterGameId()
        {
            if (moveFilterGameId > 0) return moveFilterGameId;
            int gameId = -1;
            foreach (var row in root.learnsets)
            {
                if (row.gameId > gameId) gameId = row.gameId;
            }
            moveFilterGameId = gameId;
            return gameId;
        }

        private void AddPokemonRow(PokemonEntry p)
        {
            var item = new ListViewItem(p.nationalDex.ToString());
            item.ImageKey = p.legacyId.ToString();
            item.SubItems.Add(LocalName(p.names));
            AddTypeSubItem(item, TypeAt(p.types, 0));
            AddTypeSubItem(item, TypeAt(p.types, 1));
            item.SubItems.Add(p.stats == null ? "" : p.stats.hp.ToString());
            item.SubItems.Add(p.stats == null ? "" : p.stats.attack.ToString());
            item.SubItems.Add(p.stats == null ? "" : p.stats.defense.ToString());
            item.SubItems.Add(p.stats == null ? "" : p.stats.specialAttack.ToString());
            item.SubItems.Add(p.stats == null ? "" : p.stats.specialDefense.ToString());
            item.SubItems.Add(p.stats == null ? "" : p.stats.speed.ToString());
            item.SubItems.Add(p.stats == null ? "" : p.stats.total.ToString());
            item.Tag = p;
            list.Items.Add(item);
            pokemonListItemsByLegacyId[p.legacyId] = item;
        }

        private void AddMoveRow(MoveEntry m)
        {
            var item = new ListViewItem(m.id.ToString());
            item.SubItems.Add(LocalName(m.names));
            AddTypeSubItem(item, m.type);
            AddCategorySubItem(item, m.category);
            item.SubItems.Add(MoveValue(m.power));
            item.SubItems.Add(MoveValue(m.accuracy));
            item.SubItems.Add(MoveValue(m.pp));
            item.SubItems.Add(MoveValue(m.rangeId));
            item.SubItems.Add(ValueOrDash(m.priority));
            item.Tag = m;
            list.Items.Add(item);
        }

        private void AddAbilityRow(AbilityEntry a)
        {
            var item = new ListViewItem(a.id.ToString());
            item.SubItems.Add(LocalName(a.names));
            item.SubItems.Add(LocalName(a.descriptions));
            item.Tag = a;
            list.Items.Add(item);
        }

        private void AddItemRow(ItemEntry i)
        {
            var item = new ListViewItem(i.id.ToString());
            item.ImageKey = i.id.ToString();
            item.SubItems.Add(LocalName(i.names));
            item.SubItems.Add(EnglishName(i.names));
            item.SubItems.Add(ValueOrDash(i.price));
            item.SubItems.Add(ValueOrDash(i.bagId));
            item.Tag = i;
            list.Items.Add(item);
        }

        private void AddTypeRow(TypeRef type)
        {
            var item = new ListViewItem(type.id.ToString());
            item.SubItems.Add(LocalName(type.names));
            item.SubItems.Add(EnglishName(type.names));
            item.Tag = type;
            list.Items.Add(item);
        }

        private void AddNatureRow(NatureEntry n)
        {
            var item = new ListViewItem(n.id.ToString());
            item.SubItems.Add(LocalName(n.names));
            item.SubItems.Add(EnglishName(n.names));
            item.SubItems.Add(ModifierName(n.modifiers, true));
            item.SubItems.Add(ModifierName(n.modifiers, false));
            item.Tag = n;
            list.Items.Add(item);
        }

        private void AddPlaceholderRow(string key)
        {
            var item = new ListViewItem(GetModuleTitle(key));
            item.SubItems.Add("已接入原生桌面计算面板。");
            item.Tag = key;
            list.Items.Add(item);
        }

        private void ShowDetails(object tag)
        {
            details.SuspendLayout();
            details.Controls.Clear();
            details.Padding = tag is MoveEntry ? new Padding(6) : new Padding(24);
            details.AutoScroll = !(tag is PokemonEntry || tag is MoveEntry);
            if (tag is PokemonEntry) ShowPokemon((PokemonEntry)tag);
            else if (tag is MoveEntry) ShowMove((MoveEntry)tag);
            else if (tag is AbilityEntry) ShowAbility((AbilityEntry)tag);
            else if (tag is ItemEntry) ShowItem((ItemEntry)tag);
            else if (tag is TypeRef) ShowTypeEffect((TypeRef)tag);
            else if (tag is NatureEntry) ShowNature((NatureEntry)tag);
            else ShowPlaceholder(tag == null ? "" : tag.ToString());
            details.ResumeLayout();
        }

        private FlowLayoutPanel StartDetail(string heading, string subheading)
        {
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Width = DetailContentWidth()
            };
            stack.Controls.Add(MakeTitle(heading));
            stack.Controls.Add(MakeMutedLabel(subheading));
            details.Controls.Add(stack);
            return stack;
        }

        private int DetailContentWidth()
        {
            return Math.Max(360, details.ClientSize.Width - details.Padding.Horizontal - 12);
        }

        private void ShowPokemon(PokemonEntry p)
        {
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            var infoPage = BuildPokemonInfoTab(p);
            infoPage.Tag = "built";
            tabs.TabPages.Add(infoPage);
            tabs.TabPages.Add(MakeLazyPokemonTab("详细信息"));
            tabs.TabPages.Add(MakeLazyPokemonTab("筛选（按招式）"));
            if (pokemonDetailTabIndex >= 0 && pokemonDetailTabIndex < tabs.TabPages.Count)
            {
                tabs.SelectedIndex = pokemonDetailTabIndex;
            }
            EnsurePokemonTabBuilt(tabs, p);
            tabs.SelectedIndexChanged += delegate
            {
                pokemonDetailTabIndex = tabs.SelectedIndex;
                EnsurePokemonTabBuilt(tabs, p);
            };
            details.Controls.Add(tabs);
        }

        private static TabPage MakeLazyPokemonTab(string title)
        {
            var page = MakeTabPage(title);
            page.Tag = "lazy";
            return page;
        }

        private void EnsurePokemonTabBuilt(TabControl tabs, PokemonEntry p)
        {
            if (tabs.SelectedIndex < 0 || tabs.SelectedIndex >= tabs.TabPages.Count) return;
            TabPage page = tabs.TabPages[tabs.SelectedIndex];
            if ((page.Tag as string) == "built") return;

            TabPage built = tabs.SelectedIndex == 1 ? BuildPokemonFilterTab(p) : BuildPokemonMoveFilterTab(p);
            page.SuspendLayout();
            page.Controls.Clear();
            page.Padding = built.Padding;
            page.BackColor = built.BackColor;
            while (built.Controls.Count > 0)
            {
                Control control = built.Controls[0];
                built.Controls.RemoveAt(0);
                page.Controls.Add(control);
            }
            page.Tag = "built";
            page.ResumeLayout();
        }

        private TabPage BuildPokemonInfoTab(PokemonEntry p)
        {
            var page = MakeTabPage("信息");
            page.Padding = new Padding(4);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.FromArgb(255, 250, 237)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            page.Controls.Add(layout);
            layout.Controls.Add(MakeLegacyInfoFrame(MakeLegacyDescriptionSection(p), new Padding(0, 0, 0, 4)), 0, 0);
            layout.Controls.Add(MakeLegacyInfoFrame(MakeLegacyAbilityDefenseSection(p), new Padding(0, 0, 0, 4)), 0, 1);
            layout.Controls.Add(MakeLegacyInfoFrame(MakeLegacyEvolutionGrid(p), new Padding(0, 0, 0, 4)), 0, 2);
            layout.Controls.Add(MakeLegacyInfoFrame(MakeLegacyMoveSection(p, true), new Padding(0)), 0, 3);
            return page;
        }

        private Control MakeLegacyInfoFrame(Control content, Padding margin)
        {
            var frame = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = margin,
                Padding = new Padding(4),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(255, 250, 237)
            };
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0);
            frame.Controls.Add(content);
            return frame;
        }

        private Control MakeLegacyDescriptionSection(PokemonEntry p)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(255, 250, 237)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.White,
                Margin = new Padding(8, 6, 8, 6)
            };
            string imagePath = PokemonImagePath(p.legacyId, true);
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                picture.Image = Image.FromFile(imagePath);
            }

            var description = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(255, 250, 237),
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 10f),
                Text = string.IsNullOrWhiteSpace(LocalName(p.descriptions)) ? "暂无描述。" : LocalName(p.descriptions),
                Margin = new Padding(4, 8, 4, 4)
            };

            table.Controls.Add(picture, 0, 0);
            table.Controls.Add(description, 1, 0);
            return table;
        }

        private Control MakeLegacyAbilityDefenseSection(PokemonEntry p)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(255, 250, 237)
            };

            var abilityBar = MakeAbilityBar(p);
            abilityBar.Dock = DockStyle.Top;
            abilityBar.Height = 31;
            abilityBar.Margin = new Padding(0);

            var defenseLine = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(255, 250, 237),
                Margin = new Padding(0)
            };
            defenseLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            defenseLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            defenseLine.Controls.Add(new Label
            {
                Text = "防御时",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0, 4, 0, 0)
            }, 0, 0);
            defenseLine.Controls.Add(MakeDefenseMatrix(p), 1, 0);

            panel.Controls.Add(defenseLine);
            panel.Controls.Add(abilityBar);
            return panel;
        }

        private TabPage BuildPokemonFilterTab(PokemonEntry p)
        {
            var page = MakeTabPage("详细信息");
            page.Padding = new Padding(4);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(255, 250, 237)
            };
            scroll.HorizontalScroll.Enabled = false;
            scroll.HorizontalScroll.Visible = false;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Color.FromArgb(255, 250, 237),
                Margin = new Padding(0)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.Controls.Add(MakeDetailedInfoSummary(p), 0, 0);
            content.Controls.Add(MakeSectionTitle("种族值"), 0, 1);
            content.Controls.Add(MakeStatsPanel(p.stats), 0, 2);

            scroll.Controls.Add(content);
            scroll.Resize += delegate
            {
                content.Width = Math.Max(260, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            };
            page.Controls.Add(scroll);
            return page;
        }

        private Control MakeDetailedInfoSummary(PokemonEntry p)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 4, 0, 14),
                Padding = new Padding(8),
                BackColor = Color.FromArgb(244, 234, 216)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var imageStack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(244, 234, 216),
                Margin = new Padding(0, 0, 10, 0)
            };

            var picture = new PictureBox
            {
                Width = 96,
                Height = 96,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 8)
            };
            string imagePath = PokemonImagePath(p.legacyId, true);
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                using (Image image = Image.FromFile(imagePath))
                {
                    picture.Image = new Bitmap(image);
                }
            }
            imageStack.Controls.Add(picture);

            var facts = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.FromArgb(244, 234, 216),
                Margin = new Padding(0)
            };
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            AddDetailInfoRow(facts, row++, "名字", LocalName(p.names));
            AddDetailInfoRow(facts, row++, "样子", LocalName(p.formNames));
            AddDetailInfoRow(facts, row++, "分类", LocalName(p.speciesNames));
            AddDetailInfoControlRow(facts, row++, "属性", MakeTypeBadgeRow(p.types));
            AddDetailInfoControlRow(facts, row++, "弱点", MakeDefenseTypeBadgeRow(p, true));
            AddDetailInfoControlRow(facts, row++, "抵抗", MakeDefenseTypeBadgeRow(p, false));
            AddDetailInfoRow(facts, row++, "性别比", p.genderRatio == null ? "--" : LocalName(p.genderRatio.names));
            AddDetailInfoRow(facts, row++, "身高", p.measurements == null ? "--" : ValueOrDash(p.measurements.heightMetric) + " m");
            AddDetailInfoRow(facts, row++, "体重", p.measurements == null ? "--" : ValueOrDash(p.measurements.weightMetric) + " kg");
            AddDetailInfoRow(facts, row++, "捕获度", ValueOrDash(p.captureRate));
            AddDetailInfoRow(facts, row++, "孵化周期", p.breeding == null ? "--" : ValueOrDash(p.breeding.hatchCycles));
            AddDetailInfoRow(facts, row++, "蛋群", RefListText(p.eggGroups));
            AddDetailInfoRow(facts, row++, "特性", AbilityText(p.abilities));

            table.Controls.Add(imageStack, 0, 0);
            table.Controls.Add(facts, 1, 0);
            return table;
        }

        private static void AddDetailInfoRow(TableLayoutPanel table, int row, string label, string value)
        {
            AddDetailInfoControlRow(table, row, label, new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "--" : value,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(0, 0, 180),
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            });
        }

        private static void AddDetailInfoControlRow(TableLayoutPanel table, int row, string label, Control valueControl)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            table.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, row);
            valueControl.Dock = DockStyle.Fill;
            valueControl.Margin = new Padding(0);
            table.Controls.Add(valueControl, 1, row);
        }

        private static Control MakeTypeBadgeRow(List<TypeRef> types)
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(244, 234, 216),
                Margin = new Padding(0)
            };

            if (types == null || types.Count == 0)
            {
                panel.Controls.Add(new Label { Text = "--", AutoSize = true, Margin = new Padding(0, 3, 0, 0) });
                return panel;
            }

            foreach (var type in types)
            {
                panel.Controls.Add(MakeTypeBadgeLabel(type, 44, 20, new Padding(0, 1, 4, 0)));
            }
            return panel;
        }

        private Control MakeDefenseTypeBadgeRow(PokemonEntry p, bool weaknesses)
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(244, 234, 216),
                Margin = new Padding(0)
            };

            foreach (var type in root.types.OrderBy(t => t.id))
            {
                double multiplier = ParseMultiplier(GetDefenseMultiplierText(p, type.id));
                if ((weaknesses && multiplier <= 1) || (!weaknesses && multiplier >= 1)) continue;
                panel.Controls.Add(MakeCompactTypeBadge(type));
            }

            if (panel.Controls.Count == 0)
            {
                panel.Controls.Add(new Label { Text = "--", AutoSize = true, Margin = new Padding(0, 3, 0, 0) });
            }
            return panel;
        }

        private static Label MakeCompactTypeBadge(TypeRef type)
        {
            return MakeTypeBadgeLabel(type, 38, 20, new Padding(0, 1, 4, 0));
        }

        private TabPage BuildPokemonMoveFilterTab(PokemonEntry p)
        {
            var page = MakeTabPage("筛选（按招式）");
            page.Padding = new Padding(4);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Color.FromArgb(255, 250, 237)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            page.Controls.Add(layout);

            var catalogLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(2, 0, 0, 0)
            };

            var searchPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 3),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            searchPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            searchPanel.Controls.Add(new Label
            {
                Text = "搜索",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(40, 79, 145),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            }, 0, 0);
            var moveSearchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 2, 0, 2),
                Text = moveFilterSearchText
            };
            searchPanel.Controls.Add(moveSearchBox, 1, 0);
            searchPanel.Controls.Add(new Label
            {
                Text = "版本",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(8, 4, 4, 0)
            }, 2, 0);
            var moveGameFilter = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 0, 2)
            };
            PopulateMoveFilterGameFilter(moveGameFilter);
            searchPanel.Controls.Add(moveGameFilter, 3, 0);

            var catalogGrid = MakeMoveFilterGrid();
            var conditionGrid = MakeMoveFilterGrid();

            var buttonBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 5, 0, 5),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            var addButton = MakeMoveFilterButton("▼", 38);
            var removeButton = MakeMoveFilterButton("▲", 38);
            var resetButton = MakeMoveFilterButton("重置", 98);
            addButton.Margin = new Padding(56, 0, 22, 0);
            removeButton.Margin = new Padding(0, 0, 70, 0);
            resetButton.Margin = new Padding(0);
            buttonBar.Controls.Add(addButton);
            buttonBar.Controls.Add(removeButton);
            buttonBar.Controls.Add(resetButton);

            var conditionLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(2, 0, 0, 0)
            };

            layout.Controls.Add(catalogLabel, 0, 0);
            layout.Controls.Add(searchPanel, 0, 1);
            layout.Controls.Add(catalogGrid, 0, 2);
            layout.Controls.Add(buttonBar, 0, 3);
            layout.Controls.Add(conditionLabel, 0, 4);
            layout.Controls.Add(conditionGrid, 0, 5);

            Action refreshCatalog = delegate
            {
                FillMoveCatalogGrid(catalogGrid, moveSearchBox.Text, catalogLabel);
            };
            Action refreshConditions = delegate
            {
                FillMoveConditionGrid(conditionGrid, conditionLabel);
            };
            Action addSelectedMove = delegate
            {
                if (AddSelectedMoveFilter(catalogGrid))
                {
                    refreshConditions();
                    ApplyFilters();
                }
            };
            Action removeSelectedMove = delegate
            {
                if (RemoveSelectedMoveFilter(conditionGrid))
                {
                    refreshConditions();
                    ApplyFilters();
                }
            };

            moveSearchBox.TextChanged += delegate
            {
                moveFilterSearchText = moveSearchBox.Text;
                refreshCatalog();
            };
            moveGameFilter.SelectedIndexChanged += delegate
            {
                var selected = moveGameFilter.SelectedItem as IdOption;
                if (selected == null || selected.Id == moveFilterGameId) return;
                moveFilterGameId = selected.Id;
                ApplyFilters();
            };
            catalogGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) addSelectedMove();
            };
            conditionGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) removeSelectedMove();
            };
            addButton.Click += delegate { addSelectedMove(); };
            removeButton.Click += delegate { removeSelectedMove(); };
            resetButton.Click += delegate
            {
                if (moveFilterMoveIds.Count == 0) return;
                moveFilterMoveIds.Clear();
                refreshConditions();
                ApplyFilters();
            };
            catalogGrid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.ColumnIndex < 0 || e.ColumnIndex >= catalogGrid.Columns.Count) return;
                if (moveFilterCatalogSortColumn == e.ColumnIndex) moveFilterCatalogSortAscending = !moveFilterCatalogSortAscending;
                else
                {
                    moveFilterCatalogSortColumn = e.ColumnIndex;
                    moveFilterCatalogSortAscending = true;
                }
                refreshCatalog();
            };
            conditionGrid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.ColumnIndex < 0 || e.ColumnIndex >= conditionGrid.Columns.Count) return;
                if (moveFilterConditionSortColumn == e.ColumnIndex) moveFilterConditionSortAscending = !moveFilterConditionSortAscending;
                else
                {
                    moveFilterConditionSortColumn = e.ColumnIndex;
                    moveFilterConditionSortAscending = true;
                }
                refreshConditions();
            };

            refreshCatalog();
            refreshConditions();
            return page;
        }

        private void PopulateMoveFilterGameFilter(ComboBox combo)
        {
            combo.Items.Clear();
            int currentGameId = CurrentMoveFilterGameId();
            int selectedIndex = -1;
            foreach (int gameId in root.learnsets.Select(r => r.gameId).Distinct().OrderByDescending(id => id))
            {
                int itemIndex = combo.Items.Add(new IdOption(gameId, GameName(gameId)));
                if (gameId == currentGameId) selectedIndex = itemIndex;
            }
            if (selectedIndex < 0 && combo.Items.Count > 0) selectedIndex = 0;
            if (selectedIndex >= 0) combo.SelectedIndex = selectedIndex;
        }

        private Button MakeMoveFilterButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 27,
                FlatStyle = FlatStyle.Standard,
                BackColor = Color.FromArgb(226, 226, 226),
                ForeColor = Color.FromArgb(23, 32, 27)
            };
        }

        private DataGridView MakeMoveFilterGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.FromArgb(255, 250, 237),
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 28,
                RowTemplate = { Height = 22 },
                ScrollBars = ScrollBars.Vertical,
                ShowCellToolTips = true,
                Margin = new Padding(0)
            };
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Add(MakeTextColumn("id", "#", 34));
            grid.Columns.Add(MakeTextColumn("move", "名字", 112));
            grid.Columns.Add(MakeImageColumn("type", "属性", 44));
            grid.Columns.Add(MakeImageColumn("category", "分类", 44));
            grid.Columns.Add(MakeTextColumn("power", "威", 34));
            grid.Columns.Add(MakeTextColumn("accuracy", "命", 34));
            grid.Columns.Add(MakeTextColumn("pp", "PP", 32));
            grid.Columns.Add(MakeImageColumn("range", "范围", 46));
            grid.Columns.Add(MakeTextColumn("priority", "优", 32));
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            grid.Columns["move"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["move"].MinimumWidth = 80;
            grid.Resize += delegate { ResizeMoveFilterGridColumns(grid); };
            return grid;
        }

        private void FillMoveCatalogGrid(DataGridView grid, string queryText, Label label)
        {
            string query = (queryText ?? "").Trim().ToLowerInvariant();
            var moves = SortMoveEntries(root.moves.Where(m => Match(query, MoveSearchText(m))), moveFilterCatalogSortColumn, moveFilterCatalogSortAscending);
            FillMoveFilterGridRows(grid, moves);
            label.Text = string.Format("招式（ {0} / {1} ）", moves.Count, root.moves.Count);
            UpdateMoveSortGlyph(grid, moveFilterCatalogSortColumn, moveFilterCatalogSortAscending);
        }

        private void FillMoveConditionGrid(DataGridView grid, Label label)
        {
            var moves = new List<MoveEntry>();
            foreach (int moveId in moveFilterMoveIds)
            {
                MoveEntry move;
                if (movesById.TryGetValue(moveId, out move))
                {
                    moves.Add(move);
                }
            }
            moves = SortMoveEntries(moves, moveFilterConditionSortColumn, moveFilterConditionSortAscending);
            FillMoveFilterGridRows(grid, moves);
            label.Text = string.Format("搜索条件（ {0} ）", moveFilterMoveIds.Count);
            UpdateMoveSortGlyph(grid, moveFilterConditionSortColumn, moveFilterConditionSortAscending);
        }

        private void FillMoveFilterGridRows(DataGridView grid, IEnumerable<MoveEntry> moves)
        {
            grid.Rows.Clear();
            foreach (var move in moves)
            {
                int rowIndex = grid.Rows.Add(
                    move.id.ToString(),
                    LocalName(move.names),
                    LoadCellImage(move.type == null ? "" : TypeImagePath(move.type.id)),
                    LoadCellImage(move.category == null ? "" : MoveCategoryImagePath(move.category.id)),
                    MoveValue(move.power),
                    MoveValue(move.accuracy),
                    MoveValue(move.pp),
                    LoadCellImage(MoveRangeImagePath(move.rangeId)),
                    ValueOrDash(move.priority)
                );
                grid.Rows[rowIndex].Tag = move.id;
                ApplyMoveTooltip(grid.Rows[rowIndex], move);
            }
        }

        private bool AddSelectedMoveFilter(DataGridView grid)
        {
            int moveId;
            if (!TryGetSelectedMoveId(grid, out moveId) || moveFilterMoveIds.Contains(moveId)) return false;
            moveFilterMoveIds.Add(moveId);
            return true;
        }

        private bool RemoveSelectedMoveFilter(DataGridView grid)
        {
            int moveId;
            if (!TryGetSelectedMoveId(grid, out moveId)) return false;
            return moveFilterMoveIds.Remove(moveId);
        }

        private static bool TryGetSelectedMoveId(DataGridView grid, out int moveId)
        {
            moveId = -1;
            if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is int)
            {
                moveId = (int)grid.SelectedRows[0].Tag;
                return true;
            }
            if (grid.CurrentRow != null && grid.CurrentRow.Tag is int)
            {
                moveId = (int)grid.CurrentRow.Tag;
                return true;
            }
            return false;
        }

        private static void ResizeMoveFilterGridColumns(DataGridView grid)
        {
            if (grid.Columns.Count < 9) return;
            int fixedWidth =
                grid.Columns["id"].Width +
                grid.Columns["type"].Width +
                grid.Columns["category"].Width +
                grid.Columns["power"].Width +
                grid.Columns["accuracy"].Width +
                grid.Columns["pp"].Width +
                grid.Columns["range"].Width +
                grid.Columns["priority"].Width +
                24;
            grid.Columns["move"].Width = Math.Max(80, grid.ClientSize.Width - fixedWidth);
        }

        private static List<MoveEntry> SortMoveEntries(IEnumerable<MoveEntry> moves, int sortColumn, bool sortAscending)
        {
            var sorted = moves.ToList();
            sorted.Sort(delegate(MoveEntry left, MoveEntry right)
            {
                int result = CompareMoveEntries(left, right, sortColumn);
                if (result != 0) return sortAscending ? result : -result;
                return left.id.CompareTo(right.id);
            });
            return sorted;
        }

        private static int CompareMoveEntries(MoveEntry left, MoveEntry right, int sortColumn)
        {
            switch (sortColumn)
            {
                case 0:
                    return left.id.CompareTo(right.id);
                case 1:
                    return CompareSortText(LocalName(left.names), LocalName(right.names));
                case 2:
                    return CompareSortNumber(left.type == null ? null : (object)left.type.id, right.type == null ? null : (object)right.type.id);
                case 3:
                    return CompareSortText(left.category == null ? "" : LocalName(left.category.names), right.category == null ? "" : LocalName(right.category.names));
                case 4:
                    return CompareMoveValue(left.power, right.power);
                case 5:
                    return CompareMoveValue(left.accuracy, right.accuracy);
                case 6:
                    return CompareMoveValue(left.pp, right.pp);
                case 7:
                    return CompareSortNumber(left.rangeId, right.rangeId);
                case 8:
                    return CompareSortNumber(ValueOrDash(left.priority), ValueOrDash(right.priority));
                default:
                    return left.id.CompareTo(right.id);
            }
        }

        private Control MakeAbilityBar(PokemonEntry p)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 6)
            };

            NamedRef primary = p.abilities == null ? null : p.abilities.primary;
            NamedRef secondary = p.abilities == null ? null : p.abilities.secondary;
            NamedRef hidden = p.abilities == null ? null : p.abilities.hidden;
            panel.Controls.Add(MakeAbilityButton(primary, "第一特性"));
            panel.Controls.Add(MakeAbilityButton(secondary, "第二特性"));
            panel.Controls.Add(MakeAbilityButton(hidden, "隐藏特性"));
            return panel;
        }

        private Control MakeAbilityButton(NamedRef ability, string role)
        {
            string name = ability == null ? "---" : LocalName(ability.names);
            var label = new Label
            {
                Text = string.IsNullOrWhiteSpace(name) ? "---" : name,
                AutoSize = false,
                Width = 132,
                Height = 25,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = ability == null ? Color.FromArgb(220, 220, 220) : Color.FromArgb(216, 230, 247),
                ForeColor = ability == null ? Color.FromArgb(100, 100, 100) : Color.FromArgb(23, 32, 27),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 0, 7, 4)
            };

            if (ability != null)
            {
                string tooltipText = AbilityTooltipText(ability, role);
                abilityToolTip.SetToolTip(label, tooltipText);
                label.MouseEnter += delegate { abilityToolTip.Show(tooltipText, label, label.Width / 2, label.Height, 8000); };
                label.MouseLeave += delegate { abilityToolTip.Hide(label); };
            }

            return label;
        }

        private string AbilityTooltipText(NamedRef ability, string role)
        {
            AbilityEntry detail;
            string name = LocalName(ability.names);
            if (abilitiesById.TryGetValue(ability.id, out detail))
            {
                string description = LocalName(detail.descriptions);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return role + ": " + name + Environment.NewLine + description;
                }
            }
            return role + ": " + name;
        }

        private Control MakeDefenseBlock(PokemonEntry p)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                Width = 760,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            panel.Controls.Add(new Label
            {
                Text = "防御时",
                AutoSize = true,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0, 0, 0, 1)
            });
            panel.Controls.Add(MakeDefenseMatrix(p));
            return panel;
        }

        private Control MakeDefenseMatrix(PokemonEntry p)
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 12,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 0)
            };

            for (int col = 0; col < 6; col++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            }

            for (int row = 0; row < 3; row++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            }

            var types = root.types.OrderBy(t => t.id).ToList();
            for (int i = 0; i < types.Count; i++)
            {
                TypeRef type = types[i];
                int row = i / 6;
                int pair = i % 6;
                if (row >= 3) break;

                double multiplier = ParseMultiplier(GetDefenseMultiplierText(p, type.id));
                Label typeLabel = MakeTypeBadgeLabel(type, 30, 25, new Padding(0, 0, 1, 1));
                typeLabel.Dock = DockStyle.Fill;
                table.Controls.Add(typeLabel, pair * 2, row);
                table.Controls.Add(new Label
                {
                    Text = multiplier.ToString("0.##"),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = MultiplierColor(multiplier),
                    ForeColor = Color.Black,
                    Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                    Margin = new Padding(0, 0, 2, 1)
                }, pair * 2 + 1, row);
            }

            return table;
        }

        private Control MakeLegacyEvolutionGrid(PokemonEntry p)
        {
            var grid = new DataGridView
            {
                Width = 760,
                Height = 96,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.FromArgb(255, 250, 237),
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 22,
                RowTemplate = { Height = 22 },
                Margin = new Padding(0, 0, 0, 6)
            };
            grid.Columns.Add(MakeImageColumn("fromIcon", "", 38));
            grid.Columns.Add(MakeTextColumn("arrow", "→", 28));
            grid.Columns.Add(MakeImageColumn("toIcon", "", 38));
            grid.Columns.Add(MakeTextColumn("summary", "说明", 360));
            grid.Columns["summary"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Resize += delegate
            {
                int fixedWidth = 38 + 28 + 38 + 24;
                grid.Columns["summary"].Width = Math.Max(260, grid.ClientSize.Width - fixedWidth);
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count && grid.Rows[e.RowIndex].Tag is int)
                {
                    NavigateToPokemon((int)grid.Rows[e.RowIndex].Tag);
                }
            };

            EvolutionEntry current;
            if (!evolutionByPokemonId.TryGetValue(p.legacyId, out current))
            {
                AddEmptyEvolutionRows(grid);
                return grid;
            }

            List<EvolutionEntry> family;
            if (!evolutionsByFamilyId.TryGetValue(current.familyId, out family))
            {
                AddEmptyEvolutionRows(grid);
                return grid;
            }

            foreach (var evolution in family.Where(e => e.previousPokemonId > 0))
            {
                PokemonEntry previous = FindPokemon(evolution.previousPokemonId);
                PokemonEntry target = FindPokemon(evolution.pokemonId);
                int rowIndex = grid.Rows.Add(
                    LoadPokemonSmallCellImage(previous == null ? -1 : previous.legacyId),
                    "→",
                    LoadPokemonSmallCellImage(target == null ? -1 : target.legacyId),
                    BuildEvolutionSummary(evolution)
                );
                if (target != null)
                {
                    grid.Rows[rowIndex].Tag = target.legacyId;
                }
            }

            AddEmptyEvolutionRows(grid);
            return grid;
        }

        private static void AddEmptyEvolutionRows(DataGridView grid)
        {
            while (grid.Rows.Count < 3)
            {
                grid.Rows.Add(null, "", null, "");
                DataGridViewRow row = grid.Rows[grid.Rows.Count - 1];
                row.Cells["fromIcon"].Value = new Bitmap(1, 1);
                row.Cells["toIcon"].Value = new Bitmap(1, 1);
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 237);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 250, 237);
                row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(23, 32, 27);
            }
        }

        private Control MakeLegacyMoveSection(PokemonEntry p, bool fillParent)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = !fillParent,
                Width = fillParent ? 760 : 780,
                Dock = fillParent ? DockStyle.Fill : DockStyle.None,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, fillParent ? 0 : 4, 0, 0),
                BackColor = Color.FromArgb(255, 250, 237)
            };

            var toolbar = new FlowLayoutPanel
            {
                AutoSize = true,
                Width = fillParent ? 760 : 760,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 4)
            };
            toolbar.Controls.Add(MakeLegacySectionLabel("招式"));
            toolbar.Controls.Add(new Label { Text = "版本", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(18, 6, 4, 0) });
            var gameFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 2, 0, 0) };
            toolbar.Controls.Add(gameFilter);

            var grid = MakeLegacyMoveGrid(fillParent);
            panel.Controls.Add(toolbar);
            panel.Controls.Add(grid);
            if (fillParent)
            {
                panel.Resize += delegate
                {
                    grid.Width = Math.Max(260, panel.ClientSize.Width - 2);
                    toolbar.Width = Math.Max(260, panel.ClientSize.Width - 2);
                    grid.Height = Math.Max(80, panel.ClientSize.Height - toolbar.Height - 8);
                    ResizeLegacyMoveGridColumns(grid);
                };
            }

            List<LearnsetEntry> rows;
            if (!learnsetsByPokemonId.TryGetValue(p.legacyId, out rows) || rows.Count == 0)
            {
                grid.Rows.Add("--", "没有招式学习数据", null, null, "", "", "", null, "");
                return panel;
            }

            foreach (int gameId in rows.Select(r => r.gameId).Distinct().OrderByDescending(id => id))
            {
                gameFilter.Items.Add(new IdOption(gameId, GameName(gameId)));
            }
            if (gameFilter.Items.Count > 0)
            {
                gameFilter.SelectedIndex = 0;
            }

            int moveSortColumn = 0;
            bool moveSortAscending = true;
            Action refreshGrid = delegate
            {
                var selected = gameFilter.SelectedItem as IdOption;
                FillLegacyMoveGrid(grid, rows, selected == null ? -1 : selected.Id, moveSortColumn, moveSortAscending);
            };

            gameFilter.SelectedIndexChanged += delegate
            {
                refreshGrid();
            };

            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
                if (moveSortColumn == e.ColumnIndex)
                {
                    moveSortAscending = !moveSortAscending;
                }
                else
                {
                    moveSortColumn = e.ColumnIndex;
                    moveSortAscending = true;
                }
                refreshGrid();
            };

            refreshGrid();
            return panel;
        }

        private DataGridView MakeLegacyMoveGrid(bool fillParent)
        {
            var grid = new DataGridView
            {
                Width = fillParent ? 760 : 760,
                Height = fillParent ? 150 : LegacyMoveGridHeight(),
                Dock = fillParent ? DockStyle.None : DockStyle.None,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.FromArgb(255, 250, 237),
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24,
                RowTemplate = { Height = 22 },
                ShowCellToolTips = true
            };
            grid.Columns.Add(MakeTextColumn("level", "Lv.", 72));
            grid.Columns.Add(MakeTextColumn("move", "招式", fillParent ? 96 : 132));
            grid.Columns.Add(MakeImageColumn("type", "属性", fillParent ? 44 : 58));
            grid.Columns.Add(MakeImageColumn("category", "分类", fillParent ? 44 : 58));
            grid.Columns.Add(MakeTextColumn("power", "威", 38));
            grid.Columns.Add(MakeTextColumn("accuracy", "命", 38));
            grid.Columns.Add(MakeTextColumn("pp", "PP", 38));
            grid.Columns.Add(MakeImageColumn("range", "范围", fillParent ? 48 : 62));
            grid.Columns.Add(MakeTextColumn("priority", "优", 36));
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            grid.Resize += delegate { ResizeLegacyMoveGridColumns(grid); };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count && grid.Rows[e.RowIndex].Tag is int)
                {
                    NavigateToMove((int)grid.Rows[e.RowIndex].Tag);
                }
            };
            return grid;
        }

        private static void ResizeLegacyMoveGridColumns(DataGridView grid)
        {
            if (grid.Columns.Count < 9) return;
            int fixedWidth =
                grid.Columns["level"].Width +
                grid.Columns["type"].Width +
                grid.Columns["category"].Width +
                grid.Columns["power"].Width +
                grid.Columns["accuracy"].Width +
                grid.Columns["pp"].Width +
                grid.Columns["range"].Width +
                grid.Columns["priority"].Width +
                28;
            grid.Columns["move"].Width = Math.Max(96, grid.ClientSize.Width - fixedWidth);
        }

        private int LegacyMoveGridHeight()
        {
            int available = details.ClientSize.Height - 325;
            if (available < 330) return 330;
            if (available > 460) return 460;
            return available;
        }

        private void FillLegacyMoveGrid(DataGridView grid, List<LearnsetEntry> rows, int gameId, int sortColumn, bool sortAscending)
        {
            grid.Rows.Clear();

            IEnumerable<LearnsetEntry> filtered = rows;
            if (gameId > 0)
            {
                filtered = filtered.Where(r => r.gameId == gameId);
            }

            foreach (var row in SortLegacyMoveRows(filtered, sortColumn, sortAscending))
            {
                MoveEntry move;
                movesById.TryGetValue(row.moveId, out move);
                int rowIndex = grid.Rows.Add(
                    LevelName(row.levelId),
                    move == null ? "#" + row.moveId : LocalName(move.names),
                    LoadCellImage(move == null || move.type == null ? "" : TypeImagePath(move.type.id)),
                    LoadCellImage(move == null || move.category == null ? "" : MoveCategoryImagePath(move.category.id)),
                    move == null ? "--" : MoveValue(move.power),
                    move == null ? "--" : MoveValue(move.accuracy),
                    move == null ? "--" : MoveValue(move.pp),
                    LoadCellImage(move == null ? "" : MoveRangeImagePath(move.rangeId)),
                    move == null ? "--" : ValueOrDash(move.priority)
                );
                grid.Rows[rowIndex].Tag = row.moveId;
                ApplyMoveTooltip(grid.Rows[rowIndex], move);
            }

            UpdateMoveSortGlyph(grid, sortColumn, sortAscending);

            if (grid.Rows.Count == 0)
            {
                grid.Rows.Add("--", "该版本没有招式学习数据", null, null, "", "", "", null, "");
            }
        }

        private List<LearnsetEntry> SortLegacyMoveRows(IEnumerable<LearnsetEntry> rows, int sortColumn, bool sortAscending)
        {
            var sorted = rows.ToList();
            sorted.Sort(delegate(LearnsetEntry left, LearnsetEntry right)
            {
                int result = CompareLegacyMoveRows(left, right, sortColumn);
                if (result != 0) return sortAscending ? result : -result;
                return CompareLegacyMoveRowsDefault(left, right);
            });
            return sorted;
        }

        private int CompareLegacyMoveRows(LearnsetEntry left, LearnsetEntry right, int sortColumn)
        {
            MoveEntry leftMove = MoveForLearnset(left);
            MoveEntry rightMove = MoveForLearnset(right);
            switch (sortColumn)
            {
                case 0:
                    return LearnLevelSort(left.levelId).CompareTo(LearnLevelSort(right.levelId));
                case 1:
                    return CompareSortText(leftMove == null ? "#" + left.moveId : LocalName(leftMove.names), rightMove == null ? "#" + right.moveId : LocalName(rightMove.names));
                case 2:
                    return CompareSortNumber(leftMove == null || leftMove.type == null ? null : (object)leftMove.type.id, rightMove == null || rightMove.type == null ? null : (object)rightMove.type.id);
                case 3:
                    return CompareSortText(leftMove == null || leftMove.category == null ? "" : LocalName(leftMove.category.names), rightMove == null || rightMove.category == null ? "" : LocalName(rightMove.category.names));
                case 4:
                    return CompareMoveValue(leftMove == null ? null : leftMove.power, rightMove == null ? null : rightMove.power);
                case 5:
                    return CompareMoveValue(leftMove == null ? null : leftMove.accuracy, rightMove == null ? null : rightMove.accuracy);
                case 6:
                    return CompareMoveValue(leftMove == null ? null : leftMove.pp, rightMove == null ? null : rightMove.pp);
                case 7:
                    return CompareSortNumber(leftMove == null ? null : leftMove.rangeId, rightMove == null ? null : rightMove.rangeId);
                case 8:
                    return CompareSortNumber(leftMove == null ? null : ValueOrDash(leftMove.priority), rightMove == null ? null : ValueOrDash(rightMove.priority));
                default:
                    return CompareLegacyMoveRowsDefault(left, right);
            }
        }

        private MoveEntry MoveForLearnset(LearnsetEntry row)
        {
            MoveEntry move;
            return row != null && movesById.TryGetValue(row.moveId, out move) ? move : null;
        }

        private static int CompareLegacyMoveRowsDefault(LearnsetEntry left, LearnsetEntry right)
        {
            int result = LearnLevelSort(left.levelId).CompareTo(LearnLevelSort(right.levelId));
            if (result != 0) return result;
            result = left.moveId.CompareTo(right.moveId);
            if (result != 0) return result;
            return left.gameId.CompareTo(right.gameId);
        }

        private static int CompareSortText(string left, string right)
        {
            return string.Compare(left ?? "", right ?? "", StringComparison.CurrentCultureIgnoreCase);
        }

        private static int CompareMoveValue(object left, object right)
        {
            return CompareSortNumber(MoveValue(left), MoveValue(right));
        }

        private static int CompareSortNumber(object left, object right)
        {
            double leftNumber;
            double rightNumber;
            bool hasLeft = TrySortNumber(left, out leftNumber);
            bool hasRight = TrySortNumber(right, out rightNumber);
            if (hasLeft && hasRight) return leftNumber.CompareTo(rightNumber);
            if (hasLeft) return -1;
            if (hasRight) return 1;
            return 0;
        }

        private static bool TrySortNumber(object value, out double number)
        {
            number = 0;
            if (value == null) return false;
            string text = value.ToString().Trim().TrimStart('#').Replace(",", "");
            if (text.Length == 0 || text == "--" || text == "-") return false;
            return double.TryParse(text, out number);
        }

        private static void UpdateMoveSortGlyph(DataGridView grid, int sortColumn, bool sortAscending)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            }
            if (sortColumn >= 0 && sortColumn < grid.Columns.Count)
            {
                grid.Columns[sortColumn].HeaderCell.SortGlyphDirection = sortAscending ? SortOrder.Ascending : SortOrder.Descending;
            }
        }

        private TabPage BuildPokemonDefenseTab(PokemonEntry p)
        {
            var page = MakeTabPage("防御时");
            var grid = MakeGrid(700, 420);
            grid.Columns.Add("攻击属性", 160);
            grid.Columns.Add("倍率", 90);
            grid.Columns.Add("English", 160);
            foreach (var type in root.types.OrderBy(t => t.id))
            {
                var item = new ListViewItem(LocalName(type.names));
                item.SubItems.Add(GetDefenseMultiplierText(p, type.id));
                item.SubItems.Add(EnglishName(type.names));
                grid.Items.Add(item);
            }
            page.Controls.Add(grid);
            return page;
        }

        private TabPage BuildPokemonEvolutionTab(PokemonEntry p)
        {
            var page = MakeTabPage("进化");
            var grid = MakeGrid(740, 420);
            grid.Columns.Add("阶段", 90);
            grid.Columns.Add("宝可梦", 150);
            grid.Columns.Add("由", 150);
            grid.Columns.Add("方式", 180);
            grid.Columns.Add("条件", 150);

            EvolutionEntry current;
            if (!evolutionByPokemonId.TryGetValue(p.legacyId, out current))
            {
                page.Controls.Add(MakeBodyLabel("没有进化数据。"));
                return page;
            }

            List<EvolutionEntry> family;
            if (!evolutionsByFamilyId.TryGetValue(current.familyId, out family))
            {
                family = new List<EvolutionEntry> { current };
            }

            foreach (var evolution in family)
            {
                PokemonEntry target = FindPokemon(evolution.pokemonId);
                PokemonEntry previous = FindPokemon(evolution.previousPokemonId);
                var item = new ListViewItem(evolution.stage == null ? ValueOrDash(evolution.stageId) : LocalName(evolution.stage.names));
                item.SubItems.Add(target == null ? "#" + evolution.pokemonId : LocalName(target.names) + FormSuffix(target));
                item.SubItems.Add(previous == null ? "--" : LocalName(previous.names));
                item.SubItems.Add(evolution.method == null ? "--" : LocalName(evolution.method.names));
                item.SubItems.Add(EvolutionConditionText(evolution));
                grid.Items.Add(item);
            }

            page.Controls.Add(grid);
            return page;
        }

        private TabPage BuildPokemonMovesTab(PokemonEntry p)
        {
            var page = MakeTabPage("招式");
            var grid = MakeGrid(780, 440);
            grid.Columns.Add("版本", 96);
            grid.Columns.Add("Lv./招式机", 100);
            grid.Columns.Add("招式", 150);
            grid.Columns.Add("属性", 78);
            grid.Columns.Add("分类", 78);
            grid.Columns.Add("威力", 58);
            grid.Columns.Add("命中", 58);
            grid.Columns.Add("PP", 48);

            List<LearnsetEntry> rows;
            if (!learnsetsByPokemonId.TryGetValue(p.legacyId, out rows) || rows.Count == 0)
            {
                page.Controls.Add(MakeBodyLabel("没有招式学习数据。"));
                return page;
            }

            foreach (var row in rows
                .OrderByDescending(r => r.gameId)
                .ThenBy(r => LearnLevelSort(r.levelId))
                .ThenBy(r => r.moveId)
                .Take(700))
            {
                MoveEntry move;
                movesById.TryGetValue(row.moveId, out move);
                var item = new ListViewItem(GameName(row.gameId));
                item.SubItems.Add(LevelName(row.levelId));
                item.SubItems.Add(move == null ? "#" + row.moveId : LocalName(move.names));
                item.SubItems.Add(move == null || move.type == null ? "--" : LocalName(move.type.names));
                item.SubItems.Add(move == null || move.category == null ? "--" : LocalName(move.category.names));
                item.SubItems.Add(move == null ? "--" : ValueOrDash(move.power));
                item.SubItems.Add(move == null ? "--" : ValueOrDash(move.accuracy));
                item.SubItems.Add(move == null ? "--" : ValueOrDash(move.pp));
                item.Tag = row.moveId;
                grid.Items.Add(item);
            }

            if (rows.Count > 700)
            {
                grid.Items.Add(new ListViewItem(new[] { "...", "已截断", "请用搜索/版本过滤进一步缩小", "", "", "", "", "" }));
            }

            grid.DoubleClick += delegate
            {
                if (grid.SelectedItems.Count == 0 || !(grid.SelectedItems[0].Tag is int)) return;
                NavigateToMove((int)grid.SelectedItems[0].Tag);
            };

            page.Controls.Add(grid);
            return page;
        }

        private void ShowMove(MoveEntry m)
        {
            details.Controls.Add(MakeMoveDetailPanel(m));
        }

        private Control MakeMoveDetailPanel(MoveEntry move)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(255, 250, 237),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 272));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var middle = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            middle.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            middle.Controls.Add(MakeMoveDescriptionBox(move), 0, 0);
            middle.Controls.Add(MakeMoveFilterBox(move), 0, 1);

            var pokemonGrid = MakeMovePokemonGrid();
            pokemonGrid.Margin = new Padding(6, 0, 0, 0);
            FillMovePokemonGrid(pokemonGrid, move);

            layout.Controls.Add(middle, 0, 0);
            layout.Controls.Add(pokemonGrid, 1, 0);
            return layout;
        }

        private Control MakeMoveDescriptionBox(MoveEntry move)
        {
            var group = new GroupBox
            {
                Text = "描述",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(255, 250, 237),
                ForeColor = Color.Blue,
                Font = new Font("Segoe UI", 9f),
                Text = move == null ? "" : LocalName(move.descriptions),
                Margin = new Padding(4)
            };
            group.Controls.Add(text);
            return group;
        }

        private Control MakeMoveFilterBox(MoveEntry move)
        {
            var group = new GroupBox
            {
                Text = "筛选",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = Color.FromArgb(255, 250, 237)
            };

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4, 2, 4, 4),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            stack.Controls.Add(MakeMoveFilterSearchPanel());
            stack.Controls.Add(MakeMoveFilterNumberPanel("威力", move == null ? null : move.power, moveModulePowerFilter, false, 0, 999));
            stack.Controls.Add(MakeMoveFilterPresetPanel("命中", move == null ? null : move.accuracy, root.moves.Select(m => m.accuracy), moveModuleAccuracyFilter, "%"));
            stack.Controls.Add(MakeMoveFilterPresetPanel("PP", move == null ? null : move.pp, root.moves.Select(m => m.pp), moveModulePpFilter, ""));
            stack.Controls.Add(MakeMoveFilterNumberPanel("优先级", move == null ? null : move.priority, moveModulePriorityFilter, true, -10, 10));
            stack.Controls.Add(MakeMoveFilterChoicePanel("招式 / 秘传", new[] { "—", "招式", "秘传" }, moveModuleMachineFilter, delegate(string value) { moveModuleMachineFilter = value; }));
            stack.Controls.Add(MakeMoveFilterSection("属性", MakeMoveTypeFilterButtons()));
            stack.Controls.Add(MakeMoveFilterSection("分类", MakeMoveCategoryFilterButtons()));
            stack.Controls.Add(MakeMoveFilterSection("效果对象", MakeMoveRangeFilterButtons()));
            stack.Resize += delegate
            {
                int width = Math.Max(240, stack.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
                foreach (Control control in stack.Controls) control.Width = width;
            };
            group.Controls.Add(stack);
            return group;
        }

        private Control MakeMoveFilterSearchPanel()
        {
            var panel = MakeMoveFilterRowPanel();
            panel.Controls.Add(new Label
            {
                Text = "⌕",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(40, 79, 145),
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                Margin = new Padding(0)
            }, 0, 0);
            var search = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Text = moveModuleFilterSearchText,
                Margin = new Padding(0, 2, 4, 2)
            };
            search.TextChanged += delegate
            {
                moveModuleFilterSearchText = search.Text;
                ApplyFilters();
            };
            panel.Controls.Add(search, 1, 0);
            panel.SetColumnSpan(search, 3);
            panel.Controls.Add(MakeMoveFilterToggleButton(null), 4, 0);
            return panel;
        }

        private Control MakeMoveFilterNumberPanel(string label, object rawValue, MoveNumericFilter filter, bool preserveMinusOne, decimal minimum, decimal maximum)
        {
            var panel = MakeMoveFilterRowPanel();
            decimal currentValue;
            bool hasCurrentValue = TryMoveFilterNumber(rawValue, preserveMinusOne, out currentValue);
            bool canEdit = filter.Enabled || hasCurrentValue;

            var check = new CheckBox
            {
                Dock = DockStyle.Fill,
                Checked = filter.Enabled,
                Enabled = canEdit,
                Margin = new Padding(0, 6, 0, 0)
            };
            panel.Controls.Add(check, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0)
            }, 1, 0);

            var op = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = filter.Enabled,
                Margin = new Padding(0, 3, 4, 3)
            };
            op.Items.Add("=");
            op.Items.Add(">");
            op.Items.Add("<");
            op.Items.Add(">=");
            op.Items.Add("<=");
            op.Items.Add("!=");
            op.SelectedItem = string.IsNullOrWhiteSpace(filter.Operator) ? "=" : filter.Operator;
            if (op.SelectedIndex < 0) op.SelectedIndex = 0;
            panel.Controls.Add(op, 2, 0);

            NumericUpDown number = null;
            if (canEdit)
            {
                number = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    DecimalPlaces = 0,
                    Minimum = minimum,
                    Maximum = maximum,
                    Value = ClampDecimal(filter.Enabled ? filter.Value : currentValue, minimum, maximum),
                    Enabled = filter.Enabled,
                    Margin = new Padding(0, 3, 4, 3)
                };
                panel.Controls.Add(number, 3, 0);
            }
            else
            {
                panel.Controls.Add(new TextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Enabled = false,
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = "--",
                    BackColor = Color.FromArgb(230, 230, 230),
                    Margin = new Padding(0, 3, 4, 3)
                }, 3, 0);
            }

            Button toggle = MakeMoveFilterToggleButton(canEdit ? check : null);
            panel.Controls.Add(toggle, 4, 0);

            Action apply = delegate
            {
                if (!canEdit || number == null) return;
                op.Enabled = check.Checked;
                number.Enabled = check.Checked;
                toggle.Text = check.Checked ? "-" : "+";
                filter.Enabled = check.Checked;
                if (filter.Enabled)
                {
                    filter.Operator = op.SelectedItem == null ? "=" : op.SelectedItem.ToString();
                    filter.Value = number.Value;
                    filter.ValueText = number.Value.ToString("0");
                }
                ApplyFilters();
            };

            check.CheckedChanged += delegate { apply(); };
            op.SelectedIndexChanged += delegate { if (check.Checked) apply(); };
            if (number != null) number.ValueChanged += delegate { if (check.Checked) apply(); };
            toggle.Click += delegate { check.Checked = !check.Checked; };
            return panel;
        }

        private Control MakeMoveFilterPresetPanel(string label, object rawValue, IEnumerable<object> allValues, MoveNumericFilter filter, string suffix)
        {
            var panel = MakeMoveFilterRowPanel();
            var options = BuildMoveFilterPresetOptions(allValues, suffix);
            string currentValue = filter.Enabled && !string.IsNullOrWhiteSpace(filter.ValueText)
                ? filter.ValueText
                : MoveValue(rawValue);

            var check = new CheckBox
            {
                Dock = DockStyle.Fill,
                Checked = filter.Enabled,
                Enabled = options.Count > 0,
                Margin = new Padding(0, 6, 0, 0)
            };
            panel.Controls.Add(check, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0)
            }, 1, 0);

            var op = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = filter.Enabled,
                Margin = new Padding(0, 3, 4, 3)
            };
            op.Items.Add("=");
            op.Items.Add(">");
            op.Items.Add("<");
            op.Items.Add(">=");
            op.Items.Add("<=");
            op.Items.Add("!=");
            op.SelectedItem = string.IsNullOrWhiteSpace(filter.Operator) ? "=" : filter.Operator;
            if (op.SelectedIndex < 0) op.SelectedIndex = 0;
            panel.Controls.Add(op, 2, 0);

            var value = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = filter.Enabled,
                Margin = new Padding(0, 3, 4, 3)
            };
            foreach (var option in options)
            {
                int index = value.Items.Add(option);
                if (option.Value == currentValue) value.SelectedIndex = index;
            }
            if (value.SelectedIndex < 0 && value.Items.Count > 0) value.SelectedIndex = 0;
            panel.Controls.Add(value, 3, 0);

            Button toggle = MakeMoveFilterToggleButton(check);
            panel.Controls.Add(toggle, 4, 0);

            Action apply = delegate
            {
                op.Enabled = check.Checked;
                value.Enabled = check.Checked;
                toggle.Text = check.Checked ? "-" : "+";
                filter.Enabled = check.Checked;
                if (filter.Enabled)
                {
                    var option = value.SelectedItem as FilterOption;
                    string selectedValue = option == null ? "--" : option.Value;
                    filter.Operator = op.SelectedItem == null ? "=" : op.SelectedItem.ToString();
                    filter.ValueText = selectedValue;
                    decimal numericValue;
                    if (decimal.TryParse(selectedValue.Trim().TrimStart('#').Replace(",", ""), out numericValue))
                    {
                        filter.Value = numericValue;
                    }
                }
                ApplyFilters();
            };

            check.CheckedChanged += delegate { apply(); };
            op.SelectedIndexChanged += delegate { if (check.Checked) apply(); };
            value.SelectedIndexChanged += delegate { if (check.Checked) apply(); };
            toggle.Click += delegate { check.Checked = !check.Checked; };
            return panel;
        }

        private static List<FilterOption> BuildMoveFilterPresetOptions(IEnumerable<object> values, string suffix)
        {
            var result = new List<FilterOption>();
            foreach (string value in values.Select(MoveValue).Distinct().OrderBy(MovePresetSortKey))
            {
                string label = value == "--" ? "—" : value + suffix;
                result.Add(new FilterOption(value, label));
            }
            return result;
        }

        private static double MovePresetSortKey(string value)
        {
            if (value == "--") return -1;
            double number;
            return TrySortNumber(value, out number) ? number : double.MaxValue;
        }

        private Control MakeMoveFilterChoicePanel(string label, string[] values, string activeValue, Action<string> setFilter)
        {
            var panel = MakeMoveFilterRowPanel();
            var check = new CheckBox { Dock = DockStyle.Fill, Checked = activeValue != null, Margin = new Padding(0, 6, 0, 0) };
            panel.Controls.Add(check, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0)
            }, 1, 0);
            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = activeValue != null,
                Margin = new Padding(0, 3, 4, 3)
            };
            foreach (string value in values) combo.Items.Add(value);
            combo.SelectedItem = activeValue ?? values[0];
            panel.Controls.Add(combo, 2, 0);
            panel.SetColumnSpan(combo, 2);
            Button toggle = MakeMoveFilterToggleButton(check);
            panel.Controls.Add(toggle, 4, 0);

            Action apply = delegate
            {
                combo.Enabled = check.Checked;
                toggle.Text = check.Checked ? "-" : "+";
                setFilter(check.Checked ? (combo.SelectedItem == null ? values[0] : combo.SelectedItem.ToString()) : null);
                ApplyFilters();
            };
            check.CheckedChanged += delegate { apply(); };
            combo.SelectedIndexChanged += delegate { if (check.Checked) apply(); };
            toggle.Click += delegate { check.Checked = !check.Checked; };
            return panel;
        }

        private static TableLayoutPanel MakeMoveFilterRowPanel()
        {
            var panel = new TableLayoutPanel
            {
                Height = 28,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 1),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return panel;
        }

        private Control MakeMoveFilterSection(string title, Control content)
        {
            var panel = new TableLayoutPanel
            {
                Height = MoveFilterSectionHeight(title),
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 3, 0, 3),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft
            };
            content.Dock = DockStyle.Fill;
            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(content, 0, 1);
            return panel;
        }

        private static int MoveFilterSectionHeight(string title)
        {
            if (title == "属性") return 142;
            if (title == "效果对象") return 96;
            return 50;
        }

        private FlowLayoutPanel MakeMoveTypeFilterButtons()
        {
            var panel = MakeMoveButtonWrapPanel();
            foreach (var type in root.types.OrderBy(t => t.id))
            {
                int typeId = type.id;
                var button = MakeMoveFilterImageButton(LocalName(type.names), TypeImagePath(type.id), 50, TypeColor(type.id), Color.White, moveModuleTypeFilterId == type.id);
                button.Tag = typeId;
                button.Click += delegate
                {
                    moveModuleTypeFilterId = moveModuleTypeFilterId == typeId ? -1 : typeId;
                    UpdateMoveFilterButtonSelection(panel, moveModuleTypeFilterId > 0 ? (object)typeId : null);
                    ApplyFilters();
                };
                panel.Controls.Add(button);
            }
            return panel;
        }

        private FlowLayoutPanel MakeMoveCategoryFilterButtons()
        {
            var panel = MakeMoveButtonWrapPanel();
            foreach (var category in root.moves.Where(m => m.category != null).Select(m => m.category).GroupBy(c => c.id).Select(g => g.First()).OrderBy(c => c.id))
            {
                string name = LocalName(category.names);
                int categoryId = category.id;
                var button = MakeMoveFilterImageButton(name, MoveCategoryImagePath(category.id), 48, CategoryColor(name), Color.White, moveModuleCategoryFilterId == category.id);
                button.Tag = categoryId;
                button.Click += delegate
                {
                    moveModuleCategoryFilterId = moveModuleCategoryFilterId == categoryId ? -1 : categoryId;
                    UpdateMoveFilterButtonSelection(panel, moveModuleCategoryFilterId > 0 ? (object)categoryId : null);
                    ApplyFilters();
                };
                panel.Controls.Add(button);
            }
            return panel;
        }

        private Control MakeMoveRangeFilterButtons()
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 3,
                BackColor = Color.FromArgb(255, 250, 237),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            for (int i = 0; i < 3; i++)
            {
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            }

            int index = 0;
            foreach (string rangeValue in root.moves.Select(m => MoveValue(m.rangeId)).Where(v => v != "--").Distinct().OrderBy(MovePresetSortKey))
            {
                var button = MakeMoveFilterImageButton(rangeValue, MoveRangeImagePath(rangeValue), 42, Color.FromArgb(240, 240, 240), Color.FromArgb(23, 32, 27), moveModuleRangeFilter == rangeValue);
                button.Tag = rangeValue;
                button.Dock = DockStyle.Fill;
                button.Height = 22;
                button.Click += delegate
                {
                    moveModuleRangeFilter = moveModuleRangeFilter == rangeValue ? null : rangeValue;
                    UpdateMoveFilterButtonSelection(panel, moveModuleRangeFilter);
                    ApplyFilters();
                };
                panel.Controls.Add(button, index % 3, index / 3);
                index++;
                if (index >= 9) break;
            }
            return panel;
        }

        private static FlowLayoutPanel MakeMoveButtonWrapPanel()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = new Padding(0),
                BackColor = Color.FromArgb(255, 250, 237)
            };
        }

        private Button MakeMoveFilterImageButton(string text, string imagePath, int width, Color fallbackBackColor, Color fallbackForeColor, bool selected)
        {
            var button = new Button
            {
                Width = width,
                Height = 22,
                Margin = new Padding(1),
                BackColor = Color.FromArgb(240, 240, 240),
                ForeColor = fallbackForeColor,
                FlatStyle = FlatStyle.Flat
            };
            Image image = LoadCellImage(imagePath);
            if (image != null)
            {
                button.Image = image;
                button.ImageAlign = ContentAlignment.MiddleCenter;
            }
            else
            {
                button.Text = text;
                button.BackColor = fallbackBackColor;
            }
            SetMoveFilterButtonSelected(button, selected);
            return button;
        }

        private static void UpdateMoveFilterButtonSelection(Control panel, object selectedTag)
        {
            foreach (Control control in panel.Controls)
            {
                var button = control as Button;
                if (button == null) continue;
                SetMoveFilterButtonSelected(button, selectedTag != null && object.Equals(button.Tag, selectedTag));
            }
        }

        private static void SetMoveFilterButtonSelected(Button button, bool selected)
        {
            button.FlatAppearance.BorderColor = selected ? Color.FromArgb(0, 75, 180) : Color.FromArgb(190, 190, 190);
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
        }

        private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        private static Button MakeMoveFilterToggleButton(CheckBox check)
        {
            return new Button
            {
                Text = check != null && check.Checked ? "-" : "+",
                Dock = DockStyle.Fill,
                Enabled = check != null,
                Margin = new Padding(1, 3, 0, 3)
            };
        }

        private DataGridView MakeMovePokemonGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.FromArgb(255, 250, 237),
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24,
                RowTemplate = { Height = 22 },
                ScrollBars = ScrollBars.Vertical
            };
            grid.Columns.Add(MakeTextColumn("number", "#", 34));
            grid.Columns.Add(MakeImageColumn("icon", "", 22));
            grid.Columns.Add(MakeTextColumn("name", "宝可梦", 82));
            grid.Columns.Add(MakeTextColumn("type1", "属性", 40));
            grid.Columns.Add(MakeTextColumn("type2", "属性", 40));
            grid.Columns.Add(MakeTextColumn("level", "Lv.", 44));
            grid.Columns["name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["name"].MinimumWidth = 68;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            grid.Resize += delegate { ResizeMovePokemonGridColumns(grid); };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count && grid.Rows[e.RowIndex].Tag is int)
                {
                    NavigateToPokemon((int)grid.Rows[e.RowIndex].Tag);
                }
            };
            return grid;
        }

        private static void ResizeMovePokemonGridColumns(DataGridView grid)
        {
            if (grid.Columns.Count < 6) return;
            int fixedWidth =
                grid.Columns["number"].Width +
                grid.Columns["icon"].Width +
                grid.Columns["type1"].Width +
                grid.Columns["type2"].Width +
                grid.Columns["level"].Width +
                26;
            grid.Columns["name"].Width = Math.Max(68, grid.ClientSize.Width - fixedWidth);
        }

        private void FillMovePokemonGrid(DataGridView grid, MoveEntry move)
        {
            grid.Rows.Clear();
            if (move == null) return;

            List<LearnsetEntry> rows;
            if (!learnsetsByMoveId.TryGetValue(move.id, out rows)) return;

            int gameId = MovePokemonGridGameId(rows);
            foreach (var row in rows
                .Where(r => r.gameId == gameId)
                .OrderBy(r => r.pokemonId)
                .ThenBy(r => LearnLevelSort(r.levelId)))
            {
                PokemonEntry pokemon = FindPokemon(row.pokemonId);
                if (pokemon == null) continue;
                int rowIndex = grid.Rows.Add(
                    pokemon.nationalDex.ToString(),
                    LoadPokemonSmallCellImage(pokemon.legacyId),
                    LocalName(pokemon.names),
                    TypeNameAt(pokemon.types, 0),
                    TypeNameAt(pokemon.types, 1),
                    LevelName(row.levelId)
                );
                grid.Rows[rowIndex].Tag = pokemon.legacyId;
                StyleMovePokemonGridRow(grid.Rows[rowIndex], pokemon);
            }
        }

        private int MovePokemonGridGameId(List<LearnsetEntry> rows)
        {
            int gameId = CurrentMoveFilterGameId();
            if (rows.Any(r => r.gameId == gameId)) return gameId;
            return rows.Select(r => r.gameId).DefaultIfEmpty(gameId).Max();
        }

        private static void StyleMovePokemonGridRow(DataGridViewRow row, PokemonEntry pokemon)
        {
            StyleAbilityTypeCell(row.Cells["type1"], TypeAt(pokemon.types, 0));
            StyleAbilityTypeCell(row.Cells["type2"], TypeAt(pokemon.types, 1));
        }

        private void ShowAbility(AbilityEntry a)
        {
            details.Controls.Add(MakeAbilityDetailPanel(a));
        }

        private Control MakeAbilityDetailPanel(AbilityEntry ability)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(255, 250, 237),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            top.Controls.Add(MakeAbilityDescriptionBox(ability), 0, 0);
            top.Controls.Add(MakeAbilityFilterBox(ability), 1, 0);

            var pokemonGrid = MakeAbilityPokemonGrid();
            FillAbilityPokemonGrid(pokemonGrid, ability);

            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(pokemonGrid, 0, 1);
            return layout;
        }

        private Control MakeAbilityDescriptionBox(AbilityEntry ability)
        {
            var group = new GroupBox
            {
                Text = "描述",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 5, 0),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(255, 250, 237),
                ForeColor = Color.Blue,
                Font = new Font("Segoe UI", 9f),
                Text = LocalName(ability.descriptions),
                Margin = new Padding(4)
            };
            group.Controls.Add(text);
            return group;
        }

        private Control MakeAbilityFilterBox(AbilityEntry ability)
        {
            var group = new GroupBox
            {
                Text = "筛选",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = Color.FromArgb(255, 250, 237)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                Padding = new Padding(4, 2, 4, 4),
                BackColor = Color.FromArgb(255, 250, 237)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
            for (int i = 0; i < 4; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            AddAbilityFilterSearchRow(panel, 0);
            AddAbilityFilterOptionRow(panel, 1, "发动时间", AbilityFilterOptions(a => a.trigger), ability.trigger == null ? -1 : ability.trigger.id, abilityFilterTriggerId, delegate(int id) { abilityFilterTriggerId = id; });
            AddAbilityFilterOptionRow(panel, 2, "效果对象", AbilityFilterOptions(a => a.target), ability.target == null ? -1 : ability.target.id, abilityFilterTargetId, delegate(int id) { abilityFilterTargetId = id; });
            AddAbilityFilterOptionRow(panel, 3, "特性效果", AbilityFilterOptions(a => a.effectOn), ability.effectOn == null ? -1 : ability.effectOn.id, abilityFilterEffectOnId, delegate(int id) { abilityFilterEffectOnId = id; });

            group.Controls.Add(panel);
            return group;
        }

        private void AddAbilityFilterSearchRow(TableLayoutPanel panel, int row)
        {
            panel.Controls.Add(new Label
            {
                Text = "⌕",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(40, 79, 145),
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                Margin = new Padding(0)
            }, 0, row);
            var search = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Text = abilityFilterSearchText,
                Margin = new Padding(0, 2, 3, 2)
            };
            search.TextChanged += delegate
            {
                abilityFilterSearchText = search.Text;
                ApplyFilters();
            };
            panel.Controls.Add(search, 1, row);
            panel.SetColumnSpan(search, 2);
            panel.Controls.Add(MakeAbilityFilterToggleButton(null, null), 3, row);
        }

        private void AddAbilityFilterOptionRow(TableLayoutPanel panel, int row, string label, List<NamedRef> options, int preferredId, int selectedId, Action<int> setFilter)
        {
            var check = new CheckBox { Dock = DockStyle.Fill, Checked = selectedId > 0, Margin = new Padding(0, 6, 0, 0) };
            panel.Controls.Add(check, 0, row);
            panel.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(0)
            }, 1, row);

            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = selectedId > 0,
                Margin = new Padding(0, 3, 4, 3)
            };
            foreach (var option in options)
            {
                combo.Items.Add(new IdOption(option.id, LocalName(option.names)));
            }
            SelectAbilityFilterOption(combo, selectedId > 0 ? selectedId : preferredId);
            panel.Controls.Add(combo, 2, row);

            Button toggle = MakeAbilityFilterToggleButton(check, combo);
            panel.Controls.Add(toggle, 3, row);

            Action apply = delegate
            {
                combo.Enabled = check.Checked;
                toggle.Text = check.Checked ? "-" : "+";
                int id = -1;
                var selected = combo.SelectedItem as IdOption;
                if (check.Checked && selected != null) id = selected.Id;
                setFilter(id);
                ApplyFilters();
            };

            check.CheckedChanged += delegate { apply(); };
            combo.SelectedIndexChanged += delegate { if (check.Checked) apply(); };
            toggle.Click += delegate
            {
                check.Checked = !check.Checked;
            };
        }

        private static Button MakeAbilityFilterToggleButton(CheckBox check, ComboBox combo)
        {
            return new Button
            {
                Text = check != null && check.Checked ? "-" : "+",
                Dock = DockStyle.Fill,
                Enabled = check != null && combo != null,
                Margin = new Padding(1, 3, 0, 3)
            };
        }

        private List<NamedRef> AbilityFilterOptions(Func<AbilityEntry, NamedRef> selector)
        {
            var options = new Dictionary<int, NamedRef>();
            foreach (var ability in root.abilities)
            {
                NamedRef value = selector(ability);
                if (value != null && !options.ContainsKey(value.id))
                {
                    options.Add(value.id, value);
                }
            }
            return options.Values.OrderBy(v => LocalName(v.names)).ToList();
        }

        private static void SelectAbilityFilterOption(ComboBox combo, int id)
        {
            int fallback = combo.Items.Count > 0 ? 0 : -1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var option = combo.Items[i] as IdOption;
                if (option != null && option.Id == id)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (fallback >= 0) combo.SelectedIndex = fallback;
        }

        private DataGridView MakeAbilityPokemonGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.FromArgb(255, 250, 237),
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24,
                RowTemplate = { Height = 22 },
                ScrollBars = ScrollBars.Vertical
            };
            grid.Columns.Add(MakeTextColumn("number", "#", 44));
            grid.Columns.Add(MakeImageColumn("icon", "", 28));
            grid.Columns.Add(MakeTextColumn("name", "名字", 104));
            grid.Columns.Add(MakeTextColumn("type1", "属性", 54));
            grid.Columns.Add(MakeTextColumn("type2", "属性", 54));
            grid.Columns.Add(MakeTextColumn("ability1", "特性 1", 78));
            grid.Columns.Add(MakeTextColumn("ability2", "特性 2", 78));
            grid.Columns.Add(MakeTextColumn("hidden", "隐藏特性", 88));
            grid.Columns["name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["name"].MinimumWidth = 88;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count && grid.Rows[e.RowIndex].Tag is int)
                {
                    NavigateToPokemon((int)grid.Rows[e.RowIndex].Tag);
                }
            };
            return grid;
        }

        private void FillAbilityPokemonGrid(DataGridView grid, AbilityEntry ability)
        {
            grid.Rows.Clear();
            foreach (var pokemon in root.pokemon.Where(p => PokemonHasAbility(p, ability.id)).OrderBy(p => p.nationalDex).ThenBy(p => p.formId))
            {
                int rowIndex = grid.Rows.Add(
                    pokemon.nationalDex.ToString(),
                    LoadPokemonSmallCellImage(pokemon.legacyId),
                    LocalName(pokemon.names),
                    TypeNameAt(pokemon.types, 0),
                    TypeNameAt(pokemon.types, 1),
                    AbilitySlotText(pokemon, ability.id, 1),
                    AbilitySlotText(pokemon, ability.id, 2),
                    AbilitySlotText(pokemon, ability.id, 3)
                );
                grid.Rows[rowIndex].Tag = pokemon.legacyId;
                StyleAbilityPokemonGridRow(grid.Rows[rowIndex], pokemon);
            }
        }

        private static bool PokemonHasAbility(PokemonEntry pokemon, int abilityId)
        {
            if (pokemon.abilities == null) return false;
            return (pokemon.abilities.primary != null && pokemon.abilities.primary.id == abilityId) ||
                (pokemon.abilities.secondary != null && pokemon.abilities.secondary.id == abilityId) ||
                (pokemon.abilities.hidden != null && pokemon.abilities.hidden.id == abilityId);
        }

        private static string AbilitySlotText(PokemonEntry pokemon, int abilityId, int slot)
        {
            if (pokemon.abilities == null) return "--";
            NamedRef ability = slot == 1 ? pokemon.abilities.primary : (slot == 2 ? pokemon.abilities.secondary : pokemon.abilities.hidden);
            return ability != null && ability.id == abilityId ? LocalName(ability.names) : "--";
        }

        private static void StyleAbilityPokemonGridRow(DataGridViewRow row, PokemonEntry pokemon)
        {
            StyleAbilityTypeCell(row.Cells["type1"], TypeAt(pokemon.types, 0));
            StyleAbilityTypeCell(row.Cells["type2"], TypeAt(pokemon.types, 1));
        }

        private static void StyleAbilityTypeCell(DataGridViewCell cell, TypeRef type)
        {
            if (type == null)
            {
                cell.Value = "";
                return;
            }
            cell.Value = LocalName(type.names);
            cell.Style.BackColor = TypeColor(type.id);
            cell.Style.ForeColor = Color.White;
            cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            cell.Style.Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
        }

        private void ShowItem(ItemEntry i)
        {
            var stack = StartDetail(LocalName(i.names), "#" + i.id + " / " + EnglishName(i.names));
            stack.Controls.Add(MakeImageDescriptionPanel(ItemImagePath(i.id, true), LocalName(i.descriptions), 96, 96));
            stack.Controls.Add(MakeFacts(new[]
            {
                Tuple.Create("价格", ValueOrDash(i.price)),
                Tuple.Create("背包ID", ValueOrDash(i.bagId)),
                Tuple.Create("战斗中可用", YesNo(i.flags != null && i.flags.inBattle)),
                Tuple.Create("战斗外可用", YesNo(i.flags != null && i.flags.outBattle)),
                Tuple.Create("一次性", YesNo(i.flags != null && i.flags.oneTime)),
                Tuple.Create("携带有效", YesNo(i.flags != null && i.flags.heldEffect)),
                Tuple.Create("进化相关", YesNo(i.flags != null && i.flags.evolveRelated)),
                Tuple.Create("出现世代", ItemGenerationText(i))
            }));
        }

        private void ShowTypeEffect(TypeRef attackType)
        {
            details.Controls.Add(MakeTypeEffectMatrix());
        }

        private void ShowNature(NatureEntry n)
        {
            details.Controls.Add(MakeNatureEffectMatrix());
        }

        private Control MakeTypeEffectMatrix()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(255, 250, 237)
            };

            bool inverse = false;
            var holder = new Panel { AutoSize = true, Location = new Point(0, 0), BackColor = host.BackColor };
            host.Controls.Add(holder);

            Action rebuild = null;
            rebuild = delegate
            {
                holder.Controls.Clear();
                holder.Controls.Add(BuildTypeEffectMatrix(inverse, delegate
                {
                    inverse = !inverse;
                    rebuild();
                }));
            };
            rebuild();
            return host;
        }

        private Control BuildTypeEffectMatrix(bool inverse, Action toggleInverse)
        {
            var types = root.types.OrderBy(t => t.id).ToList();
            var chart = root.typeChart == null ? null : (inverse ? root.typeChart.inverse : root.typeChart.normal);
            if (chart == null || chart.Count == 0) return MakeBodyLabel("没有属性相性数据。");

            var table = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = types.Count + 2,
                RowCount = types.Count + 2,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                BackColor = Color.White,
                Margin = new Padding(0)
            };
            int attackLabelWidth = 34;
            int attackTypeWidth = 78;
            int availableWidth = Math.Max(760, details.ClientSize.Width - details.Padding.Horizontal - 96);
            int availableHeight = Math.Max(600, details.ClientSize.Height - details.Padding.Vertical - 36);
            int typeCellWidth = Math.Max(38, Math.Min(58, (availableWidth - attackLabelWidth - attackTypeWidth - 26) / Math.Max(1, types.Count)));
            int typeCellHeight = Math.Max(30, Math.Min(38, (availableHeight - 72) / Math.Max(1, types.Count)));
            int titleRowHeight = Math.Max(36, typeCellHeight + 4);
            int typeHeaderHeight = Math.Max(32, typeCellHeight);

            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, attackLabelWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, attackTypeWidth));
            foreach (var type in types) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, typeCellWidth));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, titleRowHeight));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, typeHeaderHeight));
            foreach (var type in types) table.RowStyles.Add(new RowStyle(SizeType.Absolute, typeCellHeight));

            var toggle = new Button
            {
                Text = inverse ? "还原" : "反转",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = Color.FromArgb(238, 238, 238),
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular),
                FlatStyle = FlatStyle.Standard
            };
            toggle.Click += delegate { toggleInverse(); };
            table.Controls.Add(toggle, 1, 0);

            var defenseLabel = MakeMatrixHeaderCell("防御方", Color.FromArgb(96, 134, 220), Color.White);
            table.Controls.Add(defenseLabel, 2, 0);
            table.SetColumnSpan(defenseLabel, types.Count);

            table.Controls.Add(MakeMatrixHeaderCell("攻击方", Color.FromArgb(210, 84, 82), Color.White), 0, 2);
            table.SetRowSpan(table.GetControlFromPosition(0, 2), types.Count);

            for (int i = 0; i < types.Count; i++)
            {
                TypeRef defenseType = types[i];
                table.Controls.Add(MakeMatrixTypeBadgeLabel(defenseType, typeCellWidth - 2, typeHeaderHeight - 4), i + 2, 1);
            }

            for (int rowIndex = 0; rowIndex < types.Count; rowIndex++)
            {
                TypeRef attackType = types[rowIndex];
                table.Controls.Add(MakeMatrixTypeBadgeLabel(attackType, attackTypeWidth - 4, typeCellHeight - 4), 1, rowIndex + 2);
                TypeChartRow chartRow = chart.FirstOrDefault(r => r.attackTypeId == attackType.id);
                for (int colIndex = 0; colIndex < types.Count; colIndex++)
                {
                    TypeRef defenseType = types[colIndex];
                    double multiplier = chartRow == null ? 1 : chartRow.GetMultiplier(defenseType.id);
                    table.Controls.Add(MakeTypeMultiplierCell(multiplier), colIndex + 2, rowIndex + 2);
                }
            }

            return table;
        }

        private static Control MakeTypeMultiplierCell(double multiplier)
        {
            Color backColor;
            Color foreColor = Color.Black;
            if (multiplier == 0)
            {
                backColor = Color.Black;
                foreColor = Color.White;
            }
            else if (multiplier > 1)
            {
                backColor = Color.FromArgb(110, 255, 125);
            }
            else if (multiplier < 1)
            {
                backColor = Color.FromArgb(255, 118, 128);
            }
            else
            {
                backColor = Color.White;
            }

            return MakeMatrixHeaderCell(multiplier.ToString("0.##"), backColor, foreColor);
        }

        private Control MakeNatureEffectMatrix()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(255, 250, 237)
            };
            host.Controls.Add(BuildNatureEffectMatrix());
            return host;
        }

        private Control BuildNatureEffectMatrix()
        {
            var headers = new[] { "攻击", "防御", "速度", "特攻", "特防" };
            var natures = root.natures.OrderBy(n => n.id).ToList();
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = headers.Length + 1,
                RowCount = natures.Count + 1,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                BackColor = Color.White,
                Margin = new Padding(0)
            };
            int availableWidth = Math.Max(720, details.ClientSize.Width - details.Padding.Horizontal - 96);
            int natureNameWidth = Math.Max(150, Math.Min(220, availableWidth / 4));
            int modifierWidth = Math.Max(92, (availableWidth - natureNameWidth - 12) / headers.Length);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, natureNameWidth));
            for (int i = 0; i < headers.Length; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, modifierWidth));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            for (int i = 0; i < natures.Count; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            table.Controls.Add(MakeMatrixHeaderCell("", Color.White, Color.Black), 0, 0);
            for (int col = 0; col < headers.Length; col++)
            {
                table.Controls.Add(MakeNatureHeaderBadge(headers[col]), col + 1, 0);
            }

            for (int row = 0; row < natures.Count; row++)
            {
                table.Controls.Add(MakeNatureNameCell(LocalName(natures[row].names)), 0, row + 1);
                for (int col = 0; col < headers.Length; col++)
                {
                    table.Controls.Add(MakeNatureModifierCell(NatureModifierAt(natures[row].modifiers, col)), col + 1, row + 1);
                }
            }

            return table;
        }

        private static Control MakeNatureHeaderBadge(string text)
        {
            return MakeMatrixHeaderCell(text, Color.FromArgb(183, 158, 133), Color.White);
        }

        private static Control MakeNatureNameCell(string text)
        {
            return MakeMatrixHeaderCell(text, Color.White, Color.Black, ContentAlignment.MiddleLeft);
        }

        private static Control MakeNatureModifierCell(double value)
        {
            if (value > 0) return MakeMatrixHeaderCell("10%", Color.FromArgb(110, 255, 125), Color.Black);
            if (value < 0) return MakeMatrixHeaderCell("-10%", Color.FromArgb(255, 118, 128), Color.Black);
            return MakeMatrixHeaderCell("—", Color.White, Color.Black);
        }

        private static double NatureModifierAt(NatureModifiers modifiers, int index)
        {
            if (modifiers == null) return 0;
            switch (index)
            {
                case 0: return modifiers.attack;
                case 1: return modifiers.defense;
                case 2: return modifiers.speed;
                case 3: return modifiers.specialAttack;
                case 4: return modifiers.specialDefense;
                default: return 0;
            }
        }

        private static CenteredBadgeLabel MakeMatrixHeaderCell(string text, Color backColor, Color foreColor)
        {
            return MakeMatrixHeaderCell(text, backColor, foreColor, ContentAlignment.MiddleCenter);
        }

        private static CenteredBadgeLabel MakeMatrixHeaderCell(string text, Color backColor, Color foreColor, ContentAlignment alignment)
        {
            return new CenteredBadgeLabel
            {
                Text = text,
                Dock = DockStyle.Fill,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular),
                TextAlign = alignment,
                Margin = new Padding(0)
            };
        }

        private static CenteredBadgeLabel MakeMatrixTypeBadgeLabel(TypeRef type, int width, int height)
        {
            return new CenteredBadgeLabel
            {
                Text = LocalName(type.names),
                Dock = DockStyle.Fill,
                Width = width,
                Height = height,
                BackColor = TypeColor(type.id),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0)
            };
        }

        private void ShowPlaceholder(string key)
        {
            if (key == "catch-calc") ShowCatchCalculator();
            else if (key == "iv-calc") ShowIvCalculator();
            else if (key == "coverage-calc") ShowCoverageCalculator();
            else if (key == "breed-calc") ShowBreedCalculator();
            else if (key == "speed-calc") ShowSpeedCalculator();
            else
            {
                var stack = StartDetail(GetModuleTitle(key), "与旧版入口一致。");
                stack.Controls.Add(MakeBodyLabel("这个模块暂未识别。"));
            }
        }

        private void ShowCatchCalculator()
        {
            var stack = StartDetail("捕获率计算器", "本地公式计算，不依赖联网。适合第四世代以后常规捕获估算。");
            var form = MakeFormTable();
            var maxHp = MakeNumberBox(1, 999, 100, 0);
            var currentHp = MakeNumberBox(1, 999, 1, 0);
            var catchRate = MakeNumberBox(1, 255, 45, 0);
            var ballBonus = MakeNumberBox(0, 20, 1, 2);
            var statusBonus = MakeNumberBox(1, 3, 1, 2);
            var result = MakeResultLabel();
            AddFormRow(form, 0, "最大HP", maxHp);
            AddFormRow(form, 1, "当前HP", currentHp);
            AddFormRow(form, 2, "捕获度", catchRate);
            AddFormRow(form, 3, "球修正", ballBonus);
            AddFormRow(form, 4, "状态修正", statusBonus);
            stack.Controls.Add(form);
            var button = MakeActionButton("计算捕获率");
            button.Click += delegate
            {
                double hpMax = ToDouble(maxHp.Value);
                double hpNow = Math.Min(hpMax, ToDouble(currentHp.Value));
                double a = ((3 * hpMax - 2 * hpNow) * ToDouble(catchRate.Value) * ToDouble(ballBonus.Value) * ToDouble(statusBonus.Value)) / (3 * hpMax);
                double chance = a >= 255 ? 1.0 : Math.Pow((1048560.0 / Math.Sqrt(Math.Sqrt(16711680.0 / Math.Max(1.0, a)))) / 65536.0, 4);
                result.Text = string.Format("修正捕获值: {0:0.##}    单球成功率: {1:P2}", a, Math.Min(1.0, chance));
            };
            stack.Controls.Add(button);
            stack.Controls.Add(result);
        }

        private void ShowIvCalculator()
        {
            var stack = StartDetail("个体值计算器", "输入单项能力值后反推可能 IV。按游戏实际取整枚举 0-31。");
            var form = MakeFormTable();
            var isHp = new CheckBox { Text = "HP 项", AutoSize = true, Checked = false };
            var baseStat = MakeNumberBox(1, 255, 100, 0);
            var level = MakeNumberBox(1, 100, 50, 0);
            var observed = MakeNumberBox(1, 999, 120, 0);
            var ev = MakeNumberBox(0, 252, 0, 0);
            var nature = MakeNumberBox(0, 2, 1, 2);
            var result = MakeResultLabel();
            AddFormRow(form, 0, "是否HP", isHp);
            AddFormRow(form, 1, "种族值", baseStat);
            AddFormRow(form, 2, "等级", level);
            AddFormRow(form, 3, "实际能力", observed);
            AddFormRow(form, 4, "努力值", ev);
            AddFormRow(form, 5, "性格修正", nature);
            stack.Controls.Add(form);
            var button = MakeActionButton("反推 IV");
            button.Click += delegate
            {
                var matches = new List<int>();
                for (int iv = 0; iv <= 31; iv++)
                {
                    int stat = CalculateStat((int)baseStat.Value, iv, (int)ev.Value, (int)level.Value, ToDouble(nature.Value), isHp.Checked);
                    if (stat == (int)observed.Value)
                    {
                        matches.Add(iv);
                    }
                }
                result.Text = matches.Count == 0 ? "没有匹配 IV。请检查等级、努力值、性格修正。" : "可能 IV: " + string.Join(", ", matches.Select(v => v.ToString()).ToArray());
            };
            stack.Controls.Add(button);
            stack.Controls.Add(result);
        }

        private void ShowCoverageCalculator()
        {
            var stack = StartDetail("打击面计算器", "选择最多四个攻击属性，计算对单属性和双属性防御组合的最佳倍率。");
            var form = MakeFormTable();
            var type1 = MakeTypeCombo();
            var type2 = MakeTypeCombo();
            var type3 = MakeTypeCombo();
            var type4 = MakeTypeCombo();
            AddFormRow(form, 0, "招式属性1", type1);
            AddFormRow(form, 1, "招式属性2", type2);
            AddFormRow(form, 2, "招式属性3", type3);
            AddFormRow(form, 3, "招式属性4", type4);
            stack.Controls.Add(form);
            var button = MakeActionButton("计算打击面");
            var summary = MakeResultLabel();
            var grid = MakeGrid(760, 360);
            grid.Columns.Add("防御组合", 180);
            grid.Columns.Add("最佳倍率", 90);
            grid.Columns.Add("最佳攻击属性", 130);
            grid.Columns.Add("评价", 120);
            button.Click += delegate
            {
                grid.Items.Clear();
                var attackTypes = SelectedTypeIds(type1, type2, type3, type4);
                int superEffective = 0;
                int resisted = 0;
                int noEffect = 0;
                foreach (var combo in BuildDefenseCombos())
                {
                    double best = -1;
                    int bestAttack = -1;
                    foreach (int attackId in attackTypes)
                    {
                        double value = ChartAttackMultiplier(attackId, combo.Item1) * (combo.Item2 <= 0 ? 1 : ChartAttackMultiplier(attackId, combo.Item2));
                        if (value > best)
                        {
                            best = value;
                            bestAttack = attackId;
                        }
                    }
                    if (best >= 2) superEffective++;
                    if (best > 0 && best <= 0.5) resisted++;
                    if (best == 0) noEffect++;
                    var item = new ListViewItem(DefenseComboName(combo.Item1, combo.Item2));
                    item.SubItems.Add(best.ToString("0.##"));
                    item.SubItems.Add(TypeNameById(bestAttack));
                    item.SubItems.Add(best >= 2 ? "有效" : (best == 0 ? "无效" : (best <= 0.5 ? "被抵抗" : "普通")));
                    grid.Items.Add(item);
                }
                summary.Text = string.Format("有效打击 {0} 组，被抵抗 {1} 组，无效 {2} 组。", superEffective, resisted, noEffect);
            };
            stack.Controls.Add(button);
            stack.Controls.Add(summary);
            stack.Controls.Add(grid);
        }

        private void ShowBreedCalculator()
        {
            var stack = StartDetail("遗传路线计算器", "先按旧库招式学习表查询遗传招式和可作为来源的学习者。");
            var form = MakeFormTable();
            var moveCombo = MakeMoveCombo();
            AddFormRow(form, 0, "目标招式", moveCombo);
            stack.Controls.Add(form);
            var button = MakeActionButton("查询遗传来源");
            var summary = MakeResultLabel();
            var grid = MakeGrid(780, 390);
            grid.Columns.Add("宝可梦", 150);
            grid.Columns.Add("蛋群", 170);
            grid.Columns.Add("来源", 90);
            grid.Columns.Add("版本", 90);
            grid.Columns.Add("Lv./方式", 110);
            button.Click += delegate
            {
                grid.Items.Clear();
                var option = moveCombo.SelectedItem as IdOption;
                if (option == null) return;
                List<LearnsetEntry> rows;
                if (!learnsetsByMoveId.TryGetValue(option.Id, out rows))
                {
                    summary.Text = "没有找到该招式的学习数据。";
                    return;
                }
                var grouped = rows
                    .GroupBy(r => r.pokemonId)
                    .Select(g => g.OrderBy(r => LearnLevelSort(r.levelId)).First())
                    .Take(500)
                    .ToList();
                foreach (var row in grouped)
                {
                    PokemonEntry p = FindPokemon(row.pokemonId);
                    if (p == null) continue;
                    var item = new ListViewItem(LocalName(p.names) + FormSuffix(p));
                    item.SubItems.Add(RefListText(p.eggGroups));
                    item.SubItems.Add(LearnSourceText(row.levelId));
                    item.SubItems.Add(GameName(row.gameId));
                    item.SubItems.Add(LevelName(row.levelId));
                    grid.Items.Add(item);
                }
                int eggCount = rows.Count(r => r.levelId == 301);
                summary.Text = string.Format("找到 {0} 条学习记录，其中遗传记录 {1} 条。完整亲代链路后续可基于蛋群继续展开。", rows.Count, eggCount);
            };
            stack.Controls.Add(button);
            stack.Controls.Add(summary);
            stack.Controls.Add(grid);
        }

        private void ShowSpeedCalculator()
        {
            var stack = StartDetail("速度线计算器", "计算实战速度，可设置性格、努力值、能力阶级和道具/场地倍率。");
            var form = MakeFormTable();
            var baseSpeed = MakeNumberBox(1, 255, 100, 0);
            var level = MakeNumberBox(1, 100, 50, 0);
            var iv = MakeNumberBox(0, 31, 31, 0);
            var ev = MakeNumberBox(0, 252, 252, 0);
            var nature = MakeNumberBox(0, 2, 1, 2);
            var stage = MakeNumberBox(-6, 6, 0, 0);
            var multiplier = MakeNumberBox(0, 8, 1, 2);
            var result = MakeResultLabel();
            AddFormRow(form, 0, "速度种族值", baseSpeed);
            AddFormRow(form, 1, "等级", level);
            AddFormRow(form, 2, "IV", iv);
            AddFormRow(form, 3, "努力值", ev);
            AddFormRow(form, 4, "性格修正", nature);
            AddFormRow(form, 5, "速度阶级", stage);
            AddFormRow(form, 6, "倍率", multiplier);
            stack.Controls.Add(form);
            var button = MakeActionButton("计算速度线");
            button.Click += delegate
            {
                int raw = CalculateStat((int)baseSpeed.Value, (int)iv.Value, (int)ev.Value, (int)level.Value, ToDouble(nature.Value), false);
                double staged = raw * StageMultiplier((int)stage.Value);
                int finalSpeed = (int)Math.Floor(staged * ToDouble(multiplier.Value));
                result.Text = string.Format("面板速度: {0}    阶级后: {1:0.##}    最终速度: {2}", raw, staged, finalSpeed);
            };
            stack.Controls.Add(button);
            stack.Controls.Add(result);
        }

        private Control MakeBadgeLine(List<TypeRef> types)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 12) };
            if (types != null) foreach (var type in types) panel.Controls.Add(MakeTypeBadge(type));
            return panel;
        }

        private static TabPage MakeTabPage(string title)
        {
            return new TabPage { Text = title, BackColor = Color.FromArgb(255, 250, 237), Padding = new Padding(10) };
        }

        private static FlowLayoutPanel MakeTabStack()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
        }

        private static ListView MakeGrid(int width, int height)
        {
            return new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(244, 234, 216)
            };
        }

        private static DataGridViewTextBoxColumn MakeTextColumn(string name, string title, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = title,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static DataGridViewImageColumn MakeImageColumn(string name, string title, int width)
        {
            return new DataGridViewImageColumn
            {
                Name = name,
                HeaderText = title,
                Width = width,
                ImageLayout = DataGridViewImageCellLayout.Normal,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private Image LoadCellImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;
            Image cached;
            if (cellImageCache.TryGetValue(imagePath, out cached)) return cached;
            using (Image image = Image.FromFile(imagePath))
            {
                cached = new Bitmap(image);
                cellImageCache[imagePath] = cached;
                return cached;
            }
        }

        private Image LoadPokemonSmallCellImage(int legacyId)
        {
            string imagePath = legacyId <= 0 ? "" : PokemonImagePath(legacyId, false);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;
            using (Image image = Image.FromFile(imagePath))
            {
                var bitmap = new Bitmap(24, 20);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    graphics.DrawImage(image, new Rectangle(0, 0, 24, 20));
                }
                return bitmap;
            }
        }

        private static Control MakeImageDescriptionPanel(string imagePath, string description, int imageWidth, int imageHeight)
        {
            var panel = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Width = 760,
                Margin = new Padding(0, 10, 0, 12)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, imageWidth + 18));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var picture = new PictureBox
            {
                Width = imageWidth,
                Height = imageHeight,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 12, 0)
            };

            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                picture.Image = Image.FromFile(imagePath);
            }

            panel.Controls.Add(picture, 0, 0);
            panel.Controls.Add(MakeBodyLabel(description), 1, 0);
            return panel;
        }

        private static TableLayoutPanel MakeFormTable()
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Width = 560,
                Margin = new Padding(0, 12, 0, 12)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            return table;
        }

        private static void AddFormRow(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            table.Controls.Add(MakeMutedLabel(label), 0, row);
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, 1, row);
        }

        private static NumericUpDown MakeNumberBox(decimal min, decimal max, decimal value, int decimals)
        {
            var box = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Min(max, Math.Max(min, value)),
                DecimalPlaces = decimals,
                Increment = decimals > 0 ? 0.1M : 1M
            };
            return box;
        }

        private static Button MakeActionButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                BackColor = Color.FromArgb(31, 111, 105),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 8, 0, 8),
                Padding = new Padding(18, 6, 18, 6)
            };
        }

        private static Label MakeResultLabel()
        {
            return new Label
            {
                Text = "等待计算。",
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(23, 32, 27),
                Margin = new Padding(0, 8, 0, 12)
            };
        }

        private ComboBox MakeTypeCombo()
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.Add(new IdOption(-1, "不选择"));
            foreach (var type in root.types.OrderBy(t => t.id))
            {
                combo.Items.Add(new IdOption(type.id, LocalName(type.names)));
            }
            combo.SelectedIndex = combo.Items.Count > 1 ? 1 : 0;
            return combo;
        }

        private ComboBox MakeMoveCombo()
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var move in root.moves.OrderBy(m => LocalName(m.names)))
            {
                combo.Items.Add(new IdOption(move.id, LocalName(move.names) + " / " + EnglishName(move.names)));
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return combo;
        }

        private static List<int> SelectedTypeIds(params ComboBox[] combos)
        {
            var selected = new List<int>();
            foreach (var combo in combos)
            {
                var option = combo.SelectedItem as IdOption;
                if (option != null && option.Id > 0 && !selected.Contains(option.Id))
                {
                    selected.Add(option.Id);
                }
            }
            if (selected.Count == 0 && combos.Length > 0)
            {
                var first = combos[0].SelectedItem as IdOption;
                if (first != null && first.Id > 0) selected.Add(first.Id);
            }
            return selected;
        }

        private IEnumerable<Tuple<int, int>> BuildDefenseCombos()
        {
            foreach (var type in root.types.OrderBy(t => t.id))
            {
                yield return Tuple.Create(type.id, -1);
            }
            var ordered = root.types.OrderBy(t => t.id).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    yield return Tuple.Create(ordered[i].id, ordered[j].id);
                }
            }
        }

        private double ChartAttackMultiplier(int attackTypeId, int defenseTypeId)
        {
            var chart = root.typeChart == null ? null : root.typeChart.normal;
            var row = chart == null ? null : chart.FirstOrDefault(r => r.attackTypeId == attackTypeId);
            return row == null ? 1 : row.GetMultiplier(defenseTypeId);
        }

        private string DefenseComboName(int firstTypeId, int secondTypeId)
        {
            string first = TypeNameById(firstTypeId);
            if (secondTypeId <= 0) return first;
            return first + " / " + TypeNameById(secondTypeId);
        }

        private string TypeNameById(int typeId)
        {
            var type = root.types.FirstOrDefault(t => t.id == typeId);
            return type == null ? "--" : LocalName(type.names);
        }

        private PokemonEntry FindPokemon(int legacyId)
        {
            PokemonEntry p;
            return pokemonByLegacyId.TryGetValue(legacyId, out p) ? p : null;
        }

        private void NavigateToPokemon(int legacyId)
        {
            PokemonEntry target = FindPokemon(legacyId);
            if (target == null) return;

            ListViewItem indexedItem;
            if (module.StartsWith("pokemon") && pokemonListItemsByLegacyId.TryGetValue(legacyId, out indexedItem))
            {
                SelectPokemonListItem(indexedItem, target);
                return;
            }

            SelectModule("pokemon");
            if (pokemonListItemsByLegacyId.TryGetValue(legacyId, out indexedItem))
            {
                SelectPokemonListItem(indexedItem, target);
                return;
            }

            ShowDetails(target);
        }

        private void NavigateToMove(int moveId)
        {
            MoveEntry target;
            if (!movesById.TryGetValue(moveId, out target)) return;

            ResetMoveModuleFilters();
            SelectModule("moves");

            foreach (ListViewItem item in list.Items)
            {
                var move = item.Tag as MoveEntry;
                if (move == null || move.id != moveId) continue;
                SelectListItem(item, target);
                return;
            }

            ShowDetails(target);
        }

        private void ResetMoveModuleFilters()
        {
            moveModuleFilterSearchText = "";
            moveModulePowerFilter.Reset();
            moveModuleAccuracyFilter.Reset();
            moveModulePpFilter.Reset();
            moveModulePriorityFilter.Reset();
            moveModuleMachineFilter = null;
            moveModuleTypeFilterId = -1;
            moveModuleCategoryFilterId = -1;
            moveModuleRangeFilter = null;
        }

        private void SelectPokemonListItem(ListViewItem targetItem, PokemonEntry target)
        {
            SelectListItem(targetItem, target);
        }

        private void SelectListItem(ListViewItem targetItem, object target)
        {
            suppressListSelectionChanged = true;
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items)
            {
                item.Selected = false;
            }
            targetItem.Selected = true;
            targetItem.Focused = true;
            targetItem.EnsureVisible();
            list.EndUpdate();
            suppressListSelectionChanged = false;
            list.Select();
            ShowDetails(target);
        }

        private string GameName(int gameId)
        {
            NamedRef game;
            return gamesById.TryGetValue(gameId, out game) ? LocalName(game.names) : "#" + gameId;
        }

        private string PokemonImagePath(int legacyId, bool big)
        {
            if (string.IsNullOrWhiteSpace(imageRoot)) return "";
            return Path.Combine(imageRoot, "pokemon", big ? "big" : "small", legacyId + ".png");
        }

        private string ItemImagePath(int itemId, bool big)
        {
            if (string.IsNullOrWhiteSpace(imageRoot)) return "";
            return Path.Combine(imageRoot, "items", big ? "big" : "small", itemId + ".png");
        }

        private string TypeImagePath(int typeId)
        {
            if (string.IsNullOrWhiteSpace(imageRoot)) return "";
            return Path.Combine(imageRoot, "types", "zhCN", typeId + ".png");
        }

        private string MoveCategoryImagePath(int categoryId)
        {
            if (string.IsNullOrWhiteSpace(imageRoot)) return "";
            return Path.Combine(imageRoot, "moves", "category", categoryId + ".png");
        }

        private string MoveRangeImagePath(object rangeId)
        {
            if (string.IsNullOrWhiteSpace(imageRoot) || rangeId == null) return "";
            int id;
            if (!int.TryParse(rangeId.ToString(), out id) || id < 0) return "";
            return Path.Combine(imageRoot, "moves", "range", id + ".png");
        }

        private string LevelName(int levelId)
        {
            NamedRef level;
            return levelsById.TryGetValue(levelId, out level) ? LocalName(level.names) : "#" + levelId;
        }

        private static int LearnLevelSort(int levelId)
        {
            if (levelId <= 100) return levelId;
            if (levelId >= 101 && levelId <= 200) return 1000 + levelId;
            return 2000 + levelId;
        }

        private static string LearnSourceText(int levelId)
        {
            if (levelId == 301) return "遗传";
            if (levelId == 302) return "教学";
            if (levelId == 303) return "进化前";
            if (levelId == 305) return "活动";
            if (levelId >= 101 && levelId <= 200) return "招式机";
            if (levelId >= 201 && levelId <= 250) return "秘传机";
            if (levelId <= 100) return "升级";
            return "其他";
        }

        private string GetDefenseMultiplierText(PokemonEntry p, int typeId)
        {
            if (p.typeDefense != null)
            {
                object value;
                if (p.typeDefense.TryGetValue(typeId.ToString(), out value) && value != null)
                {
                    return Convert.ToDouble(value).ToString("0.##");
                }
            }

            double valueFromTypes = 1;
            if (p.types != null)
            {
                foreach (var defendType in p.types)
                {
                    valueFromTypes *= ChartAttackMultiplier(typeId, defendType.id);
                }
            }
            return valueFromTypes.ToString("0.##");
        }

        private string EvolutionConditionText(EvolutionEntry evolution)
        {
            if (evolution.condition != null)
            {
                return LocalName(evolution.condition.names);
            }
            if (evolution.conditionValue == null || evolution.conditionValue.ToString() == "-1")
            {
                return "--";
            }
            if (evolution.conditionKind == "level")
            {
                return "Lv." + evolution.conditionValue;
            }
            if (evolution.conditionKind == "pokemon")
            {
                int pokemonId;
                if (int.TryParse(evolution.conditionValue.ToString(), out pokemonId))
                {
                    PokemonEntry p = FindPokemon(pokemonId);
                    if (p != null) return LocalName(p.names);
                }
            }
            return evolution.conditionValue.ToString();
        }

        private string BuildEvolutionSummary(EvolutionEntry evolution)
        {
            string method = evolution.method == null ? "--" : LocalName(evolution.method.names);
            string condition = EvolutionConditionText(evolution);
            if (condition == "--") return method;
            if (evolution.conditionKind == "level" && !method.Contains("Lv."))
            {
                return "[Lv. " + condition.Replace("Lv.", "") + "] " + method;
            }
            return condition + " / " + method;
        }

        private static int CalculateStat(int baseStat, int iv, int ev, int level, double nature, bool hp)
        {
            int baseValue = (int)Math.Floor(((2 * baseStat + iv + (int)Math.Floor(ev / 4.0)) * level) / 100.0);
            if (hp)
            {
                return baseValue + level + 10;
            }
            return (int)Math.Floor((baseValue + 5) * nature);
        }

        private static double StageMultiplier(int stage)
        {
            if (stage >= 0) return (2.0 + stage) / 2.0;
            return 2.0 / (2.0 - stage);
        }

        private static double ToDouble(decimal value)
        {
            return Convert.ToDouble(value);
        }

        private static Label MakeTypeBadge(TypeRef type)
        {
            return MakeTypeBadgeLabel(type, 64, 26, new Padding(0, 0, 8, 0));
        }

        private static CenteredBadgeLabel MakeTypeBadgeLabel(TypeRef type, int width, int height, Padding margin)
        {
            return new CenteredBadgeLabel
            {
                Text = LocalName(type.names),
                AutoSize = false,
                Width = width,
                Height = height,
                BackColor = TypeColor(type.id),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
                Margin = margin
            };
        }

        private static Control MakeFacts(IEnumerable<Tuple<string, string>> facts)
        {
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Width = 720, Margin = new Padding(0, 12, 0, 12) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            int i = 0;
            foreach (var fact in facts)
            {
                int col = i % 3;
                int row = i / 3;
                if (col == 0) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
                var panel = new Panel { Dock = DockStyle.Fill, Height = 64, Margin = new Padding(0, 0, 10, 10), BackColor = Color.FromArgb(244, 234, 216) };
                panel.Controls.Add(new Label { Text = fact.Item1, AutoSize = true, Location = new Point(10, 8), ForeColor = Color.FromArgb(106, 91, 69), Font = new Font("Segoe UI", 8f, FontStyle.Bold) });
                panel.Controls.Add(new Label { Text = string.IsNullOrWhiteSpace(fact.Item2) ? "--" : fact.Item2, AutoSize = true, Location = new Point(10, 31), ForeColor = Color.FromArgb(23, 32, 27), Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) });
                table.Controls.Add(panel, col, row);
                i++;
            }
            return table;
        }

        private static Control MakeStatsPanel(Stats stats)
        {
            if (stats == null)
            {
                return MakeBodyLabel("没有种族值数据。");
            }
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 230,
                ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 8)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddStat(panel, 0, "HP", stats.hp, 255);
            AddStat(panel, 1, "ATK", stats.attack, 255);
            AddStat(panel, 2, "DEF", stats.defense, 255);
            AddStat(panel, 3, "SpA", stats.specialAttack, 255);
            AddStat(panel, 4, "SpD", stats.specialDefense, 255);
            AddStat(panel, 5, "SPD", stats.speed, 255);
            AddStat(panel, 6, "Total", stats.total, 780);
            return panel;
        }

        private static void AddStat(TableLayoutPanel panel, int row, string label, int value, int max)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.Controls.Add(MakeMutedLabel(label), 0, row);
            panel.Controls.Add(new Label { Text = value.ToString(), AutoSize = true, Font = new Font("Consolas", 10f, FontStyle.Bold) }, 1, row);
            panel.Controls.Add(new ProgressBar { Minimum = 0, Maximum = max, Value = Math.Min(max, value), Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 7) }, 2, row);
        }

        private static Label MakeTitle(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Georgia", 28f, FontStyle.Bold), ForeColor = Color.FromArgb(23, 32, 27), Margin = new Padding(0, 0, 0, 4) };
        }

        private static Label MakeSectionTitle(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Georgia", 17f, FontStyle.Bold), ForeColor = Color.FromArgb(23, 32, 27), Margin = new Padding(0, 8, 0, 10) };
        }

        private static Label MakeBodyLabel(string text)
        {
            return new Label { Text = string.IsNullOrWhiteSpace(text) ? "暂无描述。" : text, AutoSize = true, MaximumSize = new Size(760, 0), Font = new Font("Segoe UI", 11f), ForeColor = Color.FromArgb(63, 51, 36), Margin = new Padding(0, 12, 0, 12) };
        }

        private static Label MakeMutedLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(106, 91, 69), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        }

        private static bool Match(string query, string text)
        {
            return query.Length == 0 || (text ?? "").ToLowerInvariant().Contains(query);
        }

        private static string SelectedValue(ComboBox combo)
        {
            var option = combo.SelectedItem as FilterOption;
            return option == null ? "" : option.Value;
        }

        private static string PokemonSearchText(PokemonEntry p)
        {
            return string.Join(" ", new[] { p.nationalDex.ToString(), p.legacyId.ToString(), Values(p.names), Values(p.formNames), Values(p.speciesNames), Values(p.descriptions), TypeText(p.types), AbilityText(p.abilities) });
        }

        private static string MoveSearchText(MoveEntry m)
        {
            return string.Join(" ", new[] { m.id.ToString(), Values(m.names), Values(m.descriptions), m.type == null ? "" : Values(m.type.names), m.category == null ? "" : Values(m.category.names) });
        }

        private static string AbilitySearchText(AbilityEntry a)
        {
            return string.Join(" ", new[] { a.id.ToString(), Values(a.names), Values(a.descriptions), a.trigger == null ? "" : Values(a.trigger.names), a.target == null ? "" : Values(a.target.names), a.effectOn == null ? "" : Values(a.effectOn.names) });
        }

        private static string ItemSearchText(ItemEntry i)
        {
            return string.Join(" ", new[] { i.id.ToString(), Values(i.names), Values(i.descriptions), ValueOrDash(i.price) });
        }

        private static string TypeText(List<TypeRef> types)
        {
            return types == null ? "" : string.Join(" / ", types.Select(t => LocalName(t.names)).Where(s => s.Length > 0).ToArray());
        }

        private static string TypeNameAt(List<TypeRef> types, int index)
        {
            if (types == null || index < 0 || index >= types.Count) return "";
            return LocalName(types[index].names);
        }

        private static TypeRef TypeAt(List<TypeRef> types, int index)
        {
            if (types == null || index < 0 || index >= types.Count) return null;
            return types[index];
        }

        private static void AddTypeSubItem(ListViewItem item, TypeRef type)
        {
            var sub = item.SubItems.Add(type == null ? "" : LocalName(type.names));
            if (type != null)
            {
                item.UseItemStyleForSubItems = false;
                sub.BackColor = TypeColor(type.id);
                sub.ForeColor = Color.White;
                sub.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            }
        }

        private static void AddCategorySubItem(ListViewItem item, NamedRef category)
        {
            string text = category == null ? "" : LocalName(category.names);
            var sub = item.SubItems.Add(text);
            item.UseItemStyleForSubItems = false;
            sub.BackColor = CategoryColor(text);
            sub.ForeColor = Color.White;
            sub.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        }

        private static Label MakeLegacySectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Width = 132,
                Height = 24,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(216, 230, 247),
                ForeColor = Color.FromArgb(23, 32, 27),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 3),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static string MoveValue(object value)
        {
            string text = ValueOrDash(value);
            return text == "-1" ? "--" : text;
        }

        private static void ApplyMoveTooltip(DataGridViewRow row, MoveEntry move)
        {
            string tooltip = MoveTooltipText(move);
            if (string.IsNullOrWhiteSpace(tooltip)) return;
            foreach (DataGridViewCell cell in row.Cells)
            {
                cell.ToolTipText = tooltip;
            }
        }

        private static string MoveTooltipText(MoveEntry move)
        {
            if (move == null) return "";
            string description = LocalName(move.descriptions);
            if (!string.IsNullOrWhiteSpace(description)) return description;
            return LocalName(move.names);
        }

        private static double ParseMultiplier(string value)
        {
            double result;
            return double.TryParse(value, out result) ? result : 1;
        }

        private static Color MultiplierColor(double multiplier)
        {
            if (multiplier == 0) return Color.FromArgb(170, 170, 170);
            if (multiplier >= 4) return Color.FromArgb(69, 217, 71);
            if (multiplier >= 2) return Color.FromArgb(108, 237, 116);
            if (multiplier < 1) return Color.FromArgb(255, 118, 128);
            return Color.FromArgb(238, 230, 197);
        }

        private static Color CategoryColor(string category)
        {
            if (category.Contains("物理") || category.IndexOf("Physical", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Color.FromArgb(211, 84, 37);
            }
            if (category.Contains("特殊") || category.IndexOf("Special", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Color.FromArgb(77, 98, 176);
            }
            return Color.FromArgb(142, 142, 142);
        }

        private static string RefListText(List<NamedRef> refs)
        {
            if (refs == null || refs.Count == 0) return "--";
            return string.Join(" / ", refs.Select(r => LocalName(r.names)).Where(s => s.Length > 0).ToArray());
        }

        private static string AbilityText(Abilities abilities)
        {
            if (abilities == null) return "";
            var parts = new List<string>();
            if (abilities.primary != null) parts.Add(LocalName(abilities.primary.names));
            if (abilities.secondary != null) parts.Add(LocalName(abilities.secondary.names));
            if (abilities.hidden != null) parts.Add(LocalName(abilities.hidden.names) + " (Hidden)");
            return string.Join(", ", parts.Where(s => s.Length > 0).ToArray());
        }

        private static string DexNumber(int number)
        {
            return "#" + number.ToString("0000");
        }

        private static string EnglishName(Dictionary<string, string> names)
        {
            return FromDictionary(names, "en", "zhCN");
        }

        private static string LocalName(Dictionary<string, string> names)
        {
            return FromDictionary(names, "zhCN", "en");
        }

        private static string FromDictionary(Dictionary<string, string> values, string first, string fallback)
        {
            if (values == null) return "";
            string value;
            if (values.TryGetValue(first, out value) && !string.IsNullOrWhiteSpace(value) && value != "---") return value;
            if (values.TryGetValue(fallback, out value) && !string.IsNullOrWhiteSpace(value) && value != "---") return value;
            return "";
        }

        private static string Values(Dictionary<string, string> values)
        {
            return values == null ? "" : string.Join(" ", values.Values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray());
        }

        private static string FormSuffix(PokemonEntry p)
        {
            string form = LocalName(p.formNames);
            return string.IsNullOrWhiteSpace(form) || form == "---" ? "" : " / " + form;
        }

        private static string ValueOrDash(object value)
        {
            return value == null ? "--" : value.ToString();
        }

        private static string YesNo(bool value)
        {
            return value ? "是" : "否";
        }

        private static bool ItemInGeneration(ItemEntry item, int gen)
        {
            if (item.flags == null) return false;
            switch (gen)
            {
                case 1: return item.flags.inGen1;
                case 2: return item.flags.inGen2;
                case 3: return item.flags.inGen3;
                case 4: return item.flags.inGen4;
                case 5: return item.flags.inGen5;
                case 6: return item.flags.inGen6;
                case 7: return item.flags.inGen7;
                default: return false;
            }
        }

        private static string ItemGenerationText(ItemEntry item)
        {
            if (item.flags == null) return "--";
            var gens = new List<string>();
            for (int i = 1; i <= 7; i++) if (ItemInGeneration(item, i)) gens.Add(i.ToString());
            return string.Join(", ", gens.ToArray());
        }

        private static string ModifierName(NatureModifiers m, bool positive)
        {
            if (m == null) return "--";
            if (positive && m.attack > 0) return "攻击";
            if (positive && m.defense > 0) return "防御";
            if (positive && m.speed > 0) return "速度";
            if (positive && m.specialAttack > 0) return "特攻";
            if (positive && m.specialDefense > 0) return "特防";
            if (!positive && m.attack < 0) return "攻击";
            if (!positive && m.defense < 0) return "防御";
            if (!positive && m.speed < 0) return "速度";
            if (!positive && m.specialAttack < 0) return "特攻";
            if (!positive && m.specialDefense < 0) return "特防";
            return "--";
        }

        private static string ModifierText(double value)
        {
            if (value > 0) return "+10%";
            if (value < 0) return "-10%";
            return "--";
        }

        private static string GetModuleTitle(string key)
        {
            switch (key)
            {
                case "catch-calc": return "捕获率计算器";
                case "iv-calc": return "个体值计算器";
                case "coverage-calc": return "打击面计算器";
                case "breed-calc": return "遗传路线计算器";
                case "speed-calc": return "速度线计算器";
                default: return key;
            }
        }

        private static Color TypeColor(int id)
        {
            switch (id)
            {
                case 2: return Color.FromArgb(160, 70, 49);
                case 3: return Color.FromArgb(101, 128, 167);
                case 4: return Color.FromArgb(140, 76, 153);
                case 5: return Color.FromArgb(166, 107, 53);
                case 6: return Color.FromArgb(132, 115, 76);
                case 7: return Color.FromArgb(114, 141, 44);
                case 8: return Color.FromArgb(93, 77, 130);
                case 9: return Color.FromArgb(102, 119, 124);
                case 10: return Color.FromArgb(201, 93, 43);
                case 11: return Color.FromArgb(46, 117, 167);
                case 12: return Color.FromArgb(65, 125, 72);
                case 13: return Color.FromArgb(196, 149, 33);
                case 14: return Color.FromArgb(184, 76, 115);
                case 15: return Color.FromArgb(72, 149, 155);
                case 16: return Color.FromArgb(75, 98, 168);
                case 17: return Color.FromArgb(68, 56, 51);
                case 18: return Color.FromArgb(180, 92, 134);
                default: return Color.FromArgb(125, 120, 101);
            }
        }
    }

    public sealed class CenteredBadgeLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var brush = new SolidBrush(ForeColor))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                e.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
            }
        }
    }

    public sealed class MoveNumericFilter
    {
        public MoveNumericFilter()
        {
            Operator = "=";
            ValueText = "0";
        }

        public void Reset()
        {
            Enabled = false;
            Operator = "=";
            Value = 0;
            ValueText = "0";
        }

        public bool Enabled { get; set; }
        public string Operator { get; set; }
        public decimal Value { get; set; }
        public string ValueText { get; set; }
    }

    public sealed class FilterOption
    {
        public FilterOption(string value, string label) { Value = value; Label = label; }
        public string Value { get; private set; }
        public string Label { get; private set; }
        public override string ToString() { return Label; }
    }

    public sealed class IdOption
    {
        public IdOption(int id, string label) { Id = id; Label = label; }
        public int Id { get; private set; }
        public string Label { get; private set; }
        public override string ToString() { return Label; }
    }

    public sealed class ListViewColumnComparer : System.Collections.IComparer
    {
        private readonly int column;
        private readonly bool ascending;

        public ListViewColumnComparer(int column, bool ascending)
        {
            this.column = column;
            this.ascending = ascending;
        }

        public int Compare(object x, object y)
        {
            var left = x as ListViewItem;
            var right = y as ListViewItem;
            if (left == null || right == null) return 0;

            string leftText = column < left.SubItems.Count ? left.SubItems[column].Text : "";
            string rightText = column < right.SubItems.Count ? right.SubItems[column].Text : "";

            int result;
            double leftNumber;
            double rightNumber;
            if (TryParseSortNumber(leftText, out leftNumber) && TryParseSortNumber(rightText, out rightNumber))
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else
            {
                result = string.Compare(leftText, rightText, StringComparison.CurrentCultureIgnoreCase);
            }

            if (result == 0 && column != 0)
            {
                result = CompareFirstColumn(left, right);
            }

            return ascending ? result : -result;
        }

        private static int CompareFirstColumn(ListViewItem left, ListViewItem right)
        {
            double leftNumber;
            double rightNumber;
            if (TryParseSortNumber(left.Text, out leftNumber) && TryParseSortNumber(right.Text, out rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }
            return string.Compare(left.Text, right.Text, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool TryParseSortNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string normalized = text.Trim().TrimStart('#').Replace(",", "");
            if (normalized == "--" || normalized == "-") return false;
            return double.TryParse(normalized, out value);
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
        public TypeChart typeChart { get; set; }
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
        public Abilities abilities { get; set; }
        public Stats stats { get; set; }
        public Measurements measurements { get; set; }
        public BreedingInfo breeding { get; set; }
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

    public sealed class Abilities
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
        public object heightMetric { get; set; }
        public object heightImperial { get; set; }
        public object weightMetric { get; set; }
        public object weightImperial { get; set; }
    }

    public sealed class BreedingInfo
    {
        public object hatchCycles { get; set; }
        public object baseTameness { get; set; }
        public object exp { get; set; }
        public object exp100 { get; set; }
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
        public NatureModifiers modifiers { get; set; }
    }

    public sealed class NatureModifiers
    {
        public double attack { get; set; }
        public double defense { get; set; }
        public double speed { get; set; }
        public double specialAttack { get; set; }
        public double specialDefense { get; set; }
    }

    public sealed class TypeChart
    {
        public List<TypeChartRow> normal { get; set; }
        public List<TypeChartRow> inverse { get; set; }
    }

    public sealed class TypeChartRow
    {
        public int attackTypeId { get; set; }
        public bool inverse { get; set; }
        public Dictionary<string, object> multipliers { get; set; }

        public double GetMultiplier(int typeId)
        {
            if (multipliers == null) return 1;
            object value;
            if (!multipliers.TryGetValue(typeId.ToString(), out value) || value == null) return 1;
            return Convert.ToDouble(value);
        }
    }
}
