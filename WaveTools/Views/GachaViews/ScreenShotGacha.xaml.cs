using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vanara.PInvoke;
using WaveTools.Depend;
using WaveTools.Views.ToolViews;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static WaveTools.App;

namespace WaveTools.Views.GachaViews
{
    public sealed partial class ScreenShotGacha : Page
    {
        public static bool isShowGachaRecords = false;
        public static bool isScreenShotSelf = false;
        public static bool isHideUID = true;
        public static bool isFinished = false;

        public static string FilePath = null;
        public static bool isShowAllGachaInfo = false;

        public static int GachaInfoDisplayCount = 12;

        private const int MinGachaInfoDisplayCount = 12;
        private const int ScreenshotBaseHeight = 20;
        private const int ScreenshotMinHeight = 615;
        private const int ScreenshotRowHeight = 40;

        private const int ScreenshotWidthWithoutRecords = 550;
        private const int ScreenshotWidthWithRecords = 900;

        public TaskCompletionSource<bool> TaskCompletionSource { get; set; }
        public Window CurrentWindow { get; set; }

        public ScreenShotGacha()
        {
            InitializeComponent();

            Logging.Write("Switch to ScreenShotGacha", 0);

            if (isShowGachaRecords)
            {
                GachaRecords_Viewer.Visibility = Visibility.Visible;
            }
            else
            {
                GachaRecords_Viewer.Visibility = Visibility.Collapsed;

                if (TempGachaGrid.ColumnDefinitions.Count > 0)
                {
                    TempGachaGrid.ColumnDefinitions.RemoveAt(TempGachaGrid.ColumnDefinitions.Count - 1);
                }
            }

            LoadData();
        }

        private async void LoadData()
        {
            try
            {
                Logging.Write("Starting LoadData method", 0);

                string selectedUID = GachaView.selectedUid;
                int selectedCardPoolId = GachaView.selectedCardPoolId;

                Logging.Write($"Selected UID: {selectedUID}, Selected Card Pool ID: {selectedCardPoolId}", 0);

                if (string.IsNullOrWhiteSpace(selectedUID))
                {
                    Logging.Write("Selected UID is empty", 1);
                    return;
                }

                string recordsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "JSG-LLC",
                    "WaveTools",
                    "GachaRecords"
                );

                string filePath = Path.Combine(recordsDirectory, $"{selectedUID}.json");

                Logging.Write($"Records Directory: {recordsDirectory}, File Path: {filePath}", 0);

                if (!File.Exists(filePath))
                {
                    Logging.Write("File not found: " + filePath, 1);
                    return;
                }

                app_name.Text = Package.Current.DisplayName;
                app_version.Text = $"{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}.{Package.Current.Id.Version.Revision}";

                string jsonContent = await File.ReadAllTextAsync(filePath);

                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    Logging.Write("Gacha records file is empty", 1);
                    return;
                }

                var gachaData = JsonConvert.DeserializeObject<GachaModel.GachaData>(jsonContent);

                if (gachaData == null || gachaData.List == null)
                {
                    Logging.Write("Gacha data is null or List is null", 1);
                    return;
                }

                var selectedPool = gachaData.List.FirstOrDefault(pool => pool.CardPoolId == selectedCardPoolId);

                if (selectedPool == null || selectedPool.Records == null)
                {
                    Logging.Write($"No selected card pool found. CardPoolId={selectedCardPoolId}", 1);
                    return;
                }

                var records = selectedPool.Records
                    .Where(record => record != null)
                    .ToList();

                GenerateMissingTemporaryDisplayIds(records, selectedCardPoolId);

                records = records
                    .OrderByDescending(record => ParseRecordTime(record.Time))
                    .ThenByDescending(record => record.Id)
                    .ToList();

                Logging.Write($"Total records found: {records.Count}", 0);

                var rank4Records = records
                    .Where(record => record.QualityLevel == 4)
                    .ToList();

                var rank5Records = records
                    .Where(record => record.QualityLevel == 5)
                    .ToList();

                int actualVisibleInfoCount = await DisplayGachaInfo(records, selectedCardPoolId);

                await ApplyScreenshotSizeAsync(actualVisibleInfoCount);

                Task displayGachaDetailsTask = DisplayGachaDetails(
                    gachaData,
                    records,
                    rank4Records,
                    rank5Records,
                    selectedCardPoolId,
                    GachaView.cardPoolInfo
                );

                Task displayGachaRecordsTask = DisplayGachaRecords(records, actualVisibleInfoCount);

                await Task.WhenAll(displayGachaDetailsTask, displayGachaRecordsTask);

                await Task.Delay(1000);

                if (isScreenShotSelf)
                {
                    await CaptureScreenshotAsync(Content);
                }
            }
            catch (Exception ex)
            {
                Logging.Write($"ScreenShotGacha LoadData failed: {ex.Message}", 2);

                NotificationManager.RaiseNotification(
                    "截图抽卡记录失败",
                    ex.Message,
                    InfoBarSeverity.Error,
                    false,
                    5
                );
            }
        }

        public void CloseWindow()
        {
            isFinished = true;
            TaskCompletionSource?.SetResult(isScreenShotSelf);
            CurrentWindow?.Close();
        }

        private string MaskUID(string uid)
        {
            if (string.IsNullOrEmpty(uid))
            {
                return uid;
            }

            char lastChar = uid[uid.Length - 1];

            return new string('●', uid.Length - 1) + lastChar;
        }

        public async Task CaptureScreenshotAsync(UIElement element)
        {
            try
            {
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                string gachaScreenshotsFolderPath = Path.Combine(
                    documentsPath,
                    "JSG-LLC",
                    "WaveTools",
                    "GachaScreenshots"
                );

                Directory.CreateDirectory(gachaScreenshotsFolderPath);

                string now = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
                string fileName = "GachaScreenShot_" + GachaView.selectedUid + "_" + now + ".png";

                string filePath = Path.Combine(gachaScreenshotsFolderPath, fileName);

                mark.Text = now;

                await Task.Delay(100);

                ScreenShotRoot.UpdateLayout();

                var renderTargetBitmap = new RenderTargetBitmap();

                await renderTargetBitmap.RenderAsync(ScreenShotRoot);

                var pixelBuffer = await renderTargetBitmap.GetPixelsAsync();

                byte[] pixels;

                using (var reader = DataReader.FromBuffer(pixelBuffer))
                {
                    pixels = new byte[pixelBuffer.Length];
                    reader.ReadBytes(pixels);
                }

                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    var encoder = await BitmapEncoder.CreateAsync(
                        BitmapEncoder.PngEncoderId,
                        fileStream.AsRandomAccessStream()
                    );

                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        (uint)renderTargetBitmap.PixelWidth,
                        (uint)renderTargetBitmap.PixelHeight,
                        96,
                        96,
                        pixels
                    );

                    await encoder.FlushAsync();
                }

                FilePath = filePath;

                isFinished = true;

                CloseWindow();
            }
            catch (Exception ex)
            {
                Logging.Write($"CaptureScreenshotAsync failed: {ex.Message}", 2);

                var dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = $"获取截图时发生错误: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };

                await dialog.ShowAsync();
            }
        }

        private async Task DisplayGachaRecords(List<GachaModel.GachaRecord> records, int actualVisibleInfoCount)
        {
            Logging.Write("Displaying gacha records", 0);

            if (records == null)
            {
                records = new List<GachaModel.GachaRecord>();
            }

            int rowCount = Math.Max(MinGachaInfoDisplayCount, actualVisibleInfoCount);

            GachaRecords_List.ItemsSource = records
                .Take(rowCount)
                .ToList();

            await Task.CompletedTask;
        }

        private async Task<int> DisplayGachaInfo(List<GachaModel.GachaRecord> records, int selectedCardPoolId)
        {
            Logging.Write("Displaying gacha info", 0);

            if (records == null)
            {
                records = new List<GachaModel.GachaRecord>();
            }

            int userDisplayCount = GetGachaInfoDisplayCount();

            var sourceRank5Records = records
                .Where(record => record.QualityLevel == 5)
                .ToList();

            if (!isShowAllGachaInfo)
            {
                sourceRank5Records = sourceRank5Records
                    .Take(userDisplayCount)
                    .ToList();
            }

            var rank5Records = sourceRank5Records
                .Select(record => new
                {
                    Name = record.Name,
                    Count = CalculateCount(records, record.Id, 5),
                    Pity = CalculatePity(record.Name, selectedCardPoolId, GachaView.cardPoolInfo)
                })
                .ToList();

            if (rank5Records.Count == 0)
            {
                GachaInfo_List_Disable.Visibility = Visibility.Visible;
            }
            else
            {
                GachaInfo_List_Disable.Visibility = Visibility.Collapsed;
            }

            GachaInfo_List.ItemsSource = rank5Records;

            Logging.Write(
                $"Finished displaying gacha info. UserDisplayCount: {userDisplayCount}, IsShowAll: {isShowAllGachaInfo}, ActualVisibleCount: {rank5Records.Count}",
                0
            );

            await Task.CompletedTask;

            return rank5Records.Count;
        }

        private string CalculateCount(List<GachaModel.GachaRecord> records, string id, int qualityLevel)
        {
            Logging.Write("Calculating count since last target star", 0);

            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(id))
            {
                return "未找到";
            }

            int countSinceLastTargetStar = 1;
            bool foundTargetStar = false;

            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];

                if (record.QualityLevel == qualityLevel && record.Id == id)
                {
                    foundTargetStar = true;
                    break;
                }

                if (record.QualityLevel == 5)
                {
                    countSinceLastTargetStar = 1;
                }
                else
                {
                    countSinceLastTargetStar++;
                }
            }

            if (!foundTargetStar)
            {
                return "未找到";
            }

            Logging.Write($"Count since last target star: {countSinceLastTargetStar}", 0);

            return $"{countSinceLastTargetStar}";
        }

        private string CalculatePityCycleCount(List<GachaModel.GachaRecord> records, string id)
        {
            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(id))
            {
                return "0";
            }

            int count = 0;

            foreach (var record in records.AsEnumerable().Reverse())
            {
                count++;

                if (record.Id == id)
                {
                    return count.ToString();
                }

                if (record.QualityLevel == 5)
                {
                    count = 0;
                }
            }

            return "0";
        }

        private string GetRecordRemark(GachaModel.GachaRecord record, int selectedCardPoolId, GachaModel.CardPoolInfo cardPoolInfo)
        {
            if (record == null)
            {
                return string.Empty;
            }

            if (record.QualityLevel == 5)
            {
                string pity = CalculatePity(record.Name, selectedCardPoolId, cardPoolInfo);

                if (!string.IsNullOrWhiteSpace(pity))
                {
                    return pity;
                }
            }

            if (!string.IsNullOrWhiteSpace(record.ResourceType))
            {
                return record.ResourceType;
            }

            return record.QualityLevel + "星";
        }

        private string CalculatePity(string name, int selectedCardPoolId, GachaModel.CardPoolInfo cardPoolInfo)
        {
            var selectedCardPool = cardPoolInfo?.CardPools?.FirstOrDefault(cp => cp.CardPoolId == selectedCardPoolId);

            if (selectedCardPool == null)
            {
                return string.Empty;
            }

            if (selectedCardPool.isPityEnable != true)
            {
                return string.Empty;
            }

            var permanentFiveStarNames = new List<string>
            {
                "安可",
                "鉴心",
                "维里奈",
                "卡卡罗",
                "凌阳"
            };

            if (permanentFiveStarNames.Contains(name))
            {
                return "歪了";
            }

            return "没歪";
        }

        private List<int> CalculateIntervals(List<GachaModel.GachaRecord> records, int QualityLevel)
        {
            var intervals = new List<int>();
            int countSinceLastStar = 0;

            if (records == null || records.Count == 0)
            {
                return intervals;
            }

            foreach (var record in records.AsEnumerable().Reverse())
            {
                countSinceLastStar++;

                if (record.QualityLevel == QualityLevel)
                {
                    intervals.Add(countSinceLastStar);
                    countSinceLastStar = 0;
                }
            }

            return intervals;
        }

        private async Task DisplayGachaDetails(
            GachaModel.GachaData gachaData,
            List<GachaModel.GachaRecord> selectedRecords,
            List<GachaModel.GachaRecord> rank4Records,
            List<GachaModel.GachaRecord> rank5Records,
            int selectedCardPoolId,
            GachaModel.CardPoolInfo cardPoolInfo)
        {
            Gacha_Panel.Children.Clear();

            selectedRecords ??= new List<GachaModel.GachaRecord>();
            rank4Records ??= new List<GachaModel.GachaRecord>();
            rank5Records ??= new List<GachaModel.GachaRecord>();

            var scrollView = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
            };

            var contentPanel = new StackPanel();

            int countSinceLast5Star = 0;
            int countSinceLast4Star = 0;
            bool foundLast5Star = false;
            bool foundLast4Star = false;

            foreach (var record in selectedRecords)
            {
                if (!foundLast5Star && record.QualityLevel == 5)
                {
                    foundLast5Star = true;
                    foundLast4Star = true;
                }
                else if (!foundLast5Star)
                {
                    countSinceLast5Star++;
                }

                if (!foundLast4Star && record.QualityLevel == 4)
                {
                    foundLast4Star = true;
                }
                else if (!foundLast4Star)
                {
                    countSinceLast4Star++;
                }

                if (foundLast5Star && foundLast4Star)
                {
                    break;
                }
            }

            var fourStarIntervals = CalculateIntervals(selectedRecords, 4);
            var fiveStarIntervals = CalculateIntervals(selectedRecords, 5);

            string averageDraws4Star = fourStarIntervals.Count > 0
                ? fourStarIntervals.Average().ToString("F2")
                : "∞";

            string averageDraws5Star = fiveStarIntervals.Count > 0
                ? fiveStarIntervals.Average().ToString("F2")
                : "∞";

            string uid = GetUid(gachaData);

            Gacha_UID.Text = isHideUID ? MaskUID(uid) : uid;
            GachaRecords_Count.Text = "共" + selectedRecords.Count + "抽";
            GachaInfo_SinceLast5Star.Text = $"垫了{countSinceLast5Star}发";

            var basicInfoPanel = CreateDetailBorder();
            var stackPanelBasicInfo = new StackPanel();

            stackPanelBasicInfo.Children.Add(new TextBlock
            {
                Text = $"UID: {(isHideUID ? MaskUID(uid) : uid)}",
                FontWeight = FontWeights.Bold
            });

            stackPanelBasicInfo.Children.Add(new TextBlock { Text = $"总计抽数: {selectedRecords.Count}" });
            stackPanelBasicInfo.Children.Add(new TextBlock { Text = $"抽到5星次数: {rank5Records.Count}" });
            stackPanelBasicInfo.Children.Add(new TextBlock { Text = $"抽到4星次数: {rank4Records.Count}" });
            stackPanelBasicInfo.Children.Add(new TextBlock { Text = $"预计使用星声: {selectedRecords.Count * 160}" });

            basicInfoPanel.Child = stackPanelBasicInfo;
            contentPanel.Children.Add(basicInfoPanel);

            var detailInfoPanel = CreateDetailBorder();
            var stackPanelDetailInfo = new StackPanel();

            stackPanelDetailInfo.Children.Add(new TextBlock
            {
                Text = "详细统计",
                FontWeight = FontWeights.Bold
            });

            stackPanelDetailInfo.Children.Add(new TextBlock { Text = $"5星平均抽数: {averageDraws5Star}抽" });
            stackPanelDetailInfo.Children.Add(new TextBlock { Text = $"4星平均抽数: {averageDraws4Star}抽" });

            string rate4Star = selectedRecords.Count > 0
                ? (rank4Records.Count / (double)selectedRecords.Count * 100).ToString("F2") + "%"
                : "∞";

            string rate5Star = selectedRecords.Count > 0
                ? (rank5Records.Count / (double)selectedRecords.Count * 100).ToString("F2") + "%"
                : "∞";

            stackPanelDetailInfo.Children.Add(new TextBlock { Text = $"5星获取率: {rate5Star}" });
            stackPanelDetailInfo.Children.Add(new TextBlock { Text = $"4星获取率: {rate4Star}" });

            if (rank5Records.Any())
            {
                stackPanelDetailInfo.Children.Add(new TextBlock { Text = $"最近5星: {rank5Records.First().Time}" });
            }
            else
            {
                stackPanelDetailInfo.Children.Add(new TextBlock { Text = "最近5星: ∞" });
            }

            if (rank4Records.Any())
            {
                stackPanelDetailInfo.Children.Add(new TextBlock { Text = $"最近4星: {rank4Records.First().Time}" });
            }
            else
            {
                stackPanelDetailInfo.Children.Add(new TextBlock { Text = "最近4星: ∞" });
            }

            detailInfoPanel.Child = stackPanelDetailInfo;
            contentPanel.Children.Add(detailInfoPanel);

            var selectedCardPool = cardPoolInfo?.CardPools?.FirstOrDefault(cp => cp.CardPoolId == selectedCardPoolId);

            var borderFiveStar = CreateDetailBorder();
            var stackPanelFiveStar = new StackPanel();

            stackPanelFiveStar.Children.Add(new TextBlock
            {
                Text = $"距离上一个5星已经垫了{countSinceLast5Star}发",
                FontWeight = FontWeights.Bold
            });

            if (selectedCardPool != null && selectedCardPool.FiveStarPity.HasValue)
            {
                stackPanelFiveStar.Children.Add(CreateProgressBar(countSinceLast5Star, selectedCardPool.FiveStarPity.Value));
                stackPanelFiveStar.Children.Add(new TextBlock
                {
                    Text = $"保底{selectedCardPool.FiveStarPity.Value}发",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
            }

            borderFiveStar.Child = stackPanelFiveStar;
            contentPanel.Children.Add(borderFiveStar);

            var borderFourStar = CreateDetailBorder();
            var stackPanelFourStar = new StackPanel();

            stackPanelFourStar.Children.Add(new TextBlock
            {
                Text = $"距离上一个4星已经抽了{countSinceLast4Star}发",
                FontWeight = FontWeights.Bold
            });

            if (selectedCardPool != null && selectedCardPool.FourStarPity.HasValue)
            {
                stackPanelFourStar.Children.Add(CreateProgressBar(countSinceLast4Star, selectedCardPool.FourStarPity.Value));
                stackPanelFourStar.Children.Add(new TextBlock
                {
                    Text = $"保底{selectedCardPool.FourStarPity.Value}发",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                });
            }

            borderFourStar.Child = stackPanelFourStar;
            contentPanel.Children.Add(borderFourStar);

            scrollView.Content = contentPanel;
            Gacha_Panel.Children.Add(scrollView);

            await Task.CompletedTask;
        }

        private void GenerateMissingTemporaryDisplayIds(List<GachaModel.GachaRecord> records, int cardPoolId)
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            var recordsWithoutId = records
                .Where(record => record != null && string.IsNullOrWhiteSpace(record.Id))
                .ToList();

            if (recordsWithoutId.Count == 0)
            {
                return;
            }

            var timestampCounter = new Dictionary<long, int>();

            foreach (var record in recordsWithoutId.OrderByDescending(record => ParseRecordTime(record.Time)).ToList())
            {
                long timestamp = ParseRecordTime(record.Time).ToUnixTimeSeconds();

                if (!timestampCounter.ContainsKey(timestamp))
                {
                    int sameTimestampCount = recordsWithoutId.Count(item =>
                        item != null &&
                        ParseRecordTime(item.Time).ToUnixTimeSeconds() == timestamp
                    );

                    timestampCounter[timestamp] = Math.Min(sameTimestampCount, 10);
                }

                int drawNumber = timestampCounter[timestamp];

                timestampCounter[timestamp]--;

                record.Id = $"{timestamp}{cardPoolId:D4}{drawNumber:D4}";
            }
        }

        private DateTimeOffset ParseRecordTime(string time)
        {
            if (string.IsNullOrWhiteSpace(time))
            {
                return DateTimeOffset.MinValue;
            }

            if (DateTimeOffset.TryParse(time, out DateTimeOffset parsedTime))
            {
                return parsedTime;
            }

            return DateTimeOffset.MinValue;
        }

        private string GetUid(GachaModel.GachaData gachaData)
        {
            if (gachaData == null || gachaData.Info == null)
            {
                return GachaView.selectedUid ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(gachaData.Info.Uid))
            {
                return gachaData.Info.Uid;
            }

            return GachaView.selectedUid ?? string.Empty;
        }

        private Border CreateDetailBorder()
        {
            return new Border
            {
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 4),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
        }

        private ProgressBar CreateProgressBar(int value, int maximum)
        {
            return new ProgressBar
            {
                Minimum = 0,
                Maximum = maximum,
                Value = value,
                Height = 12
            };
        }

        private int GetGachaInfoDisplayCount()
        {
            if (GachaInfoDisplayCount < MinGachaInfoDisplayCount)
            {
                return MinGachaInfoDisplayCount;
            }

            return GachaInfoDisplayCount;
        }

        private int GetScreenshotHeight(int actualVisibleCount)
        {
            int rowCount = Math.Max(MinGachaInfoDisplayCount, actualVisibleCount);

            return ScreenshotMinHeight + Math.Max(0, rowCount - MinGachaInfoDisplayCount) * ScreenshotRowHeight;
        }

        private double GetElementBottom(FrameworkElement element)
        {
            if (element == null)
            {
                return 0;
            }

            try
            {
                element.UpdateLayout();

                var bottomPoint = element
                    .TransformToVisual(ScreenShotRoot)
                    .TransformPoint(new Windows.Foundation.Point(0, element.ActualHeight));

                return bottomPoint.Y;
            }
            catch
            {
                return 0;
            }
        }

        private double GetListViewContentBottom(ListView listView)
        {
            if (listView == null || listView.Items == null || listView.Items.Count == 0)
            {
                return 0;
            }

            double bottom = 0;

            for (int i = 0; i < listView.Items.Count; i++)
            {
                if (listView.ContainerFromIndex(i) is FrameworkElement item)
                {
                    bottom = Math.Max(bottom, GetElementBottom(item));
                }
            }

            return bottom;
        }

        private async Task ApplyScreenshotSizeAsync(int actualVisibleCount)
        {
            int width = isShowGachaRecords ? ScreenshotWidthWithRecords : ScreenshotWidthWithoutRecords;

            int roughHeight = GetScreenshotHeight(actualVisibleCount);

            ScreenShotRoot.Width = width;
            ScreenShotRoot.Height = roughHeight;
            ScreenShotRoot.MinHeight = roughHeight;
            ScreenShotRoot.MaxHeight = roughHeight;

            if (CurrentWindow != null)
            {
                try
                {
                    IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(CurrentWindow);
                    Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                    Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                    appWindow.Resize(new Windows.Graphics.SizeInt32(width, roughHeight));
                }
                catch (Exception ex)
                {
                    Logging.Write($"ApplyScreenshotSizeAsync rough resize failed: {ex.Message}", 1);
                }
            }

            await Task.Delay(150);

            ScreenShotRoot.UpdateLayout();
            TempGachaGrid.UpdateLayout();
            Gacha.UpdateLayout();
            GachaInfo.UpdateLayout();
            GachaRecords_Viewer.UpdateLayout();
            GachaInfo_List.UpdateLayout();
            GachaRecords_List.UpdateLayout();

            double leftBottom = GetElementBottom(Gacha_Panel);
            double middleBottom = GetListViewContentBottom(GachaInfo_List);
            double rightBottom = isShowGachaRecords ? GetListViewContentBottom(GachaRecords_List) : 0;

            double contentBottom = Math.Max(leftBottom, middleBottom);

            if (isShowGachaRecords)
            {
                contentBottom = Math.Max(contentBottom, rightBottom);
            }

            double finalHeight = Math.Ceiling(contentBottom + 8);

            if (finalHeight < ScreenshotMinHeight)
            {
                finalHeight = ScreenshotMinHeight;
            }

            ScreenShotRoot.Height = finalHeight;
            ScreenShotRoot.MinHeight = finalHeight;
            ScreenShotRoot.MaxHeight = finalHeight;

            if (CurrentWindow != null)
            {
                try
                {
                    IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(CurrentWindow);
                    Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                    Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                    appWindow.Resize(new Windows.Graphics.SizeInt32(
                        width,
                        (int)finalHeight
                    ));
                }
                catch (Exception ex)
                {
                    Logging.Write($"ApplyScreenshotSizeAsync final resize failed: {ex.Message}", 1);
                }
            }

            await Task.Delay(100);

            ScreenShotRoot.UpdateLayout();

            Logging.Write(
                $"Screenshot final size: width={width}, roughHeight={roughHeight}, finalHeight={finalHeight}, leftBottom={leftBottom}, middleBottom={middleBottom}, rightBottom={rightBottom}, actualVisibleCount={actualVisibleCount}",
                0
            );
        }
    }
}