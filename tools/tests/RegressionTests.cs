using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PodexDesktop;
using PodexTools;

namespace PodexRegressionTests
{
    internal static class RegressionTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            RunSummary summary;
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                TestRunSummary();
                TestPreservedDescriptions();
                TestPreservedDescriptionsFallbackAndOverlay();
                TestPrettyPrintedJson();
                TestTypeEffectAxisLabels();
                summary = BuildRunSummary(null);
            }
            catch (Exception ex)
            {
                summary = BuildRunSummary(ex);
            }

            if (ShouldShowDialog(args))
            {
                MessageBox.Show(
                    summary.Message,
                    summary.Title,
                    MessageBoxButtons.OK,
                    summary.ExitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }
            else if (summary.ExitCode == 0)
            {
                Console.WriteLine(summary.Message);
            }
            else
            {
                Console.Error.WriteLine(summary.Message);
            }

            return summary.ExitCode;
        }

        private static void TestRunSummary()
        {
            RunSummary success = BuildRunSummary(null);
            RunSummary failure = BuildRunSummary(new InvalidOperationException("boom"));

            Assert(success.ExitCode == 0, "Expected success summary to return exit code 0.");
            Assert(success.Title == "Podex Regression Tests", "Expected success summary title to stay stable for direct-run users.");
            Assert(success.Message.Contains("passed"), "Expected success summary to mention passing tests.");

            Assert(failure.ExitCode == 1, "Expected failure summary to return exit code 1.");
            Assert(failure.Title == "Podex Regression Tests Failed", "Expected failure summary title to clearly mark a failed run.");
            Assert(failure.Message.Contains("boom"), "Expected failure summary to include the thrown error message.");
        }

        private static RunSummary BuildRunSummary(Exception failure)
        {
            if (failure == null)
            {
                return new RunSummary
                {
                    ExitCode = 0,
                    Title = "Podex Regression Tests",
                    Message =
                        "Regression tests passed." + Environment.NewLine + Environment.NewLine +
                        "Checked:" + Environment.NewLine +
                        "- zhCN description preservation" + Environment.NewLine +
                        "- pretty-printed preview JSON" + Environment.NewLine +
                        "- inverse type-effect axis labels"
                };
            }

            return new RunSummary
            {
                ExitCode = 1,
                Title = "Podex Regression Tests Failed",
                Message =
                    "Regression tests failed." + Environment.NewLine + Environment.NewLine +
                    failure.Message
            };
        }

        private static bool ShouldShowDialog(string[] args)
        {
            if (args == null) return true;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static void TestPreservedDescriptions()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "podex-preserved-zhcn-test.json");
            File.WriteAllText(
                tempPath,
                "{\"moves\":[{\"id\":729,\"descriptions\":{\"zhCN\":\"\\u624b\\u5de5\\u62db\\u5f0f\\u63cf\\u8ff0\"}}],\"abilities\":[{\"id\":277,\"descriptions\":{\"zhCN\":\"\\u624b\\u5de5\\u7279\\u6027\\u63cf\\u8ff0\"}}],\"items\":[{\"id\":253,\"descriptions\":{\"zhCN\":\"\\u624b\\u5de5\\u9053\\u5177\\u63cf\\u8ff0\"}}]}",
                Encoding.UTF8);

            try
            {
                Dictionary<int, string> preservedMoves = ImportData.RegressionHooks.LoadPreservedMoveDescriptions(tempPath);
                Dictionary<int, string> preservedAbilities = ImportData.RegressionHooks.LoadPreservedAbilityDescriptions(tempPath);
                Dictionary<int, string> preservedItems = ImportData.RegressionHooks.LoadPreservedItemDescriptions(tempPath);

                Assert(preservedMoves.ContainsKey(729), "Expected move zhCN description to load from existing preview JSON.");
                Assert(preservedAbilities.ContainsKey(277), "Expected ability zhCN description to load from existing preview JSON.");
                Assert(preservedItems.ContainsKey(253), "Expected item zhCN description to load from existing preview JSON.");

                var move = new Dictionary<string, object>
                {
                    { "id", 729 },
                    { "descriptions", new Dictionary<string, string> { { "zhCN", "\u539f\u59cb\u62db\u5f0f\u63cf\u8ff0" } } }
                };
                var ability = new Dictionary<string, object>
                {
                    { "id", 277 },
                    { "descriptions", new Dictionary<string, string> { { "zhCN", "\u539f\u59cb\u7279\u6027\u63cf\u8ff0" } } }
                };
                var item = new Dictionary<string, object>
                {
                    { "id", 253 },
                    { "descriptions", new Dictionary<string, string> { { "zhCN", "\u539f\u59cb\u9053\u5177\u63cf\u8ff0" } } }
                };

                int changed = 0;
                changed += ImportData.RegressionHooks.ApplyPreservedZhCnDescription(move, preservedMoves);
                changed += ImportData.RegressionHooks.ApplyPreservedZhCnDescription(ability, preservedAbilities);
                changed += ImportData.RegressionHooks.ApplyPreservedZhCnDescription(item, preservedItems);

                Assert(changed == 3, "Expected preserved zhCN descriptions to overwrite all three entity descriptions.");
                Assert(ReadZhCn(move) == "\u624b\u5de5\u62db\u5f0f\u63cf\u8ff0", "Expected move zhCN description to be preserved.");
                Assert(ReadZhCn(ability) == "\u624b\u5de5\u7279\u6027\u63cf\u8ff0", "Expected ability zhCN description to be preserved.");
                Assert(ReadZhCn(item) == "\u624b\u5de5\u9053\u5177\u63cf\u8ff0", "Expected item zhCN description to be preserved.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static void TestPreservedDescriptionsFallbackAndOverlay()
        {
            string dataPath = Path.Combine(Path.GetTempPath(), "podex-preserved-data.json");
            string previewPath = Path.Combine(Path.GetTempPath(), "podex-preserved-preview.json");

            File.WriteAllText(
                dataPath,
                "{\"moves\":[{\"id\":729,\"descriptions\":{\"zhCN\":\"\\u6570\\u636e\\u6587\\u4ef6\\u63cf\\u8ff0\"}}],\"abilities\":[{\"id\":277,\"descriptions\":{\"zhCN\":\"\\u6570\\u636e\\u6587\\u4ef6\\u7279\\u6027\"}}],\"items\":[{\"id\":253,\"descriptions\":{\"zhCN\":\"\\u6570\\u636e\\u6587\\u4ef6\\u9053\\u5177\"}}]}",
                Encoding.UTF8);

            try
            {
                Dictionary<int, string> fallbackMoves = ImportData.RegressionHooks.LoadMergedPreservedMoveDescriptions(dataPath, previewPath);
                Assert(fallbackMoves.ContainsKey(729) && fallbackMoves[729] == "\u6570\u636e\u6587\u4ef6\u63cf\u8ff0", "Expected missing preview output to fall back to the current data file's zhCN move description.");

                File.WriteAllText(
                    previewPath,
                    "{\"moves\":[{\"id\":729,\"descriptions\":{\"zhCN\":\"\\u8f93\\u51fa\\u6587\\u4ef6\\u63cf\\u8ff0\"}}],\"abilities\":[{\"id\":277,\"descriptions\":{\"zhCN\":\"\\u8f93\\u51fa\\u6587\\u4ef6\\u7279\\u6027\"}}],\"items\":[{\"id\":253,\"descriptions\":{\"zhCN\":\"\\u8f93\\u51fa\\u6587\\u4ef6\\u9053\\u5177\"}}]}",
                    Encoding.UTF8);

                Dictionary<int, string> overlaidMoves = ImportData.RegressionHooks.LoadMergedPreservedMoveDescriptions(dataPath, previewPath);
                Dictionary<int, string> overlaidAbilities = ImportData.RegressionHooks.LoadMergedPreservedAbilityDescriptions(dataPath, previewPath);
                Dictionary<int, string> overlaidItems = ImportData.RegressionHooks.LoadMergedPreservedItemDescriptions(dataPath, previewPath);

                Assert(overlaidMoves[729] == "\u8f93\u51fa\u6587\u4ef6\u63cf\u8ff0", "Expected existing preview output to override the current data file's move description.");
                Assert(overlaidAbilities[277] == "\u8f93\u51fa\u6587\u4ef6\u7279\u6027", "Expected existing preview output to override the current data file's ability description.");
                Assert(overlaidItems[253] == "\u8f93\u51fa\u6587\u4ef6\u9053\u5177", "Expected existing preview output to override the current data file's item description.");
            }
            finally
            {
                if (File.Exists(dataPath)) File.Delete(dataPath);
                if (File.Exists(previewPath)) File.Delete(previewPath);
            }
        }

        private static void TestPrettyPrintedJson()
        {
            string formatted = ImportData.RegressionHooks.PrettyPrintJson("{\"meta\":{\"count\":1},\"moves\":[{\"id\":1}]}");
            Assert(formatted.Contains(Environment.NewLine), "Expected preview JSON serializer helper to pretty-print with line breaks.");
            Assert(formatted.Contains("  \"moves\""), "Expected preview JSON serializer helper to indent child properties.");
        }

        private static void TestTypeEffectAxisLabels()
        {
            string[] normal = MainForm.GetTypeEffectAxisLabels(false);
            string[] inverse = MainForm.GetTypeEffectAxisLabels(true);

            Assert(normal.Length == 2 && normal[0] == "\u9632\u5fa1\u65b9" && normal[1] == "\u653b\u51fb\u65b9", "Expected normal matrix labels to keep defense on top and attack on the side.");
            Assert(inverse.Length == 2 && inverse[0] == "\u653b\u51fb\u65b9" && inverse[1] == "\u9632\u5fa1\u65b9", "Expected inverse matrix labels to swap top and side titles.");
        }

        private static string ReadZhCn(Dictionary<string, object> row)
        {
            var descriptions = row["descriptions"] as Dictionary<string, string>;
            return descriptions != null && descriptions.ContainsKey("zhCN") ? descriptions["zhCN"] : "";
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class RunSummary
        {
            public int ExitCode { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
        }
    }
}
