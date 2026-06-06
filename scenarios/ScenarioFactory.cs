using System;
using System.Collections.Generic;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

// [v] TODO 2026.06.01 Реализовать сценарий "miner" 
// [ ] TODO 2026.06.01 Реализовать проверку, перезапуск и запуск сбора планетарки на ПОС 

/// <summary>
/// Фабрика сценариев, отвечающая за сборку и инициализацию Деревьев поведения (Behavior Trees)
/// для игровых аккаунтов на основе выбранного в конфигурации профиля автоматизации.
/// </summary>
public static class ScenarioFactory
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CreateTree

    /// <summary>
    /// Собирает и возвращает корневой управляющий узел дерева поведения для указанного сценария.
    /// </summary>
    /// <param name="scenarioName">Имя целевого сценария (например, "localwatcher", "miner").</param>
    /// <returns>Объект <see cref="BehaviorNode"/>, представляющий собой корень дерева логики бота.</returns>
    public static BehaviorNode CreateTree(string scenarioName)
    {
        return scenarioName?.ToLower() switch
        {
            "localwatcher" => BuildLocalWatcherTree(),
            "miner"        => BuildMinerTree(),

            // Фолбек-дерево по умолчанию, если имя скрипта не распознано или пусто
            _ => BuildDefaultFallbackTree()
        };
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region LocalWatcher

    /// <summary>
    /// Собирает дерево поведения для сценария «Глаз» (LocalWatcher).
    /// Сценарий циклически проверяет безопасность локального чата и реагирует на угрозы.
    /// </summary>
    private static SelectorNode BuildLocalWatcherTree()
    {
        return new SelectorNode("LocalWatcher Root",

            // ВЕТКА ТРЕВОГИ (Враг в системе)
            new SequenceNode("Emergency Response Branch",
                new ActionNode("Check System Danger Status", async (bot, _) =>
                {
                    var isSafe = SystemSafetyManager.GetSystemState(bot.EVESystem).IsSafe;
                    return isSafe is false ? NodeStatus.Success : NodeStatus.Failure;
                }),
                new ActionNode("Execute Panic Evacuation", async (bot, _) =>
                {
                    if (bot.CurrentTask != AccountTask.GoToStation)
                    {
                        bot.CurrentTask = AccountTask.GoToStation;
                        bot.IsSaveLocal = false;
                    }
                    return NodeStatus.Success;
                })
            ),

            // ВЕТКА ДИАГНОСТИКИ (Потеря интерфейса / Осмотрись)
            new SequenceNode("Look Around Branch",
                new ActionNode("Check If Interface Lost", async (bot, _) =>
                    bot.CurrentTask == AccountTask.LookAround ? NodeStatus.Success : NodeStatus.Failure),

                new ActionNode("Run Diagnostics", async (bot, token) =>
                {
                    Logger.Log($"[{bot.Settings.Name}] Интерфейс заблокирован. Выполнение макроса очистки экрана...", LogType.Warning);
                    await bot.ExecuteLookAroundDiagnosticsAsync(token);
                    bot.CurrentTask = AccountTask.CheckSecurity;
                    return NodeStatus.Success;
                })
            ),

            // ОСНОВНАЯ РАБОЧАЯ ВЕТКА (Штатный скан экрана и распределение состояний)
            new SequenceNode("Standard Security Monitor Branch",
                new ActionNode("Analyze Screen and Update State", async (bot, token) =>
                {
                    bot.CurrentTask = AccountTask.CheckSecurity;

                    SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

                    switch (result)
                    {
                        case SecurityCheckResult.Safe:
                            bot.IsSaveLocal = true;
                            return NodeStatus.Success;

                        case SecurityCheckResult.Danger:
                            bot.IsSaveLocal = false;
                            return NodeStatus.Failure;

                        case SecurityCheckResult.Unknown:
                            bot.CurrentTask = AccountTask.LookAround;
                            return NodeStatus.Failure;

                        default:
                            return NodeStatus.Failure;
                    }
                }),

                // =========================================================================
                // НОВЫЙ УЗЕЛ: ИНТЕГРАЦИЯ МАКРОСОВ СКАНИРОВАНИЯ КОРАБЛЯ И СИСТЕМЫ
                // =========================================================================
                new ActionNode("Scan Metadata If Needed", async (bot, token) =>
                {
                    // 1. СКАНИРУЕМ СИСТЕМУ (Только если она еще не определена)
                    if (string.IsNullOrWhiteSpace(bot.EVESystem) || bot.EVESystem == "Требуется ввод" || bot.EVESystem == "Не определена")
                    {
                        // Проверяем: если меню корабля сейчас НЕ открывается, сканируем систему в спокойном состоянии интерфейса
                        if (bot.CurrentTask != AccountTask.CheckYourOwnState)
                        {
                            await bot.ScanCurrentSystemAsync();

                            // Если OCR вернул пустоту, временно фиксируем заглушку, чтобы не циклиться каждую секунду
                            if (string.IsNullOrWhiteSpace(bot.EVESystem) || bot.EVESystem == "Требуется ввод")
                            {
                                bot._eveSystem = "Не определена";
                            }

                            // Даем небольшую паузу после скана системы
                            await Task.Delay(1000, token);
                        }
                    }

                    // 2. СКАНИРУЕМ КОРАБЛЬ (Только если он еще не определен)
                    if (string.IsNullOrWhiteSpace(bot.EVEShip) || bot.EVEShip == "Требуется ввод" || bot.EVEShip == "Не определен")
                    {
                        // Переключаем таск, чтобы визуально заблокировать параллельный скан системы во время макроса
                        bot.CurrentTask = AccountTask.CheckYourOwnState;

                        // Запускаем макрос с кликами и закрытием через XButton
                        await bot.ScanCurrentShipAsync();

                        if (string.IsNullOrWhiteSpace(bot.EVEShip) || bot.EVEShip.Length < 3 || bot.EVEShip.Contains("ввод"))
                        {
                            bot._eveShip = "Не определен";
                        }

                        // ЖЕСТКИЙ ПРЕДОХРАНИТЕЛЬ: После закрытия меню хангара кнопкой XButton
                        // даем игре честные 5 секунд, чтобы оверлей полностью скрылся, 
                        // и экран вернулся в исходное чистое состояние!
                        await Task.Delay(5000, token);

                        // Возвращаем штатный таск
                        bot.CurrentTask = AccountTask.CheckSecurity;
                    }

                    return NodeStatus.Success;
                }),
                // =========================================================================

                // Этот узел выполнится, ТОЛЬКО если сканирование метаданных завершилось успехом
                new ActionNode("Log Safe Status", async (bot, _) =>
                {
                    bot.CurrentTask = AccountTask.CheckYourOwnState;
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Плановый цикл мониторинга завершен. Система в безопасности.", LogType.Test);
                    return NodeStatus.Success;
                })
            )
        );
    }



    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region DefaultFallback

    private static ActionNode BuildDefaultFallbackTree()
    {
        return new ActionNode("Default Fallback Action", (_, _) => Task.FromResult(NodeStatus.Success));
    }

    #endregion

}