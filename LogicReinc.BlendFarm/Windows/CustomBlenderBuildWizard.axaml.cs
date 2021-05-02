using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public class CustomBlenderBuildWizard : Window
    {
        private StackPanel _interfaceWarning = null;
        private StackPanel _interfaceName = null;
        private StackPanel _interfaceInstall = null;
        private StackPanel _interfaceComplete = null;

        private TextBox _outputPath = null;

        public string VersionName { get; set; }

        public CustomBlenderBuildWizard()
        {
            DataContext = this;
            Width = 800;
            Height = 450;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _interfaceWarning = this.FindControl<StackPanel>("interfaceWarning");
            _interfaceName = this.FindControl<StackPanel>("interfaceName");
            _interfaceInstall = this.FindControl<StackPanel>("interfaceInstall");
            _interfaceComplete = this.FindControl<StackPanel>("interfaceComplete");

            _outputPath = this.FindControl<TextBox>("outputPath");

            ShowInterfaceWarning();
            //Focus();
        }

        public static async Task Show(Window owner)
        {
            CustomBlenderBuildWizard window = new CustomBlenderBuildWizard();

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }

        public void ShowInterfaceWarning()
        {
            HideInterfaces();
            _interfaceWarning.IsVisible = true;
        }
        public void ShowInterfaceName()
        {
            HideInterfaces();
            _interfaceName.IsVisible = true;
        }
        public async void ShowInterfaceInstall()
        {
            HideInterfaces();

            if (VersionName == null)
            {
                await MessageWindow.Show(this, "Name missing", "No version name was provided");
                ShowInterfaceName();
            }
            else
            {
                List<BlenderVersion> existing = BlenderVersion.GetBlenderVersions(SystemInfo.RelativeToApplicationDirectory("VersionCache"), SystemInfo.RelativeToApplicationDirectory("VersionCustom"));
                if (existing.Any(x => x.Name.ToLower() == VersionName.ToLower()))
                {
                    await MessageWindow.Show(this, "Name already exists", "This version name already exists");
                    ShowInterfaceName();
                }
                else
                {
                    string path = BlenderManager.GetVersionPath(SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlenderData), VersionName, SystemInfo.GetOSName());
                    _outputPath.Text = Path.GetFullPath(path);
                    _interfaceInstall.IsVisible = true;
                }
            }
        }
        public async void ShowInterfaceComplete()
        {
            HideInterfaces();

            string blenderData = SystemInfo.RelativeToApplicationDirectory("BlenderData");
            string executable = BlenderManager.GetVersionExecutablePath(blenderData, VersionName);

            if (!BlenderManager.IsVersionValid(blenderData, VersionName))
            {
                await MessageWindow.Show(this, "Missing Installation", $"Expecting Blender executable on path\n{executable}");
                ShowInterfaceInstall();
            }
            else
            {
                List<string> lines = BlenderVersion.GetCustomBlenderVersions(SystemInfo.RelativeToApplicationDirectory("VersionCustom")).Select(x => x.Name).ToList();
                lines.Add(VersionName);
                File.WriteAllLines(SystemInfo.RelativeToApplicationDirectory("VersionCustom"), lines.ToArray());

                _interfaceComplete.IsVisible = true;
            }
        }
        private void HideInterfaces()
        {
            _interfaceWarning.IsVisible = false;
            _interfaceName.IsVisible = false;
            _interfaceInstall.IsVisible = false;
            _interfaceComplete.IsVisible = false;
        }

    }
}
