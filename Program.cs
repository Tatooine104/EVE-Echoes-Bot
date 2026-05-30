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

    static partial class Program
    {

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

        #region Constants & Fields

        // 1. Создаем глобальный источник токена отмены
        private static CancellationTokenSource _cts = new();

        // 1. Глобальные переменные для управления состоянием
        public static readonly List<ActiveBotAccount> _activeBots = [];
        private static BotConfig? _config;

        /// <summary>
        /// Глобальный путь к папке Images в корне проекта.
        /// </summary>
        public static string TemplatesDir => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Images"));

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

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        Logger.Log("Бот успешно запущен.", LogType.Success);
        Logger.Log("Нажмите [ESC] в любой момент для плавной остановки.", LogType.Info);


        // Запуск фонового потока для непрерывного отслеживания нажатия управляющих клавиш
        Thread inputThread = new(ListenForCancelKey) { IsBackground = true };
        inputThread.Start();

        // Запускаем многопоточную систему (вместо InitializeBot)
        StartMultiBotSystem();

        // Главный поток программы просто засыпает и ждет, пока пользователь не нажмет ESC.
        // Пока Main ждет, все боты параллельно работают в фоне!
        try
        {
            // Ожидаем отмены через токен (когда сработает ListenForCancelKey и вызовет _cts.Cancel())
            _cts.Token.WaitHandle.WaitOne();
        }
        catch (Exception ex)
        {
            Logger.Log($"Критический сбой в главном потоке: {ex.Message}", LogType.Error);
        }

        // Перед выходом даем потокам ботов время на плавное закрытие
        StopMultiBotSystem();
        Thread.Sleep(1000);

        Logger.Log("Бот остановлен. Сессия завершена.", LogType.Warning);
    }



#endregion

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

                    var bot = new ActiveBotAccount(accountSettings)
                    {
                        Hwnd = hWnd
                    };

                    bot.Start(_cts.Token);
                    _activeBots.Add(bot);
                }

                Logger.Log($"Мультисистема запущена. Аккаунтов в работе: {_activeBots.Count}", LogType.Success);
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
            // Проверяем, нажата ли какая-нибудь клавиша в консоли
            if (Console.KeyAvailable)
            {
                ConsoleKey pressedKey = Console.ReadKey(true).Key;

                // СЦЕНАРИЙ 1: Нажатие ESC — просто плавная остановка
                if (pressedKey == ConsoleKey.Escape)
                {
                    Logger.Log("Обнаружено нажатие [ESC]. Запуск остановки всех аккаунтов.", LogType.Warning);
                    StopMultiBotSystem();
                    break;
                }

                // СЦЕНАРИЙ 2: Нажатие F10 — экстренное сохранение скриншотов и остановка
                if (pressedKey == ConsoleKey.Escape || pressedKey == ConsoleKey.F10)
                {
                    Logger.Log("Обнаружено нажатие [F10]. Создание экстренных снимков экрана и запуск остановки.", LogType.Warning);

                    string debugDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugScreenshots"));
                    
                    try
                    {
                        Directory.CreateDirectory(debugDir);

                        // Делаем скриншоты для каждого работающего в данный момент аккаунта
                        foreach (var bot in _activeBots.ToList())
                        {
                            if (bot.Hwnd == IntPtr.Zero) continue;

                            // Используем ваш графический метод захвата окна
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

                    // После сохранения скриншотов принудительно останавливаем систему и выходим
                    StopMultiBotSystem();
                    break;
                }
            }
            
            Thread.Sleep(100); // Небольшая пауза, чтобы не нагружать ядро процессора циклом
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


