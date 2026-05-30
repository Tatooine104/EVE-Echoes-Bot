using System;
using System.Collections.Generic;

namespace EVEEchoesBot
{
    public static class ScenarioFactory
    {
        // Метод возвращает список стартовых задач для конкретного сценария
        public static List<string> GetDefaultTasks(string scenarioName)
        {
            return scenarioName?.ToLower() switch
            {
                "mining" =>
                [
                    "CheckSecurity"
                ],

                // Дефолтный сценарий, если в конфиге указано что-то непонятное
                _ => ["CheckYourOwnState"]
            };
        }
    }
}