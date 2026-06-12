using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{
    private static Task<NodeStatus> CheckSystemDangerStatusAsync(ActiveBotAccount bot, CancellationToken _)
    {
        var isSafe = SystemSafetyManager.GetSystemState(bot.EVESystem).IsSafe;

        // Исправлено: если статус равен false или null — система признается опасной для корабля
        bool isDangerous = isSafe is not true;

        return Task.FromResult(isDangerous ? NodeStatus.Success : NodeStatus.Failure);
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
        SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

        switch (result)
        {
            case SecurityCheckResult.Safe:
                bot.CurrentTask = AccountTask.CheckSecurity;
                bot.IsSaveLocal = true;
                return NodeStatus.Success;

            case SecurityCheckResult.Danger:
                bot.CurrentTask = AccountTask.CheckSecurity;
                bot.IsSaveLocal = false;
                await bot.ExecuteEmergencyResponseAsync(isInitiator: true, token);
                return NodeStatus.Failure;


            case SecurityCheckResult.Unknown:
                bot.CurrentTask = AccountTask.LookAround;
                return NodeStatus.Failure;

            default:
                bot.CurrentTask = AccountTask.CheckSecurity;
                return NodeStatus.Failure;
        }
    }

    private static Task<NodeStatus> LogSafeStatusAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // Исправлено: удерживаем статус охраны локала, чтобы веб-панель видела правильный режим работы
        bot.CurrentTask = AccountTask.CheckSecurity;
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Плановый цикл мониторинга завершен. Система в безопасности.", LogType.Test);

        return Task.FromResult(NodeStatus.Success);
    }
}
