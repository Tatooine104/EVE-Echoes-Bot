using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using System.Diagnostics;

using static System.Diagnostics.Process;
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
                bot._currenttarget = null; // Сбрасываем цель, чтобы не лететь в ловушку
                Logger.Log($"[{bot.Settings.Name}] Обнаружен противник в локале!", LogType.Warning);
                await Task.Delay(5000, token); // Даем паузу перед следующим тиком
                return NodeStatus.Failure;

            case SecurityCheckResult.Unknown:
                bot.CurrentTask = AccountTask.LookAround;
                Logger.Log($"[{bot.Settings.Name}] Интерфейс потерян. Запуск макроса очистки...", LogType.Warning);
                await bot.ExecuteLookAroundDiagnosticsAsync(token);
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
        // 1. Считаем триггер времени и округляем прошедшие часы для лога
        bool isTime = !bot._planetassembly.HasValue || (DateTime.UtcNow - bot._planetassembly.Value).TotalHours >= 8;

        double hoursSinceLastAssembly = bot._planetassembly.HasValue
            ? Math.Round((DateTime.UtcNow - bot._planetassembly.Value).TotalHours, 2)
            : 99.0;

        // 2. Выводим детальный диагностический лог для отладки условий на каждом тике дерева
        Logger.Log(
            $"[PLANET-CHECK] [{bot.Settings.Name}] " +
            $"Флаг PlanetMining: {(bot.PlanetMining ? "ВКЛ" : "ВЫКЛ")} | " +
            $"На станции (В доке): {(!bot._inSpace ? "ДА" : "НЕТ (В космосе)")} | " +
            $"Прошло часов: {hoursSinceLastAssembly}/8.00 (Доступно по времени: {(isTime ? "ДА" : "НЕТ")})", 
            LogType.Test
        );

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

        // СТРУКТУРА ПЕРЕХОДОВ: Быстрый путь (Иконка) ИЛИ Длинный путь (Меню -> Кнопка)
        var navigationTree = new SelectorNode("Planet Interface Navigation",

            // Путь 1: Пробуем кликнуть по иконке быстрого доступа на экране
            new ActionNode("Try Shortcut Path", TryClickPlanetShortcutAsync),

            // Путь 2: Если иконки нет, открываем меню и ищем кнопку там
            new SequenceNode("Main Menu Fallback Path",
                new ActionNode("Open Main Menu", OpenMainMenuAsync),
                new ActionNode("Click Planet Button In Menu", ClickPlanetButtonInMenuAsync)
            )
        );

        // Выполняем навигацию. Если оба пути вернули Failure — прерываем весь цикл
        NodeStatus navStatus = await navigationTree.TickAsync(bot, token);
        if (navStatus == NodeStatus.Failure)
        {
            Logger.Log($"[{bot.Settings.Name}] Критическая ошибка: Не удалось войти в интерфейс планетарки.", LogType.Error);
            return NodeStatus.Failure;
        }

        await Task.Delay(2000, token);

        // 2. Адаптивная часть: мы внутри интерфейса, выполняем перезапуск таймеров и сбор
        Logger.Log($"[{bot.Settings.Name}] Интерфейс планетарки открыт. Выбор первой планеты...", LogType.Info);

        // Клик по первой планете в списке
        await bot.ClickToAsync(GameUi.FirstPlanet);
        await Task.Delay(1200, token); // Ждем анимацию выбора планеты

        Logger.Log($"[{bot.Settings.Name}] Отправка команды на перезапуск таймера добычи...", LogType.Info);

        // Клик по кнопке перезапуска таймера
        await bot.ClickToAsync(GameUi.PlanetTimer);
        await Task.Delay(1500, token); // Ждем подтверждения от сервера игры

        // Клик по кнопке подтверждения
        await bot.ClickToAsync(GameUi.ComfirmButton);
        await Task.Delay(1500, token); // Ждем подтверждения от сервера игры

        // Если подключен ПОС, выполняем дополнительное действие сбора ресурсов
        if (bot.POS)
        {
            Logger.Log($"[{bot.Settings.Name}] Обнаружена привязка к ПОС. Запуск подмодуля сбора...", LogType.Info);

            // Вызываем вынесенный метод со всей логикой поиска и скролла
            await CollectPlanetToPosAsync(bot, token);
        }
        else
        {
            Logger.Log($"[{bot.Settings.Name}] Режим без ПОС. Ресурсы остаются на планете.", LogType.Info);
        }

        // ЗАКРЫТИЕ ИНТЕРФЕЙСА: Возвращаем экран в исходное чистое состояние станции
        Logger.Log($"[{bot.Settings.Name}] Завершение макроса. Закрытие интерфейса планетарной добычи...", LogType.Info);
        await bot.ClickToAsync(GameUi.XButton);
        await Task.Delay(3500, token);

        // Фиксируем время успешного завершения цикла (UTC-время)
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
        await Task.Delay(1500, token);
        Rect interfaceRegion = GameRegions.ResourceList.GetOpenCvRect();
        Point? foundLaunchBtn = null;

        // Двухэтапный поиск: Попытка 1 (как есть), Попытка 2 (после скролла)
        for (int searchAttempt = 1; searchAttempt <= 2; searchAttempt++)
        {
            using (Mat? currentScreenshot = Tools.CaptureWindow(bot.Hwnd))
            {
                if (currentScreenshot?.Empty() is not false)
                {
                    return NodeStatus.Failure;
                }

                Rect safeInterfaceRegion = Tools.ClampRegion(interfaceRegion, currentScreenshot.Width, currentScreenshot.Height);
                foundLaunchBtn = Tools.FindTemplateInRegion(currentScreenshot, launchPathImg, safeInterfaceRegion, 0.80);
            }

            // Если нашли — мгновенно выходим из цикла прокрутки
            if (foundLaunchBtn.HasValue)
            {
                Logger.Log($"[{bot.Settings.Name}] Кнопка запуска сбора найдена на попытке {searchAttempt}.", LogType.Info);
                break;
            }

            // Если на первой попытке не нашли — скроллим
            if (searchAttempt == 1)
            {
                Logger.Log($"[{bot.Settings.Name}] Кнопка запуска не видна. Выполняю прокрутку списка ресурсов вниз...", LogType.Warning);
                await Task.Delay(1500, token);
                // Прокручиваем интерфейс от точки ResList вверх (чтобы список ушел вниз) на 200 пикселей
                await bot.ScrollDownAsync(GameUi.ResList, 200, token);
                await Task.Delay(1500, token); // Ждем остановки анимации списка
            }
        }

        // Если в итоге нашли кнопку — кликаем по ней
        if (foundLaunchBtn.HasValue)
        {
            await bot.ClickPointAsync(foundLaunchBtn.Value, token, minSec: 1, maxSec: 2, offset: 2);
            await Task.Delay(2000, token); // Ожидаем отправку ресурсов на ПОС

            // Клик по кнопке подтверждения
            await bot.ClickToAsync(GameUi.ComfirmButton);
            await Task.Delay(1500, token); // Ждем подтверждения от сервера игры

            return NodeStatus.Success;
        }

        Logger.Log($"[{bot.Settings.Name}] Ошибка: Кнопка 'imgLaunchButton.png' не найдена даже после скролла интерфейса.", LogType.Error);
        return NodeStatus.Failure;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ScrollDownAsync

    /// <summary>
    /// Метод расширения (Extension Method) для класса <see cref="ActiveBotAccount"/>.
    /// Выполняет асинхронный скролл (свайп) вниз от указанной точки интерфейса на заданное расстояние.
    /// </summary>
    internal static Task ScrollDownAsync(this ActiveBotAccount bot, GameUi startElement, int distance, CancellationToken token)
    {
        // Распаковываем стартовые координаты из GameUi
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
                Process.Start(psiSwipe)?.WaitForExit();
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

    #region TryClickPlanetShortcutAsync

    /// <summary>
    /// Шаг 1: Пытается найти иконку быстрого доступа к планетарке прямо на главном экране и кликнуть по ней.
    /// </summary>
    private static async Task<NodeStatus> TryClickPlanetShortcutAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Поиск иконки быстрого доступа планетарки на экране...", LogType.Info);

        if (bot.Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{bot.Settings.Name}] Окно целевой программы не найдено.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Путь к шаблону иконки на главном экране
        string pathImg = Path.Combine(Program.TemplatesDir, "imgPlanetShortcut.png");

        // Получаем регион поиска быстрой панели
        Rect searchRegion = GameRegions.FastMenu.GetOpenCvRect();

        // Захватываем скриншот окна эмулятора
        using Mat? screenshot = Tools.CaptureWindow(bot.Hwnd);
        if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
        {
            Logger.Log($"[{bot.Settings.Name}] Не удалось выполнить захват окна эмулятора.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Корректируем регион под размеры окна, чтобы избежать выхода за границы
        Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);
        if (safeRegion.Width <= 0 || safeRegion.Height <= 0)
        {
            Logger.Log($"[{bot.Settings.Name}] Область поиска иконки выходит за рамки окна.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Ищем шаблон внутри региона с точностью 80%
        Point? foundPos = Tools.FindTemplateInRegion(screenshot, pathImg, safeRegion, 0.80);

        if (foundPos.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}] Иконка быстрого доступа найдена. Клик...", LogType.Info);

            // ОПТИМИЗАЦИЯ: Используем созданный эталонный метод расширения для динамического клика
            await bot.ClickPointAsync(foundPos.Value, token, minSec: 1, maxSec: 2, offset: 2);

            // Ожидаем загрузку интерфейса планетарки
            await Task.Delay(2000, token);
            return NodeStatus.Success;
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

        // Используем наш эталонный метод расширения
        await bot.ClickToAsync(GameUi.CharMenu);

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

        // Получаем область экрана открытого главного меню
        Rect searchRegion = GameRegions.MainMenu.GetOpenCvRect();

        // Захватываем текущий скриншот окна эмулятора
        using Mat? screenshot = Tools.CaptureWindow(bot.Hwnd);
        if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
        {
            Logger.Log($"[{bot.Settings.Name}] Не удалось выполнить захват окна для сканирования меню.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Корректируем регион под реальные размеры окна эмулятора
        Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);
        if (safeRegion.Width <= 0 || safeRegion.Height <= 0)
        {
            Logger.Log($"[{bot.Settings.Name}] Область поиска кнопки меню выходит за рамки окна эмулятора.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Ищем кнопку внутри региона главного меню с точностью 80%
        Point? foundPos = Tools.FindTemplateInRegion(screenshot, pathImg, safeRegion, 0.80);

        if (foundPos.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}] Кнопка планетарной добычи найдена. Переход в интерфейс...", LogType.Info);

            // Используем наш эталонный асинхронный метод расширения для динамического клика
            await bot.ClickPointAsync(foundPos.Value, token, minSec: 1, maxSec: 2, offset: 2);

            // Даем игре время прогрузить открывшийся оверлей планетарки
            await Task.Delay(2000, token);
            return NodeStatus.Success;
        }

        Logger.Log($"[{bot.Settings.Name}] Ошибка: Пункт 'Планетарная добыча' не найден в главном меню.", LogType.Error);
        return NodeStatus.Failure;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region RunAliChatWarningAsync

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

        Rect searchRegion = GameRegions.ChatsLabels.GetOpenCvRect();
        Point? foundChat = null;
        bool isCorpChat = false;
        Mat? screenshot = null;

        // ========================================================
        // ЭТАП 1: ЦИКЛ ОТКРЫТИЯ ИНТЕРФЕЙСА ЧАТА (ДО 2-Х ПОПЫТОК)
        // ========================================================
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            await bot.ClickToAsync(GameUi.ChatsInterface);
            await Task.Delay(attempt == 1 ? 3500 : 4000, token);

            screenshot?.Dispose();
            screenshot = Tools.CaptureWindow(bot.Hwnd);

            if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Не удалось выполнить повторный захват окна. Прерывание выполнения.", LogType.Error);
                screenshot?.Dispose();
                return NodeStatus.Failure;
            }

            Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Поиск маркеров языка интерфейса чата (Попытка {attempt}).", LogType.Test);

            foundChat = Tools.FindTemplateInRegion(screenshot, pathAli, safeRegion, 0.85);
            isCorpChat = false;

            if (!foundChat.HasValue)
            {
                foundChat = Tools.FindTemplateInRegion(screenshot, pathCorp, safeRegion, 0.85);
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
            screenshot?.Dispose();
            return NodeStatus.Failure;
        }

        // ========================================================
        // ЭТАП 2: КЛИК ПО НАЙДЕННОМУ ЧАТУ
        // ========================================================
        try
        {
            string chatTypeStr = isCorpChat ? "корпорации" : "альянса";
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Обнаружен интерфейс {chatTypeStr} чата в точке (X={foundChat.Value.X}, Y={foundChat.Value.Y}).", LogType.Test);

            await bot.ClickPointAsync(foundChat.Value, token, minSec: 1, maxSec: 3, offset: 3);
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
            screenshot?.Dispose();
            return NodeStatus.Failure;
        }
        finally
        {
            screenshot?.Dispose();
        }

        await Task.Delay(2000, token);

        // ========================================================
        // ЭТАП 3: ОПТИМИЗИРОВАННАЯ ЦЕПОЧКА ОТПРАВКИ МАКРОСА В ИГРУ
        // ========================================================
        var macroSteps = new (GameUi Element, int DelayMs)[7]
        {
            (GameUi.ChatInputMenu, 1200),
            (GameUi.ChatFastInput, 1200),
            (GameUi.ChatInform,    1200),
            (GameUi.ChatMessScout, 1200),
            (GameUi.WindowCenter,  1500),
            (GameUi.ChatButtSend,  2000),
            (GameUi.WindowCenter,  0)
        };

        foreach (var (element, delayMs) in macroSteps)
        {
            await bot.ClickToAsync(element);

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Выполнение цепочки кликов оповещения альянса завершено.", LogType.Success);
        return NodeStatus.Success;
    }

    #endregion

}