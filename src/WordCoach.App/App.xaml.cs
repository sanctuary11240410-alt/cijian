using System.Windows;

namespace WordCoach.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            MainWindow = new MainWindow();
            if (e.Args.Contains("--verify-startup"))
            {
                MainWindow.Close();
                Shutdown(0);
                return;
            }
            MainWindow.Show();
        }
        catch (Exception error)
        {
            if (e.Args.Contains("--verify-startup")) { Shutdown(1); return; }
            MessageBox.Show("无法打开学习记录：" + error.Message + "\n如果程序已在运行，请返回已打开的窗口。\n现有数据未被清空。",
                "词间", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }
}
