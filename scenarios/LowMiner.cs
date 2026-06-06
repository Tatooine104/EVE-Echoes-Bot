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

    /// <summary>
    /// МАКРОС 1: Только открывает интерфейс планетарки из дока и перезапускает таймеры добычи.
    /// </summary>
    private static async Task<NodeStatus> ExecutePlanetResetOnlyAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] [Режим: Только интерфейс] Открытие меню планетарной добычи и перезапуск таймеров...", LogType.Info);

        // TODO: В будущем здесь будут ваши клики по меню без вылета из дока
        await Task.Delay(2000, token); 

        bot._planetassembly = DateTime.UtcNow;
        Logger.Log($"[{bot.Settings.Name}] Таймеры планетарки успешно перезапущены.", LogType.Info);
        return NodeStatus.Success;
    }

    /// <summary>
    /// МАКРОС 2: Полный цикл — перезапуск таймеров в меню + вылет на ПОС для физического сбора ресурсов.
    /// </summary>
    private static async Task<NodeStatus> ExecuteFullPlanetAndPosAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] [Режим: ПОС + Сбор] Перезапуск таймеров и инициация полета на ПОС для сбора ресурсов...", LogType.Info);

        // 1. Сначала делаем то же самое с интерфейсом
        // await bot.ResetPlanetTimersAsync(token);

        // 2. Затем логика работы с ПОСом (макрос сам сделает андок, варп, сбор и док обратно)
        // await bot.WarpToPOSAndCollectCargoAsync(token);
        
        await Task.Delay(4000, token); // Имитация долгого процесса с полетом

        bot._planetassembly = DateTime.UtcNow;
        Logger.Log($"[{bot.Settings.Name}] Обслуживание ПОСа и сбор планетарки завершены.", LogType.Info);
        return NodeStatus.Success;
    }



/*

    #region Miner

    /// <summary>
    /// Собирает дерево поведения для сценария «Шахтер» (Miner).
    /// Реализует строгую последовательность: Проверка локала -> Выход -> Выбор белта ->
    /// Проверка локала -> Варп -> Добыча/Мониторинг -> Возврат при угрозе/полном трюме -> Разгрузка.
    /// </summary>
    private static SelectorNode BuildMinerTree()
    {
        return new SelectorNode("Miner Root Selector",

            // =========================================================================
            // КРИТИЧЕСКИЙ КОНТРОЛЬ: ШАГ 7 и 8 (Постоянный перехват управления при опасности в космосе)
            // =========================================================================
            new SequenceNode("In-Flight Emergency Return",
                // 1. Проверяем контекст: мы должны быть строго в космосе!
                // Если мы в космосе, возвращаем Success, чтобы сиквенс шел дальше
                new ActionNode("Is In Space", async (bot, _) => bot._inSpace ? NodeStatus.Success : NodeStatus.Failure),

                // 2. Контролируем безопасность через новый enum
                new ActionNode("Is Hostile In Local", async (bot, token) =>
                {
                    // Сначала проверяем глобальный статус системы. Если КТО-ТО ДРУГОЙ уже объявил панику,
                    // нам не нужно тратить время на OCR, сразу возвращаем Success и улетаем!
                    var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);
                    if (systemState.IsSafe is false) return NodeStatus.Success;

                    // Если глобально всё чисто, проверяем сами своим OCR
                    SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

                    switch (result)
                    {
                        case SecurityCheckResult.Danger:
                            // Мы лично увидели врага! Взводим IsSaveLocal в false. 
                            // Это атомарно запустит RunAliChatWarningAsync и поднимет панику для ВСЕХ окон в системе.
                            bot.IsSaveLocal = false;
                            return NodeStatus.Success; // Возвращаем Success, чтобы лететь на станцию

                        case SecurityCheckResult.Unknown:
                            // Ослепли в космосе (всплыло окно/интерфейс заглючил). 
                            // В космосе оставаться вслепую опасно — переводим бота в режим "Осмотрись"
                            if (bot.CurrentTask != AccountTask.LookAround)
                            {
                                bot.CurrentTask = AccountTask.LookAround;
                                Logger.Log($"[{bot.Settings.Name}] Потеря интерфейса в космосе. Запуск диагностики...", LogType.Warning);
                                await bot.ExecuteLookAroundDiagnosticsAsync(token);
                            }
                            return NodeStatus.Failure; // Прерываем сиквенс паники, даем секунду осмотреться

                        case SecurityCheckResult.Safe:
                        default:
                            return NodeStatus.Failure; // Всё чисто, лететь на станцию не нужно
                    }
                }),

                // ШАГ 3: Действие (Выполнится ТОЛЬКО если шаг 1 и 2 вернули NodeStatus.Success)
                new ActionNode("Emergency Return To Station", async (bot, token) =>
                {
                    // Меняем статус на эвакуацию для UI
                    bot.CurrentTask = AccountTask.GoToStation;

                    Logger.Log($"[{bot.Settings.Name}] КРИТИЧЕСКАЯ УГРОЗА В КОСМОСЕ! Срочный уход в варп на домашнюю станцию.", LogType.Warning);

                    // Сбрасываем выбранный белт на случай паники, чтобы потом начать сначала
                    bot._currenttarget = null;

                    // Команда на варп и док
                    bool success = await bot.WarpAndDockToHomeStationAsync(token);
                    return success ? NodeStatus.Success : NodeStatus.Failure;
                })
            ),


            // =========================================================================
            // БЛОК СТАНЦИИ: ШАГИ 1, 2, 8 (если прилетели полные) и 9
            // =========================================================================
            new SequenceNode("Station Hub Branch",
                // Проверяем контекст: мы должны находиться в доке
                new ActionNode("Is Docked Check", async (bot, _) => !bot._inSpace ? NodeStatus.Success : NodeStatus.Failure),

                new SelectorNode("Station Actions",

                    // ВЕТКА ВЫГРУЗКИ: Если прилетели и рудный трюм полный — выгружаемся
                    new SequenceNode("Unload Cargo Sequence",
                        new ActionNode("Is Cargo Full Check", async (bot, token) =>
                        {
                            bool isFull = await bot.CheckIsCargoFullAsync(token);
                            return isFull ? NodeStatus.Success : NodeStatus.Failure;
                        }),
                        new ActionNode("Unload Ore", async (bot, token) =>
                        {
                            Logger.Log($"[{bot.Settings.Name}] Трюм заполнен. Выгрузка руды на склад станции.", LogType.Info);
                            return await bot.UnloadOreToHangarAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
                        })
                    ),

                    // ВЕТКА АНДОКА: Сработает, только если ветка выгрузки вернула Failure (трюм уже пуст)
                    new SequenceNode("Undock Monolithic Sequence",

                        // Предохранитель 1: На всякий случай проверяем, что трюм точно пустой перед вылетом
                        new ActionNode("Check Cargo Empty Before Undock", async (bot, token) =>
                        {
                            bool isFull = await bot.CheckIsCargoFullAsync(token);
                            return !isFull ? NodeStatus.Success : NodeStatus.Failure;
                        }),

                        // Предохранитель 2: Проверяем, что в системе безопасно перед выходом
                        new ActionNode("Check Safe Before Undock", async (bot, token) =>
                        {
                            bot.CurrentTask = AccountTask.CheckSecurity; // Обновляем статус для UI

                            SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

                            switch (result)
                            {
                                case SecurityCheckResult.Safe:
                                    Logger.Log($"[{bot.Settings.Name}] Локал чист. Безопасность подтверждена.", LogType.Info);
                                    return NodeStatus.Success; // Разрешаем сиквенсу идти дальше к самому андоку

                                case SecurityCheckResult.Danger:
                                    bot.IsSaveLocal = false;
                                    Logger.Log($"[{bot.Settings.Name}] В локале небезопасно (враги). Ожидаю на станции...", LogType.Warning);
                                    await Task.Delay(5000, token);
                                    return NodeStatus.Failure; // Прерываем сиквенс, до кнопки андока не дойдем

                                case SecurityCheckResult.Unknown:
                                    bot.CurrentTask = AccountTask.LookAround;
                                    Logger.Log($"[{bot.Settings.Name}] Статус системы неизвестен. Андок заблокирован, проверяю интерфейс...", LogType.Warning);
                                    await bot.ExecuteLookAroundDiagnosticsAsync(token);
                                    return NodeStatus.Failure; // Прерываем сиквенс

                                default:
                                    return NodeStatus.Failure;
                            }
                        }),

                        // ШАГ 2: Сам вылет из дока. Вызовется ТОЛЬКО если трюм пуст И в локале 100% безопасно
                        new ActionNode("Execute Undock", async (bot, token) =>
                        {
                            Logger.Log($"[{bot.Settings.Name}] В системе чисто. Выхожу из дока.", LogType.Info);

                            // Вызываем ваш реальный игровой метод андока
                            bool undockSuccess = await bot.UndockFromStationAsync(token);
                            return undockSuccess ? NodeStatus.Success : NodeStatus.Failure;
                        })
                    )
                )
            ),


            // =========================================================================
            // БЛОК КОСМОСА: ШАГИ 3, 4, 5, 6, 7, 8
            // =========================================================================
            new SequenceNode("Space Operations Branch",
                // Сюда бот доходит, только если он в космосе и самая верхняя ветка паники (Ветка 1) не сработала
                new SelectorNode("Space Workflow Selector",

                    // ШАГ 8: Если трюм забился прямо в процессе добычи -> летим домой
                    new SequenceNode("Return Full Cargo To Base",
                        new ActionNode("Is Cargo Full In Space", async (bot, token) =>
                        {
                            bool isFull = await bot.CheckIsCargoFullAsync(token);
                            return isFull ? NodeStatus.Success : NodeStatus.Failure;
                        }),
                        new ActionNode("Warp To Base", async (bot, token) =>
                        {
                            Logger.Log($"[{bot.Settings.Name}] Трюм заполнен. Возвращаюсь на станцию.", LogType.Info);
                            return await bot.WarpAndDockToHomeStationAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
                        })
                    ),

                    // ШАГИ 3, 4, 5: Логика выбора белта и перелета (работает, пока мы не в зоне добычи)
                    new SequenceNode("Flight To Belt Sequence",
                        // Если мы уже прилетели в зону добычи -> возвращаем Failure, чтобы пропустить эту ветку и перейти к майнингу
                        new ActionNode("Is NOT In Mining Zone", async (bot, _) => bot._isinzone ? NodeStatus.Failure : NodeStatus.Success),

                        // ПРЕДОХРАНИТЕЛЬ ВАРПА: Если корабль уже находится в режиме варпа/полёта — просто ждем окончания
                        new ActionNode("Check If Already Warping", async (bot, _) =>
                        {
                            // Если бот летит (например, проверяем по датчику скорости или анимации варпа)
                            if (bot._iswarping)
                            {
                                Logger.Log($"[{bot.Settings.Name}] Корабль в варпе. Ожидаем прибытия...", LogType.Test);
                                return NodeStatus.Success; // Возвращаем Success, чтобы завершить этот тик дерева без лишних действий
                            }
                            return NodeStatus.Failure; // Не в варпе — идем дальше к выбору/полету
                        }),

                        // Подселектор выбора белта: либо он уже выбран, либо выбираем заново
                        new SelectorNode("Belt Selection Selector",
                        new ActionNode("Is Belt Already Selected", async (bot, _) =>
                            bot._currenttarget != null ? NodeStatus.Success : NodeStatus.Failure),

                            // ШАГ 3: Выбираем астероидный пояс и запоминаем его в боте
                            new ActionNode("Select Asteroid Belt", async (bot, token) =>
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
                            })
                        ),

                        // ШАГ 4: Проверяем, что в системе безопасно ПЕРЕД варпом
                        new ActionNode("Check Safe Before Warp", async (bot, token) =>
                        {
                            bot.CurrentTask = AccountTask.CheckSecurity; // Обновляем статус для UI

                            // 1. Быстрый чек: если КТО-ТО ДРУГОЙ уже забил тревогу, мгновенно отменяем полет
                            var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);
                            if (systemState.IsSafe is false)
                            {
                                Logger.Log($"[{bot.Settings.Name}] Отмена варпа: получена глобальная тревога от другого окна!", LogType.Warning);
                                bot._currenttarget = null; // Сбрасываем цель, чтобы на следующем тике уйти в док
                                return NodeStatus.Failure;
                            }

                            // 2. Если глобально чисто, проверяем сами своим OCR
                            SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

                            switch (result)
                            {
                                case SecurityCheckResult.Safe:
                                    // В системе на 100% чисто — даем зеленый свет на варп в белт
                                    return NodeStatus.Success;

                                case SecurityCheckResult.Danger:
                                    // Враг обнаружен прямо перед прыжком!
                                    bot.IsSaveLocal = false; // Атомарно взводим панику для всех окон
                                    bot._currenttarget = null; // Сбрасываем цель копки
                                    Logger.Log($"[{bot.Settings.Name}] Отмена варпа: обнаружен противник в системе!", LogType.Warning);
                                    return NodeStatus.Failure; // Прерываем сиквенс полета

                                case SecurityCheckResult.Unknown:
                                    // Ослепли (например, мигнул экран перехода). 
                                    // Чтобы не потерять цель (белт) из-за случайного лага, НЕ сбрасываем _currenttarget.
                                    // Просто возвращаем Failure, чтобы сиквенс замер на один тик и бот попробовал снова.
                                    bot.CurrentTask = AccountTask.LookAround;
                                    Logger.Log($"[{bot.Settings.Name}] Предупреждение перед варпом: интерфейс не определен. Ожидание стабилизации...", LogType.Warning);
                                    await bot.ExecuteLookAroundDiagnosticsAsync(token);
                                    return NodeStatus.Failure;

                                default:
                                    return NodeStatus.Failure;
                            }
                        }),

                        // ШАГ 5: Варпаем на конкретный выбранный пояс
                        new ActionNode("Warp To Selected Belt", async (bot, token) =>
                        {
                            if (bot._currenttarget == null) return NodeStatus.Failure;

                            Logger.Log($"[{bot.Settings.Name}] Инициирую варп на пояс: {bot._currenttarget}", LogType.Info);

                            // Команда игре на варп
                            bool warpStarted = await bot.WarpToSpecificBeltAsync(bot._currenttarget, token);
                            return warpStarted ? NodeStatus.Success : NodeStatus.Failure;
                        })
                    ),

                    // ШАГ 6 и 7: Нахождение в белте, добыча и удержание состояния
                    new SequenceNode("Active Mining Sequence",
                        // Дополнительное действие: раз мы зашли в эту ветку, значит bot._isinzone == true. 
                        // Сбрасываем промежуточный таргет полета, он нам больше не нужен.
                        new ActionNode("Clear Flight State On Arrival", async (bot, _) =>
                        {
                            bot._currenttarget = null;
                            return NodeStatus.Success;
                        }),

                        // ШАГ 6: Начинаем добычу (Захват астероида в цель + Включение лазеров)
                        // Используем Selector, чтобы не кликать по кнопкам, если лазеры уже работают!
                        new SelectorNode("Targeting and Activation Selector",
                            // Проверяем: если цель есть И лазеры уже копают -> всё супер, узел пройден (Success)
                        new ActionNode("Check If Mining Is Active", async (bot, _) =>
                            (bot._hastarget && bot._weaponryactive) ? NodeStatus.Success : NodeStatus.Failure),

                            // Если что-то отключилось (астероид кончился) -> сиквенс включит новые
                            new SequenceNode("Lock And Mine Sequence",
                                new ActionNode("Target Asteroid", async (bot, token) =>
                                {
                                    if (bot._hastarget) return NodeStatus.Success;
                                    return await bot.TryTargetAsteroidAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
                                }),
                                new ActionNode("Activate Lasers", async (bot, token) =>
                                {
                                    if (bot._weaponryactive) return NodeStatus.Success;
                                    return await bot.ActivateLasersAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
                                })
                            )
                        ),

                        // ШАГ 7: Мониторинг наполнения трюма и локала
                        new ActionNode("Mining Monitor State", async (_, _) =>
                        {
                            // Просто удерживаем тик дерева. На следующем "тике" управление начнется сверху:
                            // проверится локал и забитость трюма.
                            return NodeStatus.Success;
                        })
                    )
                )
            )
        );
    }

    #endregion

*/

}