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

    public static List<ActiveBotAccount> GetActiveBots() => _activeBots;

    public static CancellationToken GetGlobalToken() => _cts.Token;

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Main

    /// <summary>
    /// Главная точка входа (Entry Point) всего приложения.
    /// Настраивает кодировки ввода-вывода, инициализирует глобальные ловушки критических исключений в ThreadPool/Tasks,
    /// выполняет предстартовую валидацию файлов, разворачивает многопоточную сетку окон и удерживает главный поток приложения 
    /// до получения сигнала отмены через асинхронный перехватчик аппаратных клавиш.
    /// </summary>
    [STAThread] // Обязательный атрибут для корректной работы Windows Forms (иконки в трее)
    public static void Main(string[] args)
    {
        // 1. Настраиваем системную кодировку UTF-8
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            try
            {
                using var killProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/f /im adb.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                killProcess?.WaitForExit(1000); // Даем ОС максимум 1 секунду на тушение демона
            }
            catch { /* Подавляем ошибки при выходе */ }
        };

        // Инициализируем базовые настройки Windows Forms для работы трея
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        // 2. Глобальный перехват необработанных ошибок в фоновых потоках CLR
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            string exceptionMessage = e.ExceptionObject is Exception ex ? ex.ToString() : "Неизвестный сбой среды выполнения.";
            Logger.Log($"КРИТИЧЕСКИЙ СБОЙ СИСТЕМЫ (UnhandledException): {exceptionMessage}", LogType.Error);
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Logger.Log($"КРИТИЧЕСКИЙ СБОЙ ЗАДАЧИ (UnobservedTaskException): {e.Exception?.Message}", LogType.Error);
            e.SetObserved(); 
        };

        // 3. Валидация необходимых графических файлов и шаблонов ДО старта всей системы
        if (!CheckRequiredFiles()) return;

        Logger.Log("Бот успешно запущен в фоновом режиме.", LogType.Warning);

        // 4. Запуск фонового низкоуровневого потока для непрерывного отслеживания управляющей клавиши ESC
        Thread inputThread = new(ListenForCancelKey) { IsBackground = true };
        inputThread.Start();

        // КОРРЕКЦИЯ ДЛЯ ШАГА 2: Настраиваем и запускаем встроенный веб-сервер Kestrel
        // Мы вынесем конфигурацию роутов в отдельный метод ниже, чтобы не захламлять Main
        var webApp = StartWebServer(args);

        // КОРРЕКЦИЯ ДЛЯ ШАГА 1: Создаем иконку в трее вместо консольного окна
        InitSystray();

        // 5. Инициализация многопоточной экосистемы игровых воркеров
        // ВНИМАНИЕ: По требованию №4 воркеры внутри StartMultiBotSystem() теперь 
        // НЕ должны сразу вызывать свой метод .Start(), а просто создаваться в памяти!
        StartMultiBotSystem();

        // 6. КОРРЕКЦИЯ ОЖИДАНИЯ: Вместо блокировки потока запускаем цикл Windows, 
        // который держит приложение живым в трее и обрабатывает клики мыши
        Application.Run();

        // 7. Программа выходит из ожидания после закрытия через трей или ESC.
        // Корректно тушим веб-сервер
        webApp.StopAsync().Wait();

        Thread.Sleep(1000);
        Logger.Log("Бот остановлен. Сессия завершена.", LogType.Warning);
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region StartWebServer

    private static Microsoft.AspNetCore.Builder.WebApplication StartWebServer(string[] args)
    {
        // 1. Динамически вычисляем физический корень проекта на диске
        string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
        
        // Если мы запущены в режиме отладки внутри bin/Debug/..., 
        // поднимаемся на 3 уровня вверх к исходникам проекта
        if (projectRoot.Contains("bin"))
        {
            projectRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", ".."));
        }

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions
        {
            Args = args,
            // Явно привязываем контент-корень к исходной папке проекта
            ContentRootPath = projectRoot
        });

        // Отключаем консольные логгеры .NET для фонового режима WinExe
        builder.Logging.ClearProviders();

        // Настраиваем Kestrel строго на локальный порт 5000
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(5000);
        });

        // Включаем поддержку CORS, чтобы фронтенд мог слать запросы к API
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Регистрируем Менеджер Аккаунтов в DI-контейнер
        builder.Services.AddSingleton<BotAccountManager>();

        var app = builder.Build();

        // Жестко задаем WebRootPath сервера на физическую папку wwwroot в проекте
        app.Environment.WebRootPath = Path.Combine(projectRoot, "wwwroot");

        // Настраиваем глобальные правила (CORS, файлы по умолчанию и статика)
        app.UseCors("AllowAll");
        app.UseDefaultFiles(); // Перенаправляет запрос "/" на "/index.html" автоматически
        app.UseStaticFiles();  // Раздает файлы из переназначенного нами WebRootPath

        // --- МАРШРУТЫ API ДЛЯ УПРАВЛЕНИЯ НАШИМ БОТОМ ---
        var manager = app.Services.GetRequiredService<BotAccountManager>();

        // Маршрут получения состояния (Вызывается каждую секунду из JS)
        app.MapGet("/api/state", () => Microsoft.AspNetCore.Http.Results.Json(new {
            Accounts = manager.GetAccountsState(),
            Logs = Logger.GetLastLogs() // Наш логгер из 13 строк
        }));

        // Маршрут для обработки кликов по кнопкам Управления
        app.MapPost("/api/control/{id:int}/{actionName}", (int id, string actionName) => {
            manager.HandleCommand(id, actionName);
            return Microsoft.AspNetCore.Http.Results.Ok();
        });

        // Маршрут для полной и безопасной остановки всей системы из браузера
        app.MapPost("/api/system/shutdown", () => {
            Logger.Log("Запрошено полное выключение системы через веб-интерфейс...", LogType.Warning);
            
            // 1. Сигнализируем всем фоновым потокам воркеров о необходимости остановиться
            _cts.Cancel(); 
            
            // 2. Закрываем цикл Windows Forms (это вернет управление в конец Main, 
            // где сработает авто-очистка adb.exe и корректно закроется Kestrel)
            System.Windows.Forms.Application.Exit(); 
            
            return Microsoft.AspNetCore.Http.Results.Ok();
        });

        // Запуск веб-сервера на фоне (Task.Run) для полной совместимости с Application.Run в Main
        Task.Run(async () => {
            try
            {
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Logger.Log($"Критическая ошибка веб-сервера Kestrel: {ex.Message}", LogType.Error);
            }
        });

        // Автоматически открываем веб-интерфейс в браузере по умолчанию
        string url = "http://localhost:5000";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log($"Не удалось автоматически открыть браузер: {ex.Message}", LogType.Error);
        }

        return app;
    }




#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region InitSystray

    private static void InitSystray()
    {
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        
        // Кнопка быстрого перехода в панель
        contextMenu.Items.Add("Открыть веб-панель", null, (s, e) => {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("http://localhost:5000") { UseShellExecute = true }); } catch { }
        });
        
        contextMenu.Items.Add("-"); // Разделитель
        
        // Кнопка полного выхода
        contextMenu.Items.Add("Выход из бота", null, (s, e) => {
            Logger.Log("Запрошен выход из приложения через системный трей...", LogType.Warning);
            
            // Активируем токен отмены для каскадного тушения всех воркеров
            _cts.Cancel(); 
            
            // Закрываем цикл обработки сообщений Windows Forms, возвращая управление в конец Main
            System.Windows.Forms.Application.Exit(); 
        });

        var notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            // Берем иконку, которую вы вшили в .csproj
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Text = $"EVE Echoes Bot v.0.01.002",
            Visible = true
        };

        // Защищаем иконку от сборщика мусора, привязывая её к домену приложения
        AppDomain.CurrentDomain.ProcessExit += (s, e) => notifyIcon.Visible = false;
    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Start MultiBot System

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

                    // Коннектим эмулятор по порту
                    Process.Start(new ProcessStartInfo(adbPath, $"connect {targetDevice}") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();

                    // Включаем встроенную системную сетку Android для визуального контроля кликов бота
                    Process.Start(new ProcessStartInfo(adbPath, $"-s {targetDevice} shell settings put system pointer_location 1") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                }

                // Создаем объект аккаунта (внутри его конструктора автоматически восстанавливается статистика и очередь задач)
                var bot = new ActiveBotAccount(accountSettings)
                {
                    Hwnd = hWnd
                };

                // КОРРЕКЦИЯ: Убираем Console.ReadLine(), так как оконный режим WinExe не имеет консоли.
                // Если данные не десериализовались, выставляем дефолт "Требуется ввод" — оператор заполнит это в веб-интерфейсе.
                if (string.IsNullOrEmpty(bot._eveSystem) || bot._eveSystem == "???")
                {
                    bot._eveSystem = "Требуется ввод";
                }
                if (string.IsNullOrEmpty(bot._eveShip) || bot._eveShip == "???")
                {
                    bot._eveShip = "Требуется ввод";
                }

                // КОРРЕКЦИЯ: bot.Start(_cts.Token) здесь БОЛЬШЕ НЕ ВЫЗЫВАЕТСЯ.
                // Бот просто добавляется в список инициализированных. Он ждет клика "Старт" на веб-странице.
                _activeBots.Add(bot);
            }

            if (_activeBots.Count == 0)
            {
                var testSettings = new AccSettings 
                { 
                    Name = "Тестовый Шахтер (Эмулятор выкл)", 
                    WindowTitle = "Симуляция" 
                };
                var testBot = new ActiveBotAccount(testSettings)
                {
                    _eveSystem = "Jita",
                    _eveShip = "Covetor II",
                    _inSpace = true,
                    _currenttarget = "Астероидный пояс #1"
                };
                _activeBots.Add(testBot);
            }

            Logger.Log($"Мультисистема инициализирована. Аккаунтов загружено: {_activeBots.Count}. Ожидание команды Старт из веб-панели.", LogType.Info);
        }
        catch (Exception ex)
        {
            Logger.Log($"Критический сбой при запуске мультисистемы: {ex.Message}", LogType.Error);
            _cts.Cancel(); 
        }
    }


    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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
            // КОРРЕКЦИЯ ДЛЯ WinExe: Если консоль отсутствует или ввод перенаправлен,
            // мы не опрашиваем клавиши, а просто держим поток живым до отмены через _cts
            if (Console.IsInputRedirected)
            {
                Thread.Sleep(500); // Увеличиваем задержку в фоне для экономии процессора
                continue;
            }

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
                // СЦЕНАРИЙ 2: Нажата строго клавиша F10 — экстренный дамп экранов
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
                                Logger.Log($"Снимок экрана для аккаунта '{bot.Settings.Name}' сохранен: {fileName}", LogType.Warning);
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

            // Минимальный тайм-аут для разгрузки процессора
            Thread.Sleep(100);
        }
    }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region MEMO

/*

### КОНТЕКСТ ПРОЕКТА: EVEEchoesBot (Ветка: Work)
**Архитектура:** .NET 9+, C#, Дерево поведения (Behavior Tree) вместо старого FSM.
**Масштаб:** ~4140 строк кода (высокая плотность инфраструктуры).

**Текущие ключевые компоненты:**
1. `ScenarioFactory` (static) — фабрика сборки BT (`BuildLocalWatcherTree()` и `BuildMinerTree()`). Поддерживает фолбек `BuildDefaultFallbackTree()`.
2. `ActiveBotAccount` (partial) — основной класс аккаунта. Хранит свойства стейта: `_inSpace`, `_currenttarget`, `AccountTask CurrentTask` (enum), а также флаги `_iswarping`, `_hastarget`, `_weaponryactive`, `IsInMiningZone`.
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
