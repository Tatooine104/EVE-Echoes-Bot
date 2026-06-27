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

    // 2. ИСПРАВЛЕНО HIGH: Полностью перерабатываем глобальную обертку!
    // Вместо виснущего SequenceNode мы используем SelectorNode, но проверку времени планетарки
    // делаем атомарной. Если время не подошло, бот ПЕРВЫМ ЖЕ ШАГОМ провалится в coreScenarioTree
    // за 0.0001 миллисекунды, вообще не трогая логгер и нативные замки!
    return new SelectorNode($"Global Wrapper [{scenarioName}]",

        // ВЕТКА ПЛАНЕТАРКИ: Запустится ТОЛЬКО если CheckIfPlanetMiningTimeAsync вернет Success!
        new SequenceNode("Global Planet Mining Branch",
            new ActionNode("Check Planet Mining Time", async (b, t) => await CheckIfPlanetMiningTimeAsync(b, t)),
            new ActionNode("Execute Planet Mining Macro", ExecutePlanetMiningSequenceAsync)
        ),

        // ШТАТНЫЙ СЦЕНАРИЙ: Будет выполняться в 99% случаев напрямую без оверхеда!
        coreScenarioTree
    );
}


    #endregion

    private static ActionNode BuildDefaultFallbackTree()
    {
        // Изменено: возвращаем строго типизированный ActionNode без оверхеда абстракций
        return new ActionNode("Default Fallback Action", (_, _) => Task.FromResult(NodeStatus.Success));
    }

}
