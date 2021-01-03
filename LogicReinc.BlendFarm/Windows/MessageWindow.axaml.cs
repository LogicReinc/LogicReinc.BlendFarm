using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public class MessageWindow : Window
    {
        public string MsgTitle { get; set; }
        public string Description { get; set; }

        public MessageWindow(string title, string desc)
        {
            MsgTitle = title;
            Description = desc;
            Init();
        }
        public MessageWindow()
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


        public void TriggerOk()
        {
            this.Close();
        }


        public static async Task Show(Window owner, string title, string desc)
        {
            var window = new MessageWindow(title, desc);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
