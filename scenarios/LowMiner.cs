using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Собирает дерево поведения для сценария «Шахтер в лоу-секах» (LowMiner).
    /// Сценарий управляет полным циклом добычи руды, логистикой на станцию и безопасностью.
    /// </summary>
    private static SelectorNode BuildMinerTree()
    {
        return new SelectorNode("LowMiner Root",

            // =========================================================================
            // БЛОК СТАНЦИИ: Нахождение в доке, выгрузка и безопасный выход
            // =========================================================================
            new SequenceNode("Station Hub Branch",
                new ActionNode("Is Docked Check", CheckIsDockedAsync),

                new SelectorNode("Station Actions",

                    // ВЕТКА ВЫГРУЗКИ: Выгружаем руду, если трюм полный
                    new SequenceNode("Unload Cargo Sequence",
                        new ActionNode("Is Cargo Full Check", CheckIsCargoFullAsync),
                        new ActionNode("Unload Ore", ExecuteUnloadOreAsync)
                    ),

                    // ВЕТКА АНДОКА: Вылетаем, если трюм пуст и в системе безопасно
                    new SequenceNode("Undock Monolithic Sequence",
                        new ActionNode("Check Cargo Empty Before Undock", CheckIsCargoEmptyAsync),
                        new ActionNode("Check Safe Before Undock", EvaluateSystemSecurityAsync), // Переиспользуемый метод
                        new ActionNode("Execute Undock", ExecuteUndockAsync)
                    )
                )
            ),

            // =========================================================================
            // БЛОК КОСМОСА: Полеты, навигация, варп и процесс копки
            // =========================================================================
            new SequenceNode("Space Operations Branch",

                new SelectorNode("Space Workflow Selector",

                    // ВОЗВРАТ НА БАЗУ: Если трюм заполнился во время добычи
                    new SequenceNode("Return Full Cargo To Base",
                        new ActionNode("Is Cargo Full In Space", CheckIsCargoFullAsync),
                        new ActionNode("Warp To Base", WarpToBaseAsync)
                    ),

                    // ПЕРЕЛЕТ НА БЕЛТ: Выбор астероидного пояса и прыжок к нему
                    new SequenceNode("Flight To Belt Sequence",
                        new ActionNode("Is NOT In Mining Zone", CheckIsNotInMiningZoneAsync),
                        new ActionNode("Check If Already Warping", CheckIfAlreadyWarpingAsync),

                        // Выбор пояса (если еще не выбран)
                        new SelectorNode("Belt Selection Selector",
                            new ActionNode("Is Belt Already Selected", CheckIsBeltAlreadySelectedAsync),
                            new ActionNode("Select Asteroid Belt", SelectAsteroidBeltAsync)
                        ),

                        // Проверка безопасности перед прыжком и сам варп
                        new ActionNode("Check Safe Before Warp", EvaluateSystemSecurityAsync), // Переиспользуемый метод
                        new ActionNode("Warp To Selected Belt", WarpToSelectedBeltAsync)
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


    /// <summary>
    /// Проверяет, находится ли корабль внутри дока станции.
    /// </summary>
    private static Task<NodeStatus> CheckIsDockedAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Инвертируем флаг нахождения в космосе
        return Task.FromResult(!bot._inSpace ? NodeStatus.Success : NodeStatus.Failure);
    }

    /// <summary>
    /// Проверяет, заполнен ли рудный трюм корабля.
    /// </summary>
    private static async Task<NodeStatus> CheckIsCargoFullAsync(ActiveBotAccount bot, CancellationToken token)
    {
        bool isFull = await bot.CheckIsCargoFullAsync(token);
        return isFull ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Проверяет, пуст ли рудный трюм корабля (необходим перед вылетом).
    /// </summary>
    private static async Task<NodeStatus> CheckIsCargoEmptyAsync(ActiveBotAccount bot, CancellationToken token)
    {
        bool isFull = await bot.CheckIsCargoFullAsync(token);
        return !isFull ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Выполняет разгрузку руды на склад текущей станции.
    /// </summary>
    private static async Task<NodeStatus> ExecuteUnloadOreAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Трюм заполнен. Выгрузка руды на склад станции.", LogType.Info);

        bool unloadSuccess = await bot.UnloadOreToHangarAsync(token);
        return unloadSuccess ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Выполняет команду выхода из дока станции (андок) в космос.
    /// </summary>
    private static async Task<NodeStatus> ExecuteUndockAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] В системе чисто. Выхожу из дока.", LogType.Info);

        bool undockSuccess = await bot.UndockFromStationAsync(token);
        return undockSuccess ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Выполняет варп и стыковку (док) на домашнюю станцию для разгрузки.
    /// </summary>
    private static async Task<NodeStatus> WarpToBaseAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Трюм заполнен. Возвращаюсь на станцию.", LogType.Info);

        bool success = await bot.WarpAndDockToHomeStationAsync(token);
        return success ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Проверяет, что корабль еще НЕ находится непосредственно в зоне добычи (на белте).
    /// </summary>
    private static Task<NodeStatus> CheckIsNotInMiningZoneAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return Task.FromResult(!bot._isinzone ? NodeStatus.Success : NodeStatus.Failure);
    }

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

    /// <summary>
    /// Проверяет, выбран ли уже астероидный пояс в качестве текущей цели движения.
    /// </summary>
    private static Task<NodeStatus> CheckIsBeltAlreadySelectedAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return Task.FromResult(bot._currenttarget != null ? NodeStatus.Success : NodeStatus.Failure);
    }

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

    /// <summary>
    /// Сбрасывает полетные данные и промежуточную цель варпа по прибытии в зону добычи.
    /// </summary>
    private static Task<NodeStatus> ClearFlightStateOnArrivalAsync(ActiveBotAccount bot, CancellationToken token)
    {
        bot._currenttarget = null;
        return Task.FromResult(NodeStatus.Success);
    }

    /// <summary>
    /// Проверяет, захвачен ли астероид в цель и активны ли буровые лазеры.
    /// </summary>
    private static Task<NodeStatus> CheckIfMiningIsActiveAsync(ActiveBotAccount bot, CancellationToken token)
    {
        return Task.FromResult((bot._hastarget && bot._weaponryactive) ? NodeStatus.Success : NodeStatus.Failure);
    }

    /// <summary>
    /// Пытается захватить доступный астероид в качестве мишени.
    /// </summary>
    private static async Task<NodeStatus> TargetAsteroidAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._hastarget) return NodeStatus.Success;
        return await bot.TryTargetAsteroidAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Активирует буровые лазеры на захваченную цель.
    /// </summary>
    private static async Task<NodeStatus> ActivateLasersAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._weaponryactive) return NodeStatus.Success;
        return await bot.ActivateLasersAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>
    /// Точка удержания тика дерева во время активного процесса добычи руды.
    /// </summary>
    private static Task<NodeStatus> MiningMonitorStateAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Просто успешно завершаем узел, отдавая управление наверх для нового полного цикла проверок
        return Task.FromResult(NodeStatus.Success);
    }

}