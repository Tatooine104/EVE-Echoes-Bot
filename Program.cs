using OpenCvSharp;
using static EVEEchoesBot.Program;
using EVEEchoesBot;
using static System.Diagnostics.Process;
using System.Diagnostics;
using EVEEchoesBot.resources;

namespace EVEEchoesBot;

// [v] TODO Проверить все методы и добавить новый метод Logger.Log() 
// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 
// [v] TODO 2026.05.27 Заменить все SmartClick с координатами на вызовы по енуму 
// [v] TODO 2026.05.30 Сделать переменную хранящую текущую версию программы и добавить вывод в лог 
// [ ] TODO 2026.06.01 Реализовать дерево поведения 
// [ ] TODO 2026.06.01 Навести порядок в файлах и красиво оформить код 

static partial class Program
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Constants & Fields

    /// <summary>
    /// Глобальный источник токена отмены (CancellationTokenSource) для каскадного завершения всех асинхронных воркеров приложения.
    /// </summary>
    private static CancellationTokenSource _cts = new();

    /// <summary>
    /// Атомарный флаг (0 — работает, 1 — останавливается), предотвращающий повторный вход в метод безопасной остановки бота при множественном перехвате событий.
    /// </summary>
    private static int _isStopping = 0;

    /// <summary>
    /// Глобальный потокобезопасный список всех запущенных и активных в текущей сессии аккаунтов-воркеров.
    /// </summary>
    public static readonly List<ActiveBotAccount> _activeBots = [];

    /// <summary>
    /// Ссылка на объект глобальной конфигурации приложения, содержащий параметры всех аккаунтов.
    /// </summary>
    private static BotConfig? _config;

    /// <summary>
    /// Глобальное свойство, возвращающее актуальный путь к папке с графическими шаблонами (Images).
    /// Автоматически переключает контекст между релизной директорией и отладочной папкой исходного кода проекта.
    /// </summary>
    public static string TemplatesDir
    {
        get
        {
            // Настройка пути для RELEASE-сборки (папка images лежит непосредственно в корне исполняемого файла)
            string releasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");

            if (Directory.Exists(releasePath))
            {
                return releasePath;
            }

            // Настройка фолбека для DEBUG-режима (автоматический подъем на 3 уровня выше bin/Debug/ к исходникам)
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Images"));
        }
    }

    /// <summary>
    /// Перечисление элементов графического интерфейса игры EVE Echoes с упакованными координатами клика.
    /// Каждое значение сформировано по математическому правилу сжатия векторов: <c>ИмяЭлемента = (X * 10000) + Y</c> [INDEX].
    /// </summary>
    public enum GameUi
    {
        // 1. Взаимодействие с окнами и базовым интерфейсом игры
        
        /// <summary>Иконка развертывания общей панели игровых чатов.</summary>
        ChatsInterface = 250625,
        
        /// <summary>Точка безопасности чуть ниже и правее геометрического центра окна эмулятора для сброса фокуса меню.</summary>
        WindowCenter = 8000250,

        // 2. Навигация по вкладкам и каналам связи
        
        /// <summary>Вкладка прямого канала связи альянса.</summary>
        ChatTabAli = 500450,

        // 3. Индивидуальная цепочка шагов макроса автоматического оповещения
        
        /// <summary>Кнопка активации текстового меню ввода в чат.</summary>
        ChatInputMenu = 3650700,
        
        /// <summary>Кнопка перехода в оверлей шаблонов быстрого ввода фраз.</summary>
        ChatFastInput = 11900685,
        
        /// <summary>Вкладка "Inform" для прикрепления автоматических данных разведки системы.</summary>
        ChatInform = 800400,
        
        /// <summary>Выбор предустановленного статус-сообщения "Scout" в списке быстрых команд.</summary>
        ChatMessScout = 3000600,
        
        /// <summary>Финальная кнопка "Send" для отправки сформированного пакета данных в активный канал.</summary>
        ChatButtSend = 4450695
    }

    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    #region Main

    /// <summary>
    /// Главная точка входа (Entry Point) всего приложения.
    /// Настраивает кодировки ввода-вывода, инициализирует глобальные ловушки критических исключений в ThreadPool/Tasks,
    /// выполняет предстартовую валидацию файлов, разворачивает многопоточную сетку окон и удерживает главный поток приложения 
    /// до получения сигнала отмены через асинхронный перехватчик аппаратных клавиш.
    /// </summary>
    public static void Main()
    {
        // 1. Настраиваем системную кодировку UTF-8, чтобы любые стартовые ошибки WinAPI или JSON читались корректно
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        // 2. Глобальный перехват необработанных ошибок в фоновых потоках CLR
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            string exceptionMessage = e.ExceptionObject is Exception ex ? ex.ToString() : "Неизвестный сбой среды выполнения.";
            Logger.Log($"КРИТИЧЕСКИЙ СБОЙ СИСТЕМЫ (UnhandledException): {exceptionMessage}", LogType.Error);
        };

        // Глобальный перехват и подавление необработанных ошибок внутри асинхронных задач (Task)
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Logger.Log($"КРИТИЧЕСКИЙ СБОЙ ЗАДАЧИ (UnobservedTaskException): {e.Exception?.Message}", LogType.Error);
            e.SetObserved(); // Помечаем исключение как обработанное, предотвращая падение процесса
        };

        // 3. Валидация необходимых графических файлов и шаблонов ДО старта всей системы
        if (!CheckRequiredFiles()) return;

        Logger.Log("Бот успешно запущен.", LogType.Warning);
        Logger.Log("Нажмите [ESC] в любой момент для плавной остановки.", LogType.Warning);

        // 4. Запуск фонового низкоуровневого потока для непрерывного отслеживания управляющей клавиши ESC
        Thread inputThread = new(ListenForCancelKey) { IsBackground = true };
        inputThread.Start();

        // 5. Инициализация и параллельный запуск многопоточной экосистемы игровых воркеров
        StartMultiBotSystem();

        // 6. Ожидаем сигнала отмены от токена (блокируем главный поток, пока боты работают в ThreadPool)
        try
        {
            _cts.Token.WaitHandle.WaitOne();
        }
        catch (Exception ex)
        {
            Logger.Log($"Критический сбой в главном потоке: {ex.Message}", LogType.Error);
        }

        // 7. Программа выходит из ожидания. Потоки уже останавливаются методом ListenForCancelKey.
        // Даем фиксированную задержку, чтобы фоновые потоки гарантированно успели дописать логи и сохранить файлы на диск.
        Thread.Sleep(1000);

        Logger.Log("Бот остановлен. Сессия завершена.", LogType.Warning);
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    #region Required Files Check

    /// <summary>
    /// Выполняет предстартовую валидацию целостности сборки приложения.
    /// Проверяет наличие исполняемых файлов ADB внутри папки ресурсов и существование всех эталонных графических 
    /// шаблонов OpenCV в целевой директории картинок. В случае сбоя блокирует запуск бота.
    /// </summary>
    /// <returns>Возвращает <c>true</c>, если все необходимые системные файлы и шаблоны присутствуют на диске; иначе <c>false</c>.</returns>
    private static bool CheckRequiredFiles()
    {
        // 1. Компоненты кликера ADB (теперь автоматически копируются в подпапку resources)
        string[] resourcesFiles = ["adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"];

        // 2. Шаблоны OpenCV (лежат внутри динамически определяемой папки images)
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

        // Проверяем файлы кликера внутри подпапки resources
        foreach (var file in resourcesFiles)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", file);
            if (!File.Exists(fullPath))
            {
                Logger.Log($"Критическая ошибка релиза: Отсутствует файл '{file}' по пути '{fullPath}'!", LogType.Error);
                allExist = false;
            }
        }

        // Проверяем графические шаблоны картинок в их целевой папке Images
        foreach (var file in templateFiles)
        {
            string fullPath = Path.Combine(TemplatesDir, file);
            if (!File.Exists(fullPath))
            {
                Logger.Log($"Критическая ошибка релиза: Отсутствует шаблон '{file}' по пути '{fullPath}'!", LogType.Error);
                allExist = false;
            }
        }

        // Если хотя бы один файл потерян — аварийно останавливаем запуск
        if (!allExist)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n[ОШИБКА] Работа бота невозможна. Проверьте целостность папки приложения.");
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        return allExist;
    }

    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    #region Multi-Bot System Start

    /// <summary>
    /// Производит чистый перезапуск сервера ADB, считывает глобальный конфигурационный файл,
    /// выполняет сетевое подключение каждого эмулятора по его индивидуальному порту,
    /// разворачивает координатную сетку Android, доинициализирует контекст персонажей и запускает 
    /// параллельные асинхронные воркеры для всех доступных аккаунтов.
    /// </summary>
    private static void StartMultiBotSystem()
    {
        try
        {
            // 1. Формируем путь к ADB с учетом его переноса в подпапку ресурсов
            string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "adb.exe");
            
            // Перезапускаем ADB сервер в чистом режиме для предотвращения зависших сетевых сессий
            if (File.Exists(adbPath))
            {
                Process.Start(new ProcessStartInfo(adbPath, "kill-server") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                Process.Start(new ProcessStartInfo(adbPath, "start-server") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
            }

            // Загружаем глобальный JSON-конфиг через менеджер конфигураций
            _config = ConfigManager.Load();

            if (_config?.Accounts == null || _config.Accounts.Count == 0)
            {
                Logger.Log("В конфигурации нет доступных аккаунтов. Запуск мультисистемы отменен.", LogType.Error);
                return;
            }

            if (_cts.IsCancellationRequested) _cts = new CancellationTokenSource();
            _activeBots.Clear();

            // Итерируемся по списку аккаунтов для их параллельной инициализации
            foreach (AccSettings accountSettings in _config.Accounts)
            {
                // Ищем дескриптор главного окна Windows через WinAPI по его уникальному Title
                IntPtr hWnd = WinAPI.FindWindow(null, accountSettings.WindowTitle);
                if (hWnd == IntPtr.Zero)
                {
                    Logger.Log($"Окно '{accountSettings.WindowTitle}' для аккаунта '{accountSettings.Name}' не найдено в ОС.", LogType.Error);
                    continue;
                }

                // Подключаем эмулятор к ADB по порту, прописанному в JSON, и активируем отладочную разметку экрана
                if (File.Exists(adbPath))
                {
                    string targetDevice = $"127.0.0.1:{accountSettings.AdbPort}";

                    // Коннектим эмулятор по порту, который вы нашли глазами в настройках BlueStacks/LDPlayer
                    Process.Start(new ProcessStartInfo(adbPath, $"connect {targetDevice}") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();

                    // Включаем встроенную системную сетку Android для визуального контроля кликов бота
                    Process.Start(new ProcessStartInfo(adbPath, $"-s {targetDevice} shell settings put system pointer_location 1") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                }

                // Создаем объект аккаунта (внутри его конструктора автоматически восстанавливается статистика и очередь задач)
                var bot = new ActiveBotAccount(accountSettings)
                {
                    Hwnd = hWnd
                };

                // ФОЛБЕК-ОПРОС: Если после десериализации статов поля системы или корабля остались пустыми — запрашиваем ввод у оператора
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
                            Console.Write("Введите текущую звездную систему (например, Jita): ");
                            sys = Console.ReadLine()?.Trim() ?? "";
                        }
                        bot._eveSystem = sys;
                    }

                    // Опрашиваем тип игрового корабля
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

                // Запускаем асинхронный воркер в ThreadPool, передавая токен отмены
                bot.Start(_cts.Token);
                _activeBots.Add(bot);
            }

            Logger.Log($"Мультисистема успешно запущена. Аккаунтов в работе: {_activeBots.Count}", LogType.Info);
        }
        catch (Exception ex)
        {
            Logger.Log($"Критический сбой при запуске мультисистемы: {ex.Message}", LogType.Error);
            _cts.Cancel(); // Сворачиваем запуск в случае непредвиденного системного исключения
        }
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Stop Bot

    /// <summary>
    /// Асинхронно и потокобезопасно производит остановку всей мультисистемы ботов.
    /// Использует атомарную операцию сравнения с обменом (Interlocked.CompareExchange) для защиты от повторного входа,
    /// инициирует отмену глобального токена, предоставляет фоновым воркерам временной интервал (2000 мс) 
    /// для фиксации статов на диске и полностью очищает коллекцию активных аккаунтов.
    /// </summary>
    private static async void StopMultiBotSystem()
    {
        // Защитный барьер: если значение _isStopping уже равно 1, метод сразу завершает работу без повторного входа.
        // Если значение было 0, оно атомарно меняется на 1, и код идет дальше выполнять процедуру остановки.
        if (System.Threading.Interlocked.CompareExchange(ref _isStopping, 1, 0) == 1)
        {
            return;
        }

        // 1. Отправляем сигнал отмены всем параллельно работающим потокам воркеров
        _cts.Cancel();
        Logger.Log("Всем фоновым потокам отправлен сигнал остановки. Ожидание завершения...", LogType.Warning);

        try
        {
            // 2. Даем потокам фиксированное время проснуться от Task.Delay, выполнить блок finally и вызвать SaveStats()
            await Task.Delay(2000);
        }
        catch 
        { 
            /* Игнорируем возможные системные ошибки прерывания таймера ожидания */ 
        }

        // 3. Только ТЕПЕРЬ, когда потоки гарантированно засыпают или уже закрылись, очищаем общий список
        _activeBots.Clear();

        Logger.Log("Список активных аккаунтов очищен. Система полностью остановлена.", LogType.Warning);
    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region ListenForCancelKey

    /// <summary>
    /// Непрерывно выполняется в выделенном фоновом потоке, перехватывая нажатия управляющих горячих клавиш.
    /// <list type="bullet">
    /// <item><description><c>ConsoleKey.Escape</c> — инициирует немедленный штатный запуск плавной остановки всех окон.</description></item>
    /// <item><description><c>ConsoleKey.F10</c> — производит экстренный высокоточный сбор скриншотов со всех активных эмуляторов с фиксацией на диск, после чего глушит систему.</description></item>
    /// </list>
    /// </summary>
    private static void ListenForCancelKey()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                ConsoleKey pressedKey = Console.ReadKey(true).Key;

                // СЦЕНАРИЙ 1: Нажата строго клавиша ESC — штатный плавный выход из игры
                if (pressedKey == ConsoleKey.Escape)
                {
                    Logger.Log("Обнаружено нажатие [ESC]. Запуск остановки всех аккаунтов.", LogType.Warning);
                    StopMultiBotSystem();
                    break;
                }
                // СЦЕНАРИЙ 2: Нажата строго клавиша F10 — экстренный дамп экранов для анализа сбоя перед выходом
                else if (pressedKey == ConsoleKey.F10)
                {
                    Logger.Log("Обнаружено нажатие [F10]. Создание экстренных снимков экрана и запуск остановки.", LogType.Warning);

                    string debugDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugScreenshots"));

                    try
                    {
                        Directory.CreateDirectory(debugDir);

                        // Безопасно итерируемся по копии списка живых аккаунтов
                        foreach (var bot in _activeBots.ToList())
                        {
                            if (bot.Hwnd == IntPtr.Zero) continue;

                            // Захватываем текущую графическую матрицу эмулятора через GDI
                            using OpenCvSharp.Mat? screenshot = Tools.CaptureWindow(bot.Hwnd);

                            if (screenshot?.Empty() is false && screenshot.Width > 0 && screenshot.Height > 0)
                            {
                                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                string fileName = $"{bot.Settings.Name}_F10_Emergency_{timestamp}.png";
                                string fullPath = Path.Combine(debugDir, fileName);

                                // Сохраняем аварийный кадр на диск для дебага логики стендингов или чата
                                OpenCvSharp.Cv2.ImWrite(fullPath, screenshot);
                                Logger.Log($"Снимок экрана для аккаунта '{bot.Settings.Name}' сохранен: {fileName}", LogType.Warning);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Не удалось выполнить экстренное сохранение снимков: {ex.Message}", LogType.Warning);
                    }

                    // После сбора улик вызываем каскадное тушение потоков
                    StopMultiBotSystem();
                    break;
                }
            }

            // Минимальный тайм-аут для разгрузки процессора
            Thread.Sleep(100);
        }
    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    #region ClickTo Extension

    /// <summary>
    /// Метод расширения (Extension Method) для класса <see cref="ActiveBotAccount"/>.
    /// Автоматически распаковывает двумерные координаты (X, Y) из перечисления <see cref="GameUi"/>, 
    /// после чего выполняет аппаратно-независимый клик через утилиту ADB, используя индивидуальный сетевой порт аккаунта [INDEX].
    /// </summary>
    /// <param name="bot">Экземпляр активного аккаунта бота, для которого выполняется действие [INDEX].</param>
    /// <param name="element">Элемент интерфейса игры EVE Echoes с упакованными координатами клика [INDEX].</param>
    /// <param name="minSec">Минимальное время случайной задержки перед кликом (в секундах). По умолчанию: 1.</param>
    /// <param name="maxSec">Максимальное время случайной задержки перед кликом (в секундах). По умолчанию: 3.</param>
    /// <param name="offset">Радиус случайного разброса пикселей от центра клика для защиты от анти-кликеров. По умолчанию: 3.</param>
    internal static void ClickTo(this ActiveBotAccount bot, GameUi element, int minSec = 1, int maxSec = 3, int offset = 3)
    {
        // Распаковываем двумерные координаты X и Y из упакованного Enum GameUi по вашей формуле
        int packed = (int)element;
        int x = packed / 10000;
        int y = packed % 10000;

        // Вызываем обновленный ADB-кликер, передавая порт этого конкретного эмулятора/окна
        Tools.SmartClick(x, y, minSec, maxSec, offset, adbPort: bot.Settings.AdbPort);

    #if DEBUG
        // Выводим информацию о кликах макроса только в режиме отладки (message, type)
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен клик по элементу '{element}' (X={x}, Y={y}).", LogType.Test);
    #endif
    }

    #endregion


}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region MEMO

/*

### КОНТЕКСТ ПРОЕКТА: EVEEchoesBot (Ветка: Work)
**Архитектура:** .NET 9+, C#, Дерево поведения (Behavior Tree) вместо старого FSM.
**Масштаб:** ~4140 строк кода (высокая плотность инфраструктуры).

**Текущие ключевые компоненты:**
1. `ScenarioFactory` (static) — фабрика сборки BT (`BuildLocalWatcherTree()` и `BuildMinerTree()`). Поддерживает фолбек `BuildDefaultFallbackTree()`.
2. `ActiveBotAccount` (partial) — основной класс аккаунта. Хранит свойства стейта: `_inSpace`, `_currenttarget`, `AccountTask CurrentTask` (enum), а также флаги `IsWarping`, `HasTarget`, `AreLasersActive`, `IsInMiningZone`.
3. `AccountStateDto` — объект для синхронизации и сохранения стейта в JSON под `lock (_taskLock)`.
4. `RunLoopAsync` — рабочий цикл с адаптивными тиками (1 сек в состоянии `Running` для быстрой реакции на угрозы, 5 сек в простое).
5. `OcrService` — сервис локального OCR (пакет `TesseractOCR`, параллельный движок `"eng+rus"` из `resources`, чтение через `TesseractOCR.Pix.Image.LoadFromMemory`).

**Текущий статус задач в ветке `Work`:**
- **Сценарий «Глаз» (LocalWatcher):** Дерево настроено, интегрировано переключение `AccountTask.CheckSecurity`, `SendAliChatWarning` и `CheckYourOwnState`.
- **Сценарий «Шахтер» (Miner):** Реализовано дерево по линейному ТЗ (Проверка локала -> Выход -> Выбор белта -> Проверка локала -> Варп -> Добыча/Мониторинг -> Возврат при угрозе/полном трюме -> Выгрузка). Интегрированы изменения `bot.CurrentTask`. Все методы взаимодействия с игрой вынесены в `ActiveBotAccount` в качестве заглушек (stubs). Проект успешно компилируется.
- **Динамическая смена сценариев:** Согласован подход горячей подмены корня `_behaviorTree` через метод `SwitchScenario(string newScenarioName)` для долгосрочной смены ролей бота на лету.

**Статус Канбан-доски (Всего 13 задач):**
- **Test:** 2 задачи (Дерево Miner с заглушками, Интеграция Tesseract OCR).
- **In Progress:** 1 задача.
- **Todo:** 17 задач (+1 новая: Реализация граф-карты вселенной и BFS-автопилота с поддержкой черных списков систем).

*/

#endregion
