using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;
using EVEEchoesBot.resources;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace EVEEchoesBot;

// Используем partial, чтобы архитектурно разделять инициализацию, 
// веб-сервер Kestrel и логику фонового движка по разным файлам.
static partial class Program
{
    // Сюда мы сейчас перенесём наши потокобезопасные поля коллекции ботов

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region Constants & Fields

    // Выносим иконку на уровень класса, защищая её от проходов Garbage Collector
    private static System.Windows.Forms.NotifyIcon? _notifyIcon;

    // Глобальный семафор для синхронизации графических вызовов WinAPI (GDI/BitBlt).
    // Гарантирует, что боты делают скриншоты строго по очереди (1 за раз), предотвращая дедлоки в видеокарте.
    // public static readonly System.Threading.SemaphoreSlim GdiSemaphore = new(1, 1);


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
    private static readonly List<ActiveBotAccount> _activeBots = [];

    /// <summary>
    /// Объект блокировки для потокобезопасного доступа к списку активных ботов.
    /// </summary>
    public static readonly System.Threading.Lock ActiveBotsLock = new();

    /// <summary>
    /// Ссылка на объект глобальной конфигурации приложения, содержащий параметры всех аккаунтов.
    /// </summary>
    private static BotConfig? _config;

    // Оптимизировано: использовали автосвойство с инициализатором, указывающим на папку с картинками
    public static string TemplatesDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "images");

    // /// <summary>
    // /// Глобальное свойство, возвращающее актуальный путь к папке с графическими шаблонами (Images).
    // /// Теперь работает мгновенно из оперативной памяти благодаря кэшированию.
    // /// </summary>
    // public static string TemplatesDir => _cachedTemplatesDir;

    /// <summary>
    /// ПОТОКОБЕЗОПАСНЫЙ метод получения списка ботов. Используется веб-контроллерами API и логикой паники.
    /// Гарантирует отсутствие InvalidOperationException при конкурентном чтении/записи.
    /// </summary>
    public static IReadOnlyList<ActiveBotAccount> GetActiveBots()
    {
        lock (ActiveBotsLock)
        {
            // Возвращаем изолированную копию (снимок) списка в виде ReadOnly коллекции
            return [.. _activeBots];
        }
    }

    /// <summary>
    /// Потокобезопасное получение глобального токена отмены.
    /// </summary>
    public static CancellationToken GetGlobalToken()
    {
        lock (ActiveBotsLock)
        {
            return _cts.Token;
        }
    }

    /// <summary>
    /// Метод для безопасного сброса/пересоздания токена отмены (вызывается при перезапуске системы)
    /// </summary>
    public static void ResetGlobalToken()
    {
        lock (ActiveBotsLock)
        {
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new();
            }
        }
    }

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

        // Глобальная ссылка на веб-приложение для возможности его остановки из любой точки
        WebApplication? webApp = null;

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            try
            {
                // BUG HIGH - Потенциальный Deadlock. Вызов асинхронного метода .StopAsync() через синхронную заглушку .GetAwaiter().GetResult() внутри обработчика ProcessExit часто приводит к зависанию потока. Kestrel пытается завершить запросы и освободить ресурсы, требуя контекста пула потоков, который в этот момент уже может уничтожаться или блокироваться этим GetResult(). Безопаснее использовать метод Stop() или Dispose() напрямую, либо вызывать StopAsync() без блокировки основного потока.
                webApp?.StopAsync().GetAwaiter().GetResult();

                // Принудительно гасим ADB-демон
                using var killProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/f /im adb.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                killProcess?.WaitForExit(3000); // Даем ОС до 3 секунд на гарантированное уничтожение ADB
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
            e.SetObserved(); // Помечаем ошибку как обработанную, чтобы не уронить процесс
        };

        // 3. Валидация необходимых графических файлов и шаблонов ДО старта всей системы
        if (!CheckRequiredFiles()) return;
        Logger.Log("Бот успешно запущен в фоновом режиме.", LogType.Warning);

        // 4. Запуск фоновой асинхронной задачи мониторинга управляющих клавиш ESC / F10
        // BUG MEDIUM - Антипаттерн "async/await over Task.Run". Использование конструкций `async () => await ...` внутри `Task.Run` порождает лишний стейт-машинный оверхед. Правильно писать напрямую: `Task.Run(() => Program.ListenForCancelKeyAsync());`
        _ = Task.Run(async () => await Program.ListenForCancelKeyAsync());

        // 5. Настраиваем и запускаем встроенный веб-сервер Kestrel (Неблокирующий запуск)
        // BUG HIGH - Главный подозреваемый! Зависит от того, как устроен `StartWebServer`. Если внутри него вызывается блокирующий `app.Run()` вместо асинхронного `app.RunAsync()`, то до инициализации иконки трея (шаг 6), запуска ботов (шаг 7) и `Application.Run()` (шаг 8) выполнение кода ПРОСТО НЕ ДОЙДЕТ. Поток Main зависнет внутри веб-сервера. Если же внутри `StartWebServer` используется `Task.Run(...)`, то ок, но этот метод должен возвращать объект WebApplication до фактического старта прослушивания порта, чтобы Main шел дальше.
        webApp = StartWebServer(args);


        // 6. Создаем иконку в трее вместо консольного окна
        InitSystray();

        // 7. Инициализация многопоточной экосистемы игровых воркеров
        // Воркеры просто создаются в памяти под ActiveBotsLock и ждут команду Старт
        StartMultiBotSystem();

        // 8. Запускаем цикл Windows Forms, который держит приложение живым в трее и обрабатывает клики мыши
        System.Windows.Forms.Application.Run();

        // BUG LOW - Этот код выполнится только ПОСЛЕ закрытия приложения (когда отработает Application.Run). Сон в 500мс здесь бесполезен и просто затягивает закрытие процесса.
        Thread.Sleep(500);
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

        // Исправлено: заставляем Kestrel передавать JSON-поля ровно в том регистре, в каком они написаны в C# DTO
        // Исправлено: избавились от фигурных скобок с помощью стрелочной лямбды C# 13
        builder.Services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);

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

        // ИСПРАВЛЕНО: принудительно заставляем Minimal APIs (app.MapGet) сохранять исходный C# регистр букв во всех JSON-ответах
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = null);

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
        // Исправлено: убрали Results.Json, теперь объект сериализуется через наш глобальный JSON-контекст с сохранением регистра
        app.MapGet("/api/state", () => new {
            Accounts = manager.GetAccountsState(),
            Logs = Logger.GetLastLogs()
        });

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
                // BUG HIGH - Потенциальная точка зависания (Deadlock/Race condition). Метод Tools.CaptureWindow(bot.Hwnd) вызывается асинхронно из пула потоков Kestrel. Если внутри CaptureWindow() или самого эмулятора происходит обращение к WinAPI (GetDC, BitBlt), которое требует синхронизации с UI-потоком, или если там закомментирован/отсутствует семафор GdiSemaphore, этот вызов может намертво заблокировать поток или графический контекст Windows, прервав выполнение параллельно работающего цикла бота. 
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
                // BUG HIGH - Повторяющийся критический риск дедлока/зависания. Метод `Tools.CaptureWindow(bot.Hwnd)` вызывается из асинхронного потока Kestrel параллельно с основным игровым циклом бота. Если `Tools.CaptureWindow` делает WinAPI вызовы (вроде GetDC, GetWindowDC, BitBlt) к окну эмулятора без синхронизации (отсутствует GdiSemaphore), это может намертво заблокировать графический конвейер ОС или поток самого бота, когда тот пытается параллельно захватить экран для анализа UI.
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
        app.MapPost("/api/debug/{id:int}/click/{elementName}", async (
            int id,
            string elementName,
            BotAccountManager manager,
            CancellationToken token) => // Кэстрел автоматически передаст сюда токен отмены HTTP-запроса
        {
            var bot = manager.GetAccountById(id);
            if (bot == null) return Results.NotFound(new { message = "Аккаунт не найден" });

            // Пытаемся безопасно распарсить строку в ваш Enum GameUI (C# 12+ target-typed switch / try-parse)
            if (!Enum.TryParse(elementName, true, out GameUI targetElement))
            {
                return Results.BadRequest(new { message = $"Элемент '{elementName}' не найден в перечислении GameUI" });
            }

            try
            {
                Logger.Log($"[Web Debug] Ручной вызов клика по элементу '{targetElement}' для бота {bot.Settings.Name}...", LogType.Warning);

                // Передаем токен веб-запроса. Теперь, если пользователь прервет запрос на сайте, 
                // задача в пуле потоков не будет выполняться вхолостую!
                // BUG HIGH - Потенциальная уязвимость для дедлока или бесконечного ожидания. Вызов `await bot.ClickToAsync(...)` уводит поток HTTP-запроса Kestrel в ожидание выполнения клика через ADB. Если внутри `ClickToAsync` используется синхронизация ресурсов (например, один и тот же `lock` или семафор с игровым циклом `RunLoopAsync`), то в случае, когда бот УЖЕ выполняет действие, этот веб-запрос зависнет. Более того, если внутри `ClickToAsync` нет встроенного таймаута на выполнение ADB-команды, а ADB зависнет (что бывает часто), этот `await` никогда не вернет управление.
                await bot.ClickToAsync(targetElement, token);

                return Results.Ok(new { message = $"Клик по элементу '{targetElement}' успешно отправлен" });
            }
            catch (OperationCanceledException)
            {
                // Мягко обрабатываем отмену запроса, чтобы не засорять логи сервера "критическими ошибками"
                return Results.StatusCode(499); // Client Closed Request
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
        // BUG HIGH - Потенциальная гонка при инициализации (Race Condition). Метод StartWebServer возвращает объект `app` в Main() *до того*, как Kestrel реально поднимется и начнет слушать порт, так как `app.RunAsync()` убран в фоновый `Task.Run`. Однако в `Main` сразу после вызова `StartWebServer` идет метод `StartMultiBotSystem()`, который может начать дергать зависимости API или слать HTTP/вебсокет-запросы. Если запуск затянется, боты упадут или зависнут при попытке связаться с недоступным сервером на старте. Сам `Task.Run(async () => await ...)` здесь написан правильно, но возвращать `app` без ожидания готовности хоста — это риск.
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
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("http://localhost:5000") { UseShellExecute = true });
            }
            catch { /* Подавляем ошибки, если в ОС нет дефолтного браузера */ }
        });

        contextMenu.Items.Add("-"); // Разделитель

        // Кнопка полного выхода
        contextMenu.Items.Add("Выход из бота", null, (s, e) => {
            Logger.Log("Запрошен выход из приложения через системный трей...", LogType.Warning);

            // Потокобезопасно активируем токен отмены для всех воркеров
            lock (ActiveBotsLock)
            {
                _cts.Cancel();
            }

            // Закрываем цикл обработки сообщений Windows Forms
            System.Windows.Forms.Application.Exit();
        });

        // ОПТИМИЗАЦИЯ: Автоматически вытягиваем версию из параметров проекта (Assembly Version)
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        string? version = assembly.GetName().Version?.ToString(3) ?? "0.0.1";

        // Присваиваем объект статическому полю класса, гарантируя его выживание в памяти 24/7
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(assembly.Location) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Text = $"EVE Echoes Bot v.{version}",
            Visible = true
        };

        // При выходе гарантированно убираем иконку, чтобы она не "залипала" в панели Windows
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        };
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

            // Исправлено: безопасное чтение, не вызывающее краша при запуске из-под веб-сервера Kestrel
            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("Нажмите Enter для выхода...");
                Console.Read();
            }
            else
            {
                // BUG MEDIUM - Блокировка UI-потока в Main. Метод `CheckRequiredFiles` вызывается синхронно в `Main` до запуска цикла Windows Forms. Если файлы отсутствуют и ввод перенаправлен, вызов `Thread.Sleep(5000)` заморозит стартовый поток. Хотя здесь это приводит к `Environment.Exit(1)`, в многопоточных архитектурах синхронные `Thread.Sleep` на главном потоке считаются плохой практикой. На зависание *уже работающих* сценариев этот кусок кода не влияет, так как при успешном старте метод просто возвращает `true`.
                Thread.Sleep(5000); // Даем время оператору прочитать консоль Docker/панели
            }

            // Жестко тушим приложение, не давая рантайму упасть дальше с UnhandledException
            Environment.Exit(1);
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
                // BUG HIGH - Потенциальная блокировка запуска и зависание. Вызовы `Process.Start(...).WaitForExit()` без передачи таймаута могут заблокировать выполнение приложения намертво, если процесс `adb.exe` зависнет или потребует прав администратора, которых нет у бота. Для консольной утилиты `adb` на старте критично выставлять лимит времени, например: `WaitForExit(5000)`.
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
                    // BUG HIGH - Потенциальное зависание на старте. Если эмулятор завис, порт занят или ADB ушел в бесконечный цикл ожидания сетевого сокета, `WaitForExit()` без таймаута заблокирует инициализацию всей мультисистемы ботов. Необходимо добавить ограничение по времени: `WaitForExit(3000)`.
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
                // BUG MEDIUM - Состояние гонки при добавлении в список. Добавление в `_activeBots` происходит без захвата `lock (ActiveBotsLock)`. Несмотря на то, что сейчас это выполняется в один поток в методе `Main`, архитектурно поле объявлено как защищаемое этим локом. Для предотвращения непредвиденных race conditions в будущем, операции со списком должны быть обернуты в блокировку.
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
    /// Безопасная остановка мульти-бот системы.
    /// Теперь возвращает Task вместо опасного void и защищен локами.
    /// </summary>
    private static async Task StopMultiBotSystemAsync()
    {
        // Защитный барьер: атомарно проверяем и выставляем флаг остановки
        if (System.Threading.Interlocked.CompareExchange(ref _isStopping, 1, 0) == 1)
        {
            return;
        }

        try
        {
            // 1. Потокобезопасно отправляем сигнал отмены всем воркерам
            lock (ActiveBotsLock)
            {
                _cts.Cancel();
            }

            Logger.Log("Всем фоновым потокам отправлен сигнал остановки. Ожидание завершения...", LogType.Warning);

            // 2. Даем потокам 2 секунды на корректное завершение тактов и вызов SaveStats() в finally
            // BUG MEDIUM - Метод "Fire and Forget" без реального отслеживания задач. Фиксированное ожидание в `Task.Delay(2000)` не гарантирует, что потоки воркеров завершились. Если воркер завис намертво внутри ADB-команды или бесконечного цикла, этот метод продолжит выполнение, очистит список, но скрытые потоки останутся висеть в памяти. Правильнее сохранять ссылки на запущенные `Task` игровых циклов и делать `await Task.WhenAll(botTasks)`.
            await Task.Delay(2000);

            // 3. Гарантированно и потокобезопасно очищаем список активных ботов
            lock (ActiveBotsLock)
            {
                _activeBots.Clear();
            }

            Logger.Log("Список активных аккаунтов очищен. Система полностью остановлена.", LogType.Warning);
        }
        finally
        {
            // СБРОС ФЛАГА: Возвращаем систему в рабочее состояние, чтобы её можно было запустить снова!
            System.Threading.Interlocked.Exchange(ref _isStopping, 0);
        }
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
    /// <summary>
    /// Асинхронный мониторинг управляющих клавиш. Полностью защищен от зависаний в режиме Windows Forms (Трей).
    /// </summary>
    private static async Task ListenForCancelKeyAsync()
    {
        CancellationToken globalToken = Program.GetGlobalToken();

        Logger.Log("[SYSTEM] Поток мониторинга клавиш управления ESC/F10 успешно запущен.", LogType.Info);

        while (!globalToken.IsCancellationRequested)
        {
            try
            {
                // ИСПРАВЛЕНО: Жесткий и безопасный фильтр для Windows Forms / GUI режима.
                // Если у приложения нет реального консольного окна (работаем из-под трея или Kestrel),
                // Console.KeyAvailable гарантированно вызовет Hard Deadlock ОС. 
                // Проверяем наличие окна консоли через WinAPI. Если его нет — поток просто спит, не нагружая процессор.
                IntPtr consoleWindowHandle = WinAPI.GetConsoleWindow(); // Подключите этот импорт из вашего класса WinAPI

                if (consoleWindowHandle == IntPtr.Zero || Console.IsInputRedirected)
                {
                    // Мы в режиме трея/WinExe. Консоли нет. Безопасно спим и уходим на следующий тик.
                    await Task.Delay(1000, globalToken).ConfigureAwait(false);
                    continue;
                }

                // Код опроса консоли выполнится ТОЛЬКО если приложение запущено как честная Console Application
                if (Console.KeyAvailable)
                {
                    ConsoleKey pressedKey = Console.ReadKey(true).Key;

                    if (pressedKey == ConsoleKey.Escape)
                    {
                        Logger.Log("Обнаружено нажатие [ESC]. Запуск остановки всех аккаунтов.", LogType.Warning);
                        _ = Program.StopMultiBotSystemAsync();
                        break; 
                    }

                    if (pressedKey == ConsoleKey.F10)
                    {
                        Logger.Log("Обнаружено нажатие [F10]. Создание экстренных снимков экрана и запуск остановки.", LogType.Warning);
                        string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugScreenshots");

                        try
                        {
                            Directory.CreateDirectory(debugDir);
                            var currentBots = Program.GetActiveBots();

                            foreach (var bot in currentBots)
                            {
                                if (bot.Hwnd == IntPtr.Zero) continue;

                                Mat? screenshot = null;

                                // ИСПРАВЛЕНО: Используем экземплярный семафор конкретного бота вместо заклинивающего глобального!
                                // BUG HIGH - Скрытая уязвимость для дедлока при экстренной отладке. Если бот УЖЕ завис внутри выполнения какого-то действия (например, внутри `ClickToAsync`), и этот метод удерживает `bot.AccountGdiSemaphore`, то при нажатии F10 данный цикл мониторинга застрянет на `await bot.AccountGdiSemaphore.WaitAsync`. Так как вызов идет внутри синхронного цикла `foreach`, зависание одного бота заблокирует опрос и выключение для ВСЕХ остальных ботов в системе. Для исправления этой проблемы захват семафора и создание скриншота должны быть вынесены в `Task.Run()` или выполняться с таймаутом.
                                await bot.AccountGdiSemaphore.WaitAsync(globalToken).ConfigureAwait(false);
                                try
                                {
                                    screenshot = Tools.CaptureWindow(bot.Hwnd);
                                }
                                finally
                                {
                                    bot.AccountGdiSemaphore.Release();
                                }

                                if (screenshot is { } snap && !snap.Empty() && snap.Width > 0 && snap.Height > 0)
                                {
                                    using var pin = snap; 
                                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                    string fileName = $"{bot.Settings.Name}_F10_Emergency_{timestamp}.png";
                                    string fullPath = Path.Combine(debugDir, fileName);

                                    Cv2.ImWrite(fullPath, pin);
                                    Logger.Log($"Снимок экрана для аккаунта '{bot.Settings.Name}' сохранен: {fileName}", LogType.Warning);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Не удалось выполнить экстренное сохранение снимков: {ex.Message}", LogType.Warning);
                        }

                        _ = Program.StopMultiBotSystemAsync();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break; 
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[KEYBOARD DEBUG] Ошибка опроса клавиш: {ex.Message}");
#endif
            }

            // Высокоэффективная асинхронная задержка с отключением контекста синхронизации Windows Forms
            await Task.Delay(250, globalToken).ConfigureAwait(false);
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
internal static async Task ClickToAsync(
        this ActiveBotAccount bot,
        GameUI element,
        CancellationToken token,
        int minSec = 1,
        int maxSec = 3,
        int offset = 3)
    {

        int packed = (int)element;
        int x = packed / 10000;
        int y = packed % 10000;

        token.ThrowIfCancellationRequested();

        // BUG HIGH - Главный подозреваемый! Метод запускает `Tools.SmartClick` в пуле потоков через `Task.Run`. Внутри `Tools.SmartClick` гарантированно вызывается `adb.exe shell input tap ...`. Если внутри `Tools.SmartClick` вызов внешнего процесса ADB написан синхронно (например, через `Process.Start().WaitForExit()`), и этот вызов ЗАВИСАЕТ из-за буферизации потоков вывода (stdout/stderr) или сетевого сбоя эмулятора, то этот `await` зависнет НАМЕРТВО. Из-за отсутствия таймаута внутри `Task.Run` токен `token` отменит задачу только *до* её старта, но не сможет прервать уже заклинивший внутри `SmartClick` синхронный процесс. Сценарий сделает первое действие (первый клик) и застынет навсегда.
        await Task.Run(() => Tools.SmartClick(x, y, minSec, maxSec, offset, adbPort: bot.Settings.AdbPort), token);

    #if DEBUG
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен клик по элементу '{element}' (X={x}, Y={y}).", LogType.Test);
    #endif

    }


    /// <summary>
    /// Метод расширения (Extension Method) для класса <see cref="ActiveBotAccount"/>.
    /// Выполняет асинхронный аппаратно-независимый клик по динамическим координатам <see cref="OpenCvSharp.Point"/>,
    /// полученным из OpenCV, уводя тяжелый процесс ADB в фоновый поток.
    /// </summary>
    internal static Task ClickPointAsync(
        this ActiveBotAccount bot,
        Point point, // Упростили написание типа благодаря using OpenCvSharp
        CancellationToken token,
        int minSec = 1,
        int maxSec = 2,
        int offset = 2)
    {
        // МГНОВЕННАЯ РЕАКЦИЯ: Если пользователь отменил сценарий или нажал Стоп,
        // метод выбросит OperationCanceledException сразу, не тратя время на выделение потока.
        token.ThrowIfCancellationRequested();

        // BUG HIGH - Повторение критической уязвимости. Возврат `Task.Run` напрямую без async/await избавляет от машины состояний, но полностью лишает метод возможности контролировать зависание внутри `Tools.SmartClick`. Если ADB зависнет на первом же динамическом клике (после нахождения объекта через OpenCV), этот Task никогда не перейдет в состояние `Completed`, и вызывающий узел Дерева Поведения (Behavior Tree) застрянет в состоянии ожидания (`Running`) навсегда.
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

