using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace AnonPDF
{
    internal sealed class TutorialItem
    {
        internal TutorialItem(string title, string description, string file, int? order)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            File = file ?? string.Empty;
            Order = order;
        }

        internal string Title { get; }
        internal string Description { get; }
        internal string File { get; }
        internal int? Order { get; }
    }

    internal sealed class TutorialCatalog
    {
        private TutorialCatalog(string title, string description, List<TutorialItem> items)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Items = items ?? new List<TutorialItem>();
        }

        internal string Title { get; }
        internal string Description { get; }
        internal List<TutorialItem> Items { get; }

        internal static bool TryLoad(string jsonPath, out TutorialCatalog catalog, out string errorMessage)
        {
            catalog = null;
            errorMessage = string.Empty;

            try
            {
                var token = JToken.Parse(File.ReadAllText(jsonPath));
                if (token is JObject obj)
                {
                    var items = ParseItems(obj["items"] as JArray);
                    catalog = new TutorialCatalog(
                        title: (string)obj["title"],
                        description: (string)obj["description"],
                        items: SortItems(items));
                    return true;
                }

                if (token is JArray array)
                {
                    var items = ParseItems(array);
                    catalog = new TutorialCatalog(
                        title: string.Empty,
                        description: string.Empty,
                        items: SortItems(items));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static List<TutorialItem> ParseItems(JArray array)
        {
            var items = new List<TutorialItem>();
            if (array == null)
            {
                return items;
            }

            foreach (var obj in array.OfType<JObject>())
            {
                string file = (string)obj["file"] ?? (string)obj["mp4"] ?? (string)obj["video"];
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                string title = (string)obj["title"] ?? (string)obj["name"];
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = Path.GetFileNameWithoutExtension(file);
                }

                string description = (string)obj["description"];
                int? order = ParseOrder(obj, file);
                items.Add(new TutorialItem(title, description, file, order));
            }

            return items;
        }

        private static int? ParseOrder(JObject obj, string file)
        {
            if (obj == null)
            {
                return null;
            }

            var orderToken = obj["order"] ?? obj["number"] ?? obj["index"];
            if (orderToken != null && int.TryParse(orderToken.ToString(), out int orderValue))
            {
                return orderValue;
            }

            if (!string.IsNullOrWhiteSpace(file))
            {
                var match = Regex.Match(Path.GetFileNameWithoutExtension(file), @"^(\\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int fileOrder))
                {
                    return fileOrder;
                }
            }

            return null;
        }

        private static List<TutorialItem> SortItems(List<TutorialItem> items)
        {
            return items
                .OrderBy(item => item.Order ?? int.MaxValue)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    internal sealed class TutorialBrowserForm : Form
    {
        private readonly string tutorialDirectory;
        private readonly ListView listView;

        internal TutorialBrowserForm(TutorialCatalog catalog, string tutorialDirectory)
        {
            this.tutorialDirectory = tutorialDirectory;

            Text = string.IsNullOrWhiteSpace(catalog.Title)
                ? Properties.Resources.Menu_Help_Tutorial
                : catalog.Title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(720, 420);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleLabel = new Label
            {
                Text = Text,
                AutoSize = true,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x1F, 0x2A, 0x33),
                Dock = DockStyle.Fill
            };

            var descriptionLabel = new Label
            {
                Text = catalog.Description ?? string.Empty,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x55, 0x62, 0x70),
                Dock = DockStyle.Fill
            };

            if (string.IsNullOrWhiteSpace(descriptionLabel.Text))
            {
                descriptionLabel.Visible = false;
                layout.RowStyles[1] = new RowStyle(SizeType.Absolute, 0F);
            }

            listView = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Activation = ItemActivation.OneClick,
                HotTracking = true,
                HoverSelection = true,
                Dock = DockStyle.Fill
            };
            listView.Columns.Add(Properties.Resources.Tutorial_Column_Title, 240);
            listView.Columns.Add(Properties.Resources.Tutorial_Column_Description, 440);

            foreach (var item in catalog.Items)
            {
                string titleText = item.Title;
                if (item.Order.HasValue)
                {
                    titleText = $"{item.Order.Value}. {item.Title}";
                }

                var listItem = new ListViewItem(titleText)
                {
                    Tag = item
                };
                listItem.SubItems.Add(item.Description);
                listView.Items.Add(listItem);
            }

            listView.ItemActivate += (_, __) => PlaySelectedItem();
            listView.Resize += (_, __) => ResizeListColumns();

            layout.Controls.Add(titleLabel, 0, 0);
            layout.Controls.Add(descriptionLabel, 0, 1);
            layout.Controls.Add(listView, 0, 2);
            Controls.Add(layout);

            ResizeListColumns();
        }

        private void ResizeListColumns()
        {
            if (listView.Columns.Count < 2)
            {
                return;
            }

            int total = listView.ClientSize.Width;
            int titleWidth = Math.Min(280, Math.Max(200, total / 3));
            listView.Columns[0].Width = titleWidth;
            listView.Columns[1].Width = Math.Max(100, total - titleWidth - SystemInformation.VerticalScrollBarWidth - 4);
        }

        private void PlaySelectedItem()
        {
            if (listView.SelectedItems.Count == 0)
            {
                return;
            }

            if (!(listView.SelectedItems[0].Tag is TutorialItem item))
            {
                return;
            }

            OpenTutorialVideo(item);
        }

        private void OpenTutorialVideo(TutorialItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.File))
            {
                MessageBox.Show(this, Properties.Resources.Tutorial_InvalidFormat,
                    Properties.Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string videoPath = Path.Combine(tutorialDirectory, item.File);
            if (!File.Exists(videoPath))
            {
                MessageBox.Show(this, string.Format(Properties.Resources.Tutorial_NotFound, videoPath),
                    Properties.Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                if (Owner is PDFForm pdfForm)
                {
                    pdfForm.SuspendTopMostForExternalLaunch();
                }

                var startInfo = new ProcessStartInfo(videoPath)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Properties.Resources.Tutorial_OpenError, ex.Message),
                    Properties.Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
