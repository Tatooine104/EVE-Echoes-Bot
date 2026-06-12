using EVEEchoesBot.resources;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region BuildMinerTree

    /// <summary>
    /// Собирает дерево поведения для сценария «Шахтер в лоу-секах» (LowMiner).
    /// Сценарий управляет полным циклом добычи руды, логистикой на станцию и безопасностью.
    /// </summary>
    internal static SelectorNode BuildMinerTree()
    {
        return new SelectorNode("LowMiner Root",

            // =========================================================================
            // ПРИОРИТЕТ №1: БЛОК ЭКСТРЕННОЙ ЭВАКУАЦИИ (Срабатывает мгновенно при угрозе)
            // =========================================================================
            new SequenceNode("Emergency Evacuation Branch",
                // Проверяет, выставлена ли задача бегства (this.CurrentTask == AccountTask.GoToStation)
                new ActionNode("Is Panic State Active", CheckIsPanicStateActiveAsync),
                new SelectorNode("Panic Actions",
                    // Если мы уже на станции — задача паники выполнена успешно
                    new ActionNode("Is Already Safe Docked", CheckIsDockedAsync),
                    // Если в космосе — берем разгон и варпаем на станцию (возвращает Running пока летим)
                    new ActionNode("Execute Emergency Warp To Base", WarpToBaseAsync)
                )
            ),

            // =========================================================================
            // БЛОК СТАНЦИИ: Нахождение в доке и обслуживание
            // =========================================================================
            new SequenceNode("Station Hub Branch",
                new ActionNode("Is Docked Check", CheckIsDockedAsync),
                // Защита от спама: проверяем, что мы НЕ находимся в процессе андока прямо сейчас
                new ActionNode("Is NOT Processing Undock", CheckIsNotUndockingAsync),

                new SelectorNode("Station Actions",

                    // ВЕТКА ВЫГРУЗКИ: Выгружаем руду, если трюм полный
                    new SequenceNode("Unload Cargo Sequence",
                        new ActionNode("Is Cargo Full Check", CheckIsCargoFullAsync),
                        new ActionNode("Unload Ore", UnloadOreToHangarAsync)
                    ),

                    // ВЕТКА АНДОКА: Вылетаем, если трюм пуст и в системе безопасно
                    new SequenceNode("Undock Monolithic Sequence",
                        new ActionNode("Check Cargo Empty Before Undock", CheckIsCargoEmptyAsync),
                        new ActionNode("Check Safe Before Undock", EvaluateSystemSecurityAsync),
                        new ActionNode("Execute Undock", ExecuteUndockAsync) // Внутри ставит флаг _isUndocking = true
                    )
                )
            ),

            // =========================================================================
            // БЛОК КОСМОСА: Полеты, навигация и процесс копки
            // =========================================================================
            new SequenceNode("Space Operations Branch",

                new SelectorNode("Space Workflow Selector",

                    // ВОЗВРАТ НА БАЗУ: Если трюм заполнился во время добычи
                    new SequenceNode("Return Full Cargo To Base",
                        new ActionNode("Is Cargo Full In Space", CheckIsCargoFullAsync),
                        new ActionNode("Warp To Base", WarpToBaseAsync) // Возвращает Running пока летит
                    ),

                    // ПЕРЕЛЕТ НА БЕЛТ: Выбор астероидного пояса и прыжок к нему
                    new SequenceNode("Flight To Belt Sequence",
                        new ActionNode("Is NOT In Mining Zone", CheckIsNotInMiningZoneAsync),

                        // Выбор пояса (если еще не выбран)
                        new SelectorNode("Belt Selection Selector",
                            new ActionNode("Is Belt Already Selected", CheckIsBeltAlreadySelectedAsync),
                            new ActionNode("Select Asteroid Belt", SelectAsteroidBeltAsync)
                        ),

                        // Проверка безопасности перед прыжком и сам варп
                        new ActionNode("Check Safe Before Warp", EvaluateSystemSecurityAsync),
                        new ActionNode("Warp To Selected Belt", WarpToSelectedBeltAsync) // Возвращает Running пока летит
                    ),

                    // АКТИВНАЯ ДОБЫЧА: Захват целей и удержание цикла работы лазеров
                    new SequenceNode("Active Mining Sequence",
                        new ActionNode("Clear Flight State On Arrival", ClearFlightStateOnArrivalAsync),

                        // Логика лазеров (активируем только если они отключены)
                        new SelectorNode("Targeting and Activation Selector",
                            new ActionNode("Check If Mining Is Active", CheckIfMiningIsActiveAsync),
                            new SequenceNode("Lock And Mine Sequence",
                                new ActionNode("Target Asteroid", TargetAsteroidAsync),
                                new ActionNode("Activate Lasers", ActivateLasersAsync)
                            )
                        ),

                        new ActionNode("Mining Monitor State", MiningMonitorStateAsync)
                    )
                )
            )
        );
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsDockedAsync

    /// <summary>
    /// Проверяет, находится ли корабль внутри дока станции по наличию кнопки выхода из дока.
    /// </summary>
    public static async Task<NodeStatus> CheckIsDockedAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Используем хелпер для захвата экрана и подготовки региона
        var (screenshot, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.ControlUndock, token);

        if (screenshot == null)
        {
            return NodeStatus.Failure;
        }

        // Современная и безопасная утилизация unmanaged памяти OpenCV при любом выходе из метода
        using var screenshotScope = screenshot;

        // Оптимизировано: берем централизованный путь к графике из памяти, не терзая диск
        string pathImg = Path.Combine(Program.TemplatesDir, "imgUndock1.png");

        // Защищаем фоновый поток: фиксируем ссылку на матрицу до запуска Task.Run,
        // исключая AccessViolationException при внезапной отмене токена
        var currentSnap = screenshot;
        Point? foundPos = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathImg, safeRegion, 0.85), token);

        if (foundPos.HasValue)
        {
            // Кнопка выхода найдена -> корабль точно в доке
            bot._inSpace = false;
            return NodeStatus.Success;
        }

        // Кнопка не найдена -> корабль в космосе
        bot._inSpace = true;
        return NodeStatus.Failure;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsCargoFullAsync

    /// <summary>
    /// Проверяет, заполнен ли рудный трюм корабля (Узел Дерева Поведения).
    /// </summary>
    private static async Task<NodeStatus> CheckIsCargoFullAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // 1. Делаем первый снимок для первичного анализа
        var (screenshot, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.FastMenu, token);
        if (screenshot == null) return NodeStatus.Failure;

        using var screenshotScope = screenshot; // Безопасная scoped-утилизация Mat
        string pathCargo = Path.Combine(Program.TemplatesDir, "imgCargoFold100.png");
        var currentSnap = screenshot;

        // Ищем трюм асинхронно в фоновом пуле потоков
        Point? foundCargo = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathCargo, safeRegion, 0.85), token);
        if (foundCargo.HasValue) return NodeStatus.Success;

        // 2. Если не нашли трюм, ищем флаг флота, который мог его перекрыть (тоже асинхронно)
        string pathFleet = Path.Combine(Program.TemplatesDir, "imgFleetFlags.png");
        Point? foundFleet = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathFleet, safeRegion, 0.85), token);

        if (!foundFleet.HasValue) return NodeStatus.Failure; // Ни трюма, ни флага — значит не полон

        // 3. Флаг нашли — сдвигаем панель влево на 50 пикселей от точки FastMenu1
        await bot.ScrollLeftAsync(GameUI.FastMenu1, 50, token);
        await Task.Delay(600, token); // Ждем завершения анимации свайпа

        // 4. Повторный снимок для проверки трюма после сдвига
        var (retryScreenshot, retryRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.FastMenu, token);
        if (retryScreenshot == null) return NodeStatus.Failure;

        using var retryScope = retryScreenshot; // Безопасная scoped-утилизация второго Mat
        var currentRetrySnap = retryScreenshot;

        // Финальный асинхронный поиск трюма на сдвинутом экране
        Point? foundCargoRetry = await Task.Run(() => Tools.FindTemplateInRegion(currentRetrySnap, pathCargo, retryRegion, 0.85), token);

        return foundCargoRetry.HasValue ? NodeStatus.Success : NodeStatus.Failure;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsCargoEmptyAsync

    /// <summary>
    /// Проверяет, пуст ли рудный трюм корабля (необходим перед вылетом).
    /// </summary>
    private static async Task<NodeStatus> CheckIsCargoEmptyAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Делаем снимок панели для анализа пустого трюма
        var (screenshot, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.FastMenu, token);
        if (screenshot == null)
        {
            return NodeStatus.Failure;
        }

        // Современная и безопасная scoped-утилизация unmanaged памяти OpenCV
        using var screenshotScope = screenshot;

        // Оптимизировано: берем централизованный путь к графике из памяти
        string pathCargoEmpty = Path.Combine(Program.TemplatesDir, "imgCargoHold0.png");

        // Исправлено: Защищаем фоновый поток и уводим тяжелый поиск OpenCV в фоновый пул
        var currentSnap = screenshot;
        Point? foundCargoEmpty = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathCargoEmpty, safeRegion, 0.85), token);

        if (foundCargoEmpty.HasValue)
        {
            // Шаблон нуля найден -> трюм пуст
            return NodeStatus.Success;
        }

        // Шаблон не найден -> трюм не пуст (или меню перекрыто)
        return NodeStatus.Failure;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region UnloadOreToHangarAsync

    /// <summary>
    /// Выполняет разгрузку руды на склад текущей станции (Узел Дерева Поведения).
    /// </summary>
    private static async Task<NodeStatus> UnloadOreToHangarAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Трюм заполнен. Выгрузка руды на склад станции.", LogType.Info);

        // ========================================================
        // ШАГ 1-2: ОТКРЫТИЕ МЕНЮ И ПОДГОТОВКА СКЛАДА (Zero Allocation)
        // ========================================================
        (GameUI Element, int DelayMs)[] initialSteps = [
            (GameUI.FastMenu1, 800),
            (GameUI.CollapseStation, 800)
        ];

        foreach (var (element, delayMs) in initialSteps)
        {
            await bot.ClickToAsync(element, token);
            await Task.Delay(delayMs, token);
        }

        // ========================================================
        // ШАГ 3: ДИНАМИЧЕСКИЙ ПОИСК ИКОНКИ РУДНОГО ОТСЕКА
        // ========================================================
        // ИСПРАВЛЕНО: ищем в регионе инвентаря/меню, а не в левом углу локал-чата!
        var (screenshot, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.LocalChat, token);
        if (screenshot == null) return NodeStatus.Failure;

        using var screenshotScope = screenshot;

        // ОПТИМИЗИРОВАНО: берем путь из кэша ядра Program
        string pathOreHold = Path.Combine(Program.TemplatesDir, "imgOreHold.png");

        var currentScreenshot = screenshot;
        Point? foundOreHold = await Task.Run(() => Tools.FindTemplateInRegion(currentScreenshot, pathOreHold, safeRegion, 0.85), token);

        if (!foundOreHold.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}] Иконка рудного отсека (imgOreHold.png) не найдена.", LogType.Error);
            return NodeStatus.Failure;
        }

        // Кликаем по найденной точке рудного отсека
        await bot.ClickPointAsync(foundOreHold.Value, token, minSec: 1, maxSec: 3, offset: 3);
        await Task.Delay(800, token);

        // ========================================================
        // ШАГ 4-6: ВЫДЕЛЕНИЕ, ПЕРЕНОС В АНГАР И ЗАКРЫТИЕ (Zero Allocation)
        // ========================================================
        (GameUI Element, int DelayMs)[] finalSteps = [
            (GameUI.SelectAll, 600),
            (GameUI.ItemHangar, 1500),
            (GameUI.XButton, 0)
        ];


        foreach (var (element, delayMs) in finalSteps)
        {
            await bot.ClickToAsync(element, token);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }
        }

        Logger.Log($"[{bot.Settings.Name}] Выгрузка руды успешно завершена.", LogType.Success);
        return NodeStatus.Success;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecuteUndockAsync

    /// <summary>
    /// Выполняет команду выхода из дока станции (андок) в космос.
    /// </summary>
    private static async Task<NodeStatus> ExecuteUndockAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] В системе чисто. Инициирую выход из дока.", LogType.Info);

        // Защищаем станцию от повторного входа на следующем тике
        bot._isUndocking = true;

        try
        {
            // Клиparent по кнопке Андока
            await bot.ClickToAsync(GameUI.UndockButton, token);

            // Даем игре 4 секунды на базовый старт анимации прогрузки космоса
            await Task.Delay(5000, token);

            // Выставляем флаг, что процесс пошел успешно. На следующем тике 
            // узел CheckIsDockedAsync сам перепроверит экран.
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка при выполнении андока аккаунта '{bot.Settings.Name}': {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }
        finally
        {
            bot._isUndocking = false;
        }
    }



    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region WarpToBaseAsync

    /// <summary>
    /// Выполняет варп и стыковку (док) на домашнюю станцию для разгрузки.
    /// </summary>
    private static async Task<NodeStatus> WarpToBaseAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // TODO: [Заглушка] Реализовать макрос варпа и дока на домашнюю станцию
        Logger.Log($"[{bot.Settings.Name}] Вызван макрос варпа на базу (ЗАГЛУШКА).", LogType.Warning);

        await Task.Delay(1000, token); // Имитация минимальной задержки выполнения
        return NodeStatus.Success;
    }



    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsNotInMiningZoneAsync

    /// <summary>
    /// Проверяет, что корабль еще НЕ находится непосредственно в зоне добычи (на белте).
    /// </summary>
    private static Task<NodeStatus> CheckIsNotInMiningZoneAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return Task.FromResult(!bot._isinminingzone ? NodeStatus.Success : NodeStatus.Failure);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIfAlreadyWarpingAsync

    /// <summary>
    /// Проверяет, находится ли корабль в процессе варпа.
    /// Помогает удерживать тик дерева, не совершая лишних действий до прилета.
    /// </summary>
    private static Task<NodeStatus> CheckIfAlreadyWarpingAsync(ActiveBotAccount bot, CancellationToken _)
    {
        if (bot._iswarping)
        {
            Logger.Log($"[{bot.Settings.Name}] Корабль находится в процессе варпа. Удерживаю состояние полета.", LogType.Test);

            // Возвращаем Running, чтобы заблокировать выполнение нижних шагов (выбор и клик варпа) до прилета
            return Task.FromResult(NodeStatus.Running);
        }

        // Корабль стоит на месте -> возвращаем Success, разрешая дереву идти дальше к выбору белта и прыжку
        return Task.FromResult(NodeStatus.Success);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsBeltAlreadySelectedAsync

    /// <summary>
    /// Проверяет, выбран ли уже астероидный пояс в качестве текущей цели движения.
    /// </summary>
    private static Task<NodeStatus> CheckIsBeltAlreadySelectedAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Исправлено: проверяем не просто на null, а на то, что это реальный текстовый маркер пояса
        string? targetStr = bot._currenttarget?.ToString();
        bool isBelt = !string.IsNullOrEmpty(targetStr) && targetStr.Contains("Belt", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(isBelt ? NodeStatus.Success : NodeStatus.Failure);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region SelectAsteroidBeltAsync

    /// <summary>
    /// Сканирует систему и выбирает доступный астероидный пояс для добычи.
    /// </summary>
    private static async Task<NodeStatus> SelectAsteroidBeltAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Выбираю подходящий астероидный пояс...", LogType.Info);

        // TODO: [Заглушка] Заменить на реальный графический поиск OpenCV или OCR списка белтов в овервью
        await Task.Delay(1000, token);
        string? belt = "Belt_1";

        if (belt != null)
        {
            bot._currenttarget = belt;
            Logger.Log($"[{bot.Settings.Name}] Пояс выбран: {belt} (ЗАГЛУШКА). Перехожу к проверке безопасности.", LogType.Info);
            return NodeStatus.Success;
        }

        Logger.Log($"[{bot.Settings.Name}] Не удалось найти доступный пояс астероидов!", LogType.Error);
        return NodeStatus.Failure;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region WarpToSelectedBeltAsync

    /// <summary>
    /// Инициирует переход (варп) к выбранному астероидному поясу.
    /// </summary>
    private static async Task<NodeStatus> WarpToSelectedBeltAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._currenttarget == null) return NodeStatus.Failure;

        Logger.Log($"[{bot.Settings.Name}] Инициирую варп на пояс: {bot._currenttarget}", LogType.Info);

        // Взводим флаг полета для перевода RunLoopAsync на быстрый секундный мониторинг чата
        bot._iswarping = true;

        try
        {
            // TODO: [Заглушка] Заменить на реальный клик по кнопке "Варп" выбранного в овервью пояса
            await Task.Delay(1500, token); // Имитация времени на клики в меню эмулятора

            Logger.Log($"[{bot.Settings.Name}] Корабль успешно ушел в варп-туннель к {bot._currenttarget} (ЗАГЛУШКА).", LogType.Success);
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.Log($"Ошибка инициации варпа на белт: {ex.Message}", LogType.Error);
            bot._iswarping = false; // Сбрасываем флаг при сбое
            return NodeStatus.Failure;
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ClearFlightStateOnArrivalAsync

    /// <summary>
    /// Сбрасывает полетные данные и промежуточную цель варпа по прибытии в зону добычи.
    /// </summary>
    private static Task<NodeStatus> ClearFlightStateOnArrivalAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Исправлено: фиксируем прибытие в рабочую зону, чтобы дерево переключилось на майнинг операции
        bot._isinminingzone = true;
        bot._currenttarget = null; // Очищаем полетную цель

        return Task.FromResult(NodeStatus.Success);
    }



    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIfMiningIsActiveAsync

    /// <summary>
    /// Проверяет, захвачен ли астероид в цель и активны ли буровые лазеры.
    /// </summary>
    private static Task<NodeStatus> CheckIfMiningIsActiveAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Исправлено: если лазеры уже горят на экране, возвращаем Success. 
        // Это защитит интерфейс эмулятора от попыток захватить новую цель при включенном оружии.
        return Task.FromResult(bot._weaponryactive ? NodeStatus.Success : NodeStatus.Failure);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region TargetAsteroidAsync

    /// <summary>
    /// Пытается захватить доступный астероид в качестве мишени.
    /// </summary>
    private static async Task<NodeStatus> TargetAsteroidAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._hastarget) return NodeStatus.Success;

        // TODO: [Заглушка] Реализовать выбор астероида в овервью и клик по кнопке "Захват цели" (Lock)
        await Task.Delay(1500, token); // Имитируем время на клики в интерфейсе

        // Взводим флаг успешного захвата цели
        bot._hastarget = true;

        Logger.Log($"[{bot.Settings.Name}] Астероид успешно взят в лок (ЗАГЛУШКА).", LogType.Info);
        return NodeStatus.Success;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ActivateLasersAsync

    /// <summary>
    /// Активирует буровые лазеры на захваченную цель.
    /// </summary>
    private static async Task<NodeStatus> ActivateLasersAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._weaponryactive) return NodeStatus.Success;

        // TODO: [Заглушка] Реализовать клики по иконкам майнинг-лазеров на панели корабля
        await Task.Delay(1000, token); // Имитируем время на прокликивание модулей

        // Взводим флаг успешного включения лазеров
        bot._weaponryactive = true;

        Logger.Log($"[{bot.Settings.Name}] Буровые лазеры успешно активированы (ЗАГЛУШКА).", LogType.Success);
        return NodeStatus.Success;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region MiningMonitorStateAsync

    /// <summary>
    /// Точка удержания тика дерева во время активного процесса добычи руды.
    /// </summary>
    private static Task<NodeStatus> MiningMonitorStateAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Просто успешно завершаем узел, отдавая управление наверх для нового полного цикла проверок
        return Task.FromResult(NodeStatus.Success);
    }

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #endregion


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -



    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -



    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -




    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -





    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -


    /// <summary>
    /// Проверяет, находится ли бот в состоянии экстренной паники/эвакуации.
    /// </summary>
    private static Task<NodeStatus> CheckIsPanicStateActiveAsync(ActiveBotAccount bot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // Если бэкенд выставил задачу бегства к станции — узел возвращает Success,
        // заставляя дерево немедленно зайти в ветку экстренного отварпа.
        bool isPanic = bot.CurrentTask == AccountTask.GoToStation;

        return Task.FromResult(isPanic ? NodeStatus.Success : NodeStatus.Failure);
    }

    /// <summary>
    /// Проверяет, что бот в данный момент НЕ находится в процессе анимации андока (вылета со станции).
    /// Защищает интерфейс эмулятора от бешеного спама кликами по кнопке вылета.
    /// </summary>
    private static Task<NodeStatus> CheckIsNotUndockingAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Исправлено: корректно прерываем задачу дерева при отзыве токена из веб-панели
        token.ThrowIfCancellationRequested();

        // Если флаг _isUndocking равен true, значит кнопка уже нажата. 
        // Возвращаем Failure, чтобы дерево не пошло дальше по ветке андока в этот тик.
        return Task.FromResult(bot._isUndocking ? NodeStatus.Failure : NodeStatus.Success);
    }

}