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

        return coreScenarioTree;
    }

    #endregion

    private static ActionNode BuildDefaultFallbackTree()
    {
        // Изменено: возвращаем строго типизированный ActionNode без оверхеда абстракций
        return new ActionNode("Default Fallback Action", (_, _) => Task.FromResult(NodeStatus.Success));
    }
}

