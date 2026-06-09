// Copyright (c) 2021-2024, JamXi JSG-LLC.
// All rights reserved.

// This file is part of WaveTools.

// WaveTools is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// WaveTools is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with WaveTools.  If not, see <http://www.gnu.org/licenses/>.

// For more information, please refer to <https://www.gnu.org/licenses/gpl-3.0.html>

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using WaveTools.Depend;
using static WaveTools.Views.MainView;

namespace WaveTools.Views.NotifyViews
{
    public sealed partial class NotifyGalleryView : Page
    {
        public ObservableCollection<string> Pictures { get; } = new ObservableCollection<string>();
        public List<string> JumpUrls { get; private set; }

        public NotifyGalleryView()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is GalleryNavigationData navData)
            {
                Pictures.Clear();
                foreach (var pic in navData.Pictures)
                {
                    Pictures.Add(pic);
                }
                JumpUrls = navData.JumpUrls;
            }
        }

        private void Gallery_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FlipView flipView)
            {
                int selectedPicture = flipView.SelectedIndex;
                if (JumpUrls != null && selectedPicture >= 0 && selectedPicture < JumpUrls.Count)
                {
                    string url = JumpUrls[selectedPicture];
                    if (!string.IsNullOrEmpty(url))
                    {
                        Logging.Write("Opening URL from gallery: " + url, 0);
                        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    }
                }
            }
        }
    }
}