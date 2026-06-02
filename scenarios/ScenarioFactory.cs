using System;
using System.Collections.Generic;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

// [ ] TODO 2026.06.01 Реализовать сценарий "miner" 
// [ ] TODO 2026.06.01 Реализовать проверку, перезапуск и запуск сбора планетарки на ПОС 

/// <summary>
/// Фабрика сценариев, отвечающая за сборку и инициализацию Деревьев поведения (Behavior Trees)
/// для игровых аккаунтов на основе выбранного в конфигурации профиля автоматизации.
/// </summary>
public static class ScenarioFactory
{
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

    #region Tree Builders

    /// <summary>
    /// Собирает дерево поведения для сценария «Глаз» (LocalWatcher).
    /// Сценарий циклически проверяет безопасность локального чата и реагирует на угрозы.
    /// </summary>
    private static BehaviorNode BuildLocalWatcherTree()
    {
        return new SelectorNode("LocalWatcher Root",
            
            // ВЕТКА ТРЕВОГИ: Сработает, только если проверка безопасности обнаружила угрозу
            new SequenceNode("Emergency Response Branch",
                
                // Условие: Запускаем сканирование чата. 
                // Если в системе БЕЗОПАСНО -> метод возвращает true (Success для дерева), и Sequence прерывается.
                // Если в системе ОПАСНОСТь -> метод возвращает false (Failure для дерева), инвертируем его в Success, чтобы пойти дальше.
                new ActionNode("Scan Local Chat", async (bot, token) =>
                {
                    // Вызываем ваш оригинальный метод из ActiveBotAccount
                    // Внутри него настраиваются флаги IsSaveLocal
                    bool isSafe = await bot.CheckSecurityStatusAsync(token);
                    
                    // Если безопасно — возвращаем Failure для этой ветки, чтобы робот не паниковал
                    // Если опасно — возвращаем Success, чтобы активировать шаги эвакуации ниже
                    return isSafe ? NodeStatus.Failure : NodeStatus.Success;
                }),

                // Действие: Каскадное оповещение окон и паника
                new ActionNode("Trigger System Emergency", async (bot, token) =>
                {
                    // Проверяем статус через менеджер безопасности
                    var systemState = SystemSafetyManager.GetSystemState(bot.EVESystem);
                    
                    // Если это окно первым обнаружило угрозу, отправляем макрос в чат
                    if (systemState.IsSafe == false)
                    {
                        Logger.Log($"[{bot.Settings.Name}] Обнаружен противник! Активация цепочки кликов оповещения.", LogType.Warning);
                        await bot.RunAliChatWarningAsync(token);
                    }

                    // Очищаем старые задачи и запускаем локальную реакцию на панику
                    bot.ExecuteEmergencyResponse(isInitiator: false);
                    return NodeStatus.Success;
                })
            ),

            // ВЕТКА МИРНОГО ПРОСТОЯ: Сработает, если верхняя ветка тревоги вернула Failure (то есть в системе всё чисто)
            new SequenceNode("Peaceful Idle Branch",
                new ActionNode("Log Safe Status", async (bot, token) =>
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Плановый цикл мониторинга завершен. Система в безопасности.", LogType.Test);
                    return NodeStatus.Success;
                })
            )
        );
    }


    /// <summary>
    /// Собирает дерево поведения для сценария «Шахтер» (Miner).
    /// Управляет варпом в белты, детекцией трюма, добычей и разгрузкой на станцию.
    /// </summary>
    private static BehaviorNode BuildMinerTree()
    {
        return new ActionNode("Miner Root Action", async (bot, token) =>
        {
            return await System.Threading.Tasks.Task.FromResult(NodeStatus.Success);
        });
    }

    /// <summary>
    /// Создает базовое безопасное дерево-заглушку для режима ожидания/простоя.
    /// </summary>
    private static BehaviorNode BuildDefaultFallbackTree()
    {
        return new ActionNode("Default Fallback Action", async (bot, token) =>
        {
            return await System.Threading.Tasks.Task.FromResult(NodeStatus.Success);
        });
    }

    #endregion

    #region Legacy FSM Methods (Deprecated)

    /// <summary>
    /// Устаревший метод получения плоских списков задач. Оставлен для временной обратной совместимости.
    /// </summary>
    [Obsolete("Используйте метод CreateTree для получения полноценного дерева поведения.")]
    public static List<string> GetDefaultTasks(string scenarioName)
    {
        return scenarioName?.ToLower() switch
        {
            "localwatcher" => ["CheckSecurity"],
            _ => ["CheckYourOwnState"]
        };
    }

    #endregion
}
