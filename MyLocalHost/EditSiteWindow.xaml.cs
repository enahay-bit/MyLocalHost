using System.IO;
using System.Windows;
using Microsoft.Win32;
using MyLocalHost.ViewModels;

namespace MyLocalHost;

public partial class EditSiteWindow : Window
{
    private readonly EditSiteViewModel _vm;

    public EditSiteWindow(EditSiteViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "配信するフォルダを選択" };
        if (Directory.Exists(_vm.Folder)) dialog.InitialDirectory = _vm.Folder;

        if (dialog.ShowDialog(this) == true)
        {
            _vm.Folder = dialog.FolderName;
            // 名前が空なら、フォルダ名を仮に入れておく
            if (string.IsNullOrWhiteSpace(_vm.Name))
                _vm.Name = new DirectoryInfo(dialog.FolderName).Name;
        }
    }

    private void BrowseStartPage_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_vm.Folder))
        {
            MessageBox.Show(this, "先にフォルダを指定してください。", Title);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "最初に開くページを選択",
            InitialDirectory = _vm.Folder,
            Filter = "HTML ファイル (*.html;*.htm)|*.html;*.htm|すべてのファイル (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            _vm.TrySetStartPageFromFile(dialog.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Validate())
            DialogResult = true;
    }
}
