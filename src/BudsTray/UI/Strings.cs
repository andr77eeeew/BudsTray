using BudsTray.Core;

namespace BudsTray.UI;

/// <summary>
/// Every text the app shows. Members are required, so a new language with a missing text does not compile.
/// Protocol-style labels such as ANC, LEFT and CASE are the same in all languages and stay in the code.
/// </summary>
public sealed class Strings
{
    private static readonly Strings EnglishStrings = new()
    {
        LanguageCode = "EN",
        LanguageName = "English",

        Connecting = "Connecting…",
        TooltipCase = "Case",
        OpenPanel = "Open panel",
        StartWithWindows = "Start with Windows",
        Quit = "Quit",

        AncOff = "Noise control off",
        AncTransparency = "Transparency",
        AncLow = "Noise cancelling: low",
        AncMid = "Noise cancelling: medium",
        AncHigh = "Noise cancelling: high",
        AncAdaptive = "Noise cancelling: adaptive",

        RemainingMinutes = "≈ {0} min",
        RemainingHours = "≈ {0} h",
        RemainingHoursMinutes = "≈ {0} h {1} min",
        LowBattery = "Low battery: {0}",
        LeftLevel = "left {0}%",
        RightLevel = "right {0}%",
        JustNow = "just now",
        MinutesAgo = "{0} min ago",
        HoursAgo = "{0} h ago",
        DaysAgo = "{0} d ago",

        NotConnected = "Buds not connected",
        NotConnectedHint = "The icon will appear in the tray as soon as they connect to this computer. The app waits in the background and uses almost no resources.",
        QuitApp = "QUIT APP",
        StatusAway = "NO CONNECTION",
        StatusConnecting = "CONNECTING",
        Charging = "CHARGING",
        CaseUnknown = "CASE -- · open the lid to see the level",
        TimeLeft = "TIME LEFT {0}",
        TimeLeftUnknown = "TIME LEFT -- · estimate coming soon",
        Equalizer = "Equalizer",
        LowLatency = "Low latency",
        InEarDetection = "In-ear detection",
        ConnectionPopup = "Connection pop-up",
        BatteryHistory = "Battery history",
        AppProfiles = "App profiles",
        FindBuds = "Find my buds",
        Range24Hours = "24 HOURS",
        Range7Days = "7 DAYS",
        DrainUnknown = "DRAIN -- · not enough data yet",
        DrainPerHour = "DRAIN PER HOUR",
        Percent = "{0}%",
        HotkeyTaken = "TAKEN",
        FindWarning = "The sound is very loud. Take them out of your ears first.",
        FindConfirm = "Press again to start the sound.",
        RingConfirm = "SURE?",

        ProfilesHint = "While the app is running, the buds hold the chosen mode. When it closes, the previous mode returns.",
        AddApp = "ADD APP",
        ProcessPrompt = "PROCESS NAME OR A RUNNING APP",
        ProcessName = "Process name",
        NoiseControlCaption = "NOISE CONTROL",
        LowLatencyCaption = "LOW LATENCY",
        On = "ON",
        Off = "OFF",
        Save = "SAVE",
        Delete = "DELETE",
        Cancel = "CANCEL",
        ProfileLatencyOn = "LOW LATENCY ON",
        ProfileLatencyOff = "LOW LATENCY OFF",

        PopupConnected = "CONNECTED",
        LeftBudLow = "LEFT BUD LOW",
        RightBudLow = "RIGHT BUD LOW",
        BothBudsLow = "BOTH BUDS LOW",
    };

    private static readonly Strings RussianStrings = new()
    {
        LanguageCode = "RU",
        LanguageName = "Русский",

        Connecting = "Подключаюсь…",
        TooltipCase = "Кейс",
        OpenPanel = "Открыть панель",
        StartWithWindows = "Запускать с Windows",
        Quit = "Выход",

        AncOff = "Шумоподавление выключено",
        AncTransparency = "Прозрачность",
        AncLow = "Шумоподавление: низкое",
        AncMid = "Шумоподавление: среднее",
        AncHigh = "Шумоподавление: высокое",
        AncAdaptive = "Шумоподавление: адаптивное",

        RemainingMinutes = "≈ {0} мин",
        RemainingHours = "≈ {0} ч",
        RemainingHoursMinutes = "≈ {0} ч {1} мин",
        LowBattery = "Низкий заряд: {0}",
        LeftLevel = "левый {0} %",
        RightLevel = "правый {0} %",
        JustNow = "только что",
        MinutesAgo = "{0} мин назад",
        HoursAgo = "{0} ч назад",
        DaysAgo = "{0} дн назад",

        NotConnected = "Наушники не подключены",
        NotConnectedHint = "Иконка появится в трее, как только они подключатся к этому компьютеру. Приложение ждёт в фоне и почти ничего не потребляет.",
        QuitApp = "ВЫЙТИ ИЗ ПРИЛОЖЕНИЯ",
        StatusAway = "НЕТ СВЯЗИ",
        StatusConnecting = "ПОДКЛЮЧАЮСЬ",
        Charging = "ЗАРЯД",
        CaseUnknown = "CASE -- · открой крышку, чтобы узнать заряд",
        TimeLeft = "ХВАТИТ {0}",
        TimeLeftUnknown = "ХВАТИТ -- · прогноз появится чуть позже",
        Equalizer = "Эквалайзер",
        LowLatency = "Низкая задержка",
        InEarDetection = "Определение ношения",
        ConnectionPopup = "Уведомление о подключении",
        BatteryHistory = "История заряда",
        AppProfiles = "Профили приложений",
        FindBuds = "Найти наушники",
        Range24Hours = "24 ЧАСА",
        Range7Days = "7 ДНЕЙ",
        DrainUnknown = "РАСХОД -- · пока мало данных",
        DrainPerHour = "РАСХОД В ЧАС",
        Percent = "{0} %",
        HotkeyTaken = "ЗАНЯТО",
        FindWarning = "Сигнал очень громкий. Сначала достань их из ушей.",
        FindConfirm = "Нажми ещё раз, чтобы включить сигнал.",
        RingConfirm = "ТОЧНО?",

        ProfilesHint = "Пока приложение запущено, наушники держат выбранный режим. Когда оно закроется, режим вернётся.",
        AddApp = "ДОБАВИТЬ ПРИЛОЖЕНИЕ",
        ProcessPrompt = "ИМЯ ПРОЦЕССА ИЛИ ОДНО ИЗ ЗАПУЩЕННЫХ",
        ProcessName = "Имя процесса",
        NoiseControlCaption = "ШУМОПОДАВЛЕНИЕ",
        LowLatencyCaption = "НИЗКАЯ ЗАДЕРЖКА",
        On = "ВКЛ",
        Off = "ВЫКЛ",
        Save = "СОХРАНИТЬ",
        Delete = "УДАЛИТЬ",
        Cancel = "ОТМЕНА",
        ProfileLatencyOn = "ЗАДЕРЖКА ВКЛ",
        ProfileLatencyOff = "ЗАДЕРЖКА ВЫКЛ",

        PopupConnected = "ПОДКЛЮЧЕНЫ",
        LeftBudLow = "ЛЕВЫЙ РАЗРЯЖАЕТСЯ",
        RightBudLow = "ПРАВЫЙ РАЗРЯЖАЕТСЯ",
        BothBudsLow = "ОБА РАЗРЯЖАЮТСЯ",
    };

    public static Strings Current { get; private set; } = EnglishStrings;

    public static Strings For(AppLanguage language) => language switch
    {
        AppLanguage.Russian => RussianStrings,
        _ => EnglishStrings,
    };

    public static void Select(AppLanguage language) => Current = For(language);

    public required string LanguageCode { get; init; }
    public required string LanguageName { get; init; }

    public required string Connecting { get; init; }
    public required string TooltipCase { get; init; }
    public required string OpenPanel { get; init; }
    public required string StartWithWindows { get; init; }
    public required string Quit { get; init; }

    public required string AncOff { get; init; }
    public required string AncTransparency { get; init; }
    public required string AncLow { get; init; }
    public required string AncMid { get; init; }
    public required string AncHigh { get; init; }
    public required string AncAdaptive { get; init; }

    public required string RemainingMinutes { get; init; }
    public required string RemainingHours { get; init; }
    public required string RemainingHoursMinutes { get; init; }
    public required string LowBattery { get; init; }
    public required string LeftLevel { get; init; }
    public required string RightLevel { get; init; }
    public required string JustNow { get; init; }
    public required string MinutesAgo { get; init; }
    public required string HoursAgo { get; init; }
    public required string DaysAgo { get; init; }

    public required string NotConnected { get; init; }
    public required string NotConnectedHint { get; init; }
    public required string QuitApp { get; init; }
    public required string StatusAway { get; init; }
    public required string StatusConnecting { get; init; }
    public required string Charging { get; init; }
    public required string CaseUnknown { get; init; }
    public required string TimeLeft { get; init; }
    public required string TimeLeftUnknown { get; init; }
    public required string Equalizer { get; init; }
    public required string LowLatency { get; init; }
    public required string InEarDetection { get; init; }
    public required string ConnectionPopup { get; init; }
    public required string BatteryHistory { get; init; }
    public required string AppProfiles { get; init; }
    public required string FindBuds { get; init; }
    public required string Range24Hours { get; init; }
    public required string Range7Days { get; init; }
    public required string DrainUnknown { get; init; }
    public required string DrainPerHour { get; init; }
    public required string Percent { get; init; }
    public required string HotkeyTaken { get; init; }
    public required string FindWarning { get; init; }
    public required string FindConfirm { get; init; }
    public required string RingConfirm { get; init; }

    public required string ProfilesHint { get; init; }
    public required string AddApp { get; init; }
    public required string ProcessPrompt { get; init; }
    public required string ProcessName { get; init; }
    public required string NoiseControlCaption { get; init; }
    public required string LowLatencyCaption { get; init; }
    public required string On { get; init; }
    public required string Off { get; init; }
    public required string Save { get; init; }
    public required string Delete { get; init; }
    public required string Cancel { get; init; }
    public required string ProfileLatencyOn { get; init; }
    public required string ProfileLatencyOff { get; init; }

    public required string PopupConnected { get; init; }
    public required string LeftBudLow { get; init; }
    public required string RightBudLow { get; init; }
    public required string BothBudsLow { get; init; }
}
