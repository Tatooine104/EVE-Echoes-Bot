using OpenCvSharp;
using static EVEEchoesBot.Program;
using EVEEchoesBot;
using static System.Diagnostics.Process;
using System.Diagnostics;

namespace EVEEchoesBot
{

// [ ] TODO Проверить все методы и добавить новый метод Logger.Log() 
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
        Logger.Log("Бот успешно запущен.", LogType.Success);
        Logger.Log("Нажмите [ESC] в любой момент для плавной остановки скрипта.", LogType.Info);

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
            Logger.Log($"Ошибка в главном потоке: {ex.Message}", LogType.Error);
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
                        Logger.Log($"[Ошибка] Не найдено окно '{accountSettings.WindowTitle}' для аккаунта '{accountSettings.Name}'.", LogType.Error);
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

                Logger.Log($"Мультибот запущен. Аккаунтов в работе: {_activeBots.Count}", LogType.Success);
            }
            catch (Exception ex)
            {
                Logger.Log($"Критическая ошибка старта: {ex.Message}", LogType.Error);
                _cts.Cancel();
            }
        }

#endregion

#region Stop Bot

        private static void StopMultiBotSystem()
        {
            _cts.Cancel();

            // Блокируем доступ, если _activeBots используется в других потоках программы
            _activeBots.Clear();

            Logger.Log("Всем фоновым потокам ботов отправлен сигнал на остановку. Список активных ботов очищен.", LogType.Warning);
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
                // Проверяем, нажата ли клавиша ESC
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                {
                    Logger.Log("Обнаружено нажатие [ESC]. Инициирую остановку всех ботов...", LogType.Warning);

                    // Вызываем наш новый метод остановки, который плавно гасит все задачи Task
                    StopMultiBotSystem();
                    break;
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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен клик по элементу {element} (X={x}, Y={y})", LogType.Test);
        #endif
        }

#endregion

    }

}


