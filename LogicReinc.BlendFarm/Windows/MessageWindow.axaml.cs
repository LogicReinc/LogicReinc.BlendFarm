using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public class MessageWindow : Window
    {
        public string MsgTitle { get; set; }
        public string Description { get; set; }

        public MessageWindow(string title, string desc, int width = 500, int height = 200)
        {
            MsgTitle = title;
            Description = desc;
            Init(width, height);
        }
        public MessageWindow()
        {
            Init(500, 200);
        }

        public void Init(int width, int height)
        {
            this.DataContext = this;
            Height = height;
            Width = width;
            MinHeight = height;
            //MaxHeight = height;
            MinWidth = width;
            MaxWidth = width;
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
            await Show(owner, title, desc, 500, 200);
        }
        public static async Task ShowOnUIThread(Window owner, string title, string desc)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Show(owner, title, desc));
        }

        public static async Task Show(Window owner, string title, string desc, int width, int height)
        {
            var window = new MessageWindow(title, desc, width, height);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
