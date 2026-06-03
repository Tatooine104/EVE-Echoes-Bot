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

            // ВЕТКА ТРЕВОГИ: Сработает, только если проверка безопасности обнаружила угрозу
            new SequenceNode("Emergency Response Branch",

                // Условие: Запускаем сканирование чата. 
                new ActionNode("Scan Local Chat", async (bot, token) =>
                {
                    // Выставляем статус проверки безопасности для логов и DTO
                    bot.CurrentTask = AccountTask.CheckSecurity;

                    bool isSafe = await bot.CheckSecurityStatusAsync(token);
                    return isSafe ? NodeStatus.Failure : NodeStatus.Success;
                }),

                // Действие: Каскадное оповещение окон и паника
                new ActionNode("Trigger System Emergency", async (bot, token) =>
                {
                    // Меняем статус на отправку варнинга
                    bot.CurrentTask = AccountTask.SendAliChatWarning;

                    // Проверяем статус через менеджер безопасности
                    var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);

                    // Если это окно первым обнаружило угрозу, отправляем макрос в чат
                    if (systemState.IsSafe is false)
                    {
                        Logger.Log($"[{bot.Settings.Name}] Обнаружен противник! Активация цепочки кликов оповещения.", LogType.Warning);
                        await bot.RunAliChatWarningAsync(token);
                    }

                    bot.ExecuteEmergencyResponse(isInitiator: false);
                    return NodeStatus.Success;
                })
            ),

            // ВЕТКА МИРНОГО ПРОСТОЯ: Сработает, если верхняя ветка тревоги вернула Failure (то есть в системе всё чисто)
            new SequenceNode("Peaceful Idle Branch",
                new ActionNode("Log Safe Status", async (bot, _) =>
                {
                    // В мирное время переводим бота в базовый режим простоя
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
                // Проверяем контекст: мы должны быть в космосе (если в доке — ветка пропускается)
                new ActionNode("Is In Space", async (bot, _) => bot._inSpace ? NodeStatus.Failure : NodeStatus.Success),

                // ШАГ 7/8: Постоянно контролируем безопасность. Если ОПАСНОСТЬ -> Success (идем дальше)
                new ActionNode("Is Hostile In Local", async (bot, token) =>
                {
                    bool isSafe = await bot.CheckSecurityStatusAsync(token);
                    return !isSafe ? NodeStatus.Success : NodeStatus.Failure;
                }),

                // Действие: Экстренный возврат на станцию
                new ActionNode("Emergency Return To Station", async (bot, token) =>
                {
                    Logger.Log($"[{bot.Settings.Name}] КРИТИЧЕСКАЯ УГРОЗА! Срочный возврат на станцию.", LogType.Warning);

                    // Сбрасываем выбранный белт на случай паники, чтобы потом начать сначала
                    bot._currenttarget = null;

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

                    // ШАГ 9: Если прилетели и рудный трюм полный — выгружаемся
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

                    // ШАГ 1 и 2: Трюм пустой, готовы к вылету
                    new SequenceNode("Undock Sequence",
                        // ШАГ 1: Проверяем, что в системе безопасно перед выходом
                        new ActionNode("Check Safe Before Undock", async (bot, token) =>
                        {
                            bool isSafe = await bot.CheckSecurityStatusAsync(token);
                            if (!isSafe)
                            {
                                // ШАГ 1.2: Нет - ждем в доке (возвращаем Success, чтобы завершить тик и не идти к андоку)
                                Logger.Log($"[{bot.Settings.Name}] В локале небезопасно. Ожидаю в доке...", LogType.Warning);
                                await System.Threading.Tasks.Task.Delay(5000, token); // Защитная пауза перед следующим тиком
                                return NodeStatus.Success;
                            }
                            // ШАГ 1.1: Да - переходим к следующему шагу сиквенса
                            return NodeStatus.Success;
                        }),

                        // Дополнительный предохранитель: не андокаться, если трюм всё еще полный
                        new ActionNode("Check Cargo Empty Before Undock", async (bot, token) =>
                        {
                            bool isFull = await bot.CheckIsCargoFullAsync(token);
                            return !isFull ? NodeStatus.Success : NodeStatus.Failure;
                        }),

                        // ШАГ 2: Выходим из дока
                        new ActionNode("Undock", async (bot, token) =>
                        {
                            Logger.Log($"[{bot.Settings.Name}] В системе чисто. Выхожу из дока.", LogType.Info);
                            return await bot.UndockFromStationAsync(token) ? NodeStatus.Success : NodeStatus.Failure;
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
                            bool isSafe = await bot.CheckSecurityStatusAsync(token);
                            if (!isSafe)
                            {
                                // ШАГ 4.2: Небезопасно — сбрасываем цель. На следующем тике сработает верхний блок паники
                                bot._currenttarget = null;
                                return NodeStatus.Failure;
                            }
                            return NodeStatus.Success;
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