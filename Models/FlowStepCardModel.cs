using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TestMethodSettingCardDEMO.Models
{
    public enum StepCardKind
    {
        Preparation,
        SpecimenGrip,
        Preload,
        MilestoneStart,
        TensileStage,
        StopCriteria,
        MilestoneStop,
        PostProcess,
        MilestoneEnd
    }

    /// <summary>
    /// 試驗基本資料模型（記憶體內示範資料）
    /// </summary>
    public class TestMethodBasicInfo : INotifyPropertyChanged
    {
        private string _testId = "TM-001";
        private string _testName = "金屬拉伸試驗";
        private string _loadSource = "1：100 kgf";
        private string _strokeSource = "橫擔位移";

        public ObservableCollection<string> LoadSourceOptions { get; } = new()
        {
            "1：100 kgf",
            "2：500 kgf",
            "3：50 N",
            "4：10 kN"
        };

        public ObservableCollection<string> StrokeSourceOptions { get; } = new()
        {
            "橫擔位移",
            "光學伸長計",
            "夾具間距",
            "外接位移計"
        };

        public string TestId
        {
            get => _testId;
            set => SetField(ref _testId, value);
        }

        public string TestName
        {
            get => _testName;
            set => SetField(ref _testName, value);
        }

        public string LoadSource
        {
            get => _loadSource;
            set => SetField(ref _loadSource, value);
        }

        public string StrokeSource
        {
            get => _strokeSource;
            set => SetField(ref _strokeSource, value);
        }

        public void ResetToDefault()
        {
            TestId = "TM-001";
            TestName = "金屬拉伸試驗";
            LoadSource = "1：100 kgf";
            StrokeSource = "橫擔位移";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    /// <summary>
    /// 單張試驗流程卡片資料模型。
    /// 具備穩定 Id 與獨立的卡片身份（Title），排列順序由 SequenceNumber 反映。
    /// </summary>
    public class FlowStepCardModel : INotifyPropertyChanged
    {
        private int _sequenceNumber;
        private bool _isBeingDragged;
        private bool _isSelected;

        // 1. 試驗準備
        private bool _zeroLoad = true;
        private bool _zeroStroke = true;
        private bool _inputSpecimenSize = true;

        // 2. 試片夾持
        private string _autoUnloadSpeed = "5";

        // 3. 預荷重
        private string _targetPreload = "10";

        // 5 & 6. 第一段／第二段拉伸
        private string _crossheadSpeed = "1";
        private string _targetSignal = "橫擔向上位移";
        private string _comparisonOperator = "≥";
        private string _targetValue = "10";

        // 7. 停止判定
        private string _startJudgeLoad = "10";
        private string _loadDropPercent = "20";
        private string _loadDropAmount = "100";

        // 9. 收尾流程
        private bool _autoSave = true;
        private bool _inputBreakMode = true;
        private bool _autoReturn = true;
        private string _returnSpeed = "100";
        private string _positionMode = "指定位置";
        private string _targetPosition = "100";

        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string CategoryTag { get; init; } = string.Empty;
        public StepCardKind Kind { get; init; }
        public string MilestoneBadge { get; init; } = string.Empty;
        public string MilestoneDescription { get; init; } = string.Empty;
        public string MilestoneSubnote { get; init; } = string.Empty;
        public string AccentHex { get; init; } = "#2563EB";

        public static IReadOnlyList<string> SignalOptions { get; } = new[]
        {
            "橫擔向上位移",
            "橫擔向下位移",
            "荷重訊號",
            "伸長計變形量",
            "經過時間"
        };

        public static IReadOnlyList<string> OperatorOptions { get; } = new[]
        {
            "≥",
            ">",
            "≤",
            "<",
            "="
        };

        public static IReadOnlyList<string> PositionModeOptions { get; } = new[]
        {
            "指定位置",
            "機械原點",
            "試驗起點"
        };

        public int SequenceNumber
        {
            get => _sequenceNumber;
            set
            {
                if (SetField(ref _sequenceNumber, value))
                {
                    OnPropertyChanged(nameof(SequenceText));
                }
            }
        }

        public string SequenceText => _sequenceNumber.ToString("00", CultureInfo.InvariantCulture);

        public bool IsBeingDragged
        {
            get => _isBeingDragged;
            set => SetField(ref _isBeingDragged, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool IsMilestone =>
            Kind is StepCardKind.MilestoneStart or StepCardKind.MilestoneStop or StepCardKind.MilestoneEnd;

        // --- 1. 試驗準備欄位 ---
        public bool ZeroLoad
        {
            get => _zeroLoad;
            set => SetField(ref _zeroLoad, value);
        }

        public bool ZeroStroke
        {
            get => _zeroStroke;
            set => SetField(ref _zeroStroke, value);
        }

        public bool InputSpecimenSize
        {
            get => _inputSpecimenSize;
            set => SetField(ref _inputSpecimenSize, value);
        }

        // --- 2. 試片夾持欄位 ---
        public string AutoUnloadSpeed
        {
            get => _autoUnloadSpeed;
            set
            {
                if (SetField(ref _autoUnloadSpeed, value))
                {
                    OnPropertyChanged(nameof(IsUnloadStageSkipped));
                }
            }
        }

        public bool IsUnloadStageSkipped => ParseNumber(_autoUnloadSpeed) == 0d;

        // --- 3. 預荷重欄位 ---
        public string TargetPreload
        {
            get => _targetPreload;
            set
            {
                if (SetField(ref _targetPreload, value))
                {
                    OnPropertyChanged(nameof(IsPreloadStageSkipped));
                }
            }
        }

        public bool IsPreloadStageSkipped => ParseNumber(_targetPreload) == 0d;

        // --- 5 & 6. 拉伸階段欄位 ---
        public string CrossheadSpeed
        {
            get => _crossheadSpeed;
            set => SetField(ref _crossheadSpeed, value);
        }

        public string TargetSignal
        {
            get => _targetSignal;
            set => SetField(ref _targetSignal, value);
        }

        public string ComparisonOperator
        {
            get => _comparisonOperator;
            set => SetField(ref _comparisonOperator, value);
        }

        public string TargetValue
        {
            get => _targetValue;
            set => SetField(ref _targetValue, value);
        }

        // --- 7. 停止判定欄位 ---
        public string StartJudgeLoad
        {
            get => _startJudgeLoad;
            set => SetField(ref _startJudgeLoad, value);
        }

        public string LoadDropPercent
        {
            get => _loadDropPercent;
            set => SetField(ref _loadDropPercent, value);
        }

        public string LoadDropAmount
        {
            get => _loadDropAmount;
            set => SetField(ref _loadDropAmount, value);
        }

        // --- 9. 收尾流程欄位 ---
        public bool AutoSave
        {
            get => _autoSave;
            set => SetField(ref _autoSave, value);
        }

        public bool InputBreakMode
        {
            get => _inputBreakMode;
            set => SetField(ref _inputBreakMode, value);
        }

        public bool AutoReturn
        {
            get => _autoReturn;
            set => SetField(ref _autoReturn, value);
        }

        public string ReturnSpeed
        {
            get => _returnSpeed;
            set => SetField(ref _returnSpeed, value);
        }

        public string PositionMode
        {
            get => _positionMode;
            set => SetField(ref _positionMode, value);
        }

        public string TargetPosition
        {
            get => _targetPosition;
            set => SetField(ref _targetPosition, value);
        }

        /// <summary>
        /// 數值增減按鈕輔助方法：依據欄位名稱對數值作加減並保留合理範圍。
        /// </summary>
        public void StepNumericField(string fieldKey, double delta)
        {
            switch (fieldKey)
            {
                case nameof(AutoUnloadSpeed):
                    AutoUnloadSpeed = StepStringValue(AutoUnloadSpeed, delta, 0, 500);
                    break;
                case nameof(TargetPreload):
                    TargetPreload = StepStringValue(TargetPreload, delta, 0, 5000);
                    break;
                case nameof(CrossheadSpeed):
                    CrossheadSpeed = StepStringValue(CrossheadSpeed, delta, 0.1, 1000);
                    break;
                case nameof(TargetValue):
                    TargetValue = StepStringValue(TargetValue, delta, 0, 2000);
                    break;
                case nameof(StartJudgeLoad):
                    StartJudgeLoad = StepStringValue(StartJudgeLoad, delta, 0, 5000);
                    break;
                case nameof(LoadDropPercent):
                    LoadDropPercent = StepStringValue(LoadDropPercent, delta, 1, 100);
                    break;
                case nameof(LoadDropAmount):
                    LoadDropAmount = StepStringValue(LoadDropAmount, delta, 0, 5000);
                    break;
                case nameof(ReturnSpeed):
                    ReturnSpeed = StepStringValue(ReturnSpeed, delta, 1, 2000);
                    break;
                case nameof(TargetPosition):
                    TargetPosition = StepStringValue(TargetPosition, delta, 0, 2000);
                    break;
            }
        }

        private static double ParseNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0d;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out val))
                return val;
            return 0d;
        }

        private static string StepStringValue(string currentText, double delta, double min, double max)
        {
            double current = ParseNumber(currentText);
            double next = Math.Clamp(Math.Round(current + delta, 2), min, max);
            return next.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static List<FlowStepCardModel> CreateDefaultCards()
        {
            return new List<FlowStepCardModel>
            {
                new FlowStepCardModel
                {
                    Id = "card-01-prep",
                    SequenceNumber = 1,
                    Title = "試驗準備",
                    CategoryTag = "前置設定",
                    Kind = StepCardKind.Preparation,
                    ZeroLoad = true,
                    ZeroStroke = true,
                    InputSpecimenSize = true
                },
                new FlowStepCardModel
                {
                    Id = "card-02-grip",
                    SequenceNumber = 2,
                    Title = "試片夾持",
                    CategoryTag = "夾持補償",
                    Kind = StepCardKind.SpecimenGrip,
                    AutoUnloadSpeed = "5"
                },
                new FlowStepCardModel
                {
                    Id = "card-03-preload",
                    SequenceNumber = 3,
                    Title = "預荷重",
                    CategoryTag = "起始張力",
                    Kind = StepCardKind.Preload,
                    TargetPreload = "10"
                },
                new FlowStepCardModel
                {
                    Id = "card-04-start",
                    SequenceNumber = 4,
                    Title = "試驗開始",
                    CategoryTag = "里程碑",
                    Kind = StepCardKind.MilestoneStart,
                    MilestoneBadge = "START MILESTONE",
                    MilestoneDescription = "正式進入試驗控制迴路並開始同步記錄荷重—位移曲線資料。",
                    MilestoneSubnote = "此階段作為測試資料取樣與曲線繪製的基準起點。"
                },
                new FlowStepCardModel
                {
                    Id = "card-05-tensile1",
                    SequenceNumber = 5,
                    Title = "第一段拉伸",
                    CategoryTag = "程序控制",
                    Kind = StepCardKind.TensileStage,
                    CrossheadSpeed = "1",
                    TargetSignal = "橫擔向上位移",
                    ComparisonOperator = "≥",
                    TargetValue = "10"
                },
                new FlowStepCardModel
                {
                    Id = "card-06-tensile2",
                    SequenceNumber = 6,
                    Title = "第二段拉伸",
                    CategoryTag = "程序控制",
                    Kind = StepCardKind.TensileStage,
                    CrossheadSpeed = "5",
                    TargetSignal = "橫擔向上位移",
                    ComparisonOperator = "≥",
                    TargetValue = "30"
                },
                new FlowStepCardModel
                {
                    Id = "card-07-stopjudge",
                    SequenceNumber = 7,
                    Title = "停止判定",
                    CategoryTag = "破斷偵測",
                    Kind = StepCardKind.StopCriteria,
                    StartJudgeLoad = "10",
                    LoadDropPercent = "20",
                    LoadDropAmount = "100"
                },
                new FlowStepCardModel
                {
                    Id = "card-08-stop",
                    SequenceNumber = 8,
                    Title = "試驗停止",
                    CategoryTag = "里程碑",
                    Kind = StepCardKind.MilestoneStop,
                    MilestoneBadge = "STOP MILESTONE",
                    MilestoneDescription = "達成停止條件後立即停止橫擔致動輸出，鎖定最大荷重與斷裂點。",
                    MilestoneSubnote = "系統在此凍結即時控制迴路並轉入收尾程序。"
                },
                new FlowStepCardModel
                {
                    Id = "card-09-post",
                    SequenceNumber = 9,
                    Title = "收尾流程",
                    CategoryTag = "試後處理",
                    Kind = StepCardKind.PostProcess,
                    AutoSave = true,
                    InputBreakMode = true,
                    AutoReturn = true,
                    ReturnSpeed = "100",
                    PositionMode = "指定位置",
                    TargetPosition = "100"
                },
                new FlowStepCardModel
                {
                    Id = "card-10-end",
                    SequenceNumber = 10,
                    Title = "試驗結束",
                    CategoryTag = "里程碑",
                    Kind = StepCardKind.MilestoneEnd,
                    MilestoneBadge = "COMPLETE MILESTONE",
                    MilestoneDescription = "完成單次試驗循環、結算特徵值並準備下一組試片待命狀態。",
                    MilestoneSubnote = "流程設定到此結束，可隨時拖曳調整各階段順序。"
                }
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
