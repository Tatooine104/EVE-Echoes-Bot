using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{
private static SelectorNode BuildLocalWatcherTree()
{
    return new SelectorNode("LocalWatcher Root",

        // =========================================================================
        // ПРИОРИТЕТ №1: БЛОК АВТОМАТИЧЕСКОЙ ПЛАНЕТАРНОЙ ДОБЫЧИ (Срабатывает раз в 8 часов)
        // =========================================================================
        new SequenceNode("Global Planet Mining Branch",
            // Проверяет условия (Включен ли флаг, на станции ли мы, прошли ли 8 часов).
            // Если условия не подошли — ветка мгновенно возвращает Failure, 
            // и Selector без задержек переходит к мониторингу локала.
            // Измените вызов узла на плоский синхронный возврат:
            new ActionNode("Check Planet Mining Conditions", (b, _) => Task.FromResult(CheckIfPlanetMiningTime(b))),
            new ActionNode("Execute Planet Mining Macro", ExecutePlanetMiningSequenceAsync)
        ),

        // =========================================================================
        // ПРИОРИТЕТ №2: КРИТИЧЕСКАЯ БЕЗОПАСНОСТЬ (Мониторинг локала КАЖДУЮ СЕКУНДУ)
        // =========================================================================
        new SelectorNode("Emergency Response Selector",
            new ActionNode("Is Already Evacuating Check", CheckIsPanicStateActiveAsync),

            new SequenceNode("Trigger Emergency Panic",
                new ActionNode("Check System Danger Status", CheckSystemDangerStatusAsync),
                new ActionNode("Execute Panic Evacuation", ExecutePanicEvacuationAsync)
            )
        ),

        // =========================================================================
        // ПРИОРИТЕТ №3: ВОССТАНОВЛЕНИЕ ИНТЕРФЕЙСА (Срабатывает ТОЛЬКО при сбое)
        // =========================================================================
        new SequenceNode("Look Around Branch",
            // Метод возвращает Success ТОЛЬКО если интерфейс сломан/потерян.
            // Если интерфейс в норме, метод возвращает Failure, и тяжелая диагностика НЕ запускается.
            new ActionNode("Check If Interface Lost", CheckIfInterfaceLostAsync),
            new ActionNode("Run Diagnostics", RunDiagnosticsAsync)
        ),

        // =========================================================================
        // ПРИОРИТЕТ №4: СТАНДАРТНЫЙ ЕЖЕСЕКУНДНЫЙ МОНИТОРИНГ ЭКРАНА И ОБНОВЛЕНИЕ СТАТУСА
        // =========================================================================
        new SequenceNode("Standard Security Monitor Branch",
            new ActionNode("Analyze Screen and Update State", AnalyzeScreenAndUpdateStateAsync),
            new ActionNode("Log Safe Status", LogSafeStatusAsync)
        )
    );
}


    private static Task<NodeStatus> LogSafeStatusAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // Защищаем запись стейта под локальным тиком воркера
        bot.CurrentTask = AccountTask.CheckSecurity;

        // Выводим информационный лог в консоль и веб-панель
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Плановый цикл мониторинга завершен. Система в безопасности.", LogType.Info);

        return Task.FromResult(NodeStatus.Success);
    }

    /// <summary>
    /// Проверяет статус опасности текущей солнечной системы.
    /// Полностью защищен от зависаний при отсутствии инициализации данных.
    /// </summary>
    private static Task<NodeStatus> CheckSystemDangerStatusAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // ЗАЩИТНЫЙ ФИЛЬТР: Если система еще не введена пользователем в UI панели,
        // мгновенно выходим со статусом Failure, не блокируя работу дерева и не вызывая дедлоков!
        if (string.IsNullOrWhiteSpace(bot.EVESystem) ||
            bot.EVESystem.Equals("Требуется ввод", StringComparison.OrdinalIgnoreCase) ||
            bot.EVESystem.Equals("???", StringComparison.OrdinalIgnoreCase))
        {
            // Система не инициализирована — панику не поднимаем, даем дереву идти дальше
            return Task.FromResult(NodeStatus.Failure);
        }

        try
        {
            // Вызываем проверку системы только если имя валидно
            // BUG MEDIUM — Состояние гонки (Race Condition) из-за избыточного чтения свойства под капотом. 
            // Метод `SystemSafetyManager.GetSystemState(bot.EVESystem)` лезет в `ConcurrentDictionary`. Это безопасно, но прямо внутри свойства `bot.EVESystem` у тебя всё еще крутится тяжелый синхронный `lock (_taskLock)`, который опрашивается веб-панелью. 
            // Чтобы полностью убрать микрофризы дерева во время параллельного пуллинга Kestrel, в начале этого метода лучше один раз вычитать строку в локальную переменную (например, `string currentSystem = bot.EVESystem;`) и дальше работать строго с ней.
            var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);

            if (systemState is null)
            {
                return Task.FromResult(NodeStatus.Failure);
            }

            bool isDangerous = systemState.IsSafe is false;
            return Task.FromResult(isDangerous ? NodeStatus.Success : NodeStatus.Failure);
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"[SAFETY ERROR] Сбой проверки локала для {bot.EVESystem}: {ex.Message}");
#endif
            return Task.FromResult(NodeStatus.Failure);
        }
    }


private static async Task<NodeStatus> ExecutePanicEvacuationAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot.CurrentTask != AccountTask.GoToStation)
        {
            bot.IsSaveLocal = false;

            // Исправлено: вызываем централизованный метод спасения всей системы ботов и отправки логов
            await bot.ExecuteEmergencyResponseAsync(isInitiator: true, token);
        }
        return NodeStatus.Success;
    }

    // Привели к единому стандарту именования аргументов асинхронных узлов дерева
    private static Task<NodeStatus> CheckIfInterfaceLostAsync(ActiveBotAccount bot, CancellationToken _)
    {
        return Task.FromResult(bot.CurrentTask == AccountTask.LookAround ? NodeStatus.Success : NodeStatus.Failure);
    }

    private static async Task<NodeStatus> RunDiagnosticsAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Интерфейс потерян или заблокирован. Ожидание восстановления экрана...", LogType.Warning);

        // Даем игре время на прогрузку оверлеев или закрытие зависших окон
        // BUG MEDIUM — Задержка блокирует реактивность дерева поведения. Метод `await Task.Delay(3000)` внутри `RunDiagnosticsAsync` заставляет узел висеть 3 секунды. В архитектуре реактивного Behavior Tree узлы должны возвращать `NodeStatus.Running` и управлять задержками через внешнее время или давать главному циклу `RunLoopAsync` переваривать тики, иначе бот на 3 секунды слепнет к изменениям в локал-чате. Но так как это ветка восстановления интерфейса, намертво логику паники это не заклинит.
        await Task.Delay(3000, token);
        bot.CurrentTask = AccountTask.CheckSecurity;

        return NodeStatus.Success;
    }

    private static async Task<NodeStatus> AnalyzeScreenAndUpdateStateAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Делаем зрение разговорчивым
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Запуск планового анализа экрана...", LogType.Test);

        SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

        switch (result)
        {
            case SecurityCheckResult.Safe:
                // BUG HIGH — Сохранение критического затыка для LowMiner на станции. Так как этот метод является общим («шаренным») для обоих сценариев в фабрике, жесткая принудительная перезапись `bot.CurrentTask = AccountTask.CheckSecurity;` при успешном сканировании — это главная причина, почему майнер бесконечно крутит проверку дока на станции и не взлетает. Для LocalWatcher это штатно. Но для LowMiner этот метод затирает текущую полетную задачу, заставляя дерево думать, что корабль должен заниматься исключительно мониторингом.
                // Чтобы исправить этот затык, перезаписывать задачу на CheckSecurity нужно ТОЛЬКО если текущий сценарий — это LocalWatcher, либо если корабль уже находится в космосе на добыче. Если это майнер в доке, его CurrentTask должен оставаться нетронутым (или переключаться на обслуживание/андок).
                if (bot.Settings.Script?.ToLower() == "localwatcher" || bot._inSpace)
                {
                    bot.CurrentTask = AccountTask.CheckSecurity;
                }

                bot.IsSaveLocal = true;
                return NodeStatus.Success;

            case SecurityCheckResult.Danger:
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] ОПАСНОСТЬ! Обнаружен угрожающий статус локала.", LogType.Test);
                bot.CurrentTask = AccountTask.CheckSecurity;
                bot.IsSaveLocal = false;
                await bot.ExecuteEmergencyResponseAsync(isInitiator: true, token);
                return NodeStatus.Failure;

            case SecurityCheckResult.Unknown:
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Статус экрана НЕОПРЕДЕЛЕН (Unknown). Переключаю задачу на LookAround.", LogType.Warning);
                bot.CurrentTask = AccountTask.LookAround;
                return NodeStatus.Failure;

            default:
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Сбой анализа безопасности (Default фолбек). Возврат к охране.", LogType.Error);
                bot.CurrentTask = AccountTask.CheckSecurity;
                return NodeStatus.Failure;
        }
    }


}

