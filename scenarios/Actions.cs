using EVEEchoesBot.resources;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using System.Diagnostics;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region EvaluateSystemSecurityAsync

    /// <summary>
    /// Универсальный метод проверки безопасности системы.
    /// Подходит как для штатного мониторинга, так и для проверок перед андоком/варпом.
    /// </summary>
    private static async Task<NodeStatus> EvaluateSystemSecurityAsync(ActiveBotAccount bot, CancellationToken token)
    {
        bot.CurrentTask = AccountTask.CheckSecurity;

        // 1. Быстрый чек: проверяем глобальный статус системы (не забил ли тревогу другой бот)
        var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);

        // Исправлено: корректно обрабатываем тип bool? (если равен false или null — система опасна)
        if (systemState.IsSafe is not true)
        {
            Logger.Log($"[{bot.Settings.Name}] Глобальная тревога! Система небезопасна.", LogType.Warning);
            bot.IsSaveLocal = false;
            bot._currenttarget = null; // Сбрасываем цель, если она была
            return NodeStatus.Failure;
        }

        // 2. Если глобально чисто, проверяем сами через OCR на экране
        SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

        switch (result)
        {
            case SecurityCheckResult.Safe:
                bot.IsSaveLocal = true;
                return NodeStatus.Success;

            case SecurityCheckResult.Danger:
                bot.IsSaveLocal = false;
                bot._currenttarget = null;
                Logger.Log($"[{bot.Settings.Name}] Обнаружен противник в локале! Активирую экстренную эвакуацию...", LogType.Warning);

                // Исправлено: принудительно переключаем бота и его соседей в режим бегства на станцию
                await bot.ExecuteEmergencyResponseAsync(isInitiator: true, token);
                return NodeStatus.Failure;

            case SecurityCheckResult.Unknown:
                bot.CurrentTask = AccountTask.LookAround;
                Logger.Log($"[{bot.Settings.Name}] Интерфейс потерян или перекрыт. Перехожу в режим ожидания и осмотра.", LogType.Warning);

                // Даем игре 3 секунды на возможную прогрузку интерфейса перед следующим тиком
                await Task.Delay(3000, token);
                return NodeStatus.Failure;

            default:
                return NodeStatus.Failure;
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
    #region CheckIfPlanetMiningTimeAsync

    /// <summary>
    /// Проверяет, пришло ли время для обслуживания планетарной добычи (выполняется только в доке).
    /// </summary>
    private static Task<NodeStatus> CheckIfPlanetMiningTimeAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // МГНОВЕННЫЙ ФИЛЬТР: если планетарка выключена в JSON, сразу выходим без спама в логи
        if (!bot.PlanetMining)
        {
            return Task.FromResult(NodeStatus.Failure);
        }

        // 1. Считаем триггер времени и округляем прошедшие часы для лога
        bool isTime = !bot._planetassembly.HasValue || (DateTime.Now - bot._planetassembly.Value).TotalHours >= 8;


        double hoursSinceLastAssembly = bot._planetassembly.HasValue
            ? Math.Round((DateTime.Now - bot._planetassembly.Value).TotalHours, 2)
            : 99.0;

#if DEBUG
        // 2. Выводим детальный диагностический лог для отладки условий на каждом тике дерева
        Logger.Log(
            $"[PLANET-CHECK] [{bot.Settings.Name}] " +
            $"Флаг PlanetMining: {(bot.PlanetMining ? "ВКЛ" : "ВЫКЛ")} | " +
            $"На станции (В доке): {(!bot._inSpace ? "ДА" : "НЕТ (В космосе)")} | " +
            $"Прошло часов: {hoursSinceLastAssembly}/8.00 (Доступно по времени: {(isTime ? "ДА" : "НЕТ")})",
            LogType.Test
        );
#endif

        // 3. Финальная проверка условий для пропуска к макросу
        if (bot._inSpace || !bot.PlanetMining)
        {
            return Task.FromResult(NodeStatus.Failure);
        }

        return Task.FromResult(isTime ? NodeStatus.Success : NodeStatus.Failure);
    }

    #endregion


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecutePlanetMiningSequenceAsync

    /// <summary>
    /// Выполняет взаимодействие с интерфейсом планетарной добычи внутри станции.
    /// </summary>
    private static async Task<NodeStatus> ExecutePlanetMiningSequenceAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Инициация входа в интерфейс планетарной добычи...", LogType.Info);

        // ========================================================
        // ЭТАП 1: НАВИГАЦИЯ (Вход в интерфейс)
        // ========================================================
        NodeStatus navStatus = await TryClickPlanetShortcutAsync(bot, token);

        if (navStatus == NodeStatus.Failure)
        {
            Logger.Log($"[{bot.Settings.Name}] Быстрый путь недоступен. Переход на резервный путь через меню.", LogType.Warning);

            if (await OpenMainMenuAsync(bot, token) == NodeStatus.Success)
            {
                navStatus = await ClickPlanetButtonInMenuAsync(bot, token);
            }
        }

        if (navStatus != NodeStatus.Success)
        {
            Logger.Log($"[{bot.Settings.Name}] Критическая ошибка: Не удалось войти в интерфейс планетарки.", LogType.Error);
            return NodeStatus.Failure;
        }

        await Task.Delay(2000, token);

        // ========================================================
        // ЭТАП 2: ОПТИМИЗИРОВАННАЯ СЕРИЯ КЛИКОВ (ЧЕРЕЗ ЦИКЛ)
        // ========================================================
        Logger.Log($"[{bot.Settings.Name}] Интерфейс открыт. Запуск циклической цепочки перезапуска...", LogType.Info);

        // Описываем шаги: какой элемент нажать и сколько миллисекунд подождать ПОСЛЕ клика
        // Используем синтаксис коллекций C# 12+ [ ... ]
        // Исправлено: заменено на массив для безопасного пересечения асинхронного await
        (GameUI Element, int DelayMs, string LogMessage)[] miningSteps = [
            (GameUI.FirstPlanet,   1200, "Выбор первой планеты в списке..."),
            (GameUI.PlanetTimer,   1500, "Отправка команды на перезапуск таймера..."),
            (GameUI.ConfirmButton, 1500, "Ожидание и отправка подтверждения диалога...")
        ];

        // Выполняем шаги в едином компактном цикле
        foreach (var (element, delayMs, logMessage) in miningSteps)
        {
            Logger.Log($"[{bot.Settings.Name}] {logMessage}", LogType.Info);

            await bot.ClickToAsync(element, token);
            await Task.Delay(delayMs, token);
        }

        // ========================================================
        // ЭТАП 3: ДОПОЛНИТЕЛЬНЫЕ МОДУЛИ И ЗАКРЫТИЕ
        // ========================================================
        if (bot.POS)
        {
            Logger.Log($"[{bot.Settings.Name}] Обнаружена привязка к ПОС. Запуск подмодуля сбора...", LogType.Info);

            // Исправлено: если сбор ресурсов на ПОС провалился — прерываем выполнение макроса
            if (await CollectPlanetToPosAsync(bot, token) == NodeStatus.Failure)
            {
                return NodeStatus.Failure;
            }
        }

        Logger.Log($"[{bot.Settings.Name}] Завершение макроса. Закрытие интерфейса планетарной добычи...", LogType.Info);

        // Последний шаг закрытия окна выносим отдельно, так как после него идет фиксация стейта
        await bot.ClickToAsync(GameUI.XButton, token);
        await Task.Delay(3500, token);

        bot._planetassembly = DateTime.Now;
        Logger.Log($"[{bot.Settings.Name}] Цикл планетарной добычи успешно обработан.", LogType.Success);

        return NodeStatus.Success;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CollectPlanetToPosAsync

    /// <summary>
    /// Дополнительный подмакрос: Ищет кнопку запуска сбора ресурсов на ПОС,
    /// при необходимости прокручивает интерфейс вниз и выполняет нажатие.
    /// </summary>
    private static async Task<NodeStatus> CollectPlanetToPosAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Поиск кнопки запуска сбора ресурсов на ПОС...", LogType.Info);

        string launchPathImg = Path.Combine(Program.TemplatesDir, "imgLaunchButton.png");
        Point? foundLaunchBtn = null;

        // ========================================================
        // ПОПЫТКА 1: Поиск кнопки "как есть" при открытии интерфейса
        // ========================================================
        await Task.Delay(1500, token);

        // Вызываем созданный нами ранее сквозной метод расширения
        var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.ResourceList, token);

        if (screenshot != null)
        {
            using var scope = screenshot; // Гарантированная scoped-утилизация unmanaged памяти
            var currentSnap = screenshot;

            foundLaunchBtn = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, launchPathImg, safeRegion, 0.80), token);
        }

        // ========================================================
        // ПОПЫТКА 2: Если не нашли — выполняем скролл и ищем заново
        // ========================================================
        if (!foundLaunchBtn.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}] Кнопка запуска не видна на первом экране. Выполняю прокрутку вниз...", LogType.Warning);

            await Task.Delay(500, token);
            await bot.ScrollDownAsync(GameUI.ResList, 200, token);
            await Task.Delay(1500, token); // Ожидаем завершения анимации скролла игры

            // Повторный захват через тот же безопасный метод расширения
            var (retryScreenshot, retryRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.ResourceList, token);

            if (retryScreenshot != null)
            {
                using var retryScope = retryScreenshot;
                var currentRetrySnap = retryScreenshot;

                foundLaunchBtn = await Task.Run(() => Tools.FindTemplateInRegion(currentRetrySnap, launchPathImg, retryRegion, 0.80), token);
            }
        }

        // ========================================================
        // ЭТАП 3: ВЗАИМОДЕЙСТВИЕ С НАЙДЕННОЙ КНОПКОЙ
        // ========================================================
        if (foundLaunchBtn.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}] Кнопка запуска сбора успешно обнаружена.", LogType.Info);

            await bot.ClickPointAsync(foundLaunchBtn.Value, token, minSec: 1, maxSec: 2, offset: 2);
            await Task.Delay(2000, token); // Ожидаем реакцию интерфейса

            await bot.ClickToAsync(GameUI.ConfirmButton, token);
            await Task.Delay(1500, token);

            return NodeStatus.Success;
        }

        Logger.Log($"[{bot.Settings.Name}] Ошибка: Кнопка 'imgLaunchButton.png' не найдена даже после скролла.", LogType.Error);
        return NodeStatus.Failure;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ScrollDownAsync

    /// <summary>
    /// Метод расширения (Extension Method) для класса <see cref="ActiveBotAccount"/>.
    /// Выполняет асинхронный скролл (свайп) вниз от указанной точки интерфейса на заданное расстояние.
    /// </summary>
    internal static Task ScrollDownAsync(this ActiveBotAccount bot, GameUI startElement, int distance, CancellationToken token)
    {
        // Распаковываем стартовые координаты из GameUI
        int packed = (int)startElement;
        int startX = packed / 10000;
        int startY = packed % 10000;

        // Рассчитываем конечную точку свайпа вверх (чтобы интерфейс прокрутился ВНИЗ)
        int endX = startX;
        int endY = startY - distance;

        string deviceTarget = $"127.0.0.1:{bot.Settings.AdbPort}";
        string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "adb.exe");
        string argsSwipe = $"-s {deviceTarget} shell input swipe {startX} {startY} {endX} {endY} 500"; // 500 мс на жест

        // Уводим выполнение процесса ADB в фоновый поток
        return Task.Run(() =>
        {
            try
            {
                ProcessStartInfo psiSwipe = new(adbPath, argsSwipe) { CreateNoWindow = true, UseShellExecute = false };
                using var currentProcess = Process.Start(psiSwipe);

                // Оптимизировано: использовали условный доступ ?. для проверки на null
                if (currentProcess?.WaitForExit(3000) is false)
                {
                    currentProcess.Kill();
                    Logger.Log($"[{bot.Settings.Name}] Команда ADB скролла убита по таймауту.", LogType.Warning);
                }
    #if DEBUG

                Logger.Log($"[{bot.Settings.Name}] Отправлен свайп от {startElement} (X={startX}, Y={startY}) вверх на {distance}px.", LogType.Test);
    #endif
            }
            catch (Exception ex)
            {
                Logger.Log($"Сбой при отправке команды скролла через ADB: {ex.Message}", LogType.Error);
            }
        }, token);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ScrollLeftAsync

    /// <summary>
    /// Метод расширения для класса <see cref="ActiveBotAccount"/>.
    /// Выполняет асинхронный скролл (свайп) влево от указанной точки интерфейса на заданное расстояние.
    /// </summary>
    internal static Task ScrollLeftAsync(this ActiveBotAccount bot, GameUI startElement, int distance, CancellationToken token)
    {
        // Распаковываем стартовые координаты из GameUI (по вашей схеме 4 знаков)
        int packed = (int)startElement;
        int startX = packed / 10000;
        int startY = packed % 10000;

        // Рассчитываем конечную точку свайпа влево (уменьшаем X)
        int endX = startX - distance;
        int endY = startY;

        string deviceTarget = $"127.0.0.1:{bot.Settings.AdbPort}";
        string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "adb.exe");
        string argsSwipe = $"-s {deviceTarget} shell input swipe {startX} {startY} {endX} {endY} 500";

        return Task.Run(() =>
        {
            try
            {
                ProcessStartInfo psiSwipe = new(adbPath, argsSwipe) { CreateNoWindow = true, UseShellExecute = false };
                using var currentProcess = Process.Start(psiSwipe);

                // Оптимизировано: защита от бесконечного ожидания с использованием условного доступа ?.
                if (currentProcess?.WaitForExit(3000) is false)
                {
                    currentProcess.Kill();
                    Logger.Log($"[{bot.Settings.Name}] Команда ADB горизонтального скролла убита по таймауту.", LogType.Warning);
                }
#if DEBUG
                Logger.Log($"[{bot.Settings.Name}] Отправлен свайп от {startElement} (X={startX}, Y={startY}) влево на {distance}px.", LogType.Test);
#endif
            }
            catch (Exception ex)
            {
                Logger.Log($"Сбой при отправке команды горизонтального скролла через ADB: {ex.Message}", LogType.Error);
            }
        }, token);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region TryClickPlanetShortcutAsync

    /// <summary>
    /// Шаг 1: Пытается найти иконку быстрого доступа к планетарке прямо на главном экране и кликнуть по ней.
    /// </summary>
    private static async Task<NodeStatus> TryClickPlanetShortcutAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Поиск иконки доступа планетарки на экране...", LogType.Info);

        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}] Окно целевой программы не найдено.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Путь к шаблону иконки на главном экране
        string pathImg = Path.Combine(Program.TemplatesDir, "imgPlanetShortcut.png");

        // ИСПРАВЛЕНО: Теперь метод использует твой сквозной метод расширения для подготовки кадра
        var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.FastMenu, token);

        if (screenshot == null)
        {
            // Логирование сбоя захвата и очистка брака уже сработали внутри хелпера
            return NodeStatus.Failure;
        }

        // Гарантированная scoped-утилизация unmanaged памяти OpenCV при любом выходе из метода
        using var screenshotScope = screenshot;

        try
        {
            // Уводим тяжелый поиск OpenCV в фоновый пул потоков
            var currentScreenshot = screenshot;
            Point? foundPos = await Task.Run(() => Tools.FindTemplateInRegion(currentScreenshot, pathImg, safeRegion, 0.80), token);

            if (foundPos.HasValue)
            {
                Logger.Log($"[{bot.Settings.Name}] Иконка доступа найдена. Клик...", LogType.Test);

                // Используем созданный эталонный метод расширения для динамического клика
                await bot.ClickPointAsync(foundPos.Value, token, minSec: 1, maxSec: 2, offset: 2);

                // Ожидаем загрузку интерфейса планетарки
                await Task.Delay(2000, token);
                return NodeStatus.Success;
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Пробрасываем корректную асинхронную отмену
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}] Сбой при анализе быстрого интерфейса планетарки: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }

        Logger.Log($"[{bot.Settings.Name}] Иконка быстрого доступа не обнаружена.", LogType.Test);
        return NodeStatus.Failure;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region OpenMainMenuAsync

    /// <summary>
    /// Атомарное действие: Открывает главное меню персонажа (CharMenu).
    /// Переиспользуется в любых сценариях, где требуется доступ к общим вкладкам игры.
    /// </summary>
    private static async Task<NodeStatus> OpenMainMenuAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Открытие главного меню игры (кликом по CharMenu)...", LogType.Info);

        // Исправлено: Прокинули сквозной токен отмены в метод расширения клика
        await bot.ClickToAsync(GameUI.CharMenu, token);

        // Даем игре честную секунду на отрисовку меню поверх экрана
        await Task.Delay(1000, token);

        return NodeStatus.Success;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ClickPlanetButtonInMenuAsync

    /// <summary>
    /// Шаг 3: Ищет кнопку планетарной добычи внутри открытого главного меню и кликает по ней.
    /// </summary>
    private static async Task<NodeStatus> ClickPlanetButtonInMenuAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Поиск пункта меню 'Планетарная добыча' внутри главного меню...", LogType.Info);

        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}] Окно целевой программы не найдено.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Путь к файлу-шаблону кнопки планетарки внутри меню
        string pathImg = Path.Combine(Program.TemplatesDir, "imgPlanetMenuButton.png");

        if (!File.Exists(pathImg))
        {
            Logger.Log($"[{bot.Settings.Name}] Файл шаблона '{Path.GetFileName(pathImg)}' отсутствует на диске!", LogType.Error);
            return NodeStatus.Failure;
        }

        // ИСПРАВЛЕНО: Теперь метод использует твой сквозной метод расширения для подготовки кадра главного меню
        var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.MainMenu, token);

        if (screenshot == null)
        {
            // Логирование сбоя захвата и очистка брака уже сработали внутри хелпера
            return NodeStatus.Failure;
        }

        // Гарантированная scoped-утилизация unmanaged памяти OpenCV при любом выходе из метода
        using var screenshotScope = screenshot;

        try
        {
            // Уводим тяжелый поиск OpenCV в фоновый пул потоков
            var currentScreenshot = screenshot;
            Point? foundPos = await Task.Run(() => Tools.FindTemplateInRegion(currentScreenshot, pathImg, safeRegion, 0.80), token);

            if (foundPos.HasValue)
            {
                Logger.Log($"[{bot.Settings.Name}] Кнопка планетарной добычи найдена. Переход в интерфейс...", LogType.Info);

                // Используем наш эталонный асинхронный метод расширения для динамического клика
                await bot.ClickPointAsync(foundPos.Value, token, minSec: 1, maxSec: 2, offset: 2);

                // Даем игре время прогрузить открывшийся оверлей планетарки
                await Task.Delay(2000, token);
                return NodeStatus.Success;
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Пробрасываем корректную асинхронную отмену
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}] Сбой при сканировании главного меню: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }

        Logger.Log($"[{bot.Settings.Name}] Ошибка: Пункт 'Планетарная добыча' не найден в главном меню.", LogType.Error);
        return NodeStatus.Failure;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region RunAliChatWarningAsync

    // Оптимизация памяти: выносим шаги макроса в статическое поле, чтобы не выделять память при каждом вызове
    private static readonly (GameUI Element, int DelayMs)[] AllianceMacroSteps = [
        (GameUI.ChatInputMenu, 1200),
        (GameUI.ChatFastInput, 1200),
        (GameUI.ChatInform,    1200),
        (GameUI.ChatMessScout, 1200),
        (GameUI.WindowCenter,  1500),
        (GameUI.ChatButtSend,  2000),
        (GameUI.WindowCenter,  0)
    ];

    /// <summary>
    /// Асинхронно выполняет высокоточный макрос оповещения альянса или корпорации о появлении угрозы в локале.
    /// </summary>
    public static async Task<NodeStatus> RunAliChatWarningAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Начало выполнения макроса оповещения альянса.", LogType.Test);

        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Целевое окно программы не найдено. Прерывание выполнения.", LogType.Error);
            return NodeStatus.Failure;
        }

        string pathAli = Path.Combine(Program.TemplatesDir, "imgAliChatENG.png");
        string pathCorp = Path.Combine(Program.TemplatesDir, "imgCorpChatENG.png");

        if (!File.Exists(pathAli) || !File.Exists(pathCorp))
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Файлы шаблонов чата отсутствуют на диске. Прерывание выполнения.", LogType.Error);
            return NodeStatus.Failure;
        }

        Point? foundChat = null;
        bool isCorpChat = false;
        Mat? screenshot = null;

        try
        {
            // ========================================================
            // ЭТАП 1: ЦИКЛ ОТКРЫТИЯ ИНТЕРФЕЙСА ЧАТА (ДО 2-Х ПОПЫТОК)
            // ========================================================
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                await bot.ClickToAsync(GameUI.ChatsInterface, token);
                await Task.Delay(attempt == 1 ? 3500 : 4000, token);

                // Перед новой попыткой освобождаем Mat из предыдущей итерации цикла
                screenshot?.Dispose();

                // ОПТИМИЗИРОВАНО: Хелпер сам сделает захват под семафором, проверит на брак и вернет Rect
                var (freshSnap, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.ChatsLabels, token);

                if (freshSnap == null)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Не удалось получить кадр чата на попытке {attempt}.", LogType.Error);
                    return NodeStatus.Failure;
                }

                screenshot = freshSnap; // Передаем ссылку в переменную для дальнейшего OpenCV-поиска

                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Поиск маркеров языка интерфейса чата (Попытка {attempt}).", LogType.Test);

                // Кэшируем локальные переменные для передачи в Task.Run
                var currentScreenshot = screenshot;
                var currentRegion = safeRegion;

                // Выносим тяжелый поиск OpenCV из вызывающего потока
                foundChat = await Task.Run(() => Tools.FindTemplateInRegion(currentScreenshot, pathAli, currentRegion, 0.85), token);
                isCorpChat = false;

                if (!foundChat.HasValue)
                {
                    foundChat = await Task.Run(() => Tools.FindTemplateInRegion(currentScreenshot, pathCorp, currentRegion, 0.85), token);
                    isCorpChat = true;
                }

                if (foundChat.HasValue) break;

                if (attempt == 1)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] ...Интерфейс чата не открылся. Повторная попытка клика.", LogType.Warning);
                }
            }

            if (!foundChat.HasValue)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Шаблоны чата не обнаружены после повторного клика.", LogType.Error);
                return NodeStatus.Failure;
            }

            // ========================================================
            // ЭТАП 2: КЛИК ПО НАЙДЕННОМУ ЧАТУ
            // ========================================================
            string chatTypeStr = isCorpChat ? "корпорации" : "альянса";
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Обнаружен интерфейс {chatTypeStr} чата в точке (X={foundChat.Value.X}, Y={foundChat.Value.Y}).", LogType.Test);

            await bot.ClickPointAsync(foundChat.Value, token, minSec: 1, maxSec: 3, offset: 3);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }
        finally
        {
            // ГАРАНТИРОВАННАЯ очистка памяти OpenCV при любом исходе макроса
            screenshot?.Dispose();
        }

        await Task.Delay(2000, token);

        // ========================================================
        // ЭТАП 3: ОПТИМИЗИРОВАННАЯ ЦЕПОЧКА ОТПРАВКИ МАКРОСА В ИГРУ
        // ========================================================
        foreach (var (element, delayMs) in AllianceMacroSteps)
        {
            await bot.ClickToAsync(element, token);

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Выполнение цепочки кликов оповещения альянса завершено.", LogType.Success);
        return NodeStatus.Success;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region PrepareScreenshotRegionAsync

    // Глобальный или статический семафор на уровне сервиса захвата для синхронизации GDI вызовов
    private static readonly System.Threading.SemaphoreSlim _gdiSemaphore = new(1, 1);

    /// <summary>
    /// Универсальный метод захвата экрана и подготовки безопасной области поиска.
    /// Возвращает кортеж (screenshot, safeRegion). Если захват не удался, возвращает (null, safeRegion с нулевыми размерами).
    /// </summary>
    // Исправлено: сделали метод публичным методом расширения (добавлено слово this)
    public static async Task<(Mat? Screenshot, Rect SafeRegion)> PrepareScreenshotRegionAsync(this ActiveBotAccount bot, GameRegions region, CancellationToken token)
    {
        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}] Окно целевой программы не найдено.", LogType.Error);
            return (null, new Rect());
        }

        Mat? screenshot = null;

        // Сначала занимаем очередь. Если токен отменится ТУТ, поток вылетит ДО блока try, 
        // не вызывая ложного и опасного Release() в блоке finally.
        await Program.GdiSemaphore.WaitAsync(token);

        try
        {
            // Выполняем захват в фоновом потоке
            screenshot = await Task.Run(() => Tools.CaptureWindow(bot.Hwnd), token);
        }
        catch (OperationCanceledException)
        {
            // Если токен отменился во время ожидания семафора или выполнения Task.Run,
            // гарантированно чистим screenshot, если он успел создаться под капотом.
            screenshot?.Dispose();
            throw;
        }
        finally
        {
            // Исправлено: освобождаем именно тот глобальный семафор, который заняли перед блоком try
            Program.GdiSemaphore.Release();
        }

        // Использование условного доступа ?. и явного сравнения с true
        if (screenshot?.Empty() ?? true)
        {
            Logger.Log($"[{bot.Settings.Name}] Не удалось выполнить захват окна эмулятора.", LogType.Error);
            screenshot?.Dispose();
            return (null, new Rect());
        }

        // 4. Получаем и корректируем регион под размеры окна
        Rect searchRegion = region.GetOpenCvRect();
        Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

        if (safeRegion.Width <= 0 || safeRegion.Height <= 0)
        {
            Logger.Log($"[{bot.Settings.Name}] Область поиска [{region}] выходит за рамки окна.", LogType.Error);
            screenshot.Dispose();
            return (null, new Rect());
        }

        return (screenshot, safeRegion);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region PrepareSpaceInterfaceAsync

    public static async Task<bool> PrepareSpaceInterfaceAsync(this ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Инициализация космического интерфейса: отдаление камеры и открытие грида...", LogType.Info);

        try
        {
            // 1. Отдаление камеры (симулируем щипок/зум пальцами наружу для отварпа камеры на максимум)
            // Координаты Pinch/Zoom в ADB передаются как последовательность, но проще и надежнее отправить 
            // 3-4 быстрых свайпа от центра к краям, либо нажать специальную кнопку интерфейса, если она заведена.
            // Если у тебя заведен элемент GameUI.ZoomOutButton, используем его. 
            // Если нет — симулируем стандартный жест отдаления через ADB:

            string deviceTarget = $"127.0.0.1:{bot.Settings.AdbPort}";
            string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "adb.exe");

            // Симуляция быстрого жеста отдаления (в зависимости от разрешения, настроим базовый жест)
            // Для универсальности нажмем горячую клавишу или выполним свайпы. 
            // Но так как ты просил нажать на ДВЕ ТОЧКИ — мы будем использовать твои элементы GameUI!
            Logger.Log($"[{bot.Settings.Name}] Отдаляю камеру корабля на максимум...", LogType.Test);
            await bot.ClickToAsync(GameUI.CoreInSpace, token);
            await Task.Delay(800, token);

            // 2. Открытие меню локального грида (овервью)
            Logger.Log($"[{bot.Settings.Name}] Открываю панель локального овервью/грида...", LogType.Test);
            await bot.ClickToAsync(GameUI.EyeIconClose, token);
            await Task.Delay(1200, token); // Даем анимации списка открыться

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка при подготовке космического интерфейса для '{bot.Settings.Name}': {ex.Message}", LogType.Error);
            return false;
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

}