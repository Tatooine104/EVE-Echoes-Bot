using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

/// <summary>
/// Фабрика сценариев, отвечающая за сборку и инициализацию Деревьев поведения (Behavior Trees)
/// для игровых аккаунтов на основе выбранного в конфигурации профиля автоматизации.
/// </summary>
public static partial class ScenarioFactory
{
    #region CreateTree

    public static BehaviorNode CreateTree(string scenarioName)
    {
        // 1. Получаем базовое изолированное дерево сценария
        BehaviorNode coreScenarioTree = scenarioName?.ToLower() switch
        {
            "localwatcher" => BuildLocalWatcherTree(),
            "lowminer"     => BuildMinerTree(),
            _              => BuildDefaultFallbackTree()
        };

        // 2. Собираем финальную структуру с единой автоматической веткой планетарки.
        // Передаем статические методы-делегаты, которые принимают (account, token) на каждом тике.
        return new SelectorNode($"Global Wrapper [{scenarioName}]",

            // ГЛОБАЛЬНАЯ ВЕТКА: Сработает на станции раз в 8 часов, выполнит макрос и вернет FAILURE,
            // чтобы дерево гарантированно перешло к выполнению основного coreScenarioTree.
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
        // Приведение к единой сигнатуре (account, token)
        return new ActionNode("Default Fallback Action", (_, _) => Task.FromResult(NodeStatus.Success));
    }

    #endregion
}

