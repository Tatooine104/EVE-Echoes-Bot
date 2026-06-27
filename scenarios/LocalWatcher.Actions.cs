using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{

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
        await Task.Delay(3000, token);
        bot.CurrentTask = AccountTask.CheckSecurity;

        return NodeStatus.Success;
    }

    private static async Task<NodeStatus> AnalyzeScreenAndUpdateStateAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Делаем зрение разговорчивым
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}] Запуск планового анализа экрана...", LogType.Warning);

        SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

        switch (result)
        {
            case SecurityCheckResult.Safe:
                // BUG HIGH — Логическая петля, удерживающая бота в вечном сканировании. 
                // Когда `CheckSecurityStatusAsync` возвращает `Safe` (в локале чисто), этот метод жестко перезаписывает:
                // `bot.CurrentTask = AccountTask.CheckSecurity;`
                // Да, это переключает статус в режим охраны, но если бот `Lana Muc` (`LowMiner`) находится на станции, 
                // ему НУЖНО переходить к андоку и майнингу! Из-за того, что здесь принудительно выставляется `CheckSecurity`, 
                // на следующем тике Дерево Поведения сценария `LowMiner` снова видит этот статус, считает, что нужно 
                // продолжать только охрану, и никогда не пускает управление в ветку `BuildMinerTree()`.
                // Назначение `CurrentTask` при статусе `Safe` должно зависеть от того, какая задача стоит перед кораблем!
                bot.CurrentTask = AccountTask.CheckSecurity;
                bot.IsSaveLocal = true;
                // Возвращаем Success, чтобы Sequence пошел дальше к LogSafeStatusAsync
                return NodeStatus.Success;

            case SecurityCheckResult.Danger:
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] ОПАСНОСТЬ! Обнаружен угрожающий статус локала.", LogType.Warning);
                bot.CurrentTask = AccountTask.CheckSecurity;
                bot.IsSaveLocal = false;
                await bot.ExecuteEmergencyResponseAsync(isInitiator: true, token);
                return NodeStatus.Failure;

            case SecurityCheckResult.Unknown:
                // ИСПРАВЛЕНО: Теперь бот ЧЕТКО скажет в лог, что он ослеп или не узнает интерфейс
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Статус экрана НЕОПРЕДЕЛЕН (Unknown). Переключаю задачу на LookAround.", LogType.Warning);
                bot.CurrentTask = AccountTask.LookAround;
                return NodeStatus.Failure;

            default:
                // ИСПРАВЛЕНО: Лог на случай непредвиденного сбоя детекции
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Сбой анализа безопасности (Default фолбек). Возврат к охране.", LogType.Error);
                bot.CurrentTask = AccountTask.CheckSecurity;
                return NodeStatus.Failure;
        }
    }
}