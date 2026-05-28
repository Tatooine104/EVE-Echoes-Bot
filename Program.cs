using OpenCvSharp;
using static EVEEchoesBot.Program;
using EVEEchoesBot;

namespace EVEEchoesBot
{

// [ ] TODO Проверить все методы и добавить новый метод Logger.Log() 
// [v] TODO 2026.05.27 Заменить все SmartClick с координатами на вызовы по енуму 

    static partial class Program
    {

        #region Constants & Fields

        // 1. Создаем глобальный источник токена отмены
        private static CancellationTokenSource _cts = new();

        #endregion


    // 1. Глобальные переменные для управления состоянием
    private static readonly List<ActiveBotAccount> _activeBots = [];
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
        ChatFastInput  = 12500700, // Мню быстрого ввода
        ChatInputMenu  =  3650700, // Меню ввода чата
        ChatTabAli     =   500450, // Вкладка чата альянса
        ChatsInterface =   250600  // Интерфейс чатов

        // hWnd.ClickTo(GameUi.ChatTabAli); // Пример вызова

    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
// - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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

#region StartMultiBotSystem

    private static void StartMultiBotSystem()
    {
        try
        {
            // 1. Загружаем конфигурацию один раз при старте
            _config = ConfigManager.Load();

            if (_config?.Accounts == null || _config.Accounts.Count == 0)
            {
                Logger.Log("В конфигурации нет доступных аккаунтов. Запуск отменен.", LogType.Error);
                _cts.Cancel();
                return;
            }

            // Пересоздаем токен отмены на случай повторного перезапуска системы
            if (_cts.IsCancellationRequested)
            {
                _cts = new CancellationTokenSource();
            }

            _activeBots.Clear();

            // 2. Проходим по каждому аккаунту из конфига и готовим его к запуску
            foreach (WindowSettings accountSettings in _config.Accounts)
            {
                // Находим окно эмулятора по его WindowTitle из JSON
                IntPtr hWnd = WinAPI.FindWindow(null, accountSettings.WindowTitle);

                if (hWnd == IntPtr.Zero)
                {
                    Logger.Log($"[Ошибка] Не найдено запущенное окно '{accountSettings.WindowTitle}' для аккаунта '{accountSettings.Name}'. Пропускаем.", LogType.Error);
                    continue;
                }

                // Создаем экземпляр нашего нового класса. 
                // Он сам внутри себя переведет строку FirstTask в нужный Enum.
                var bot = new ActiveBotAccount(accountSettings)
                {
                    Hwnd = hWnd
                };

                // Запускаем бота в индивидуальном фоновом потоке
                bot.Start(_cts.Token);

                // Сохраняем ссылку на работающего бота в список управления
                _activeBots.Add(bot);
            }

            Logger.Log($"Многопоточная система успешно запущена. Аккаунтов в работе: {_activeBots.Count} из {_config.Accounts.Count}", LogType.Success);
        }
        catch (Exception ex)
        {
            Logger.Log($"Критическая ошибка при запуске мультибота: {ex.Message}", LogType.Error);
            _cts.Cancel();
        }
    }

    private static void StopMultiBotSystem()
    {
        _cts.Cancel();
        Logger.Log("Всем фоновым потокам ботов отправлен сигнал на остановку.", LogType.Warning);
}

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

#endregion

        internal static void ClickTo(this IntPtr hWnd, GameUi element, int minSec = 1, int maxSec = 3, int offset = 3)
        {
            // Распаковываем координаты обратно в X и Y
            int rawValue = (int)element;
            int x = rawValue / 10000;
            int y = rawValue % 10000;

            // Вызываем ваш оригинальный метод
            Tools.SmartClick(hWnd, x, y, minSec, maxSec, offset);
        }
    }

}


