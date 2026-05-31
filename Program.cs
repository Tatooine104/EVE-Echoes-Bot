using OpenCvSharp;
using static EVEEchoesBot.Program;
using EVEEchoesBot;
using static System.Diagnostics.Process;
using System.Diagnostics;

namespace EVEEchoesBot
{

// [v] TODO Проверить все методы и добавить новый метод Logger.Log() 
// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 
// [v] TODO 2026.05.27 Заменить все SmartClick с координатами на вызовы по енуму 
// [ ] TODO 2026.05.30 Сделать переменную хранящую текущую версию программы и добавить вывод в лог 

    static partial class Program
    {

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Constants & Fields

        public static readonly string _ProgVersion = $"v.{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.01.000"}";

        // 1. Создаем глобальный источник токена отмены
        private static CancellationTokenSource _cts = new();

        // Флаг для предотвращения повторного входа в метод остановки
        private static int _isStopping = 0;

        // 1. Глобальные переменные для управления состоянием
        public static readonly List<ActiveBotAccount> _activeBots = [];
        private static BotConfig? _config;

        /// <summary>
        /// Глобальный путь к папке Images в корне проекта.
        /// </summary>
        public static string TemplatesDir
        {
            get
            {
                // 1. Путь для РЕЛИЗА (папка images лежит прямо рядом с .exe)
                string releasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");

                if (Directory.Exists(releasePath))
                {
                    return releasePath;
                }

                // 2. Откатываемся на путь для ОТЛАДКИ (если запускаем из Visual Studio)
                return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Images"));
            }
        }

        public enum GameUi
        {
            // ИмяЭлемента = (X * 10000) + Y (упаковываем X и Y в одно число для Enum)
            // MenuButton = 500450,    // X: 50,  Y: 450 (Ваш пример)
            ChatButtSend   =  4450695, // Кнопка чата "Send"
            WindowCenter   =  8000250, // Точка чуть ниже и правее центра окна
            ChatMessScout  =  3000600, // Сообщение "Scout"
            ChatInform     =   800400, // Меню "Inform"
            ChatFastInput  = 11900685, // Мню быстрого ввода
            ChatInputMenu  =  3650700, // Меню ввода чата
            ChatTabAli     =   500450, // Вкладка чата альянса
            ChatsInterface =   250625  // Интерфейс чатов

            // hWnd.ClickTo(GameUi.ChatTabAli); // Пример вызова

        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Main

        public static void Main()
        {
            // 1. Настраиваем кодировку, чтобы любые стартовые ошибки читались корректно
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;

            // 2. Глобальный перехват ошибок в фоновых потоках
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                string exceptionMessage = e.ExceptionObject is Exception ex ? ex.ToString() : "Неизвестный сбой среды выполнения.";
                Logger.Log($"КРИТИЧЕСКИЙ СБОЙ СИСТЕМЫ (UnhandledException): {exceptionMessage}", LogType.Error);
            };

            // Глобальный перехват ошибок в тасках
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Logger.Log($"КРИТИЧЕСКИЙ СБОЙ ТАСКА (UnobservedTaskException): {e.Exception?.Message}", LogType.Error);
                e.SetObserved();
            };

            // 3. Проверка файлов ДО старта всей системы
            if (!CheckRequiredFiles()) return;

            Logger.Log("Бот успешно запущен.", LogType.Info);
            Logger.Log("Нажмите [ESC] в любой момент для плавной остановки.", LogType.Info);

            // 4. Запуск фонового потока для отслеживания [ESC]
            Thread inputThread = new(ListenForCancelKey) { IsBackground = true };
            inputThread.Start();

            // 5. Запуск многопоточных ботов
            StartMultiBotSystem();

            // 6. Ожидаем сигнала отмены от токена (пока боты работают параллельно)
            try
            {
                _cts.Token.WaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                Logger.Log($"Критический сбой в главном потоке: {ex.Message}", LogType.Error);
            }

            // 7. Программа выходит из ожидания. Потоки уже останавливаются методом ListenForCancelKey.
            // Даем 1 секунду, чтобы фоновые потоки успели дописать логи и сохранить файлы на диск.
            Thread.Sleep(1000);

            Logger.Log("Бот остановлен. Сессия завершена.", LogType.Warning);
        }




#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

private static bool CheckRequiredFiles()
{
    // 1. Компоненты кликера (всегда лежат в корне рядом с .exe)
    string[] rootFiles = ["adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"];

    // 2. Шаблоны OpenCV (лежат внутри папки images)
    string[] templateFiles =
    [
        "imgAliChatENG.png",
        "imgBeltCondensed.png",
        "imgBeltMoon.png",
        "imgCorpChatENG.png",
        "imgLocalChatHead.png",
        "imgLocalChatIcon.png",
        "imgLocalCriminal.png",
        "imgLocalMinus.png",
        "imgLocalNeutral.png"
    ];

    bool allExist = true;

    // Проверяем файлы кликера в корне
    foreach (var file in rootFiles)
    {
        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
        if (!File.Exists(fullPath))
        {
            Logger.Log($"Критическая ошибка релиза: Отсутствует файл '{file}' по пути '{fullPath}'!", LogType.Error);
            allExist = false;
        }
    }

    // Проверяем шаблоны картинок в их целевой папке
    foreach (var file in templateFiles)
    {
        string fullPath = Path.Combine(TemplatesDir, file);
        if (!File.Exists(fullPath))
        {
            Logger.Log($"Критическая ошибка релиза: Отсутствует шаблон '{file}' по пути '{fullPath}'!", LogType.Error);
            allExist = false;
        }
    }

    if (!allExist)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n[ОШИБКА] Работа бота невозможна. Проверьте целостность папки приложения.");
        Console.WriteLine("Нажмите любую клавишу для выхода...");
        Console.ReadKey();
    }

    return allExist;
}


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Start Bot

        private static void StartMultiBotSystem()
        {
            try
            {
                // 1. Перезапускаем ADB сервер в чистом режиме
                string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
                if (File.Exists(adbPath))
                {
                    Process.Start(new ProcessStartInfo(adbPath, "kill-server") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                    Process.Start(new ProcessStartInfo(adbPath, "start-server") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                }

                _config = ConfigManager.Load();

                if (_config?.Accounts == null || _config.Accounts.Count == 0)
                {
                    Logger.Log("В конфигурации нет доступных аккаунтов. Запуск отменен.", LogType.Error);
                    return;
                }

                if (_cts.IsCancellationRequested) _cts = new CancellationTokenSource();
                _activeBots.Clear();

                foreach (AccSettings accountSettings in _config.Accounts)
                {
                    IntPtr hWnd = WinAPI.FindWindow(null, accountSettings.WindowTitle);
                    if (hWnd == IntPtr.Zero)
                    {
                        Logger.Log($"Окно '{accountSettings.WindowTitle}' для аккаунта '{accountSettings.Name}' не найдено.", LogType.Error);
                        continue;
                    }

                    // БЕРЕМ ПОРТ НАПРЯМУЮ ИЗ ВАШЕГО JSON И СРАЗУ АКТИВИРУЕМ ИНЖЕНЕРНУЮ СЕТКУ
                    if (File.Exists(adbPath))
                    {
                        string targetDevice = $"127.0.0.1:{accountSettings.AdbPort}";

                        // Коннектим эмулятор по порту, который вы нашли глазами в настройках
                        Process.Start(new ProcessStartInfo(adbPath, $"connect {targetDevice}") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();

                        // Включаем сетку
                        Process.Start(new ProcessStartInfo(adbPath, $"-s {targetDevice} shell settings put system pointer_location 1") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                    }

                    // Создаем объект аккаунта (внутри его конструктора или инициализации вызывается TryLoadLastStatsAndQueue)
                    var bot = new ActiveBotAccount(accountSettings)
                    {
                        Hwnd = hWnd
                    };

                    // ПРОВЕРКА И ОПРОС: Если после загрузки статов поля остались пустыми или содержат "???"
                    if (string.IsNullOrEmpty(bot._eveSystem) || bot._eveSystem == "???" ||
                        string.IsNullOrEmpty(bot._eveShip) || bot._eveShip == "???")
                    {
                        Console.ResetColor();
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n--- Дополнительная настройка для аккаунта [{accountSettings.Name}] ---");
                        Console.ResetColor();

                        // Опрашиваем звездную систему
                        if (string.IsNullOrEmpty(bot._eveSystem) || bot._eveSystem == "???")
                        {
                            string sys = "";
                            while (string.IsNullOrWhiteSpace(sys))
                            {
                                Console.Write("Введите текущую звездную систему (например, UB-UQZ): ");
                                sys = Console.ReadLine()?.Trim() ?? "";
                            }
                            bot._eveSystem = sys;
                        }

                        // Опрашиваем тип корабля
                        if (string.IsNullOrEmpty(bot._eveShip) || bot._eveShip == "???")
                        {
                            string ship = "";
                            while (string.IsNullOrWhiteSpace(ship))
                            {
                                Console.Write("Введите название корабля (например, Covetor II): ");
                                ship = Console.ReadLine()?.Trim() ?? "";
                            }
                            bot._eveShip = ship;
                        }

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Данные успешно приняты!");
                        Console.ResetColor();
                    }

                    // Теперь запускаем бот, зная, что теги системы и корабля гарантированно заполнены!
                    bot.Start(_cts.Token);
                    _activeBots.Add(bot);
                }

                Logger.Log($"Мультисистема запущена. Аккаунтов в работе: {_activeBots.Count}", LogType.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Критический сбой при запуске мультисистемы: {ex.Message}", LogType.Error);
                _cts.Cancel();
            }
        }

#endregion

#region Stop Bot

        private static async void StopMultiBotSystem()
        {

            // Если значение _isStopping уже равно 1, метод сразу завершает работу без повторного лога
            // Если значение было 0, оно атомарно меняется на 1, и код идет дальше
            if (System.Threading.Interlocked.CompareExchange(ref _isStopping, 1, 0) == 1)
            {
                return;
            }

            // 1. Отправляем сигнал отмены всем потокам
            _cts.Cancel();
            Logger.Log("Всем фоновым потокам отправлен сигнал остановки. Ожидание завершения...", LogType.Warning);

            try
            {
                // 2. Даем потокам время проснуться от Task.Delay, выполнить блок finally и вызвать SaveStats()
                await Task.Delay(2000);
            }
            catch { /* Игнорируем возможные ошибки таймера */ }

            // 3. Только ТЕПЕРЬ, когда потоки гарантированно засыпают или уже закрылись, очищаем список
            _activeBots.Clear();

            Logger.Log("Список активных аккаунтов очищен. Система полностью остановлена.", LogType.Warning);
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region ListenForCancelKey

        /// <summary>
        /// Постоянно работает в фоновом потоке, отслеживая нажатия управляющих клавиш:
        /// <list type="bullet">
        /// <item><description><c>ConsoleKey.Escape</c> — инициирует плавную остановку всех процессов бота.</description></item>
        /// <item><description><c>ConsoleKey.F10</c> — запускает мгновенный изолированный тест эмуляции клика драйвером.</description></item>
        /// </list>
        /// </summary>
        private static void ListenForCancelKey()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKey pressedKey = Console.ReadKey(true).Key;

                    // СЦЕНАРИЙ 1: Нажата строго клавиша ESC
                    if (pressedKey == ConsoleKey.Escape)
                    {
                        Logger.Log("Обнаружено нажатие [ESC]. Запуск остановки всех аккаунтов.", LogType.Warning);
                        StopMultiBotSystem();
                        break;
                    }
                    // СЦЕНАРИЙ 2: Нажата строго клавиша F10 (без дублирования ESC)
                    else if (pressedKey == ConsoleKey.F10)
                    {
                        Logger.Log("Обнаружено нажатие [F10]. Создание экстренных снимков экрана и запуск остановки.", LogType.Warning);

                        string debugDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugScreenshots"));

                        try
                        {
                            Directory.CreateDirectory(debugDir);

                            foreach (var bot in _activeBots.ToList())
                            {
                                if (bot.Hwnd == IntPtr.Zero) continue;

                                using OpenCvSharp.Mat? screenshot = Tools.CaptureWindow(bot.Hwnd);

                                if (screenshot?.Empty() is false && screenshot.Width > 0 && screenshot.Height > 0)
                                {
                                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                    string fileName = $"{bot.Settings.Name}_F10_Emergency_{timestamp}.png";
                                    string fullPath = Path.Combine(debugDir, fileName);

                                    OpenCvSharp.Cv2.ImWrite(fullPath, screenshot);
                                    Logger.Log($"Снимок экрана для аккаунта '{bot.Settings.Name}' сохранен: {fileName}", LogType.Info);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Не удалось выполнить экстренное сохранение снимков: {ex.Message}", LogType.Warning);
                        }

                        StopMultiBotSystem();
                        break;
                    }
                }

                Thread.Sleep(100);
            }
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region ClickTo

        internal static void ClickTo(this ActiveBotAccount bot, GameUi element, int minSec = 1, int maxSec = 3, int offset = 3)
        {
            // Распаковываем координаты X и Y из вашего Enum GameUi
            int packed = (int)element;
            int x = packed / 10000;
            int y = packed % 10000;

            // Вызываем обновленный ADB-кликер, передавая порт этого конкретного бота
            Tools.SmartClick(x, y, minSec, maxSec, offset, adbPort: bot.Settings.AdbPort);

        #if DEBUG
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен клик по элементу '{element}' (X={x}, Y={y}).", LogType.Test);
        #endif
        }

#endregion

    }

}


