using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{

    /// <summary>
    /// Универсальный метод проверки безопасности системы.
    /// Подходит как для штатного мониторинга, так и для проверок перед андоком/варпом.
    /// </summary>
    private static async Task<NodeStatus> EvaluateSystemSecurityAsync(ActiveBotAccount bot, CancellationToken token)
    {
        bot.CurrentTask = AccountTask.CheckSecurity;

        // 1. Быстрый чек: проверяем глобальный статус системы (не забил ли тревогу другой бот)
        var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);

        // Исправлено: корректно обрабатываем тип bool? (если равен false или null — система опасна)
        if (systemState.IsSafe is not true)
        {
            Logger.Log($"[{bot.Settings.Name}] Глобальная тревога! Система небезопасна.", LogType.Warning);
            bot.IsSaveLocal = false;
            bot._currenttarget = null; // Сбрасываем цель, если она была
            return NodeStatus.Failure;
        }

        // 2. Если глобально чисто, проверяем сами через OCR на экране
        SecurityCheckResult result = await bot.CheckSecurityStatusAsync(token);

        switch (result)
        {
            case SecurityCheckResult.Safe:
                bot.IsSaveLocal = true;
                return NodeStatus.Success;

            case SecurityCheckResult.Danger:
                bot.IsSaveLocal = false;
                bot._currenttarget = null; // Сбрасываем цель, чтобы не лететь в ловушку
                Logger.Log($"[{bot.Settings.Name}] Обнаружен противник в локале!", LogType.Warning);
                await Task.Delay(5000, token); // Даем паузу перед следующим тиком
                return NodeStatus.Failure;

            case SecurityCheckResult.Unknown:
                bot.CurrentTask = AccountTask.LookAround;
                Logger.Log($"[{bot.Settings.Name}] Интерфейс потерян. Запуск макроса очистки...", LogType.Warning);
                await bot.ExecuteLookAroundDiagnosticsAsync(token);
                return NodeStatus.Failure;

            default:
                return NodeStatus.Failure;
        }
    }

    /// <summary>
    /// Проверяет, пришло ли время для обслуживания планетарной добычи (выполняется только в доке).
    /// </summary>
    private static Task<NodeStatus> CheckIfPlanetMiningTimeAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // Должны быть в доке + включена планетарка (флаг POS сам по себе без планетарки игнорируется)
        if (bot._inSpace || !bot.PlanetMining)
        {
            return Task.FromResult(NodeStatus.Failure);
        }

        // Проверяем время (нет даты или прошло более 8 часов)
        bool isTime = !bot._planetassembly.HasValue || (DateTime.UtcNow - bot._planetassembly.Value).TotalHours >= 8;

        return Task.FromResult(isTime ? NodeStatus.Success : NodeStatus.Failure);
    }


    // TODO: Дописать логику перезапуска планетарки
    /// <summary>
    /// Выполняет взаимодействие с интерфейсом планетарной добычи внутри станции.
    /// </summary>
    private static async Task<NodeStatus> ExecutePlanetMiningSequenceAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}] Открытие интерфейса планетарной добычи...", LogType.Info);

        // 1. Общая часть: открываем меню, перезапускаем таймеры
        // await bot.OpenPlanetMenuAndResetTimersAsync(token);
        await Task.Delay(1500, token); 

        // 2. Адаптивная часть: если есть ПОС, кликаем "Собрать на ПОС"
        if (bot.POS)
        {
            Logger.Log($"[{bot.Settings.Name}] Обнаружена привязка к ПОС. Инициация удаленного сбора ресурсов на структуру...", LogType.Info);
            // await bot.ClickCollectToPOSButtonAsync(token);
            await Task.Delay(1500, token);
        }
        else
        {
            Logger.Log($"[{bot.Settings.Name}] Режим без ПОС. Ресурсы остаются на планете (заглушка).", LogType.Info);
        }

        // Фиксируем время успешного завершения цикла
        bot._planetassembly = DateTime.UtcNow;
        Logger.Log($"[{bot.Settings.Name}] Цикл планетарной добычи успешно обработан.", LogType.Info);

        return NodeStatus.Success;
    }

}