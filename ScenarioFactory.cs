using System;
using System.Collections.Generic;

namespace EVEEchoesBot
{

// [ ] TODO 2026.06.01 Реализовать сценарий "miner" 
// [ ] TODO 2026.06.01 Реализовать проверку, перезапуск и запуск сбора планетарки на ПОС 

    public static class ScenarioFactory
    {
        public static List<string> GetDefaultTasks(string scenarioName)
        {
            return scenarioName?.ToLower() switch
            {
                "localwatcher" =>
                [
                    "CheckSecurity" // Бот делает плановую проверку
                ],

                // Дефолтный сценарий
                _ => ["CheckYourOwnState"]
            };
        }
    }
}