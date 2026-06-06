using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{
    private static SelectorNode BuildLocalWatcherTree()
    {
        return new SelectorNode("LocalWatcher Root",

            new SequenceNode("Emergency Response Branch",
                new ActionNode("Check System Danger Status", CheckSystemDangerStatusAsync),
                new ActionNode("Execute Panic Evacuation", ExecutePanicEvacuationAsync)
            ),

            new SequenceNode("Look Around Branch",
                new ActionNode("Check If Interface Lost", CheckIfInterfaceLostAsync),
                new ActionNode("Run Diagnostics", RunDiagnosticsAsync)
            ),

            new SequenceNode("Standard Security Monitor Branch",
                new ActionNode("Analyze Screen and Update State", AnalyzeScreenAndUpdateStateAsync),
                new ActionNode("Log Safe Status", LogSafeStatusAsync)
            )
        );
    }
}
