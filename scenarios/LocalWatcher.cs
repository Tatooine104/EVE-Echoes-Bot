using System.Threading;
using System.Threading.Tasks;
using EVEEchoesBot.resources;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{
    private static SelectorNode BuildLocalWatcherTree()
    {
        return new SelectorNode("LocalWatcher Root",

            // =========================================================================
            // ПРИОРИТЕТ №1: КРИТИЧЕСКАЯ БЕЗОПАСНОСТЬ (Мониторинг локала КАЖДУЮ СЕКУНДУ)
            // =========================================================================
            new SelectorNode("Emergency Response Selector",
                new ActionNode("Is Already Evacuating Check", CheckIsPanicStateActiveAsync),

                new SequenceNode("Trigger Emergency Panic",
                    new ActionNode("Check System Danger Status", CheckSystemDangerStatusAsync),
                    new ActionNode("Execute Panic Evacuation", ExecutePanicEvacuationAsync)
                )
            ),

            // =========================================================================
            // ПРИОРИТЕТ №2: ВОССТАНОВЛЕНИЕ ИНТЕРФЕЙСА (Срабатывает ТОЛЬКО при сбое)
            // =========================================================================
            new SequenceNode("Look Around Branch",
                // ИСПРАВЛЕНО: Метод должен возвращать Success ТОЛЬКО если интерфейс сломан/потерян.
                // Если интерфейс в норме, метод возвращает Failure, и тяжелая диагностика НЕ запускается.
                new ActionNode("Check If Interface Lost", CheckIfInterfaceLostAsync),
                new ActionNode("Run Diagnostics", RunDiagnosticsAsync)
            ),

            // =========================================================================
            // ПРИОРИТЕТ №3: СТАНДАРТНЫЙ ЕЖЕСЕКУНДНЫЙ МОНИТОРИНГ ЭКРАНА И ОБНОВЛЕНИЕ СТАТУСА
            // =========================================================================
            new SequenceNode("Standard Security Monitor Branch",
                // BUG HIGH — Логическая ловушка, замыкающая бота V04KO в вечный цикл на станции!
                // Посмотри лог: бот V04KO отработал планетарку, а затем встал намертво на станции и пишет "НАЙДЕНА. Робот находится на СТАНЦИИ" (это вызывается внутри AnalyzeScreenAndUpdateStateAsync).
                // Узел "Analyze Screen and Update State" сканирует экран, видит, что корабль придокан, обновляет внутренние флаги и возвращает `NodeStatus.Success` (потому что сканирование прошло успешно!).
                // Так как он вернул `Success`, `SequenceNode` идет дальше и вызывает узел "Log Safe Status", который выводит лог и тоже возвращает `Success`.
                // Весь `SequenceNode` ("Standard Security Monitor Branch") возвращает `Success` наверх в корень. 
                // А для сценария "localwatcher" (Наблюдатель) — это КОНЕЦ! Наблюдатель и должен просто сидеть в доке и сканировать экран, для него это штатное поведение. 
                // Но этот же самый узел `AnalyzeScreenAndUpdateStateAsync` (или `CheckIsDockedAsync`) используется и в сценарии `LowMiner` (Lana Muc)! Давай посмотрим, как устроен майнер.
                new ActionNode("Analyze Screen and Update State", AnalyzeScreenAndUpdateStateAsync),
                new ActionNode("Log Safe Status", LogSafeStatusAsync)
            )
        );
    }

    
    private static Task<NodeStatus> LogSafeStatusAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // Защищаем запись стейта под локальным тиком воркера
        bot.CurrentTask = AccountTask.CheckSecurity;

        // Выводим информационный лог в консоль и веб-панель
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Плановый цикл мониторинга завершен. Система в безопасности.", LogType.Info);

        return Task.FromResult(NodeStatus.Success);
    }

}

