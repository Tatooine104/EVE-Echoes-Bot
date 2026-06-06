using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{
    private static Task<NodeStatus> CheckSystemDangerStatusAsync(ActiveBotAccount bot, CancellationToken _)
    {
        var isSafe = SystemSafetyManager.GetSystemState(bot.EVESystem).IsSafe;
        return Task.FromResult(isSafe is false ? NodeStatus.Success : NodeStatus.Failure);
    }

    private static Task<NodeStatus> ExecutePanicEvacuationAsync(ActiveBotAccount bot, CancellationToken _)
    {
        if (bot.CurrentTask != AccountTask.GoToStation)
        {
            bot.CurrentTask = AccountTask.GoToStation;
            bot.IsSaveLocal = false;
        }
        return Task.FromResult(NodeStatus.Success);
    }

    private static Task<NodeStatus> CheckIfInterfaceLostAsync(ActiveBotAccount bot, CancellationToken _)
    {
        return Task.FromResult(bot.CurrentTask == AccountTask.LookAround ? NodeStatus.Success : NodeStatus.Failure);
    }

    private static async Task<NodeStatus> RunDiagnosticsAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Интерфейс заблокирован. Выполнение макроса очистки экрана...", LogType.Warning);
        await bot.ExecuteLookAroundDiagnosticsAsync(token);
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
        bot.CurrentTask = AccountTask.CheckYourOwnState;
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Плановый цикл мониторинга завершен. Система в безопасности.", LogType.Test);
        return Task.FromResult(NodeStatus.Success);
    }
}
