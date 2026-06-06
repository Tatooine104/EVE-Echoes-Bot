using System;
using System.Threading.Tasks; // Добавили для Task.FromResult
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

// [v] TODO 2026.06.01 Реализовать сценарий "lowminer" 
// [ ] TODO 2026.06.01 Реализовать проверку, перезапуск и запуск сбора планетарки на ПОС 

/// <summary>
/// Фабрика сценариев, отвечающая за сборку и инициализацию Деревьев поведения (Behavior Trees)
/// для игровых аккаунтов на основе выбранного в конфигурации профиля автоматизации.
/// </summary>
public static partial class ScenarioFactory
{
    #region CreateTree

    public static BehaviorNode CreateTree(string scenarioName)
    {
        // 1. Получаем базовое дерево сценария
        BehaviorNode coreScenarioTree = scenarioName?.ToLower() switch
        {
            "localwatcher" => BuildLocalWatcherTree(),
            "lowminer"     => BuildMinerTree(),
            _              => BuildDefaultFallbackTree()
        };

        // 2. Собираем финальную структуру с единой автоматической веткой планетарки
        return new SelectorNode($"Global Wrapper [{scenarioName}]",

            // ГЛОБАЛЬНАЯ ВЕТКА: Сработает на станции раз в 8 часов, сделает всё через меню и закроется
            new SequenceNode("Global Planet Mining Branch",
                new ActionNode("Check Planet Mining Conditions", CheckIfPlanetMiningTimeAsync),
                new ActionNode("Execute Planet Mining Macro", ExecutePlanetMiningSequenceAsync)
            ),

            // ШТАТНЫЙ СЦЕНАРИЙ: Работает во всех остальных случаях
            coreScenarioTree
        );
    }

    #endregion

    #region DefaultFallback

    /// <summary>
    /// Создает резервный узел по умолчанию, если запрошенный сценарий не найден.
    /// </summary>
    private static ActionNode BuildDefaultFallbackTree()
    {
        return new ActionNode("Default Fallback Action", (_, _) => Task.FromResult(NodeStatus.Success));
    }

    #endregion
}
