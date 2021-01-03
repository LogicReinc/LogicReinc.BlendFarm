using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public class YesNoWindow : Window
    {
        public string MsgTitle { get; set; }
        public string Description { get; set; }

        public bool Response { get; set; } = false;

        public YesNoWindow(string title, string desc)
        {
            MsgTitle = title;
            Description = desc;
            Init();
        }
        public YesNoWindow()
        {
            Init();
        }

        public void Init()
        {
            this.DataContext = this;
            Height = 200;
            Width = 500;
            CanResize = false;

            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }


        public void TriggerYes()
        {
            Response = true;
            this.Close();
        }

        public void TriggerNo()
        {
            Response = false;
            this.Close();
        }

        public static async Task<bool> Show(Window owner, string title, string desc)
        {
            var window = new YesNoWindow(title, desc);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
            return window.Response;
        }
    }
}
