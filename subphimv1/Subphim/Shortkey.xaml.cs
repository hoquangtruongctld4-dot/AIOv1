using System.Windows;
using System.Windows.Input;

namespace subphimv1
{

    public partial class ShortcutsWindow : Window
    {
        public ShortcutsWindow()
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;
        }
    
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }

        }
    }
}
