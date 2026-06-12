using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{
    private static SelectorNode BuildLocalWatcherTree()
    {
        return new SelectorNode("LocalWatcher Root",

            // ИСПРАВЛЕНО: Изменено на SelectorNode, чтобы эвакуация проверяла стейт 
            // и не кликала по кнопкам на каждом тике, если корабль уже улетает
            new SelectorNode("Emergency Response Selector",
                // Если мы уже на станции или задача GoToStation уже активна — этот узел вернет Success и защитит от повторного входа
                new ActionNode("Is Already Evacuating Check", CheckIsPanicStateActiveAsync),

                // Если мы еще в космосе и паники нет, срабатывает Sequence проверки локала и запуска эвакуации
                new SequenceNode("Trigger Emergency Panic",
                    new ActionNode("Check System Danger Status", CheckSystemDangerStatusAsync),
                    new ActionNode("Execute Panic Evacuation", ExecutePanicEvacuationAsync)
                )
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
