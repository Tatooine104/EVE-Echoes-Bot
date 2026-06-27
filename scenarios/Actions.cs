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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Глобальная тревога! Система небезопасна.", LogType.Warning);
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
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Обнаружен противник в локале! Активирую экстренную эвакуацию...", LogType.Warning);

                // Исправлено: принудительно переключаем бота и его соседей в режим бегства на станцию
                await bot.ExecuteEmergencyResponseAsync(isInitiator: true, token);
                return NodeStatus.Failure;

            case SecurityCheckResult.Unknown:
                bot.CurrentTask = AccountTask.LookAround;
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Интерфейс потерян или перекрыт. Перехожу в режим ожидания и осмотра.", LogType.Warning);

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
    /// Выводит диагностику в лог строго один раз в час для защиты от спама на тиках дерева.
    /// </summary>
    /// <summary>
    /// Проверяет, пришло ли время для обслуживания планетарной добычи (выполняется только в доке).
    /// Полностью очищен от вложенных блокировок для исключения дедлоков в STA-модели потоков.
    /// </summary>
private static async Task<NodeStatus> CheckIfPlanetMiningTimeAsync(ActiveBotAccount bot, CancellationToken token)
{
    // ИСПРАВЛЕНО HIGH - Принудительно разрываем синхронный контекст!
    // Этот вызов заставляет await освободить текущий поток и перенести выполнение
    // в пул потоков CLR. Это полностью ликвидирует Spin-Wait заклинивание на первой секунде!
    await Task.Yield();

    if (!bot.PlanetMining)
    {
        return NodeStatus.Failure;
    }

    bool isTime = !bot._planetassembly.HasValue || (DateTime.Now - bot._planetassembly.Value).TotalHours >= 8;

    double currentHours = bot._planetassembly.HasValue
        ? (DateTime.Now - bot._planetassembly.Value).TotalHours
        : 99.0;

    int currentHoursInt = (int)Math.Floor(currentHours);

#if DEBUG
    bool isNewHour = currentHoursInt != bot._lastLoggedPlanetHours;
    bool shouldLog = isTime || isNewHour;

    if (shouldLog)
    {
        double logHoursDisplay = bot._planetassembly.HasValue ? Math.Round(currentHours, 2) : 99.0;

        Logger.Log(
            $"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] " +
            $"Флаг PlanetMining: {(bot.PlanetMining ? "ВКЛ" : "ВЫКЛ")} | " +
            $"На станции (В доке): {(!bot._inSpace ? "ДА" : "НЕТ (В космосе)")} | " +
            $"Прошло часов: {logHoursDisplay}/8.00 (Доступно по времени: {(isTime ? "ДА" : "НЕТ")})",
            LogType.Test
        );

        // Потокобезопасно обновляем флаг под персональным локом бота во избежание Race Condition памяти
        lock (bot._taskLock)
        {
            bot._lastLoggedPlanetHours = isTime ? -1 : currentHoursInt;
        }
    }
#endif

    if (bot._inSpace || !bot.PlanetMining)
    {
        return NodeStatus.Failure;
    }

    return isTime ? NodeStatus.Success : NodeStatus.Failure;
}


    #endregion


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecutePlanetMiningSequenceAsync

    /// <summary>
    /// Выполняет взаимодействие с интерфейсом планетарной добычи внутри станции.
    /// </summary>
    private static async Task<NodeStatus> ExecutePlanetMiningSequenceAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Инициация входа в интерфейс планетарной добычи...", LogType.Info);

        try
        {
            // ========================================================
            // ЭТАП 1: НАВИГАЦИЯ (Вход в интерфейс)
            // ========================================================
            NodeStatus navStatus = await TryClickPlanetShortcutAsync(bot, token);

            if (navStatus == NodeStatus.Failure)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Быстрый путь недоступен. Переход на резервный путь через меню.", LogType.Warning);

                if (await OpenMainMenuAsync(bot, token) == NodeStatus.Success)
                {
                    navStatus = await ClickPlanetButtonInMenuAsync(bot, token);
                }
            }

            if (navStatus != NodeStatus.Success)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Критическая ошибка: Не удалось войти в интерфейс планетарки.", LogType.Error);
                return NodeStatus.Failure;
            }

            await Task.Delay(2000, token);

            // ========================================================
            // ЭТАП 2: ОПТИМИЗИРОВАННАЯ СЕРИЯ КЛИКОВ (ЧЕРЕЗ ЦИКЛ)
            // ========================================================
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Интерфейс открыт. Запуск циклической цепочки перезапуска...", LogType.Info);

            // Описываем шаги: какой элемент нажать и сколько миллисекунд подождать ПОСЛЕ клика
            (GameUI Element, int DelayMs, string LogMessage)[] miningSteps = [
                (GameUI.FirstPlanet,   1200, "Выбор первой планеты в списке..."),
                (GameUI.PlanetTimer,   1500, "Отправка команды на перезапуск таймера..."),
                (GameUI.ConfirmButton, 1500, "Ожидание и отправка подтверждения диалога...")
            ];

            // Выполняем шаги в едином компактном цикле
            foreach (var (element, delayMs, logMessage) in miningSteps)
            {
                token.ThrowIfCancellationRequested();
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] {logMessage}", LogType.Info);

                await bot.ClickToAsync(element, token);
                await Task.Delay(delayMs, token);
            }

            // ========================================================
            // ЭТАП 3: ДОПОЛНИТЕЛЬНЫЕ МОДУЛИ И ЗАКРЫТИЕ
            // ========================================================
            if (bot.POS)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Обнаружена привязка к ПОС. Запуск подмодуля сбора...", LogType.Info);

                // Если сбор ресурсов на ПОС провалился — прерываем выполнение макроса
                if (await CollectPlanetToPosAsync(bot, token) == NodeStatus.Failure)
                {
                    return NodeStatus.Failure;
                }
            }

            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Завершение макроса. Закрытие интерфейса планетарной добычи...", LogType.Info);

            // Последний шаг закрытия окна выносим отдельно
            await bot.ClickToAsync(GameUI.XButton, token);
            await Task.Delay(3500, token);

            // ИСПРАВЛЕНО: Записываем число -1 вместо ложного false, так как тип поля строго int
            lock (bot._taskLock)
            {
                bot._planetassembly = DateTime.Now;
                bot._lastLoggedPlanetHours = -1; // Сбрасываем почасовой счетчик для нового 8-часового цикла
            }

            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Цикл планетарной добычи успешно обработан. Ветка закрыта на 8 часов.", LogType.Success);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Критическая ошибка во время выполнения макроса планетарки: {ex.Message}", LogType.Error);

            // ПРЕДОХРАНИТЕЛЬ: При сбое сдвигаем таймер на 15 минут в будущее под internal Lock
            // ИСПРАВЛЕНО: Сюда также передаем число -1
            lock (bot._taskLock)
            {
                bot._planetassembly = DateTime.Now.AddHours(-7.75);
                bot._lastLoggedPlanetHours = -1;
            }
            return NodeStatus.Failure;
        }
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
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Поиск кнопки запуска сбора ресурсов на ПОС...", LogType.Info);

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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка запуска не видна на первом экране. Выполняю прокрутку вниз...", LogType.Warning);

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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка запуска сбора успешно обнаружена.", LogType.Info);

            await bot.ClickPointAsync(foundLaunchBtn.Value, token, minSec: 1, maxSec: 2, offset: 2);
            await Task.Delay(2000, token); // Ожидаем реакцию интерфейса

            await bot.ClickToAsync(GameUI.ConfirmButton, token);
            await Task.Delay(1500, token);

            return NodeStatus.Success;
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Кнопка 'imgLaunchButton.png' не найдена даже после скролла.", LogType.Error);
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
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Команда ADB скролла убита по таймауту.", LogType.Warning);
                }
    #if DEBUG

                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен свайп от {startElement} (X={startX}, Y={startY}) вверх на {distance}px.", LogType.Test);
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
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Команда ADB горизонтального скролла убита по таймауту.", LogType.Warning);
                }
#if DEBUG
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отправлен свайп от {startElement} (X={startX}, Y={startY}) влево на {distance}px.", LogType.Test);
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
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Поиск иконки доступа планетарки на экране...", LogType.Info);

        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Окно целевой программы не найдено.", LogType.Error);
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
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Иконка доступа найдена. Клик...", LogType.Test);

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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Сбой при анализе быстрого интерфейса планетарки: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Иконка быстрого доступа не обнаружена.", LogType.Test);
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
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Открытие главного меню игры (кликом по CharMenu)...", LogType.Info);

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
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Поиск пункта меню 'Планетарная добыча' внутри главного меню...", LogType.Info);

        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Окно целевой программы не найдено.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Путь к файлу-шаблону кнопки планетарки внутри меню
        string pathImg = Path.Combine(Program.TemplatesDir, "imgPlanetMenuButton.png");

        if (!File.Exists(pathImg))
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Файл шаблона '{Path.GetFileName(pathImg)}' отсутствует на диске!", LogType.Error);
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
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка планетарной добычи найдена. Переход в интерфейс...", LogType.Info);

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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Сбой при сканировании главного меню: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Пункт 'Планетарная добыча' не найден в главном меню.", LogType.Error);
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


    public static async Task<(Mat? Screenshot, Rect SafeRegion)> PrepareScreenshotRegionAsync(this ActiveBotAccount bot, GameRegions region, CancellationToken token)
    {
        if (bot.Hwnd == IntPtr.Zero) return (null, new Rect());

        Mat? screenshot = null;

        try
        {
            // Просто вызываем метод. Вся магия и безопасность теперь внутри CaptureWindow!
            screenshot = await Task.Run(() => Tools.CaptureWindow(bot.Hwnd, bot.AccountGdiSemaphore), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            screenshot?.Dispose();
            return (null, new Rect()); // Возвращаем пустой кадр вместо падения дерева
        }

        if (screenshot?.Empty() ?? true)
        {
            screenshot?.Dispose();
            return (null, new Rect());
        }

        Rect searchRegion = region.GetOpenCvRect();
        Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

        return (screenshot, safeRegion);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region PrepareSpaceInterfaceAsync

    public static async Task<bool> PrepareSpaceInterfaceAsync(this ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Инициализация космического интерфейса: отдаление камеры и открытие грида...", LogType.Info);

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
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Отдаляю камеру корабля на максимум...", LogType.Test);
            await bot.ClickToAsync(GameUI.CoreInSpace, token);
            await Task.Delay(800, token);

            // 2. Открытие меню локального грида (овервью)
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Открываю панель локального овервью/грида...", LogType.Test);
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