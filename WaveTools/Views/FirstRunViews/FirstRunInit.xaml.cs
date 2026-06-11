// Copyright (c) 2021-2024, JamXi JSG-LLC.
// All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using WaveTools.Depend;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WaveTools.Views.FirstRunViews
{
    public sealed partial class FirstRunInit : Page
    {
        public FirstRunInit()
        {
            InitializeComponent();
            Logging.Write("Switch to FirstRunInit", 0);
            AppDataController.SetFirstRunStatus(1);

            DefaultPathText.Text = "默认目录：\n" + AppDataController.DefaultDataRootPath;
            CustomPathText.Text = "选择后会复制当前已有数据，并继续使用该目录。";
        }

        private void UseDefaultLocation_Click(object sender, RoutedEventArgs e)
        {
            if (!AppDataController.UseDefaultDataRoot(true, out string errorMessage))
            {
                ShowError(errorMessage);
                return;
            }

            GoToRestorePage();
        }

        private async void UseCustomLocation_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = await PickFolderAsync();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (!AppDataController.UseCustomDataRoot(folderPath, true, out string errorMessage))
            {
                ShowError(errorMessage);
                return;
            }

            CustomPathText.Text = folderPath;
            GoToRestorePage();
        }

        private async Task<string> PickFolderAsync()
        {
            FolderPicker picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            IntPtr hwnd = IntPtr.Zero;
            if (App.MainWindow != null)
            {
                hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            }

            if (hwnd == IntPtr.Zero)
            {
                hwnd = CommonHelpers.FileHelpers.GetActiveWindow();
            }

            if (hwnd != IntPtr.Zero)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            Windows.Storage.StorageFolder folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private void GoToRestorePage()
        {
            AppDataController.SetFirstRunStatus(2);

            Frame parentFrame = GetParentFrame(this);
            if (parentFrame != null)
            {
                parentFrame.Navigate(typeof(FirstRunRestore));
            }
        }

        private void ShowError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "设置数据目录失败。";
            }

            App.NotificationManager.RaiseNotification("数据目录不可用", message, InfoBarSeverity.Error);
        }

        private Frame GetParentFrame(FrameworkElement child)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);

            while (parent != null && !(parent is Frame))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            return parent as Frame;
        }
    }
}
