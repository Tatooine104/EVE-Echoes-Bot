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
// [v] TODO 2026.06.01 Реализовать дерево поведения 
// [v] TODO 2026.06.01 Навести порядок в файлах и красиво оформить код 

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
    /// Объект блокировки для потокобезопасного доступа к списку активных ботов.
    /// </summary>
    public static readonly System.Threading.Lock ActiveBotsLock = new(); // <-- ДОБАВИТЬ ЭТУ СТРОКУ


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

    // TODO: Выяснить что это?
    public record ControlPropertyValueDto(string Value);

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
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5000));

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

        // Разрешаем Kestrel раздавать физическую папку DebugScreenshots по виртуальному пути /DebugScreenshots
        string externalDebugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
        Directory.CreateDirectory(externalDebugDir); // Защита от падения, если папки еще нет

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(externalDebugDir),
            RequestPath = "/DebugScreenshots"
        });

        // Маршрут получения состояния (Простой и надежный)
        app.MapGet("/api/state", () => Microsoft.AspNetCore.Http.Results.Json(new {
            Accounts = manager.GetAccountsState(),
            Logs = Logger.GetLastLogs()
        }));

        // Маршрут для обработки кликов по кнопкам Управления
        app.MapPost("/api/control/{id:int}/{actionName}", async (int id, string actionName, ControlPropertyValueDto? dto) => {
            // 1. Обработка ручной установки системы (только если dto пришел)
            if (dto != null && actionName.Equals("setSystem", StringComparison.OrdinalIgnoreCase))
            {
                bool success = manager.SetAccountSystem(id, dto.Value);
                return Microsoft.AspNetCore.Http.Results.Ok();
            }

            // 2. Обработка ручной установки корабля (только если dto пришел)
            if (dto != null && actionName.Equals("setShip", StringComparison.OrdinalIgnoreCase))
            {
                bool success = manager.SetAccountShip(id, dto.Value);
                return Microsoft.AspNetCore.Http.Results.Ok();
            }

            // 3. Фолбек для всех остальных ваших команд (start, stop и т.д.) - теперь они не упадут с 400 ошибкой
            manager.HandleCommand(id, actionName);
            return Microsoft.AspNetCore.Http.Results.Ok();
        });

        // POST /api/debug/{id:int}/screenshot — Сделать живой снимок экрана эмулятора
        app.MapPost("/api/debug/{id:int}/screenshot", async (int id, BotAccountManager manager) =>
        {
            var bot = manager.GetAccountById(id);
            if (bot == null) return Results.NotFound(new { message = "Аккаунт не найден" });
            if (bot.Hwnd == IntPtr.Zero) return Results.BadRequest(new { message = "Окно эмулятора не привязано" });

            try
            {
                using OpenCvSharp.Mat? screenshot = Tools.CaptureWindow(bot.Hwnd);
                if (screenshot?.Empty() is not false) return Results.BadRequest(new { message = "Не удалось захватить кадр" });

                // Путь строго в физическую папку DebugScreenshots на уровне шаблонов
                string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
                Directory.CreateDirectory(debugDir);

                // Имя строго по вашему стандарту: debug_screenshot_<аккаунт>.png
                string fileName = $"debug_screenshot_{bot.Settings.Name}.png";
                string fullPath = Path.Combine(debugDir, fileName);

                OpenCvSharp.Cv2.ImWrite(fullPath, screenshot);

                // Отдаем виртуальный путь для браузера
                return Results.Ok(new { url = $"/DebugScreenshots/{fileName}?t={DateTime.Now.Ticks}" });
            }
            catch (Exception ex) { return Results.Problem($"Ошибка: {ex.Message}"); }
        });

        // GET /api/debug/enums/regions — Получить список всех регионов OpenCV
        app.MapGet("/api/debug/enums/regions", () => 
        {
            // Извлекаем имена из вашего Enum GameRegions
            string[] regions = Enum.GetNames<GameRegions>();
            return Results.Ok(regions);
        });

        // GET /api/debug/enums/ui — Получить список всех элементов кликов GameUI
        app.MapGet("/api/debug/enums/ui", () => 
        {
            // Извлекаем имена из вашего Enum GameUI
            string[] uiElements = Enum.GetNames<GameUI>();
            return Results.Ok(uiElements);
        });


        // GET /api/debug/{id:int}/region/{regionName} — Вырезать и посмотреть конкретный регион экрана
        app.MapGet("/api/debug/{id:int}/region/{regionName}", async (int id, string regionName, BotAccountManager manager) =>
        {
            var bot = manager.GetAccountById(id);
            if (bot == null) return Results.NotFound(new { message = "Аккаунт не найден" });
            if (bot.Hwnd == IntPtr.Zero) return Results.BadRequest(new { message = "Окно эмулятора не привязано" });

            if (!Enum.TryParse(regionName, true, out GameRegions targetRegion))
                return Results.BadRequest(new { message = $"Регион '{regionName}' не найден" });

            try
            {
                using OpenCvSharp.Mat? screenshot = Tools.CaptureWindow(bot.Hwnd);
                if (screenshot?.Empty() is not false) return Results.BadRequest(new { message = "Не удалось захватить кадр" });

                OpenCvSharp.Rect rect = targetRegion.GetOpenCvRect();
                OpenCvSharp.Rect safeRect = Tools.ClampRegion(rect, screenshot.Width, screenshot.Height);
                if (safeRect.Width <= 0 || safeRect.Height <= 0) return Results.BadRequest(new { message = "Регион за границами" });

                using OpenCvSharp.Mat cropped = new(screenshot, safeRect);

                string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
                Directory.CreateDirectory(debugDir);

                // Имя строго по вашему стандарту: debug_screenshot_<имя региона>.png
                string fileName = $"debug_screenshot_{regionName}.png";
                string fullPath = Path.Combine(debugDir, fileName);

                OpenCvSharp.Cv2.ImWrite(fullPath, cropped);

                return Results.Ok(new { url = $"/DebugScreenshots/{fileName}?t={DateTime.Now.Ticks}" });
            }
            catch (Exception ex) { return Results.Problem($"Ошибка: {ex.Message}"); }
        });

        // POST /api/debug/{id:int}/click/{elementName} — Отправить клик по выбранному элементу GameUI
        app.MapPost("/api/debug/{id:int}/click/{elementName}", async (int id, string elementName, BotAccountManager manager) =>
        {
            var bot = manager.GetAccountById(id);
            if (bot == null) return Results.NotFound(new { message = "Аккаунт не найден" });

            // Пытаемся безопасно распарсить строку в ваш Enum GameUI
            if (!Enum.TryParse(elementName, true, out GameUI targetElement))
            {
                return Results.BadRequest(new { message = $"Элемент '{elementName}' не найден в перечислении GameUI" });
            }

            try
            {
                Logger.Log($"[Web Debug] Ручной вызов клика по элементу '{targetElement}' для бота {bot.Settings.Name}...", LogType.Warning);

                // Используем наш асинхронный метод расширения по стандарту проекта
                // Так как это веб-дебаг, передаем CancellationToken.None или глобальный токен
                await bot.ClickToAsync(targetElement);

                return Results.Ok(new { message = $"Клик по элементу '{targetElement}' успешно отправлен" });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Ошибка отправки клика: {ex.Message}");
            }
        });

        // Маршрут для полной и безопасной остановки всей системы из браузера
        app.MapPost("/api/system/shutdown", () => {
            Logger.Log("Запрошено полное выключение системы через веб-интерфейс...", LogType.Warning);

            _cts.Cancel();

            // 1. Проверяем, есть ли вообще открытые окна
            if (System.Windows.Forms.Application.OpenForms.Count > 0)
            {
                // 2. ФИКС: Берем ПЕРВОЕ конкретное окно (индекс 0) из коллекции
                var mainForm = System.Windows.Forms.Application.OpenForms[0];

                // 3. Безопасно проверяем, что форма существует и не уничтожена
                if (mainForm?.IsDisposed is false)
                {
                    // 4. Вызываем маршалинг потока у конкретного окна через стрелочную лямбду
                    mainForm.BeginInvoke(() => System.Windows.Forms.Application.Exit());
                }
            }
            else
            {
                // Если окон нет (работаем в консольном режиме/сервисе), тушим напрямую
                System.Windows.Forms.Application.Exit();
            }

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
        const string url = "http://localhost:5000";
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

        // [ ] TODO 2026.06.03 Сделать тут ссылку на номер версии из параметров проекта (как в логере) 
        var notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            // Берем иконку, которую вы вшили в .csproj
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Text = "EVE Echoes Bot v.0.01.002",
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

                // Если данные не десериализовались, выставляем дефолт "Требуется ввод"
                if (string.IsNullOrEmpty(bot._eveSystem) || bot._eveSystem == "???")
                {
                    bot._eveSystem = "Требуется ввод";
                }
                if (string.IsNullOrEmpty(bot._eveShip) || bot._eveShip == "???")
                {
                    bot._eveShip = "Требуется ввод";
                }

                // ИСПРАВЛЕНО: Синхронизируем внутренние флаги стейта бота с его личным JSON-конфигом
                bot.PlanetMining = accountSettings.PlanetMining;
                bot.POS = accountSettings.POS;

                // Бот просто добавляется в список инициализированных. Он ждет клика "Старт" на веб-странице.
                _activeBots.Add(bot);
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
    /// Автоматически распаковывает двумерные координаты (X, Y) из перечисления <see cref="GameUI"/>,
    /// после чего выполняет асинхронный аппаратно-независимый клик через утилиту ADB [INDEX].
    /// </summary>
    internal static async Task ClickToAsync(this ActiveBotAccount bot, GameUI element, int minSec = 1, int maxSec = 3, int offset = 3)
    {
        // Распаковываем двумерные координаты X и Y из упакованного Enum GameUI по вашей формуле
        int packed = (int)element;
        int x = packed / 10000;
        int y = packed % 10000;

        // Вызываем обновленный ADB-кликер (если SmartClick поддерживает async, используем await, 
        // либо оборачиваем в Task.Run, чтобы не блокировать UI поток WinForms)
        await Task.Run(() => Tools.SmartClick(x, y, minSec, maxSec, offset, adbPort: bot.Settings.AdbPort));

    #if DEBUG
        // Выводим информацию о кликах макроса только в режиме отладки (message, type)
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен клик по элементу '{element}' (X={x}, Y={y}).", LogType.Test);
    #endif
    }

    /// <summary>
    /// Метод расширения (Extension Method) для класса <see cref="ActiveBotAccount"/>.
    /// Выполняет асинхронный аппаратно-независимый клик по динамическим координатам <see cref="OpenCvSharp.Point"/>,
    /// полученным из OpenCV, уводя тяжелый процесс ADB в фоновый поток.
    /// </summary>
    internal static Task ClickPointAsync(this ActiveBotAccount bot, OpenCvSharp.Point point, CancellationToken token, int minSec = 1, int maxSec = 2, int offset = 2)
    {
        // Оптимизировано: убрали async/await и возвращаем Task напрямую
        return Task.Run(() => Tools.SmartClick(
            point.X,
            point.Y,
            minSec,
            maxSec,
            offset,
            adbPort: bot.Settings.AdbPort
        ), token);
    }


    #endregion


}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

