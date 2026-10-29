using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TestMethodSettingCardDEMO.Models;

namespace TestMethodSettingCardDEMO
{
    public partial class MainWindow : Window
    {
        // ==================== 版面與動畫常數 ====================
        public const double CardWidth = 256.0;
        public const double CardHeight = 436.0;
        public const double CardGap = 36.0;
        public const double SlotStride = CardWidth + CardGap; // 292 DIP
        public const double TrackPaddingLeft = 24.0;
        public const double TrackPaddingRight = 24.0;
        public const double TrackTop = 14.0;

        // 邊界穩定區（Hysteresis）：避免滑鼠停在邊界或卡片中線附近時持續左右抖動
        private const double InsertHysteresis = 20.0;

        // 水平自動捲動感應區寬度（48～72 DIP 規範區間內）
        private const double AutoScrollEdgeZone = 64.0;

        // 讓位動畫時長（160～220ms 規範區間內，平順緩出）
        private static readonly Duration ReflowAnimationDuration = new(TimeSpan.FromMilliseconds(190));
        private static readonly Duration PlaceholderMoveDuration = new(TimeSpan.FromMilliseconds(145));
        private static readonly Duration DropSettleDuration = new(TimeSpan.FromMilliseconds(165));
        private static readonly IEasingFunction SmoothEaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        // ==================== 資料與視覺容器狀態 ====================
        public TestMethodBasicInfo BasicInfo { get; } = new();

        private readonly List<FlowStepCardModel> _cards = new();
        private readonly Dictionary<string, Border> _cardContainers = new();
        private readonly Dictionary<string, TranslateTransform> _cardTransforms = new();
        private readonly TranslateTransform _placeholderTransform = new();

        // ==================== 拖曳互動暫存狀態 ====================
        private FlowStepCardModel? _pendingDragCard;
        private Border? _pendingDragHeader;
        private Point _mouseDownWindowPos;
        private Point _grabOffsetInCard;

        private bool _isDragging;
        private bool _isSettlingAnimation;
        private Action? _pendingSettleFinalizeAction;

        private FlowStepCardModel? _draggedCard;
        private int _originalIndex = -1;
        private int _currentInsertIndex = -1;
        private Point _lastMouseInWindow;

        private readonly DispatcherTimer _autoScrollTimer;

        public IReadOnlyList<FlowStepCardModel> Cards => _cards;
        public bool IsDraggingCard => _isDragging;
        public int CurrentInsertIndex => _currentInsertIndex;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            DropPlaceholder.RenderTransform = _placeholderTransform;

            _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;

            InitializeDefaultDemoState();
        }

        // ==================== 初始化與重設示範 ====================

        /// <summary>
        /// 建立或重設初始 10 張試驗流程卡片、基本資料與捲動位置。
        /// </summary>
        public void InitializeDefaultDemoState()
        {
            FinalizeSettleAnimationImmediately();
            CleanupDragStateImmediate();

            BasicInfo.ResetToDefault();

            _cards.Clear();
            _cards.AddRange(FlowStepCardModel.CreateDefaultCards());

            RebuildTrackVisuals();
            UpdateSequenceNumbersAndSummary();

            FlowScrollViewer.ScrollToHorizontalOffset(0);
            SetStatusBanner("拖曳卡片標題可調整順序", isAccent: false);
        }

        private void ResetDemoButton_Click(object sender, RoutedEventArgs e)
        {
            InitializeDefaultDemoState();
            SetStatusBanner("已重設示範：恢復初始 1～10 卡片順序、預設參數與捲動位置", isAccent: true);
        }

        /// <summary>
        /// 僅於初始化與「重設示範」時建立卡片視覺樹；拖曳過程絕不重建容器。
        /// </summary>
        private void RebuildTrackVisuals()
        {
            CardsCanvas.Children.Clear();
            ArrowsCanvas.Children.Clear();
            _cardContainers.Clear();
            _cardTransforms.Clear();

            int count = _cards.Count;
            double totalTrackWidth = TrackPaddingLeft + count * CardWidth + Math.Max(0, count - 1) * CardGap + TrackPaddingRight;

            FlowTrackHost.Width = totalTrackWidth;
            CardsCanvas.Width = totalTrackWidth;
            CardsCanvas.Height = CardHeight + TrackTop * 2;
            ArrowsCanvas.Width = totalTrackWidth;
            ArrowsCanvas.Height = CardHeight + TrackTop * 2;
            PlaceholderCanvas.Width = totalTrackWidth;
            PlaceholderCanvas.Height = CardHeight + TrackTop * 2;

            // 1. 繪製卡片之間的低調方向箭頭
            for (int i = 0; i < count - 1; i++)
            {
                FrameworkElement arrowVisual = CreateConnectorArrowVisual();
                double arrowCenterX = GetSlotX(i) + CardWidth + CardGap * 0.5;
                double arrowCenterY = TrackTop + CardHeight * 0.5;
                Canvas.SetLeft(arrowVisual, arrowCenterX - 13);
                Canvas.SetTop(arrowVisual, arrowCenterY - 13);
                ArrowsCanvas.Children.Add(arrowVisual);
            }

            // 2. 建立每張卡片的獨立容器與內部控制項
            for (int i = 0; i < count; i++)
            {
                FlowStepCardModel card = _cards[i];
                var translate = new TranslateTransform(0, 0);
                Border container = BuildCardVisual(card, translate);

                _cardContainers[card.Id] = container;
                _cardTransforms[card.Id] = translate;

                Canvas.SetLeft(container, GetSlotX(i));
                Canvas.SetTop(container, TrackTop);
                Panel.SetZIndex(container, 10);

                CardsCanvas.Children.Add(container);
            }

            Canvas.SetLeft(DropPlaceholder, GetSlotX(0));
            Canvas.SetTop(DropPlaceholder, TrackTop);
        }

        public static double GetSlotX(int slotIndex)
        {
            return TrackPaddingLeft + slotIndex * SlotStride;
        }

        public static double GetSlotCenter(int slotIndex)
        {
            return GetSlotX(slotIndex) + CardWidth * 0.5;
        }

        public static double GetBoundaryMidpointAfterSlot(int slotIndex)
        {
            return GetSlotX(slotIndex) + CardWidth + CardGap * 0.5;
        }

        // ==================== 卡片視覺與欄位建構 ====================

        private static FrameworkElement CreateConnectorArrowVisual()
        {
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };

            var path = new Path
            {
                Data = Geometry.Parse("M 6,13 L 18,13 M 13.5,8.5 L 18.5,13 L 13.5,17.5"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            badge.Child = path;
            return badge;
        }

        private Border BuildCardVisual(FlowStepCardModel card, TranslateTransform translate)
        {
            var shadow = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Direction = 270,
                Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                Opacity = 0.06
            };

            var cardBorder = new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                CornerRadius = new CornerRadius(12),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1.5),
                RenderTransform = translate,
                Effect = shadow,
                DataContext = card,
                Tag = card
            };

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // --- 1. 標題拖曳把手區 (僅此區域可啟動卡片拖曳) ---
            Border headerBorder = BuildCardHeader(card, cardBorder);
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);

            // --- 2. 卡片內容欄位區 ---
            FrameworkElement bodyContent = BuildCardBody(card);
            Grid.SetRow(bodyContent, 1);
            rootGrid.Children.Add(bodyContent);

            // --- 3. 卡片底部階段類型小註腳 ---
            Border footerBorder = BuildCardFooter(card);
            Grid.SetRow(footerBorder, 2);
            rootGrid.Children.Add(footerBorder);

            cardBorder.Child = rootGrid;

            // 處理滑入、焦點與選取視覺狀態
            cardBorder.MouseEnter += (_, _) => UpdateCardVisualState(cardBorder, card);
            cardBorder.MouseLeave += (_, _) => UpdateCardVisualState(cardBorder, card);
            cardBorder.IsKeyboardFocusWithinChanged += (_, _) =>
            {
                if (cardBorder.IsKeyboardFocusWithin)
                {
                    SelectCard(card);
                }
                UpdateCardVisualState(cardBorder, card);
            };
            cardBorder.PreviewMouseDown += (_, _) => SelectCard(card);

            return cardBorder;
        }

        private Border BuildCardHeader(FlowStepCardModel card, Border cardBorder)
        {
            bool isMilestone = card.IsMilestone;
            Color headerBgColor = isMilestone
                ? Color.FromRgb(0xEF, 0xF6, 0xFF)
                : Color.FromRgb(0xF8, 0xFA, 0xFC);

            var headerBorder = new Border
            {
                Name = "CardHeaderDragHandle",
                Background = new SolidColorBrush(headerBgColor),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(10.5, 10.5, 0, 0),
                Padding = new Thickness(14, 11, 12, 11),
                Cursor = Cursors.SizeAll,
                ToolTip = "按住此標題列並左右拖曳，即可調整此流程卡片順序"
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 序號膠囊標籤 (01 ~ 10)
            Color badgeColor = card.Kind switch
            {
                StepCardKind.MilestoneStart => Color.FromRgb(0x02, 0x84, 0xC7),
                StepCardKind.MilestoneStop => Color.FromRgb(0xD9, 0x77, 0x06),
                StepCardKind.MilestoneEnd => Color.FromRgb(0x05, 0x96, 0x69),
                _ => Color.FromRgb(0x25, 0x63, 0xEB)
            };

            var seqBadge = new Border
            {
                MinWidth = 32,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(badgeColor),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var seqText = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            seqText.SetBinding(TextBlock.TextProperty, new Binding(nameof(FlowStepCardModel.SequenceText)));
            seqBadge.Child = seqText;
            Grid.SetColumn(seqBadge, 0);
            headerGrid.Children.Add(seqBadge);

            // 卡片主標題與分類副標題
            var titleStack = new StackPanel
            {
                Margin = new Thickness(10, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = card.Title,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var categoryText = new TextBlock
            {
                Text = card.CategoryTag,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 1, 0, 0)
            };

            titleStack.Children.Add(titleText);
            titleStack.Children.Add(categoryText);
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            // 右側六點拖曳把手圖示 (⋮⋮)
            var gripBadge = new Border
            {
                Width = 28,
                Height = 30,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            };

            var gripPath = new Path
            {
                Data = Geometry.Parse(
                    "M 9,7 A 1.4,1.4 0 1 1 9,9.8 A 1.4,1.4 0 1 1 9,7 " +
                    "M 16,7 A 1.4,1.4 0 1 1 16,9.8 A 1.4,1.4 0 1 1 16,7 " +
                    "M 9,13.5 A 1.4,1.4 0 1 1 9,16.3 A 1.4,1.4 0 1 1 9,13.5 " +
                    "M 16,13.5 A 1.4,1.4 0 1 1 16,16.3 A 1.4,1.4 0 1 1 16,13.5 " +
                    "M 9,20 A 1.4,1.4 0 1 1 9,22.8 A 1.4,1.4 0 1 1 9,20 " +
                    "M 16,20 A 1.4,1.4 0 1 1 16,22.8 A 1.4,1.4 0 1 1 16,20"),
                Fill = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            gripBadge.Child = gripPath;

            headerBorder.MouseEnter += (_, _) =>
            {
                gripBadge.Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                gripPath.Fill = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            };
            headerBorder.MouseLeave += (_, _) =>
            {
                gripBadge.Background = Brushes.Transparent;
                gripPath.Fill = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            };

            Grid.SetColumn(gripBadge, 2);
            headerGrid.Children.Add(gripBadge);

            headerBorder.Child = headerGrid;

            // 綁定拖曳手勢事件（僅在標題列上）
            headerBorder.MouseLeftButtonDown += (s, e) => HeaderBorder_MouseLeftButtonDown(card, cardBorder, headerBorder, e);
            headerBorder.MouseMove += HeaderBorder_MouseMove;
            headerBorder.MouseLeftButtonUp += HeaderBorder_MouseLeftButtonUp;
            headerBorder.LostMouseCapture += HeaderBorder_LostMouseCapture;

            return headerBorder;
        }

        private FrameworkElement BuildCardBody(FlowStepCardModel card)
        {
            var bodyPanel = new StackPanel
            {
                Margin = new Thickness(15, 13, 15, 10)
            };

            switch (card.Kind)
            {
                case StepCardKind.Preparation:
                    bodyPanel.Children.Add(CreateSectionHeader("執行前準備項目"));
                    bodyPanel.Children.Add(CreateBoundCheckBox("荷重歸零", nameof(FlowStepCardModel.ZeroLoad)));
                    bodyPanel.Children.Add(CreateBoundCheckBox("位移歸零", nameof(FlowStepCardModel.ZeroStroke)));
                    bodyPanel.Children.Add(CreateBoundCheckBox("試片尺寸輸入", nameof(FlowStepCardModel.InputSpecimenSize)));
                    bodyPanel.Children.Add(CreateInfoBox(
                        "於啟動拉伸程序前自動執行感測器歸零與試片幾何參數確認。",
                        isHighlightWhenZero: false,
                        zeroBindProperty: null));
                    break;

                case StepCardKind.SpecimenGrip:
                    bodyPanel.Children.Add(CreateSectionHeader("夾持力量補償"));
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "自動卸力速度",
                        propertyName: nameof(FlowStepCardModel.AutoUnloadSpeed),
                        unit: "N/s",
                        stepDelta: 1.0));
                    bodyPanel.Children.Add(CreateInfoBox(
                        "設為 0 時略過此階段",
                        isHighlightWhenZero: true,
                        zeroBindProperty: nameof(FlowStepCardModel.IsUnloadStageSkipped)));
                    break;

                case StepCardKind.Preload:
                    bodyPanel.Children.Add(CreateSectionHeader("試片預張力設定"));
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "目標預荷重",
                        propertyName: nameof(FlowStepCardModel.TargetPreload),
                        unit: "N",
                        stepDelta: 5.0));
                    bodyPanel.Children.Add(CreateInfoBox(
                        "設為 0 時略過此階段",
                        isHighlightWhenZero: true,
                        zeroBindProperty: nameof(FlowStepCardModel.IsPreloadStageSkipped)));
                    break;

                case StepCardKind.MilestoneStart:
                case StepCardKind.MilestoneStop:
                case StepCardKind.MilestoneEnd:
                    bodyPanel.Children.Add(BuildMilestoneBody(card));
                    break;

                case StepCardKind.TensileStage:
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "橫擔位移速度",
                        propertyName: nameof(FlowStepCardModel.CrossheadSpeed),
                        unit: "mm/s",
                        stepDelta: 1.0));
                    bodyPanel.Children.Add(CreateBoundComboBoxField(
                        label: "目標訊號",
                        itemsSource: FlowStepCardModel.SignalOptions,
                        selectedPropertyName: nameof(FlowStepCardModel.TargetSignal)));
                    bodyPanel.Children.Add(CreateBoundComboBoxField(
                        label: "比較符號",
                        itemsSource: FlowStepCardModel.OperatorOptions,
                        selectedPropertyName: nameof(FlowStepCardModel.ComparisonOperator)));
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "目標值",
                        propertyName: nameof(FlowStepCardModel.TargetValue),
                        unit: "mm",
                        stepDelta: 5.0));
                    break;

                case StepCardKind.StopCriteria:
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "開始判定荷重",
                        propertyName: nameof(FlowStepCardModel.StartJudgeLoad),
                        unit: "kgf",
                        stepDelta: 5.0));
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "荷重下降百分比",
                        propertyName: nameof(FlowStepCardModel.LoadDropPercent),
                        unit: "%",
                        stepDelta: 5.0));
                    bodyPanel.Children.Add(CreateNumericStepperField(
                        card,
                        label: "荷重下降量",
                        propertyName: nameof(FlowStepCardModel.LoadDropAmount),
                        unit: "kgf",
                        stepDelta: 10.0));
                    break;

                case StepCardKind.PostProcess:
                    bodyPanel.Children.Add(CreateBoundCheckBox("自動存檔", nameof(FlowStepCardModel.AutoSave), compact: true));
                    bodyPanel.Children.Add(CreateBoundCheckBox("破壞形式輸入", nameof(FlowStepCardModel.InputBreakMode), compact: true));
                    bodyPanel.Children.Add(CreateBoundCheckBox("自動回位", nameof(FlowStepCardModel.AutoReturn), compact: true));

                    // 回位參數區（當「自動回位」未勾選時呈現停用狀態，但數值完整保留）
                    var returnSectionBorder = new Border
                    {
                        Margin = new Thickness(0, 6, 0, 0),
                        Padding = new Thickness(10, 8, 10, 8),
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                        BorderThickness = new Thickness(1)
                    };
                    returnSectionBorder.SetBinding(IsEnabledProperty, new Binding(nameof(FlowStepCardModel.AutoReturn)));

                    var returnStack = new StackPanel();
                    returnStack.Children.Add(CreateNumericStepperField(
                        card,
                        label: "回位速度",
                        propertyName: nameof(FlowStepCardModel.ReturnSpeed),
                        unit: "mm/s",
                        stepDelta: 10.0,
                        compact: true));
                    returnStack.Children.Add(CreateBoundComboBoxField(
                        label: "位置模式",
                        itemsSource: FlowStepCardModel.PositionModeOptions,
                        selectedPropertyName: nameof(FlowStepCardModel.PositionMode),
                        compact: true));
                    returnStack.Children.Add(CreateNumericStepperField(
                        card,
                        label: "目標位置",
                        propertyName: nameof(FlowStepCardModel.TargetPosition),
                        unit: "mm",
                        stepDelta: 10.0,
                        compact: true));

                    returnSectionBorder.Child = returnStack;

                    card.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(FlowStepCardModel.AutoReturn))
                        {
                            returnSectionBorder.Opacity = card.AutoReturn ? 1.0 : 0.45;
                        }
                    };
                    returnSectionBorder.Opacity = card.AutoReturn ? 1.0 : 0.45;

                    bodyPanel.Children.Add(returnSectionBorder);
                    break;
            }

            return bodyPanel;
        }

        private static FrameworkElement BuildMilestoneBody(FlowStepCardModel card)
        {
            (Color accentColor, Color softBgColor, Color borderColor, string iconPathData) = card.Kind switch
            {
                StepCardKind.MilestoneStart => (
                    Color.FromRgb(0x02, 0x84, 0xC7),
                    Color.FromRgb(0xF0, 0xF9, 0xFF),
                    Color.FromRgb(0xBA, 0xE6, 0xFD),
                    "M 9,6.5 L 19.5,13 L 9,19.5 Z"),
                StepCardKind.MilestoneStop => (
                    Color.FromRgb(0xD9, 0x77, 0x06),
                    Color.FromRgb(0xFF, 0xFB, 0xEB),
                    Color.FromRgb(0xFD, 0xE6, 0x8A),
                    "M 7.5,7.5 L 18.5,7.5 L 18.5,18.5 L 7.5,18.5 Z"),
                _ => (
                    Color.FromRgb(0x05, 0x96, 0x69),
                    Color.FromRgb(0xEC, 0xFD, 0xF5),
                    Color.FromRgb(0xA7, 0xF3, 0xD0),
                    "M 6.5,13.5 L 11,18 L 19.5,8.5")
            };

            var container = new Border
            {
                Background = new SolidColorBrush(softBgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 20, 16, 18),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var iconCircle = new Border
            {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 12)
            };

            var iconPath = new Path
            {
                Data = Geometry.Parse(iconPathData),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (card.Kind == StepCardKind.MilestoneEnd)
            {
                iconPath.Stroke = new SolidColorBrush(accentColor);
                iconPath.StrokeThickness = 2.8;
                iconPath.StrokeStartLineCap = PenLineCap.Round;
                iconPath.StrokeEndLineCap = PenLineCap.Round;
                iconPath.StrokeLineJoin = PenLineJoin.Round;
            }
            else
            {
                iconPath.Fill = new SolidColorBrush(accentColor);
            }

            iconCircle.Child = iconPath;
            stack.Children.Add(iconCircle);

            var pill = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            pill.Child = new TextBlock
            {
                Text = card.MilestoneBadge,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor)
            };
            stack.Children.Add(pill);

            stack.Children.Add(new TextBlock
            {
                Text = card.MilestoneDescription,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                LineHeight = 20
            });

            stack.Children.Add(new TextBlock
            {
                Text = card.MilestoneSubnote,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                LineHeight = 17
            });

            container.Child = stack;
            return container;
        }

        private static Border BuildCardFooter(FlowStepCardModel card)
        {
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 10.5, 10.5),
                Padding = new Thickness(14, 7, 14, 7)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftText = new TextBlock
            {
                Text = card.IsMilestone ? "流程節點里程碑" : "可編輯參數階段",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var rightText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            rightText.SetBinding(TextBlock.TextProperty, new Binding(nameof(FlowStepCardModel.SequenceNumber))
            {
                StringFormat = "順序 #{0}"
            });

            Grid.SetColumn(leftText, 0);
            Grid.SetColumn(rightText, 1);
            grid.Children.Add(leftText);
            grid.Children.Add(rightText);

            footer.Child = grid;
            return footer;
        }

        private static TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(2, 0, 0, 8)
            };
        }

        private CheckBox CreateBoundCheckBox(string label, string propertyName, bool compact = false)
        {
            var cb = new CheckBox
            {
                Content = label,
                Style = (Style)FindResource("ModernCheckBoxStyle"),
                Margin = new Thickness(0, 0, 0, compact ? 5 : 8)
            };
            cb.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(propertyName)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return cb;
        }

        private FrameworkElement CreateBoundComboBoxField(
            string label,
            System.Collections.IEnumerable itemsSource,
            string selectedPropertyName,
            bool compact = false)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, compact ? 6 : 10)
            };

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Margin = new Thickness(1, 0, 0, 3)
            });

            var combo = new ComboBox
            {
                Style = (Style)FindResource("ModernComboBoxStyle"),
                ItemsSource = itemsSource,
                Height = compact ? 30 : 33
            };
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(selectedPropertyName)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            stack.Children.Add(combo);
            return stack;
        }

        /// <summary>
        /// 建立數值輸入與增減按鈕欄位，數值與單位分離且右對齊，不把單位塞入數值字串。
        /// </summary>
        private FrameworkElement CreateNumericStepperField(
            FlowStepCardModel card,
            string label,
            string propertyName,
            string unit,
            double stepDelta,
            bool compact = false)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, compact ? 6 : 10)
            };

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Margin = new Thickness(1, 0, 0, 3)
            });

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左側整合式控制盒：[ − ] [ 數值輸入框 ] [ + ]
            var boxBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = compact ? 30 : 34,
                Padding = new Thickness(3, 0, 3, 0)
            };

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var minusBtn = new Button
            {
                Content = "−",
                Style = (Style)FindResource("StepperButtonStyle"),
                Width = compact ? 23 : 25,
                Height = compact ? 22 : 25,
                ToolTip = $"減少 {stepDelta}"
            };
            minusBtn.Click += (_, _) => card.StepNumericField(propertyName, -stepDelta);

            var valueTextBox = new TextBox
            {
                Style = (Style)FindResource("StepperInnerTextBoxStyle"),
                Tag = propertyName
            };
            valueTextBox.SetBinding(TextBox.TextProperty, new Binding(propertyName)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            valueTextBox.GotKeyboardFocus += (_, _) =>
            {
                boxBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
            };
            valueTextBox.LostKeyboardFocus += (_, _) =>
            {
                boxBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            };

            var plusBtn = new Button
            {
                Content = "+",
                Style = (Style)FindResource("StepperButtonStyle"),
                Width = compact ? 23 : 25,
                Height = compact ? 22 : 25,
                ToolTip = $"增加 {stepDelta}"
            };
            plusBtn.Click += (_, _) => card.StepNumericField(propertyName, stepDelta);

            Grid.SetColumn(minusBtn, 0);
            Grid.SetColumn(valueTextBox, 1);
            Grid.SetColumn(plusBtn, 2);

            innerGrid.Children.Add(minusBtn);
            innerGrid.Children.Add(valueTextBox);
            innerGrid.Children.Add(plusBtn);
            boxBorder.Child = innerGrid;

            // 右側獨立單位標籤（對齊於欄位旁，不混入數值字串）
            var unitBadge = new Border
            {
                MinWidth = 44,
                Height = compact ? 30 : 34,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(7, 0, 7, 0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1)
            };

            unitBadge.Child = new TextBlock
            {
                Text = unit,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(boxBorder, 0);
            Grid.SetColumn(unitBadge, 1);
            rowGrid.Children.Add(boxBorder);
            rowGrid.Children.Add(unitBadge);

            stack.Children.Add(rowGrid);
            return stack;
        }

        private static FrameworkElement CreateInfoBox(string text, bool isHighlightWhenZero, string? zeroBindProperty)
        {
            var infoBorder = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel();
            var mainText = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            stack.Children.Add(mainText);

            if (isHighlightWhenZero && !string.IsNullOrEmpty(zeroBindProperty))
            {
                var stateText = new TextBlock
                {
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                infoBorder.Loaded += (_, _) =>
                {
                    if (infoBorder.DataContext is FlowStepCardModel model)
                    {
                        void SyncHintState()
                        {
                            bool skipped = zeroBindProperty == nameof(FlowStepCardModel.IsUnloadStageSkipped)
                                ? model.IsUnloadStageSkipped
                                : model.IsPreloadStageSkipped;

                            if (skipped)
                            {
                                infoBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB));
                                infoBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A));
                                stateText.Text = "目前設定為 0：將略過此階段";
                                stateText.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
                            }
                            else
                            {
                                infoBorder.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF9, 0xFF));
                                infoBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD));
                                stateText.Text = "目前狀態：正常執行此階段";
                                stateText.Foreground = new SolidColorBrush(Color.FromRgb(0x03, 0x69, 0xA1));
                            }
                        }

                        model.PropertyChanged += (_, e) =>
                        {
                            if (e.PropertyName == zeroBindProperty)
                                SyncHintState();
                        };
                        SyncHintState();
                    }
                };

                stack.Children.Add(stateText);
            }

            infoBorder.Child = stack;
            return infoBorder;
        }

        private void SelectCard(FlowStepCardModel selectedCard)
        {
            foreach (FlowStepCardModel card in _cards)
            {
                card.IsSelected = ReferenceEquals(card, selectedCard);
                if (_cardContainers.TryGetValue(card.Id, out Border? container))
                {
                    UpdateCardVisualState(container, card);
                }
            }
        }

        private static void UpdateCardVisualState(Border cardBorder, FlowStepCardModel card)
        {
            if (cardBorder.Effect is not DropShadowEffect shadow)
                return;

            if (card.IsSelected || cardBorder.IsKeyboardFocusWithin)
            {
                cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                cardBorder.BorderThickness = new Thickness(2);
                shadow.BlurRadius = 18;
                shadow.Opacity = 0.11;
            }
            else if (cardBorder.IsMouseOver)
            {
                cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
                cardBorder.BorderThickness = new Thickness(1.5);
                shadow.BlurRadius = 16;
                shadow.Opacity = 0.09;
            }
            else
            {
                cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                cardBorder.BorderThickness = new Thickness(1.5);
                shadow.BlurRadius = 14;
                shadow.Opacity = 0.06;
            }
        }

        // ==================== 拖曳排序核心邏輯（5.1 ～ 5.4） ====================

        private void HeaderBorder_MouseLeftButtonDown(
            FlowStepCardModel card,
            Border cardBorder,
            Border headerBorder,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            FinalizeSettleAnimationImmediately();
            CleanupDragStateImmediate();

            SelectCard(card);

            _pendingDragCard = card;
            _pendingDragHeader = headerBorder;
            _mouseDownWindowPos = e.GetPosition(this);
            _grabOffsetInCard = e.GetPosition(cardBorder);
            _lastMouseInWindow = _mouseDownWindowPos;

            headerBorder.CaptureMouse();
            e.Handled = true;
        }

        private void HeaderBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_pendingDragCard == null && !_isDragging)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isDragging)
                {
                    CancelActiveDrag("滑鼠按鍵已釋放，已取消拖曳");
                }
                else
                {
                    ClearPendingDragCapture();
                }
                return;
            }

            Point mouseInWindow = e.GetPosition(this);
            _lastMouseInWindow = mouseInWindow;

            if (!_isDragging)
            {
                Vector delta = mouseInWindow - _mouseDownWindowPos;
                double minH = Math.Max(SystemParameters.MinimumHorizontalDragDistance, 5.0);
                double minV = Math.Max(SystemParameters.MinimumVerticalDragDistance, 5.0);

                if (Math.Abs(delta.X) < minH && Math.Abs(delta.Y) < minV)
                {
                    return; // 尚未超過系統拖曳門檻，單純點擊不啟動拖曳
                }

                StartCardDrag(_pendingDragCard!, mouseInWindow, _grabOffsetInCard);
            }

            if (_isDragging)
            {
                UpdateActiveDrag(mouseInWindow);
            }
        }

        private void HeaderBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            Point mouseInWindow = e.GetPosition(this);
            _lastMouseInWindow = mouseInWindow;

            if (_isDragging)
            {
                e.Handled = true;
                if (IsPointInsideValidDropZone(mouseInWindow))
                {
                    CommitActiveDrag();
                }
                else
                {
                    CancelActiveDrag("於流程有效區外放開，已取消拖曳並還原原始順序");
                }
            }
            else
            {
                ClearPendingDragCapture();
            }
        }

        private bool _isReleasingCaptureInternally;

        private void HeaderBorder_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isReleasingCaptureInternally)
                return;

            if (_isDragging)
            {
                CancelActiveDrag("失去滑鼠捕捉，已取消拖曳並還原原始順序");
            }
            else
            {
                _pendingDragCard = null;
                _pendingDragHeader = null;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && (_isDragging || _pendingDragCard != null))
            {
                e.Handled = true;
                if (_isDragging)
                {
                    CancelActiveDrag("已按 Esc 取消拖曳，恢復原始順序");
                }
                else
                {
                    ClearPendingDragCapture();
                }
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (_isDragging)
            {
                CancelActiveDrag("視窗失去作用中狀態，已取消拖曳並還原原始順序");
            }
            else if (_pendingDragCard != null)
            {
                ClearPendingDragCapture();
            }
        }

        /// <summary>
        /// 5.1 正式啟動卡片拖曳（支援滑鼠手勢與自動化驗收呼叫）
        /// </summary>
        public void StartCardDrag(FlowStepCardModel card, Point currentMouseInWindow, Point? grabOffsetOverride = null)
        {
            FinalizeSettleAnimationImmediately();

            int origIdx = _cards.IndexOf(card);
            if (origIdx < 0 || !_cardContainers.TryGetValue(card.Id, out Border? draggedContainer))
                return;

            if (grabOffsetOverride.HasValue)
            {
                _grabOffsetInCard = grabOffsetOverride.Value;
            }
            else if (_grabOffsetInCard.X <= 0 && _grabOffsetInCard.Y <= 0)
            {
                _grabOffsetInCard = new Point(CardWidth * 0.5, 24.0);
            }

            _isDragging = true;
            _draggedCard = card;
            _originalIndex = origIdx;
            _currentInsertIndex = origIdx;
            _lastMouseInWindow = currentMouseInWindow;
            card.IsBeingDragged = true;

            // 在隱藏原卡片前，先擷取清晰局部快照供浮動預覽使用，確保所有已修改欄位內容如實呈現
            CaptureCardSnapshotToPreview(draggedContainer);

            // 原卡片設為全透明隱藏（注意：不可將 IsHitTestVisible 設為 false，否則 WPF 會強制釋放子元素 headerBorder 的滑鼠捕捉！）
            draggedContainer.Opacity = 0.0;

            // 設定單一虛線佔位框到起始槽位
            _placeholderTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _placeholderTransform.X = 0;
            Canvas.SetLeft(DropPlaceholder, GetSlotX(_currentInsertIndex));
            Canvas.SetTop(DropPlaceholder, TrackTop);
            PlaceholderIndexHintText.Text = $"插入為第 {_currentInsertIndex + 1} 步";
            DropPlaceholder.Visibility = Visibility.Visible;

            // 顯示最上層浮動預覽卡片（放大 1.02 倍、陰影與微透明，且記住抓取偏移不跳到滑鼠中心）
            DragPreviewBorder.BeginAnimation(Canvas.LeftProperty, null);
            DragPreviewBorder.BeginAnimation(Canvas.TopProperty, null);
            DragPreviewBorder.Visibility = Visibility.Visible;
            UpdateFloatingPreviewPosition(currentMouseInWindow);

            // 淡化卡片之間的流程方向箭頭
            AnimateElementOpacity(ArrowsCanvas, 0.25, TimeSpan.FromMilliseconds(150));

            // 啟動邊緣自動捲動計時器
            _autoScrollTimer.Start();

            SetStatusBanner($"正在拖曳「{card.Title}」：預定插入至第 {_currentInsertIndex + 1} 步（Esc 取消）", isAccent: true);
        }

        /// <summary>
        /// 5.2 更新拖曳位置、計算插入索引與觸發前後卡片平滑讓位動畫
        /// </summary>
        public void UpdateActiveDrag(Point mouseInWindow)
        {
            if (!_isDragging || _draggedCard == null)
                return;

            _lastMouseInWindow = mouseInWindow;
            UpdateFloatingPreviewPosition(mouseInWindow);

            bool insideDropZone = IsPointInsideValidDropZone(mouseInWindow);
            DragOutsideWarningBanner.Visibility = insideDropZone ? Visibility.Collapsed : Visibility.Visible;

            // 將滑鼠座標轉換至橫向軌道內容座標系（已自動包含 ScrollViewer 水平偏移）
            Point mouseInTrack = TranslatePoint(mouseInWindow, FlowTrackHost);
            int newInsertIndex = ComputeInsertIndexWithHysteresis(mouseInTrack.X, _currentInsertIndex);

            if (newInsertIndex != _currentInsertIndex)
            {
                int previousInsertIndex = _currentInsertIndex;
                _currentInsertIndex = newInsertIndex;

                AnimatePlaceholderToSlot(previousInsertIndex, _currentInsertIndex);
                AnimateOtherCardsReflow(_originalIndex, _currentInsertIndex);
            }

            if (insideDropZone)
            {
                SetStatusBanner(
                    $"正在拖曳「{_draggedCard.Title}」：預定插入為第 {_currentInsertIndex + 1} 步（放開完成排序，Esc 取消）",
                    isAccent: true);
            }
            else
            {
                SetStatusBanner(
                    $"「{_draggedCard.Title}」位於流程區外：此時放開滑鼠將取消拖曳並還原順序",
                    isAccent: false);
            }
        }

        /// <summary>
        /// 計算插入索引（排除被拖曳卡片，並在每個槽位邊界加入穩定區 Hysteresis 防止中線抖動）
        /// </summary>
        private int ComputeInsertIndexWithHysteresis(double mouseXInTrack, int currentInsertIndex)
        {
            int count = _cards.Count;
            if (count <= 1)
                return 0;

            int candidate = Math.Clamp(currentInsertIndex, 0, count - 1);

            // 向右跨越判定：滑鼠需超過槽位邊界中點 + InsertHysteresis 才遞增目標索引
            while (candidate < count - 1)
            {
                double boundaryRight = GetBoundaryMidpointAfterSlot(candidate) + InsertHysteresis;
                if (mouseXInTrack > boundaryRight)
                {
                    candidate++;
                }
                else
                {
                    break;
                }
            }

            // 向左跨越判定：滑鼠需低於前一個槽位邊界中點 - InsertHysteresis 才遞減目標索引
            while (candidate > 0)
            {
                double boundaryLeft = GetBoundaryMidpointAfterSlot(candidate - 1) - InsertHysteresis;
                if (mouseXInTrack < boundaryLeft)
                {
                    candidate--;
                }
                else
                {
                    break;
                }
            }

            return candidate;
        }

        /// <summary>
        /// 讓單一虛線佔位框平滑移至目標槽位（全程只有一個佔位框）
        /// </summary>
        private void AnimatePlaceholderToSlot(int fromSlot, int toSlot)
        {
            double targetLeft = GetSlotX(toSlot);
            double currentVisualX = Canvas.GetLeft(DropPlaceholder) + _placeholderTransform.X;

            Canvas.SetLeft(DropPlaceholder, targetLeft);
            double startOffset = currentVisualX - targetLeft;
            _placeholderTransform.X = startOffset;

            var anim = new DoubleAnimation
            {
                To = 0,
                Duration = PlaceholderMoveDuration,
                EasingFunction = SmoothEaseOut
            };
            _placeholderTransform.BeginAnimation(TranslateTransform.XProperty, anim, HandoffBehavior.SnapshotAndReplace);

            PlaceholderIndexHintText.Text = $"插入為第 {toSlot + 1} 步";
        }

        /// <summary>
        /// 根據原始索引與目前預定插入索引，為其餘每張卡片計算目標位移並從當前可見位置平滑過渡（不瞬移、不重建）
        /// </summary>
        private void AnimateOtherCardsReflow(int originalIndex, int insertIndex)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (i == originalIndex)
                    continue;

                FlowStepCardModel card = _cards[i];
                if (!_cardTransforms.TryGetValue(card.Id, out TranslateTransform? transform))
                    continue;

                double targetOffsetX = 0.0;
                if (originalIndex < insertIndex)
                {
                    // 被拖曳卡片往後移：介於 (originalIndex, insertIndex] 的卡片向左讓位 1 個槽位
                    if (i > originalIndex && i <= insertIndex)
                    {
                        targetOffsetX = -SlotStride;
                    }
                }
                else if (originalIndex > insertIndex)
                {
                    // 被拖曳卡片往前移：介於 [insertIndex, originalIndex) 的卡片向右讓位 1 個槽位
                    if (i >= insertIndex && i < originalIndex)
                    {
                        targetOffsetX = +SlotStride;
                    }
                }

                var anim = new DoubleAnimation
                {
                    To = targetOffsetX,
                    Duration = ReflowAnimationDuration,
                    EasingFunction = SmoothEaseOut
                };
                transform.BeginAnimation(TranslateTransform.XProperty, anim, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private void UpdateFloatingPreviewPosition(Point mouseInWindow)
        {
            Point mouseInOverlay = TranslatePoint(mouseInWindow, DragOverlayCanvas);
            double left = mouseInOverlay.X - _grabOffsetInCard.X;
            double top = mouseInOverlay.Y - _grabOffsetInCard.Y;

            Canvas.SetLeft(DragPreviewBorder, left);
            Canvas.SetTop(DragPreviewBorder, top);
        }

        /// <summary>
        /// 使用 DrawingVisual + VisualBrush 將卡片自身的 [0,0,CardWidth,CardHeight] 範圍繪製為高解析度位圖，
        /// 避免直接 Render(UIElement) 受到父層 Canvas.Left 偏移影響。
        /// </summary>
        private void CaptureCardSnapshotToPreview(Border cardBorder)
        {
            cardBorder.UpdateLayout();
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int pixelWidth = Math.Max(1, (int)Math.Round(CardWidth * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Round(CardHeight * dpi.DpiScaleY));

            Visual targetVisual = (cardBorder.Child as Visual) ?? cardBorder;
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                var vb = new VisualBrush(targetVisual)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewbox = new Rect(0, 0, 1, 1)
                };
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(0, 0, CardWidth, CardHeight), 10, 10);
                dc.DrawRectangle(vb, null, new Rect(0, 0, CardWidth, CardHeight));
            }

            var rtb = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96.0 * dpi.DpiScaleX,
                96.0 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);

            rtb.Render(dv);
            DragPreviewImageRect.Fill = new ImageBrush(rtb)
            {
                Stretch = Stretch.Fill
            };
        }

        // ==================== 5.3 水平自動捲動 ====================

        private void AutoScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isDragging || _draggedCard == null)
            {
                StopAutoScrollAndIndicators();
                return;
            }

            Point mouseInViewport = TranslatePoint(_lastMouseInWindow, FlowViewportGrid);
            double viewportWidth = FlowViewportGrid.ActualWidth;
            double viewportHeight = FlowViewportGrid.ActualHeight;

            if (viewportWidth <= 0 || mouseInViewport.Y < -40 || mouseInViewport.Y > viewportHeight + 40)
            {
                LeftAutoScrollIndicator.Opacity = 0;
                RightAutoScrollIndicator.Opacity = 0;
                return;
            }

            double scrollDelta = 0.0;

            if (mouseInViewport.X < AutoScrollEdgeZone && FlowScrollViewer.HorizontalOffset > 0)
            {
                double proximity = Math.Clamp((AutoScrollEdgeZone - mouseInViewport.X) / AutoScrollEdgeZone, 0.12, 1.0);
                scrollDelta = -(4.0 + 18.0 * proximity * proximity);
                LeftAutoScrollIndicator.Opacity = Math.Clamp(proximity * 0.95, 0.25, 0.95);
                RightAutoScrollIndicator.Opacity = 0;
            }
            else if (mouseInViewport.X > viewportWidth - AutoScrollEdgeZone &&
                     FlowScrollViewer.HorizontalOffset < FlowScrollViewer.ScrollableWidth)
            {
                double proximity = Math.Clamp((mouseInViewport.X - (viewportWidth - AutoScrollEdgeZone)) / AutoScrollEdgeZone, 0.12, 1.0);
                scrollDelta = +(4.0 + 18.0 * proximity * proximity);
                RightAutoScrollIndicator.Opacity = Math.Clamp(proximity * 0.95, 0.25, 0.95);
                LeftAutoScrollIndicator.Opacity = 0;
            }
            else
            {
                LeftAutoScrollIndicator.Opacity = 0;
                RightAutoScrollIndicator.Opacity = 0;
            }

            if (Math.Abs(scrollDelta) > 0.01)
            {
                double nextOffset = Math.Clamp(
                    FlowScrollViewer.HorizontalOffset + scrollDelta,
                    0.0,
                    FlowScrollViewer.ScrollableWidth);

                FlowScrollViewer.ScrollToHorizontalOffset(nextOffset);
                FlowScrollViewer.UpdateLayout();

                // 捲動期間即使滑鼠未移動，仍持續依據新捲動偏移重新計算插入位置
                UpdateActiveDrag(_lastMouseInWindow);
            }
        }

        private void StopAutoScrollAndIndicators()
        {
            _autoScrollTimer.Stop();
            LeftAutoScrollIndicator.Opacity = 0;
            RightAutoScrollIndicator.Opacity = 0;
        }

        // ==================== 5.4 放下提交與取消還原 ====================

        private bool IsPointInsideValidDropZone(Point pointInWindow)
        {
            Point ptInSection = TranslatePoint(pointInWindow, FlowSectionBorder);
            return ptInSection.X >= -16 &&
                   ptInSection.X <= FlowSectionBorder.ActualWidth + 16 &&
                   ptInSection.Y >= -16 &&
                   ptInSection.Y <= FlowSectionBorder.ActualHeight + 16;
        }

        /// <summary>
        /// 在有效區放開滑鼠：提交排序，浮動預覽平滑落位，更新序號與底部摘要
        /// </summary>
        public void CommitActiveDrag()
        {
            if (!_isDragging || _draggedCard == null)
                return;

            FlowStepCardModel draggedCard = _draggedCard;
            int fromIndex = _originalIndex;
            int toIndex = Math.Clamp(_currentInsertIndex, 0, _cards.Count - 1);

            _isDragging = false;
            StopAutoScrollAndIndicators();
            ClearPendingDragCapture();
            DragOutsideWarningBanner.Visibility = Visibility.Collapsed;

            Point targetSlotInOverlay = FlowTrackHost.TranslatePoint(new Point(GetSlotX(toIndex), TrackTop), DragOverlayCanvas);
            double currentLeft = Canvas.GetLeft(DragPreviewBorder);
            double currentTop = Canvas.GetTop(DragPreviewBorder);
            double dist = Math.Sqrt(Math.Pow(targetSlotInOverlay.X - currentLeft, 2) + Math.Pow(targetSlotInOverlay.Y - currentTop, 2));

            bool isFinalized = false;
            void FinalizeCommit()
            {
                if (isFinalized)
                    return;
                isFinalized = true;

                _isSettlingAnimation = false;
                _pendingSettleFinalizeAction = null;

                if (fromIndex != toIndex && fromIndex >= 0 && fromIndex < _cards.Count)
                {
                    _cards.RemoveAt(fromIndex);
                    _cards.Insert(toIndex, draggedCard);
                }

                // 同步所有卡片的新槽位座標並歸零 TranslateTransform（視覺位置無縫銜接）
                for (int i = 0; i < _cards.Count; i++)
                {
                    FlowStepCardModel c = _cards[i];
                    if (_cardTransforms.TryGetValue(c.Id, out TranslateTransform? tr))
                    {
                        tr.BeginAnimation(TranslateTransform.XProperty, null);
                        tr.X = 0;
                    }
                    if (_cardContainers.TryGetValue(c.Id, out Border? container))
                    {
                        Canvas.SetLeft(container, GetSlotX(i));
                        Canvas.SetTop(container, TrackTop);
                        container.Opacity = 1.0;
                        container.IsHitTestVisible = true;
                    }
                    c.IsBeingDragged = false;
                }

                DropPlaceholder.Visibility = Visibility.Collapsed;
                DragPreviewBorder.BeginAnimation(Canvas.LeftProperty, null);
                DragPreviewBorder.BeginAnimation(Canvas.TopProperty, null);
                DragPreviewBorder.Visibility = Visibility.Collapsed;
                AnimateElementOpacity(ArrowsCanvas, 1.0, TimeSpan.FromMilliseconds(160));

                UpdateSequenceNumbersAndSummary();

                if (fromIndex == toIndex)
                {
                    SetStatusBanner($"「{draggedCard.Title}」維持於第 {toIndex + 1} 步（順序未變更）", isAccent: false);
                }
                else
                {
                    SetStatusBanner($"已將「{draggedCard.Title}」由第 {fromIndex + 1} 步調整至第 {toIndex + 1} 步", isAccent: true);
                }

                _draggedCard = null;
                _originalIndex = -1;
                _currentInsertIndex = -1;
            }

            if (fromIndex == toIndex && dist < 4.0)
            {
                FinalizeCommit();
                return;
            }

            _isSettlingAnimation = true;
            _pendingSettleFinalizeAction = FinalizeCommit;

            AnimatePreviewToPoint(targetSlotInOverlay, DropSettleDuration, FinalizeCommit);
        }

        /// <summary>
        /// Esc、區外放開、視窗失焦或失去捕捉時：取消拖曳並平滑還原原始順序
        /// </summary>
        public void CancelActiveDrag(string reasonMessage = "已取消拖曳並還原原始順序")
        {
            if (!_isDragging || _draggedCard == null)
            {
                CleanupDragStateImmediate();
                return;
            }

            int origIndex = _originalIndex;

            _isDragging = false;
            StopAutoScrollAndIndicators();
            ClearPendingDragCapture();
            DragOutsideWarningBanner.Visibility = Visibility.Collapsed;

            // 所有讓位中的卡片平滑動畫退回原始槽位 (TranslateTransform.X -> 0)
            AnimateOtherCardsReflow(origIndex, origIndex);
            AnimatePlaceholderToSlot(_currentInsertIndex, origIndex);

            Point origSlotInOverlay = FlowTrackHost.TranslatePoint(new Point(GetSlotX(origIndex), TrackTop), DragOverlayCanvas);

            bool isFinalized = false;
            void FinalizeCancel()
            {
                if (isFinalized)
                    return;
                isFinalized = true;

                _isSettlingAnimation = false;
                _pendingSettleFinalizeAction = null;

                for (int i = 0; i < _cards.Count; i++)
                {
                    FlowStepCardModel c = _cards[i];
                    if (_cardTransforms.TryGetValue(c.Id, out TranslateTransform? tr))
                    {
                        tr.BeginAnimation(TranslateTransform.XProperty, null);
                        tr.X = 0;
                    }
                    if (_cardContainers.TryGetValue(c.Id, out Border? container))
                    {
                        Canvas.SetLeft(container, GetSlotX(i));
                        Canvas.SetTop(container, TrackTop);
                        container.Opacity = 1.0;
                        container.IsHitTestVisible = true;
                    }
                    c.IsBeingDragged = false;
                }

                DropPlaceholder.Visibility = Visibility.Collapsed;
                DragPreviewBorder.BeginAnimation(Canvas.LeftProperty, null);
                DragPreviewBorder.BeginAnimation(Canvas.TopProperty, null);
                DragPreviewBorder.Visibility = Visibility.Collapsed;
                AnimateElementOpacity(ArrowsCanvas, 1.0, TimeSpan.FromMilliseconds(150));

                UpdateSequenceNumbersAndSummary();
                SetStatusBanner(reasonMessage, isAccent: false);

                _draggedCard = null;
                _originalIndex = -1;
                _currentInsertIndex = -1;
            }

            _isSettlingAnimation = true;
            _pendingSettleFinalizeAction = FinalizeCancel;

            AnimatePreviewToPoint(origSlotInOverlay, DropSettleDuration, FinalizeCancel);
        }

        public void FinalizeSettleAnimationImmediately()
        {
            if (_isSettlingAnimation && _pendingSettleFinalizeAction != null)
            {
                Action action = _pendingSettleFinalizeAction;
                _pendingSettleFinalizeAction = null;
                _isSettlingAnimation = false;
                action();
            }
        }

        private void AnimatePreviewToPoint(Point targetInOverlay, Duration duration, Action onCompleted)
        {
            var animX = new DoubleAnimation
            {
                To = targetInOverlay.X,
                Duration = duration,
                EasingFunction = SmoothEaseOut
            };
            var animY = new DoubleAnimation
            {
                To = targetInOverlay.Y,
                Duration = duration,
                EasingFunction = SmoothEaseOut
            };

            animX.Completed += (_, _) => onCompleted();

            DragPreviewBorder.BeginAnimation(Canvas.LeftProperty, animX, HandoffBehavior.SnapshotAndReplace);
            DragPreviewBorder.BeginAnimation(Canvas.TopProperty, animY, HandoffBehavior.SnapshotAndReplace);
        }

        private void ClearPendingDragCapture()
        {
            Border? header = _pendingDragHeader;
            _pendingDragCard = null;
            _pendingDragHeader = null;

            if (header != null && header.IsMouseCaptured)
            {
                _isReleasingCaptureInternally = true;
                try
                {
                    header.ReleaseMouseCapture();
                }
                finally
                {
                    _isReleasingCaptureInternally = false;
                }
            }
        }

        private void CleanupDragStateImmediate()
        {
            _isDragging = false;
            StopAutoScrollAndIndicators();
            ClearPendingDragCapture();
            _draggedCard = null;
            _originalIndex = -1;
            _currentInsertIndex = -1;

            foreach (FlowStepCardModel c in _cards)
            {
                c.IsBeingDragged = false;
                if (_cardTransforms.TryGetValue(c.Id, out TranslateTransform? tr))
                {
                    tr.BeginAnimation(TranslateTransform.XProperty, null);
                    tr.X = 0;
                }
                if (_cardContainers.TryGetValue(c.Id, out Border? container))
                {
                    container.Opacity = 1.0;
                    container.IsHitTestVisible = true;
                }
            }

            DropPlaceholder.Visibility = Visibility.Collapsed;
            DragPreviewBorder.BeginAnimation(Canvas.LeftProperty, null);
            DragPreviewBorder.BeginAnimation(Canvas.TopProperty, null);
            DragPreviewBorder.Visibility = Visibility.Collapsed;
            DragOutsideWarningBanner.Visibility = Visibility.Collapsed;
            ArrowsCanvas.BeginAnimation(OpacityProperty, null);
            ArrowsCanvas.Opacity = 1.0;
        }

        private static void AnimateElementOpacity(UIElement element, double toOpacity, TimeSpan duration)
        {
            var anim = new DoubleAnimation
            {
                To = toOpacity,
                Duration = new Duration(duration),
                EasingFunction = SmoothEaseOut
            };
            element.BeginAnimation(OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        // ==================== 序號與底部狀態列同步 ====================

        private void UpdateSequenceNumbersAndSummary()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].SequenceNumber = i + 1;
            }

            string summary = string.Join("  →  ", _cards.Select(c => $"{c.SequenceNumber}.{c.Title}"));
            FlowSequenceSummaryText.Text = summary;
            FlowSequenceSummaryText.ToolTip = summary;
        }

        private void SetStatusBanner(string text, bool isAccent)
        {
            StatusActionText.Text = text;
            if (isAccent)
            {
                StatusPillBorder.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
                StatusPillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD));
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                StatusActionText.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF));
            }
            else
            {
                StatusPillBorder.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                StatusPillBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                StatusActionText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            }
        }

        // ==================== 實機與自動化驗收支援方法 ====================

        public Border? GetCardContainer(string cardId) =>
            _cardContainers.TryGetValue(cardId, out Border? b) ? b : null;

        public TranslateTransform? GetCardTransform(string cardId) =>
            _cardTransforms.TryGetValue(cardId, out TranslateTransform? t) ? t : null;
    }
}
