using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    public class ProjectWindow : Window
    {
        static List<BlenderVersion> versions = null;

        private TextBox fileSelection = null;
        private ComboBox comboVersions = null;
        private ListBox history = null;

        private bool _startedNew = false;

        public ProjectWindow()
        {
            versions = BlenderVersion.GetBlenderVersions();
            DataContext = this;

            LocalServer.Start();
            Closed += (a, b) =>
            {
                if (!_startedNew)
                    LocalServer.Stop();
            };

            this.InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Width = 600;
            Height = 570;

            fileSelection = this.FindControl<TextBox>("fileSelect");

            comboVersions = this.FindControl<ComboBox>("versionSelect");
            comboVersions.Items = versions;
            comboVersions.SelectedIndex = 0;

            history = this.FindControl<ListBox>("history");
            history.Items = BlendFarmSettings.Instance.History.ToList();
            history.SelectedItem = null;
            history.SelectionChanged += (a, b) =>{
                if (b.AddedItems.Count > 0)
                {
                    string list = ((HistoryEntry)b.AddedItems[0]).Path;
                    //history.SelectedItems = null;

                    fileSelection.Text = list;
                }
            };
        }

        public async void ShowFileDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter()
            {
                Name = "Blender File",
                Extensions = { "blend" }
            });
            dialog.AllowMultiple = false;
            string[] results = await dialog.ShowAsync(this);
            if(results.Length > 0)
            {
                fileSelection.Text = results[0];
            }
        }

        public void LoadProject()
        {
            string file = fileSelection.Text;
            BlenderVersion version = (BlenderVersion)comboVersions.SelectedItem;

            if (!File.Exists(file))
            {
                MessageWindow.Show(this, "File not found", $"{file} does not exist");
                return;
            }

            string path = Path.GetFullPath(file);

            HistoryEntry entry = BlendFarmSettings.Instance.History.FirstOrDefault(x => x.Path == path);
            if (entry != null)
                BlendFarmSettings.Instance.History.Remove(entry);

            BlendFarmSettings.Instance.History.Add(new BlendFarmSettings.HistoryEntry()
            {
                Name = Path.GetFileName(file),
                Path = file,
                Date = DateTime.Now
            });
            BlendFarmSettings.Instance.History = BlendFarmSettings.Instance.History.OrderByDescending(x => x.Date).Take(10).ToList();
            BlendFarmSettings.Instance.Save();

            _startedNew = true;
            new RenderWindow(version, path).Show();

            this.Close();
        }
    }
}
