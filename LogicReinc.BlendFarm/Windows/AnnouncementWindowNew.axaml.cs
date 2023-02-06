using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LogicReinc.BlendFarm.Windows
{
    public partial class AnnouncementWindowNew : Window
    {
        public AnnouncementWindowNew()
        {
            InitializeComponent();
        }

        public void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
