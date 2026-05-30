using System;
using System.Collections.Generic;

namespace EVEEchoesBot
{
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