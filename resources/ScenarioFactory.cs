using System;
using System.Collections.Generic;

namespace EVEEchoesBot.resources;

// [ ] TODO 2026.06.01 Реализовать сценарий "miner" 
// [ ] TODO 2026.06.01 Реализовать проверку, перезапуск и запуск сбора планетарки на ПОС 

/// <summary>
/// Фабрика сценариев, отвечающая за генерацию базовых списков задач (цепочек макросов) 
/// для игровых аккаунтов на основе выбранного профиля автоматизации.
/// </summary>
public static class ScenarioFactory
{
    /// <summary>
    /// Возвращает стартовый набор строковых идентификаторов задач для инициализации или перезапуска указанного сценария.
    /// </summary>
    /// <param name="scenarioName">Имя целевого сценария (например, "localwatcher", "miner").</param>
    /// <returns>Список <see cref="List{T}"/> строк с именами начальных задач автоматизации.</returns>
    public static List<string> GetDefaultTasks(string scenarioName)
    {
        return scenarioName?.ToLower() switch
        {
            "localwatcher" =>
            [
                "CheckSecurity" // Бот делает плановую проверку локал-чата
            ],

            // Дефолтный сценарий-фолбек, если имя скрипта не распознано или пусто
            _ => ["CheckYourOwnState"]
        };
    }
}
