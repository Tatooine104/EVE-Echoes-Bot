using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{

    // Поле внутри класса ActiveBotAccount для отслеживания состояния вылета
    public bool _isUndocking;


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region BuildMinerTree

    /// <summary>
    /// Собирает дерево поведения для сценария «Шахтер в лоу-секах» (LowMiner).
    /// Сценарий управляет полным циклом добычи руды, логистикой на станцию и безопасностью.
    /// </summary>
    private static SelectorNode BuildMinerTree()
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

        using (screenshot) // Гарантируем очистку unmanaged памяти OpenCV
        {
            string pathImg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgUndock1.png");

            // Ищем шаблон кнопки "Undock" с точностью 85%
            Point? foundPos = await Task.Run(() => Tools.FindTemplateInRegion(screenshot, pathImg, safeRegion, 0.85), token);

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

        using (screenshot)
        {
            string pathCargo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgCargoFold100.png");

            // Ищем трюм синхронно в памяти
            Point? foundCargo = Tools.FindTemplateInRegion(screenshot, pathCargo, safeRegion, 0.85);
            if (foundCargo.HasValue) return NodeStatus.Success;

            // 2. Если не нашли трюм, ищем флаг флота, который мог его перекрыть
            string pathFleet = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgFleetFlags.png");
            Point? foundFleet = Tools.FindTemplateInRegion(screenshot, pathFleet, safeRegion, 0.85);

            if (!foundFleet.HasValue) return NodeStatus.Failure; // Ни трюма, ни флага — значит не полон
        }

        // 3. Флаг нашли — сдвигаем панель влево на 50 пикселей от точки FastMenu1
        await bot.ScrollLeftAsync(GameUI.FastMenu1, 50, token);
        await Task.Delay(600, token); // Ждем завершения анимации свайпа

        // 4. Повторный снимок для проверки трюма после сдвига
        var (retryScreenshot, retryRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.FastMenu, token);
        if (retryScreenshot == null) return NodeStatus.Failure;

        using (retryScreenshot)
        {
            string pathCargo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgCargoFold100.png");
            Point? foundCargo = Tools.FindTemplateInRegion(retryScreenshot, pathCargo, retryRegion, 0.85);

            return foundCargo.HasValue ? NodeStatus.Success : NodeStatus.Failure;
        }
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

        using (screenshot) // Гарантируем очистку unmanaged памяти OpenCV
        {
            string pathCargoEmpty = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgCargoHold0.png");

            // Ищем шаблон пустого трюма синхронно в памяти
            Point? foundCargoEmpty = Tools.FindTemplateInRegion(screenshot, pathCargoEmpty, safeRegion, 0.85);

            if (foundCargoEmpty.HasValue)
            {
                // Шаблон нуля найден -> трюм пуст
                return NodeStatus.Success;
            }

            // Шаблон не найден -> трюм не пуст (или меню перекрыто)
            return NodeStatus.Failure;
        }
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
        ReadOnlySpan<(GameUI Element, int DelayMs)> initialSteps = [
            (GameUI.FastMenu1, 800),
            (GameUI.CollapseStation, 800)
        ];

        foreach (var (element, delayMs) in initialSteps)
        {
            // Исправлено: передан обязательный токен
            await bot.ClickToAsync(element, token);
            await Task.Delay(delayMs, token);
        }

        // ========================================================
        // ШАГ 3: ДИНАМИЧЕСКИЙ ПОИСК ИКОНКИ РУДНОГО ОТСЕКА
        // ========================================================
        var (screenshot, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.LocalChat, token);
        if (screenshot == null) return NodeStatus.Failure;

        // Гарантируем очистку памяти OpenCV через упрощенный using declaration
        using var screenshotScope = screenshot;

        Point? foundOreHold;
        string pathOreHold = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgOreHold.png");

        // Выносим тяжелый поиск OpenCV в фоновый поток пула
        var currentScreenshot = screenshot;
        foundOreHold = await Task.Run(() => Tools.FindTemplateInRegion(currentScreenshot, pathOreHold, safeRegion, 0.85), token);

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
        ReadOnlySpan<(GameUI Element, int DelayMs)> finalSteps = [
            (GameUI.SelectAll, 600),
            (GameUI.ItemHangar, 1500), 
            (GameUI.XButton, 0)
        ];

        foreach (var (element, delayMs) in finalSteps)
        {
            // Исправлено: передан обязательный токен
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
        Logger.Log($"[{bot.Settings.Name}] В системе чисто. Выхожу из дока.", LogType.Info);

        bool undockSuccess = await bot.UndockFromStationAsync(token);
        return undockSuccess ? NodeStatus.Success : NodeStatus.Failure;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region WarpToBaseAsync

    /// <summary>
    /// Выполняет варп и стыковку (док) на домашнюю станцию для разгрузки.
    /// </summary>
    private static async Task<NodeStatus> WarpToBaseAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Трюм заполнен. Возвращаюсь на станцию.", LogType.Info);

        bool success = await bot.WarpAndDockToHomeStationAsync(token);
        return success ? NodeStatus.Success : NodeStatus.Failure;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsNotInMiningZoneAsync

    /// <summary>
    /// Проверяет, что корабль еще НЕ находится непосредственно в зоне добычи (на белте).
    /// </summary>
    private static Task<NodeStatus> CheckIsNotInMiningZoneAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return Task.FromResult(!bot._isinzone ? NodeStatus.Success : NodeStatus.Failure);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIfAlreadyWarpingAsync

    /// <summary>
    /// Проверяет, находится ли корабль в процессе варпа.
    /// Помогает удерживать тик дерева, не совершая лишних действий до прилета.
    /// </summary>
    private static Task<NodeStatus> CheckIfAlreadyWarpingAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._iswarping)
        {
            Logger.Log($"[{bot.Settings.Name}] Корабль в варпе. Ожидаем прибытия...", LogType.Test);
            return Task.FromResult(NodeStatus.Success);
        }
        return Task.FromResult(NodeStatus.Failure);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsBeltAlreadySelectedAsync

    /// <summary>
    /// Проверяет, выбран ли уже астероидный пояс в качестве текущей цели движения.
    /// </summary>
    private static Task<NodeStatus> CheckIsBeltAlreadySelectedAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return Task.FromResult(bot._currenttarget != null ? NodeStatus.Success : NodeStatus.Failure);
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

        var belt = await bot.ScanAndSelectAvailableBeltAsync(token);
        if (belt != null)
        {
            bot._currenttarget = belt;
            Logger.Log($"[{bot.Settings.Name}] Пояс выбран: {belt}. Перехожу к проверке безопасности.", LogType.Info);
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

        bool warpStarted = await bot.WarpToSpecificBeltAsync(bot._currenttarget, token);
        return warpStarted ? NodeStatus.Success : NodeStatus.Failure;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ClearFlightStateOnArrivalAsync

    /// <summary>
    /// Сбрасывает полетные данные и промежуточную цель варпа по прибытии в зону добычи.
    /// </summary>
    private static Task<NodeStatus> ClearFlightStateOnArrivalAsync(ActiveBotAccount bot, CancellationToken token)
    {
        bot._currenttarget = null;
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
        return Task.FromResult((bot._hastarget && bot._weaponryactive) ? NodeStatus.Success : NodeStatus.Failure);
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
        return await bot.TryTargetAsteroidAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
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
        return await bot.ActivateLasersAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
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

    #region Undock 

    /// <summary>
    /// ДЕЙСТВИЕ: Производит отстыковку (андок) корабля от станции.
    /// </summary>
    public async Task<bool> UndockFromStationAsync(CancellationToken token)
    {
        await Task.Delay(200, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} запускает процедуру андока.", LogType.Test);

        // Для теста переводим стейт корабля в космос
        _inSpace = true;
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Warp And Dock

    /// <summary>
    /// ДЕЙСТВИЕ: Инициирует варп и автоматический док на домашнюю станцию/цитадель.
    /// </summary>
    public async Task<bool> WarpAndDockToHomeStationAsync(CancellationToken token)
    {
        // Симулируем задержку на сетевой запрос или клик по интерфейсу
        await Task.Delay(100, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} выполняет команду: Варп и Док на домашнюю станцию.", LogType.Test);

        // Для теста принудительно переводим стейт в док (космос = false)
        _inSpace = false;
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Check Cargo

    /// <summary>
    /// ДЕЙСТВИЕ: Проверяет текущую заполненность рудного трюма корабля.
    /// </summary>
    public async Task<bool> CheckIsCargoFullAsync(CancellationToken token)
    {
        await Task.Delay(50, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} проверяет заполненность трюма.", LogType.Test);

        // По умолчанию возвращаем false, чтобы бот не уходил в бесконечный цикл разгрузки на старте
        return false;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Unload Ore

    /// <summary>
    /// ДЕЙСТВИЕ: Переносит всю добытую руду из трюма корабля на склад станции.
    /// </summary>
    public async Task<bool> UnloadOreToHangarAsync(CancellationToken token)
    {
        await Task.Delay(500, token); // Выгрузка обычно занимает чуть больше времени
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} успешно разгрузил руду на склад станции.", LogType.Test);
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -


    #region Select Belt

    /// <summary>
    /// ДЕЙСТВИЕ: Сканирует овервью или меню игры, выбирает подходящий пояс астероидов.
    /// </summary>
    /// <returns>Возвращает объект (строку) с названием пояса, либо null, если ничего не найдено.</returns>
    public async Task<object?> ScanAndSelectAvailableBeltAsync(CancellationToken token)
    {
        await Task.Delay(150, token);
        const string mockBeltName = "Asteroid Belt Cluster-Alpha";
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} отсканировал локацию и выбрал: {mockBeltName}.", LogType.Test);
        return mockBeltName;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Warp To Belt

    /// <summary>
    /// ДЕЙСТВИЕ: Инициирует разгон и переход в варп на конкретно выбранный пояс астероидов.
    /// </summary>
    public async Task<bool> WarpToSpecificBeltAsync(object? targetBelt, CancellationToken token)
    {
        await Task.Delay(100, token);
        string beltName = targetBelt?.ToString() ?? "Unknown Belt";
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} отправлен в варп на точку: {beltName}.", LogType.Test);
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Try Target Asteroid

    /// <summary>
    /// ДЕЙСТВИЕ: Находит ближайший астероид в овервью космоса и берет его в захват (Lock Target).
    /// </summary>
    public async Task<bool> TryTargetAsteroidAsync(CancellationToken token)
    {
        await Task.Delay(100, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} захватил астероид в цель.", LogType.Test);
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Activate Lasers

    /// <summary>
    /// ДЕЙСТВИЕ: Включает буровые/шахтерские лазеры (модули) корабля для начала добычи.
    /// </summary>
    public async Task<bool> ActivateLasersAsync(CancellationToken token)
    {
        await Task.Delay(150, token); // Имитация задержки на клик по модулю
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} отправил команду на активацию буровых лазеров.", LogType.Test);

        // Здесь в будущем будет выставляться флаг _weaponryactive = true
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Legacy FSM Methods (Deprecated)

    /// <summary>
    /// Устаревший метод получения плоских списков задач. Оставлен для временной обратной совместимости.
    /// </summary>
    [Obsolete("Используйте метод CreateTree для получения полноценного дерева поведения.")]
    public List<string> GetDefaultTasks(string scenarioName)
    {
        return scenarioName?.ToLower() switch
        {
            "localwatcher" => ["CheckSecurity"],
            _ => ["CheckYourOwnState"]
        };
    }

    #endregion

    /// <summary>
    /// Логика "Осмотрись": выполняет аппаратно-независимые клики для закрытия случайных поп-апов,
    /// окон наград или рекламы, мешающих обзору OCR.
    /// </summary>
    internal async Task ExecuteLookAroundDiagnosticsAsync(CancellationToken token)
    {
        Log($"[{Settings.Name}] Запуск макроса 'Осмотрись': попытка восстановить интерфейс.", LogType.Info);

        try
        {
            // 1. Нажимаем клавишу ESC через ADB, чтобы закрыть любые случайные окна
            // (Параметр KEYCODE_ESCAPE в Android равен 111, либо используйте вашу обертку Tools)
            // Tools.SendKeyEvent(111, Settings.AdbPort); 

            // 2. Делаем небольшую паузу, чтобы интерфейс успел отреагировать
            await Task.Delay(1500, token);

            // 3. Делаем клик по «пустому» безопасному месту экрана, где обычно нет кнопок,
            // чтобы сбросить фокус с возможных зависших элементов интерфейса
            // Tools.SmartClick(100, 100, minSec: 0, maxSec: 1, offset: 0, adbPort: Settings.AdbPort);

            // await Task.Delay(1000, token);
        }
        catch (Exception ex)
        {
            Log($"[{Settings.Name}] Ошибка при выполнении диагностики экрана: {ex.Message}", LogType.Error);
        }
    }

    // private static async Task<NodeStatus> ExecuteUndockAsync(ActiveBotAccount bot, CancellationToken token)
    // {
    //     // Шаг 1: Инициация действия
    //     if (!bot._isUndocking)
    //     {
    //         Logger.Log($"[{bot.Settings.Name}] Нажимаю кнопку Андока. Начинаю ожидание выхода в космос...", LogType.Info);
    //         await bot.ClickToAsync(GameUI.UndockButton, token);

    //         bot._isUndocking = true;
    //         bot._actionTimeout = DateTime.Now.AddSeconds(30); // Жесткий таймаут защиты — 30 сек
    //         return NodeStatus.Running;
    //     }

    //     // Шаг 2: Контроль на последующих тиках дерева
    //     if (bot._inSpace)
    //     {
    //         Logger.Log($"[{bot.Settings.Name}] Корабль успешно вышел в космос! Андок завершен.", LogType.Success);
    //         bot._isUndocking = false; // Сбрасываем флаг ожидания
    //         return NodeStatus.Success; // Позволяет дереву перейти к космическим операциям
    //     }

    //     // Защита от бесконечного зависания в анимации (например, если игра вылетела)
    //     if (DateTime.Now > bot._actionTimeout)
    //     {
    //         Logger.Log($"[{bot.Settings.Name}] Критическая ошибка: Превышен таймаут ожидания андока (30с).", LogType.Error);
    //         bot._isUndocking = false;
    //         return NodeStatus.Failure;
    //     }

    //     // Корабль все еще летит по трубе станции — возвращаем Running
    //     return NodeStatus.Running;
    // }

    // private static async Task<NodeStatus> WarpToSelectedBeltAsync(ActiveBotAccount bot, CancellationToken token)
    // {
    //     // Шаг 1: Инициация прыжка
    //     if (!bot._isWarping)
    //     {
    //         Logger.Log($"[{bot.Settings.Name}] Инициирую варп на выбранный астероидный пояс.", LogType.Info);
    //         await bot.ClickToAsync(GameUI.WarpButton, token);

    //         bot._isWarping = true;
    //         bot._actionTimeout = DateTime.Now.AddSeconds(60); // Максимальное время полета
    //         return NodeStatus.Running;
    //     }

    //     // Шаг 2: Контроль состояния (Этот блок вызывается каждую секунду из RunLoopAsync)
    //     // Мы проверяем графический флаг, который выставляется в вашем модуле анализа экрана OpenCV
    //     if (bot._isWarpingActiveGraphicFlag) 
    //     {
    //         // Пока на экране горит анимация варпа, дерево не идет дальше, 
    //         // но RunLoopAsync проверяет этот узел каждую секунду и контролирует локал чат!
    //         return NodeStatus.Running; 
    //     }

    //     // Шаг 3: Прибытие на место
    //     Logger.Log($"[{bot.Settings.Name}] Корабль вышел из варпа на белте. Начинаем копку.", LogType.Success);
    //     bot._isWarping = false;
    //     bot.IsInMiningZone = true; // Выставляем флаг для дерева
    //     return NodeStatus.Success;
    // }


    /// <summary>
    /// Проверяет, находится ли бот в состоянии экстренной паники/эвакуации.
    /// </summary>
    private static Task<NodeStatus> CheckIsPanicStateActiveAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromResult(NodeStatus.Failure);
        }

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
        if (token.IsCancellationRequested)
        {
            return Task.FromResult(NodeStatus.Failure);
        }

        // Если флаг _isUndocking равен true, значит кнопка уже нажата. 
        // Возвращаем Failure, чтобы дерево не пошло дальше по ветке андока в этот тик.
        return Task.FromResult(bot._isUndocking ? NodeStatus.Failure : NodeStatus.Success);
    }


}