using EVEEchoesBot.resources;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace EVEEchoesBot.scenarios;

public static partial class ScenarioFactory
{


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region BuildMinerTree

    /// <summary>
    /// Собирает дерево поведения для сценария «Шахтер в лоу-секах» (LowMiner).
    /// Сценарий управляет полным циклом добычи руды, логистикой на станцию и безопасностью.
    /// </summary>
internal static SelectorNode BuildMinerTree()
{
    return new SelectorNode("LowMiner Root",

        // =========================================================================
        // ПРИОРИТЕТ №1: БЛОК АВТОМАТИЧЕСКОЙ ПЛАНЕТАРНОЙ ДОБЫЧИ (Срабатывает раз в 8 часов)
        // =========================================================================
        new SequenceNode("Global Planet Mining Branch",
            new ActionNode("Check Planet Mining Conditions", (b, _) => Task.FromResult(CheckIfPlanetMiningTime(b))),
            new ActionNode("Execute Planet Mining Macro", ExecutePlanetMiningSequenceAsync)
        ),

        // =========================================================================
        // ПРИОРИТЕТ №2: БЛОК ЭКСТРЕННОЙ ЭВАКУАЦИИ (Срабатывает мгновенно при угрозе)
        // =========================================================================
        new SequenceNode("Emergency Evacuation Branch",
            new ActionNode("Is Panic State Active", CheckIsPanicStateActiveAsync),
            new SelectorNode("Panic Actions",
                new ActionNode("Is Already Safe Docked", CheckIsDockedAsync),
                new ActionNode("Execute Emergency Warp To Base", WarpToBaseAsync)
            )
        ),

        // =========================================================================
        // ПРИОРИТЕТ №3: МОНОЛИТНЫЙ БЛОК СТАНЦИИ (Выполняется строго по пунктам 1-7)
        // =========================================================================
        new SequenceNode("Station Monolithic Branch",
            // 1) Определяем в доке мы или нет (Пункт 1). Если не в доке — сразу Failure (Пункт 5)
            new ActionNode("Is Docked Check", CheckIsDockedAsync),
            
            // Защита от спама повторными кликами во время анимации вылета
            new ActionNode("Is NOT Processing Undock", CheckIsNotUndockingAsync),

            // Выбираем действие на станции строго на основе заполненности трюма
            new SelectorNode("Station Action Selector",

                // ВЕТКА ВЫГРУЗКИ: Если трюм НЕ пуст — переходим к выгрузке (Пункт 6)
                new SequenceNode("Unload Ore Sequence",
                    new ActionNode("Check Cargo Not Empty", async (b, t) => {
                        NodeStatus emptyCheck = await CheckIsCargoEmptyAsync(b, t);
                        // Инвертируем: если трюм НЕ пуст (Failure для Empty), значит для выгрузки это Success!
                        return emptyCheck == NodeStatus.Failure ? NodeStatus.Success : NodeStatus.Failure;
                    }),
                    new ActionNode("Unload Ore To Hangar", UnloadOreToHangarAsync)
                ),

                // ВЕТКА АНДОКА: Если трюм пуст (Пункт 2) И в системе безопасно (Пункт 3) -> вылетаем (Пункт 4)
                new SequenceNode("Undock Execution Sequence",
                    // 2) Проверяем пустой ли трюм (Пункт 2). Если пуст — вернет Success
                    new ActionNode("Verify Cargo Is Empty Before Undock", CheckIsCargoEmptyAsync),
                    
                    // 3) Проверяем безопасно ли в системе (Пункт 3). Если опасно — вернет Failure и будем ждать (Пункт 7)
                    new ActionNode("Verify System Is Safe Before Undock", EvaluateSystemSecurityAsync),
                    
                    // 4) Если везде ДА — инициируем выход из дока (Пункт 4)
                    new ActionNode("Execute Undock Action", ExecuteUndockAsync)
                )
            )
        ),

        // =========================================================================
        // ПРИОРИТЕТ №4: БЛОК КОСМОСА (Полеты, навигация и процесс копки)
        // =========================================================================
        new SequenceNode("Space Operations Branch",
            new ActionNode("Verify Is In Space", (bot, _) => Task.FromResult(bot._inSpace ? NodeStatus.Success : NodeStatus.Failure)),
            new SelectorNode("Space Workflow Selector",
                new SequenceNode("Return Full Cargo To Base",
                    new ActionNode("Is Cargo Full In Space", CheckIsCargoFullAsync),
                    new ActionNode("Warp To Base", WarpToBaseAsync)
                ),
                new SequenceNode("Flight To Belt Sequence",
                    new ActionNode("Is NOT In Mining Zone", CheckIsNotInMiningZoneAsync),
                    new SelectorNode("Belt Selection Selector",
                        new ActionNode("Is Belt Already Selected", CheckIsBeltAlreadySelectedAsync),
                        new ActionNode("Select Asteroid Belt", SelectAsteroidBeltAsync)
                    ),
                    new ActionNode("Check Safe Before Warp", EvaluateSystemSecurityAsync),
                    new ActionNode("Warp To Selected Belt", WarpToSelectedBeltAsync)
                ),
                new SequenceNode("Active Mining Sequence",
                    new ActionNode("Clear Flight State On Arrival", ClearFlightStateOnArrivalAsync),
                    new SelectorNode("Targeting and Activation Selector",
                        new ActionNode("Check If Mining Is Active", CheckIfMiningIsActiveAsync),
                        new SequenceNode("Lock And Mine Sequence",
                            new ActionNode("Target Asteroid", TargetAsteroidAsync),
                            new ActionNode("Activate Lasers", ActivateLasersAsync)
                        )
                    ),
                    new ActionNode("Mining Monitor State", MiningMonitorStateAsync)
                )
            )
        )
    );
}




    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsDockedAsync

    /// <summary>
    /// Проверяет, находится ли корабль внутри дока станции по наличию кнопки выхода из дока.
    /// </summary>
    public static async Task<NodeStatus> CheckIsDockedAsync(ActiveBotAccount bot, CancellationToken token)
    {

        // Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] !!!!!!", LogType.Warning);
        try
        {
            // ИСПРАВЛЕНО: Вызываем родной экземплярный метод скриншотов конкретного бота.
            // Это гарантирует изолированный захват экрана своего окна эмулятора без статических клин-замков.
            var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.ControlUndock, token);

            if (screenshot is null)
            {
                // Если эмулятор лагает и не отдал кадр, возвращаем Failure, чтобы дерево попробовало снова на следующем тике
                return NodeStatus.Failure;
            }

            // Гарантированная утилизация unmanaged памяти OpenCV при любом выходе из метода
            using var screenshotScope = screenshot;

            // Берем путь к файлу шаблона из кэша ядра платформы
            string pathImg = Path.Combine(Program.TemplatesDir, "imgUndock1.png");

            // Блокируем ссылку на матрицу пикселей для безопасного фонового поиска в пуле Task.Run
            var currentSnap = screenshot;
            Point? foundPos = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathImg, safeRegion, 0.85), token);

            if (foundPos.HasValue)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка imgUndock1 НАЙДЕНА. Робот находится на СТАНЦИИ.", LogType.Warning);

                lock (bot._taskLock)
                {
                    bot._inSpace = false;
                    bot._isUndocking = false;

                    // ИСПРАВЛЕНО HIGH: Если бот находился в режиме паники/эвакуации (GoToStation),
                    // но зрение ЖЕСТКО подтвердило, что корабль уже сидит на станции — 
                    // задача эвакуации полностью ВЫПОЛНЕНА. Сбрасываем стейт паники в CheckSecurity,
                    // чтобы на следующем тике дерево пропустило выполнение в мирный блок обслуживания и андока!
                    if (bot.CurrentTask == AccountTask.GoToStation)
                    {
                        Logger.Log($"[{bot.Settings.Name}] Эвакуация завершена. Сбрасываю стейт паники. Возврат к штатной рутине.", LogType.Info);
                        bot.CurrentTask = AccountTask.CheckSecurity;
                    }
                }
                return NodeStatus.Success;
            }


            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка imgUndock1 НЕ найдена. Робот находится в КОСМОСЕ.", LogType.Warning);

            // ИСПРАВЛЕНО HIGH: Убираем lock(bot) и здесь
            lock (bot._taskLock)
            {
                bot._inSpace = true;
            }
            return NodeStatus.Failure;

        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Исключение в методе проверки дока CheckIsDockedAsync: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsCargoFullAsync

    /// <summary>
    /// Проверяет, заполнен ли рудный трюм корабля (Узел Дерева Поведения).
    /// </summary>
private static async Task<NodeStatus> CheckIsCargoFullAsync(ActiveBotAccount bot, CancellationToken token)
{
    // ИСПРАВЛЕНО HIGH: Если бот находится на станции, физический интерфейс трюма скрыт.
    // Опираемся на закэшированное значение из DTO/памяти во избежание слепоты дерева!
    if (!bot._inSpace)
    {
        // Если бот придокался, мы считаем статус заполненности на основе флага _isfullore.
        // Это позволит ветке выгрузки руды успешно запуститься на станции!
        return bot._isfullore is true ? NodeStatus.Success : NodeStatus.Failure;
    }

    // 1. Делаем снимок зоны фаст-меню (выполняется только в космосе)
    var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.FastMenu, token).ConfigureAwait(false);
    if (screenshot == null) return NodeStatus.Failure;

    using var screenshotScope = screenshot;
    var currentSnap = screenshot;

    string pathCargoIcon = Path.Combine(Program.TemplatesDir, "imgCargoHoldIcon.png");
    string pathCargo100 = Path.Combine(Program.TemplatesDir, "imgCargoHold100.png"); // Исправлена опечатка Fold -> Hold

    // Шаг 1: Ищем сам факт наличия иконки трюма на экране
    Point? foundCargoIcon = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathCargoIcon, safeRegion, 0.85), token).ConfigureAwait(false);

    if (foundCargoIcon.HasValue)
    {
        Point? foundFull = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathCargo100, safeRegion, 0.85), token).ConfigureAwait(false);
        if (foundFull.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Трюм полностью заполнен. Пора на станцию.", LogType.Info);
            lock (bot._taskLock) { bot._isfullore = true; } // Фиксируем стейт
            return NodeStatus.Success;
        }

        lock (bot._taskLock) { bot._isfullore = false; }
        return NodeStatus.Failure; 
    }

    // Шаг 3: Проверка на флаги флота
    string pathFleet = Path.Combine(Program.TemplatesDir, "imgFleetFlags.png");
    Point? foundFleet = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathFleet, safeRegion, 0.85), token).ConfigureAwait(false);

    if (!foundFleet.HasValue)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Панель трюма отсутствует в космосе. Сбой UI.", LogType.Warning);
        return NodeStatus.Failure;
    }

    // Шаг 4: Корректирующий свайп
    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Панель сдвинута флагом флота. Выполняю корректирующий свайп.", LogType.Test);
    await bot.ScrollLeftAsync(GameUI.FastMenu1, 50, token).ConfigureAwait(false);
    await Task.Delay(600, token).ConfigureAwait(false);

    var (retryScreenshot, retryRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.FastMenu, token).ConfigureAwait(false);
    if (retryScreenshot == null) return NodeStatus.Failure;

    using var retryScope = retryScreenshot;
    Point? foundFullRetry = await Task.Run(() => Tools.FindTemplateInRegion(retryScreenshot, pathCargo100, retryRegion, 0.85), token).ConfigureAwait(false);
    
    if (foundFullRetry.HasValue)
    {
        lock (bot._taskLock) { bot._isfullore = true; }
        return NodeStatus.Success;
    }
    return NodeStatus.Failure;
}


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsCargoEmptyAsync

    /// <summary>
    /// Проверяет, пуст ли рудный трюм корабля (необходим перед вылетом).
    /// </summary>
private static async Task<NodeStatus> CheckIsCargoEmptyAsync(ActiveBotAccount bot, CancellationToken token)
{

    // Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] СТАРТ CheckIsCargoEmptyAsync", LogType.Test);
    // ИСПРАВЛЕНО HIGH: Защита интерфейса дока станции.
    // Находясь на станции, мы физически не видим иконку трюма. 
    // Если флаг полной руды сброшен (равен false или null) — считаем трюм пустым и готовым к андоку!
    if (!bot._inSpace)
    {
        return bot._isfullore is not true ? NodeStatus.Success : NodeStatus.Failure;
    }

    // 1. Делаем снимок зоны фаст-меню (Отработает строго в космосе)
    var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.FastMenu, token).ConfigureAwait(false);
    if (screenshot == null) return NodeStatus.Failure;

    using var screenshotScope = screenshot;
    var currentSnap = screenshot;

    string pathCargoIcon = Path.Combine(Program.TemplatesDir, "imgCargoHoldIcon.png");
    string pathCargo0 = Path.Combine(Program.TemplatesDir, "imgCargoHold0.png");

    // Шаг 1: Ищем сам факт наличия иконки трюма на экране
    Point? foundCargoIcon = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathCargoIcon, safeRegion, 0.85), token).ConfigureAwait(false);

    if (foundCargoIcon.HasValue)
    {
        Point? foundEmpty = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathCargo0, safeRegion, 0.85), token).ConfigureAwait(false);
        if (foundEmpty.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Трюм идеально пуст", LogType.Test);
            lock (bot._taskLock) { bot._isfullore = false; }
            return NodeStatus.Success;
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Трюм виден, но в нем что-то лежит", LogType.Test);
        return NodeStatus.Failure;
    }

    // Шаг 3: Проверка на флаги флота
    string pathFleet = Path.Combine(Program.TemplatesDir, "imgFleetFlags.png");
    Point? foundFleet = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathFleet, safeRegion, 0.85), token).ConfigureAwait(false);

    if (!foundFleet.HasValue)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Панель трюма отсутствует в космосе. Сбой UI.", LogType.Warning);
        return NodeStatus.Failure;
    }

    // Шаг 4: Сдвигаем панель
    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Панель сдвинута флагом флота. Выполняю корректирующий свайп.", LogType.Test);
    await bot.ScrollLeftAsync(GameUI.FastMenu1, 50, token).ConfigureAwait(false);
    await Task.Delay(600, token).ConfigureAwait(false);

    var (retryScreenshot, retryRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.FastMenu, token).ConfigureAwait(false);
    if (retryScreenshot == null) return NodeStatus.Failure;

    using var retryScope = retryScreenshot;
    Point? foundEmptyRetry = await Task.Run(() => Tools.FindTemplateInRegion(retryScreenshot, pathCargo0, retryRegion, 0.85), token).ConfigureAwait(false);
    
    if (foundEmptyRetry.HasValue)
    {
        lock (bot._taskLock) { bot._isfullore = false; }
        return NodeStatus.Success;
    }
    return NodeStatus.Failure;
}


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region UnloadOreToHangarAsync

    // [ ] TODO 2026.06.14 Нужно в логике учесть что иконка быстрого меню может быть перекрыта флагами флота 
    /// <summary>
    /// Выполняет разгрузку руды на склад текущей станции (Узел Дерева Поведения).
    /// </summary>
private static async Task<NodeStatus> UnloadOreToHangarAsync(ActiveBotAccount bot, CancellationToken token)
{
    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Трюм заполнен. Выгрузка руды на склад станции.", LogType.Info);

    // ========================================================
    // ШАГ 1-2: ОТКРЫТИЕ МЕНЮ И ПОДГОТОВКА СКЛАДА
    // ========================================================
    (GameUI Element, int DelayMs)[] initialSteps = [
        (GameUI.FastMenu1, 800),
        (GameUI.CollapseStation, 800)
    ];

    foreach (var (element, delayMs) in initialSteps)
    {
        await bot.ClickToAsync(element, token).ConfigureAwait(false);
        await Task.Delay(delayMs, token).ConfigureAwait(false);
    }

    // ========================================================
    // ШАГ 3: ДИНАМИЧЕСКИЙ ПОИСК ИКОНКИ РУДНОГО ОТСЕКА
    // ========================================================
    // ИСПРАВЛЕНО HIGH: Изменен регион поиска с LocalChat на FastMenu (или твой регион окна инвентаря),
    // чтобы OpenCV физически видел иконку отсека на экране склада!
    var (screenshot, safeRegion) = await PrepareScreenshotRegionAsync(bot, GameRegions.FastMenu, token).ConfigureAwait(false);
    if (screenshot == null) return NodeStatus.Failure;

    using var screenshotScope = screenshot;
    string pathOreHold = Path.Combine(Program.TemplatesDir, "imgOreHold.png");

    Point? foundOreHold = await Task.Run(() => Tools.FindTemplateInRegion(screenshot, pathOreHold, safeRegion, 0.85), token).ConfigureAwait(false);

    if (!foundOreHold.HasValue)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Иконка рудного отсека не найдена. Сброс окон.", LogType.Error);
        await bot.ClickToAsync(GameUI.XButton, token).ConfigureAwait(false); // Страхуем и закрываем интерфейс
        return NodeStatus.Failure;
    }

    // Кликаем по найденной точке рудного отсека
    await bot.ClickPointAsync(foundOreHold.Value, token, minSec: 1, maxSec: 2, offset: 3).ConfigureAwait(false);
    await Task.Delay(800, token).ConfigureAwait(false);

    // ========================================================
    // ШАГ 4-6: ВЫДЕЛЕНИЕ, ПЕРЕНОС В АНГАР И ЗАКРЫТИЕ
    // ========================================================
    (GameUI Element, int DelayMs)[] finalSteps = [
        (GameUI.SelectAll, 900),
        (GameUI.ItemHangar, 1500),
        (GameUI.XButton, 0)
    ];

    foreach (var (element, delayMs) in finalSteps)
    {
        await bot.ClickToAsync(element, token).ConfigureAwait(false);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, token).ConfigureAwait(false);
        }
    }

    // ИСПРАВЛЕНО HIGH: Атомарно сбрасываем флаги заполненности трюма!
    // Теперь на следующем тике дерево поймет, что корабль ПУСТ, и штатно отправит его на андок!
    lock (bot._taskLock)
    {
        bot._isfullore = false;
        bot._isfullmain = false;
    }

    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Выгрузка руды успешно завершена. Трюм очищен.", LogType.Success);
    return NodeStatus.Success;
}


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecuteUndockAsync

    /// <summary>
    /// Выполняет команду выхода из дока станции (андок) в космос.
    /// </summary>
private static async Task<NodeStatus> ExecuteUndockAsync(ActiveBotAccount bot, CancellationToken token)
{
    // Защита от отмены
    token.ThrowIfCancellationRequested();

    // Если мы уже физически зафиксировали космос на прошлых тиках — задача успешно выполнена!
    if (bot._inSpace)
    {
        bot._isUndocking = false;
        return NodeStatus.Success;
    }

    // ПЕРВЫЙ ВХОД: Кликаем по кнопке и взводим стартовый таймер анимации
    if (!bot._isUndocking)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] В системе чисто. Инициирую выход из дока.", LogType.Info);
        bot._isUndocking = true;
        
        // Фиксируем точное время начала андока, чтобы проверять длительность анимации
        bot._lastLoggedPlanetHours = 0; // Используем свободное поле как маркер или введи DateTime поле

        await bot.ClickToAsync(GameUI.UndockButton, token).ConfigureAwait(false);
        
        // Даем 5 секунд первичного ожидания, чтобы экран гарантированно потемнел
        await Task.Delay(5000, token).ConfigureAwait(false);
        return NodeStatus.Running; // Возвращаем Running, отдавая управление в цикл!
    }

    // ПОВТОРНЫЕ ТИКИ (Проверка прогрузки космоса каждую секунду без блокировки дерева)
    string pathEyeImg = Path.Combine(Program.TemplatesDir, "imgEyeIcon.png");
    var (screenshot, safeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.EyeIconClose, token).ConfigureAwait(false);

    if (screenshot != null)
    {
        using var scope = screenshot;
        Point? foundEye = await Task.Run(() => Tools.FindTemplateInRegion(screenshot, pathEyeImg, safeRegion, 0.82), token).ConfigureAwait(false);

        if (foundEye.HasValue)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Интерфейс космоса успешно прогружен. Вылет подтвержден!", LogType.Success);
            bot._inSpace = true;
            bot._isUndocking = false;

            // Настраиваем овервью в космосе
            _ = Task.Run(async () => await bot.PrepareSpaceInterfaceAsync(Program.GetGlobalToken()));
            return NodeStatus.Success;
        }
    }

    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ожидаю появление интерфейса космоса (Корабль в процессе андока)...", LogType.Info);
    
    // Возвращаем Running, чтобы дерево продолжало удерживать эту ветку на следующих тиках
    return NodeStatus.Running;
}


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region WarpToBaseAsync


    /// <summary>
    /// Выполняет варп и стыковку (док) на домашнюю станцию/цитадель для разгрузки.
    /// Мониторит процесс дока по исчезновению иконки глаза из открытого космоса.
    /// </summary>
    private static async Task<NodeStatus> WarpToBaseAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Инициирован макрос варпа и дока на домашнюю базу.", LogType.Info);

        // Пути к шаблонам изображений
        string pathImgCitade = Path.Combine(Program.TemplatesDir, "imgCitadel.png");
        string pathImgCitadel = Path.Combine(Program.TemplatesDir, "imgCitadel.png");
        string pathImgEnter = Path.Combine(Program.TemplatesDir, "imgEnter.png");
        string pathImgEyeIcon = Path.Combine(Program.TemplatesDir, "imgEyeIcon.png");

        try
        {
            // --- ШАГ 1: В регионе GridFilter ищем imgCitadel.png и нажимаем ---
            var (filterScreen, filterRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridFilter, token);
            if (filterScreen != null)
            {
                using var scope = filterScreen;
                Point? foundFilter = await Task.Run(() => Tools.FindTemplateInRegion(filterScreen, pathImgCitade, filterRegion, 0.82), token);

                if (foundFilter.HasValue)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Вкладка станций обнаружена в GridFilter. Выполняю клик.", LogType.Info);
                    await bot.ClickPointAsync(foundFilter.Value, token);
                    await Task.Delay(1500, token); // Задержка на прогрузку списка объектов
                }
                else
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Вкладка пресета imgCitade не найдена в GridFilter.", LogType.Error);
                    return NodeStatus.Failure;
                }
            }

            // --- ШАГ 2: В регионе GridList ищем imgCitadel.png и нажимаем ---
            var (listScreen, listRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridList, token);
            if (listScreen != null)
            {
                using var scope = listScreen;
                Point? foundCitadel = await Task.Run(() => Tools.FindTemplateInRegion(listScreen, pathImgCitadel, listRegion, 0.82), token);

                if (foundCitadel.HasValue)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Домашняя цитадель найдена в GridList. Выделяю объект.", LogType.Info);
                    await bot.ClickPointAsync(foundCitadel.Value, token);
                    await Task.Delay(1500, token); // Задержка на появление контекстных кнопок действий
                }
                else
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Домашняя цитадель imgCitadel не найдена в текущем овервью.", LogType.Error);
                    return NodeStatus.Failure;
                }
            }

            // --- ШАГ 3: В регионе SpaceActions ищем imgEnter.png и нажимаем ---
            var (actionScreen, actionRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.SpaceActions, token);
            if (actionScreen != null)
            {
                using var scope = actionScreen;
                Point? foundEnter = await Task.Run(() => Tools.FindTemplateInRegion(actionScreen, pathImgEnter, actionRegion, 0.82), token);

                if (foundEnter.HasValue)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка входа/стыковки imgEnter обнаружена. Инициирую док.", LogType.Success);
                    await bot.ClickPointAsync(foundEnter.Value, token);

                    // --- ИСПРАВЛЕНО: ШАГ 4: Универсальная проверка разгона и ухода в варп по AU/s ---
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Команда отправлена. Ожидаю фиксации прыжка по приборам скорости...", LogType.Info);
                    bool warpConfirmed = false;

                    // Даем кораблю до 12 секунд на разгон (8 попыток с шагом в 1.5 секунды)
                    for (int check = 1; check <= 10; check++)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(3000, token);

                        if (await IsShipInWarpDriveAsync(bot, token))
                        {
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Прыжок зафиксирован! Корабль успешно вошел в варп-туннель.", LogType.Success);
                            bot._iswarping = true; // Синхронизируем внутренний флаг под Lock / SaveStats
                            warpConfirmed = true;
                            break;
                        }

                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Корабль разгоняется. Валидация приборов, попытка {check}/8...", LogType.Test);
                    }

                    if (!warpConfirmed)
                    {
                        // Мягкий фолбек: если наложился графический лаг, взводим флаг варпа принудительно, чтобы не вешать цикл
                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Предупреждение: Прямой маркер AU/s не зафиксирован в отведенное время. Возможно, прыжок произошел слишком быстро. Перехожу к мониторингу дока.", LogType.Warning);
                        bot._iswarping = true;
                    }
                }
                else
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Цитадель выбрана, но кнопка стыковки imgEnter не появилась.", LogType.Error);
                    return NodeStatus.Failure;
                }
            }

            // --- ШАГ 5 & 6: Циклический мониторинг дока (Сначала по исчезновению глаза, затем по появлению Андока) ---
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Начинаю отслеживание перелета. Ожидаю исчезновение интерфейса космоса...", LogType.Info);

            string pathImgUndock = Path.Combine(Program.TemplatesDir, "imgUndock1.png");
            bool spaceLeft = false;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                // Проверка каждые 5 секунд по ТЗ
                await Task.Delay(5000, token);

                // Флаг для проверки, удалось ли вообще захватить хоть какой-то экран на этой итерации
                bool screenshotCaptured = false;

                // ЭТАП 1: Ждем, пока корабль покинет космос (исчезнет глаз)
                if (!spaceLeft)
                {
                    var (eyeScreen, eyeRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.EyeIconOpen, token);
                    if (eyeScreen != null)
                    {
                        screenshotCaptured = true;
                        using var scope = eyeScreen;
                        Point? foundEye = await Task.Run(() => Tools.FindTemplateInRegion(eyeScreen, pathImgEyeIcon, eyeRegion, 0.82), token);

                        if (foundEye.HasValue)
                        {
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Корабль все еще в космосе (летит в варпе).", LogType.Test);
                            continue;
                        }
                        else
                        {
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Интерфейс космоса закрылся. Ожидаю появление интерфейса станции...", LogType.Success);
                            spaceLeft = true;
                            // Сразу переходим к следующей итерации для проверки кнопки андока
                            continue;
                        }
                    }
                }
                // ЭТАП 2: Космос покинут, теперь ждем появление кнопки imgUndock1.png на станции
                else
                {
                    var (undockScreen, undockRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.ControlUndock, token);
                    if (undockScreen != null)
                    {
                        screenshotCaptured = true;
                        using var scope = undockScreen;
                        Point? foundUndock = await Task.Run(() => Tools.FindTemplateInRegion(undockScreen, pathImgUndock, undockRegion, 0.82), token);

                        if (foundUndock.HasValue)
                        {
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка imgUndock1 обнаружена в ComtrolUndock! Док подтвержден.", LogType.Success);

                            // Актуализируем стейт бота под архитектуру мемо
                            bot._inSpace = false;
                            bot._iswarping = false;
                            bot._isUndocking = true; // Выставляем флаг строго по вашему указанию

                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Стыковка и прогрузка станции успешно завершены.", LogType.Success);
                            return NodeStatus.Success; // Успешно завершаем узел
                        }
                        else
                        {
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Экран станции загружается. Кнопка андока еще не появилась.", LogType.Test);
                            // Продолжаем цикл ожидания (еще +5 секунд на следующей итерации)
                            continue;
                        }
                    }
                }

                // Предохранитель на случай сбоя захвата экрана (если PrepareScreenshotRegionAsync вернул null)
                if (!screenshotCaptured)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Предупреждение: Не удалось захватить целевой регион интерфейса. Повторная попытка.", LogType.Warning);
                }
            } // Конец цикла while(true)
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Исключение в макросе варпа на базу: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsNotInMiningZoneAsync

    /// <summary>
    /// Проверяет, что корабль еще НЕ находится непосредственно в зоне добычи (на белте).
    /// </summary>
    private static async Task<NodeStatus> CheckIsNotInMiningZoneAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Проверяю, находится ли корабль в зоне добычи.", LogType.Info);

        string pathAsteroid = Path.Combine(Program.TemplatesDir, "imgAsteroid1.png");

        try
        {
            // --- ШАГ 1: В регионе GridFilter ищем imgAsteroid1.png ---
            var (screenshot, filterRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridFilter, token);
            if (screenshot != null)
            {
                using var scope = screenshot;
                Point? foundAsteroid = await Task.Run(() => Tools.FindTemplateInRegion(screenshot, pathAsteroid, filterRegion, 0.82), token);

                // --- ШАГ 2: Нашли — мы в зоне добычи ---
                if (foundAsteroid.HasValue)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Маркер астероида найден. Корабль находится в зоне добычи.", LogType.Success);
                    bot._isinminingzone = true; // Синхронизируем внутренний флаг аккаунта

                    return NodeStatus.Failure; // Возвращаем Failure, так как это проверка "IsNotInMiningZone"
                }
            }

            // --- ШАГ 3: Не нашли — корабль НЕ в зоне добычи ---
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Маркер астероида не обнаружен. Корабль НЕ в зоне добычи.", LogType.Warning);
            bot._isinminingzone = false;

            return NodeStatus.Success; // Возвращаем Success, подтверждая, что мы еще НЕ в зоне добычи
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Исключение при проверке зоны добычи: {ex.Message}", LogType.Error);
            // В случае ошибки возвращаем текущее состояние флага как безопасный фолбек
            return !bot._isinminingzone ? NodeStatus.Success : NodeStatus.Failure;
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIfAlreadyWarpingAsync

    // [ ] TODO 2026.06.14 Сделать проверку варпа и использоваь в дереве 
    /// <summary>
    /// Универсальный метод проверки нахождения корабля в режиме варпа по маркеру размерности скорости (AU/s или АЕ/с).
    /// </summary>
    private static async Task<bool> IsShipInWarpDriveAsync(ActiveBotAccount bot, CancellationToken token)
    {
        try
        {
            // Вырезаем регион спидометра под капаситором
            var (screenshot, speedRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.ShipControl, token);
            if (screenshot == null) return false;

            using var scope = screenshot;

            // 1. Вырезаем подматрицу региона скорости
            using Mat speedMat = new(screenshot, speedRegion);

            // 2. Переводим в оттенки серого
            using Mat grayMat = new();
            Cv2.CvtColor(speedMat, grayMat, ColorConversionCodes.BGR2GRAY);

            // 3. БИНАРИЗАЦИЯ: Отсекаем полупрозрачный фон. 
            // Все пиксели ярче 200 (белый шрифт) станут 255 (чисто белый), остальное — 0 (чисто черный).
            using Mat binaryMat = new();
            Cv2.Threshold(grayMat, binaryMat, 200, 255, ThresholdTypes.Binary);

            // 4. Переводим очищенную черно-белую матрицу в байты для Tesseract
            byte[] imageBytes = binaryMat.ToBytes(".png");

            // ИСПРАВЛЕНО: Вызываем реальный потокобезопасный синглтон OcrService.Instance и его метод RecognizeText
            string recognizedText = await Task.Run(() =>
                EVEEchoesBot.resources.OcrService.Instance.RecognizeText(imageBytes), token);

            if (!string.IsNullOrWhiteSpace(recognizedText))
            {
                // Переводим в нижний регистр для надежного сопоставления
                string lowerText = recognizedText.ToLower();

                // Проверяем наличие английского маркера астрономических единиц или косой черты
                if (lowerText.Contains("au/s"))
                {
#if DEBUG
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] OCR детекция варпа: '{recognizedText.Trim()}'. Маркер варпа подтвержден.", LogType.Test);
#endif
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка при OCR проверке спидометра: {ex.Message}", LogType.Warning);
            return false;
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsBeltAlreadySelectedAsync

    // [ ] TODO 2026.06.14 Сделать проверку 
    /// <summary>
    /// Проверяет, выбран ли уже астероидный пояс в качестве текущей цели движения.
    /// </summary>
    private static Task<NodeStatus> CheckIsBeltAlreadySelectedAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Сделать проверку (ЗАГЛУШКА).", LogType.Warning);
        // Исправлено: проверяем не просто на null, а на то, что это реальный текстовый маркер пояса
        string? targetStr = null;
        bool isBelt = !string.IsNullOrEmpty(targetStr) && targetStr.Contains("Belt", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(isBelt ? NodeStatus.Success : NodeStatus.Failure);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region SelectAsteroidBeltAsync

    /// <summary>
    /// Выполняет поиск доступных астероидных поясов в овервью, выбирает случайный,
    /// инициирует разгон/подлет и сохраняет его физические координаты в память бота.
    /// </summary>
    /// <summary>
    /// Выполняет приоритетный выбор астероидного пояса: Лунные (Тип 1) -> Плотные (Тип 2) -> Обычные (Тип 3).
    /// </summary>
    private static async Task<NodeStatus> SelectAsteroidBeltAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Инициирована процедура приоритетного выбора астероидного пояса.", LogType.Info);

        string pathOreBelts = Path.Combine(Program.TemplatesDir, "imgOreBelts.png");
        string pathApproach = Path.Combine(Program.TemplatesDir, "imgApproach.png");
        string pathMining1 = Path.Combine(Program.TemplatesDir, "imgMining1.png");
        string pathMining2 = Path.Combine(Program.TemplatesDir, "imgMining2.png");

        try
        {
            // --- ЭТАП 1: Проверка и активация вкладки овервью ---
            var (screenshot, filterRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridFilter, token);
            if (screenshot != null)
            {
                using var filterScope = screenshot;
                OpenCvSharp.Point? foundFilter = await Task.Run(() => Tools.FindTemplateInRegion(screenshot, pathOreBelts, filterRegion, 0.82), token);

                if (foundFilter.HasValue)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Фильтр белтов найден в GrifFilter. Активирую вкладку.", LogType.Info);
                    await bot.ClickPointAsync(foundFilter.Value, token);
                    await Task.Delay(1500, token);
                }
                else
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Фильтр белтов в GrifFilter не найден. Настройка грида (Шаг 8).", LogType.Warning);
                    return await ConfigureGridSettingsAsync(bot, pathMining1, pathMining2, token);
                }
            }

            // --- ЭТАП 2: Каскадный поиск по приоритетам в GridList ---
            var (gridScreenshot, gridRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridList, token);
            if (gridScreenshot == null) return NodeStatus.Failure;

            using var gridScope = gridScreenshot;
            OpenCvSharp.Point? selectedBeltPoint = null;

            // ИСПРАВЛЕНО: Адаптировано под ваш скриншот овервью. 
            // Высота строки списка составляет ~85 пикселей.
            const int rowHeight = 85;

            // Смещение сверху (около 15 пикселей), чтобы не захватывать черную разделительную 
            // линию и нижнюю часть плашки заголовка "Mining v"
            const int topOffset = 15;

            int maxRows = (gridRegion.Height - topOffset) / rowHeight;

            // --- ТИП 1: Поиск надписи "Moon Astroid" через OCR строк ---
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] [Приоритет 1] Ищу лунные астероиды 'Moon Astroid'...", LogType.Info);
            selectedBeltPoint = await ScanGridListForTextAsync(gridScreenshot, gridRegion, rowHeight, maxRows, topOffset, "moon", token);

            // --- ТИП 2: Поиск надписи "Condensed" (если не нашли Лунные) ---
            if (selectedBeltPoint == null)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] [Приоритет 2] Лунные не найдены. Ищу плотные астероиды 'Condensed'...", LogType.Info);
                selectedBeltPoint = await ScanGridListForTextAsync(gridScreenshot, gridRegion, rowHeight, maxRows, topOffset, "condensed", token);
            }


            // --- ТИП 3: Поиск обычной картинки imgOreBelts.png (если текстовые пресеты пусты) ---
            if (selectedBeltPoint == null)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] [Приоритет 3] Текстовые пресеты не найдены. Ищу обычный пояс по шаблону...", LogType.Info);
                List<OpenCvSharp.Point> foundBelts = await Task.Run(() => Tools.FindAllTemplatesInRegion(gridScreenshot, pathOreBelts, gridRegion, 0.82), token);

                if (foundBelts?.Count > 0)
                {
                    Random rand = new();
                    selectedBeltPoint = foundBelts[rand.Next(foundBelts.Count)];
                }
            }

            // --- ЭТАП 3: Обработка выбранной цели и клик по imgApproach ---
            if (selectedBeltPoint != null)
            {
                OpenCvSharp.Point targetBelt = selectedBeltPoint.Value;
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Пояс успешно выбран. Выполняю клик по строке списка.", LogType.Info);
                await bot.ClickPointAsync(targetBelt, token);
                await Task.Delay(1500, token);

                var (actionScreenshot, actionRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.SpaceActions, token);
                if (actionScreenshot != null)
                {
                    using var actionScope = actionScreenshot;
                    OpenCvSharp.Point? foundApproach = await Task.Run(() => Tools.FindTemplateInRegion(actionScreenshot, pathApproach, actionRegion, 0.82), token);

                    if (foundApproach.HasValue)
                    {
                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка разгона imgApproach обнаружена. Инициирую сближение.", LogType.Success);
                        await bot.ClickPointAsync(foundApproach.Value, token);

                        // Потокобезопасно сохраняем OpenCV-точку цели под локом бота
                        lock (bot)
                        {
                            bot._currenttarget = targetBelt;
                        }

                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Координаты пояса зафиксированы в памяти: {targetBelt.X}x{targetBelt.Y}.", LogType.Success);
                        return NodeStatus.Success;
                    }
                    else
                    {
                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Пояс выбран, но кнопка разгона imgApproach не появилась.", LogType.Error);
                        return NodeStatus.Failure;
                    }
                }
            }
            else
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] В GridList не найдено ни одного пояса. Проверяю пресеты (Шаг 8).", LogType.Warning);
                return await ConfigureGridSettingsAsync(bot, pathMining1, pathMining2, token);
            }

            return NodeStatus.Failure;
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Критическая ошибка в макросе выбора белты: {ex.Message}", LogType.Error);
            return NodeStatus.Failure;
        }
    }

    /// <summary>
    /// Вспомогательный метод построчного OCR-сканирования региона GridList для поиска ключевого слова.
    /// </summary>
    private static async Task<OpenCvSharp.Point?> ScanGridListForTextAsync(
        Mat gridScreenshot,
        OpenCvSharp.Rect gridRegion,
        int rowHeight,
        int maxRows,
        int topOffset,
        string keyword,
        CancellationToken token)
    {
        for (int i = 0; i < maxRows; i++)
        {
            token.ThrowIfCancellationRequested();

            // ИСПРАВЛЕНО: Рассчитываем прямоугольник текущей строки списка с учетом верхнего отступа
            int rowY = gridRegion.Y + topOffset + (i * rowHeight);
            if (rowY + rowHeight > gridScreenshot.Height) break;

            OpenCvSharp.Rect rowRect = new(gridRegion.X, rowY, gridRegion.Width, rowHeight);

            using Mat rowMat = new(gridScreenshot, rowRect);
            using Mat grayMat = new();
            Cv2.CvtColor(rowMat, grayMat, ColorConversionCodes.BGR2GRAY);

            // Бинаризация для очистки текста от полупрозрачного фона овервью
            using Mat binaryMat = new();
            Cv2.Threshold(grayMat, binaryMat, 180, 255, ThresholdTypes.Binary);

            byte[] imageBytes = binaryMat.ToBytes(".png");
            string text = await Task.Run(() => EVEEchoesBot.resources.OcrService.Instance.RecognizeText(imageBytes), token);

            // ИСПРАВЛЕНО: Высокопроизводительное сравнение строк без выделения памяти через OrdinalIgnoreCase
            if (!string.IsNullOrWhiteSpace(text) && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                // Вычисляем точку центра этой конкретной строки в глобальных координатах экрана
                int centerX = gridRegion.X + (gridRegion.Width / 2);
                int centerY = rowY + (rowHeight / 2);

#if DEBUG
                Logger.Log($"[OCR DEBUG] Найдено совпадение '{keyword}' в строке #{i + 1}. Координаты клика: {centerX}x{centerY}", LogType.Test);
#endif
                return new OpenCvSharp.Point(centerX, centerY);
            }

        }
        return null;
    }


    /// <summary>
    /// Вспомогательный метод для обработки шагов 8-12 (Переключение пресетов овервью / настроек грида).
    /// </summary>
    private static async Task<NodeStatus> ConfigureGridSettingsAsync(ActiveBotAccount bot, string pathMining1, string pathMining2, CancellationToken token)
    {
        // --- ШАГ 8 & 9: В регионе GridList ищем картинку imgMining1.png ---
        var (checkScreenshot, checkRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridList, token);
        if (checkScreenshot != null)
        {
            using var scope = checkScreenshot;
            Point? foundMining1 = await Task.Run(() => Tools.FindTemplateInRegion(checkScreenshot, pathMining1, checkRegion, 0.82), token);

            if (foundMining1.HasValue)
            {
                // Если нашли — значит, вкладка правильная (майнинг), но астероидов физически нет в системе
                // --- ЗАГЛУШКА: Ожидание респавна или смена системы ---
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] [ЗАГЛУШКА] Обнаружен маркер imgMining1. Белтов в системе нет. Вхожу в режим ожидания 10 сек.", LogType.Warning);
                await Task.Delay(10000, token);
                return NodeStatus.Failure; // Возвращаем Failure, чтобы дерево повторило попытку на следующем тике
            }
        }

        // --- ШАГ 10: Если не нашли imgMining1, то нажимаем на точку GridSettings ---
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Настройки интерфейса сбиты. Кликаю по кнопке GridSettings.", LogType.Info);
        await bot.ClickToAsync(GameUI.GridSettings, token);
        await Task.Delay(2000, token); // Ждем открытия контекстного меню настроек

        // --- ШАГ 11: В области GridList ищем картинку imgMining2.png и нажимаем её ---
        var (settingsScreenshot, settingsRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.GridList, token);
        if (settingsScreenshot != null)
        {
            using var scope = settingsScreenshot;
            Point? foundMining2 = await Task.Run(() => Tools.FindTemplateInRegion(settingsScreenshot, pathMining2, settingsRegion, 0.82), token);

            if (foundMining2.HasValue)
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Найдена строка пресета imgMining2. Активирую корректный фильтр.", LogType.Success);
                await bot.ClickPointAsync(foundMining2.Value, token);
                await Task.Delay(2000, token); // Даем интерфейсу обновиться

                // --- ШАГ 12: Переходим к шагу 1 (Рекурсивный перезапуск этого же узла с обновленным фильтром) ---
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Фильтр успешно изменен. Перезапускаю процедуру поиска белты.", LogType.Info);
                return await SelectAsteroidBeltAsync(bot, token);
            }
            else
            {
                Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Меню GridSettings открыто, но пресет imgMining2 не найден.", LogType.Error);
                return NodeStatus.Failure;
            }
        }

        return NodeStatus.Failure;
    }



    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region WarpToSelectedBeltAsync

    /// <summary>
    /// Инициирует переход (варп) к выбранному астероидному поясу.
    /// </summary>
    /// <summary>
    /// Выполняет клик по сохраненным в памяти координатам пояса, инициирует варп через овервью
    /// и контролирует вход корабля в варп-туннель по приборам спидометра.
    /// </summary>
    private static async Task<NodeStatus> WarpToSelectedBeltAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Потокобезопасно извлекаем координаты выбранной белты из стейта аккаунта
        OpenCvSharp.Point? targetPoint;
        lock (bot)
        {
            targetPoint = bot._currenttarget;
        }

        if (targetPoint == null)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Попытка варпа к пустой цели. Сбрасываю узел.", LogType.Error);
            return NodeStatus.Failure;
        }

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Инициирую варп на сохраненный пояс. Координаты цели: {targetPoint.Value.X}x{targetPoint.Value.Y}.", LogType.Info);

        string pathWarp = Path.Combine(Program.TemplatesDir, "imgWarp.png");

        try
        {
            // --- ШАГ 1: Кликаем на выбранный пояс в овервью ---
            // Используем физические координаты OpenCV, сохраненные на этапе выбора
            await bot.ClickPointAsync(targetPoint.Value, token);
            await Task.Delay(1500, token); // Задержка на появление контекстного меню действий SpaceActions

            // --- ШАГ 2: В регионе SpaceActions (или GridList согласно ТЗ) ищем imgWarp.png и нажимаем на него ---
            // Примечание: Для контекстных кнопок действий овервью обычно используется GameRegions.SpaceActions, 
            // но метод PrepareScreenshotRegionAsync безопасно обработает переданный вами целевой регион.
            var (actionScreenshot, actionRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.SpaceActions, token);
            if (actionScreenshot != null)
            {
                using var actionScope = actionScreenshot; // Уникальное имя области видимости для исключения конфликтов Roslyn
                OpenCvSharp.Point? foundWarp = await Task.Run(() => Tools.FindTemplateInRegion(actionScreenshot, pathWarp, actionRegion, 0.82), token);

                if (foundWarp.HasValue)
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Кнопка imgWarp обнаружена. Отправляю команду на гиперпрыжок.", LogType.Info);
                    await bot.ClickPointAsync(foundWarp.Value, token);

                    // Взводим флаг полета для перевода RunLoopAsync на быстрый секундный мониторинг чата/локала
                    lock (bot)
                    {
                        bot._iswarping = true;
                    }

                    // --- ШАГ 3: Вызываем универсальный метод проверки ухода в варп по приборам AU/s ---
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Команда отдана. Начинаю валидацию разгона по спидометру...", LogType.Info);
                    bool warpConfirmed = false;

                    // Даем кораблю до 12 секунд на разгон (8 попыток с шагом в 1.5 секунды)
                    for (int check = 1; check <= 10; check++)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(3000, token);

                        // Вызываем наш готовый OCR-метод проверки единиц измерения скорости
                        if (await IsShipInWarpDriveAsync(bot, token))
                        {
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Прыжок подтвержден приборами! Корабль успешно вошел в варп-туннель.", LogType.Success);
                            warpConfirmed = true;
                            break;
                        }

                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Корабль набирает скорость. Ожидание варпа, попытка {check}/8...", LogType.Test);
                    }

                    if (!warpConfirmed)
                    {
                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Предупреждение: Прямой маркер AU/s не зафиксирован. Возможно, прыжок произошел мгновенно или наложился лаг отрисовки.", LogType.Warning);
                    }

                    // Согласно логике дерева, узел прыжка возвращает Success, передавая управление мониторингу прибытия
                    return NodeStatus.Success;
                }
                else
                {
                    Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Ошибка: Пояс выделен на экране, но кнопка варпа imgWarp не появилась.", LogType.Error);
                    return NodeStatus.Failure;
                }
            }

            return NodeStatus.Failure;
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Исключение в макросе варпа на выбранный белт: {ex.Message}", LogType.Error);

            // Сбрасываем флаг полета при критическом сбое
            lock (bot)
            {
                bot._iswarping = false;
            }
            return NodeStatus.Failure;
        }
    }



    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ClearFlightStateOnArrivalAsync

    /// <summary>
    /// Сбрасывает полетные данные и промежуточную цель варпа по прибытии в зону добычи.
    /// </summary>
    /// <summary>
    /// Контролирует выход из варпа по появлению и последующему исчезновению надписи "SHIP STOPPING".
    /// Сбрасывает полетный стейт и переводит бота в режим активной добычи на белте.
    /// </summary>
    private static async Task<NodeStatus> ClearFlightStateOnArrivalAsync(ActiveBotAccount bot, CancellationToken token)
    {
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Проверяю статус прибытия корабля из варпа.", LogType.Info);

        try
        {
            // 1. Захватываем регион управления кораблем, где появляется надпись торможения
            var (screenshot, controlRegion) = await bot.PrepareScreenshotRegionAsync(GameRegions.ShipControl, token);
            if (screenshot != null)
            {
                using var controlScope = screenshot;

                // 2. Вырезаем подматрицу региона плашки уведомлений
                using Mat controlMat = new(screenshot, controlRegion);

                // 3. Переводим в оттенки серого
                using Mat grayMat = new();
                Cv2.CvtColor(controlMat, grayMat, ColorConversionCodes.BGR2GRAY);

                // 4. БИНАРИЗАЦИЯ: Отсекаем полупрозрачный фон.
                // Буквы "SHIP STOPPING" плотные и светлые, порог 180 идеально сделает их чисто белыми, а фон - черным.
                using Mat binaryMat = new();
                Cv2.Threshold(grayMat, binaryMat, 180, 255, ThresholdTypes.Binary);

                // 5. Кодируем очищенную матрицу в байты формата PNG для Tesseract
                byte[] imageBytes = binaryMat.ToBytes(".png");

                // 6. Потокобезопасно распознаем текст через наш глобальный синглтон
                string recognizedText = await Task.Run(() =>
                    EVEEchoesBot.resources.OcrService.Instance.RecognizeText(imageBytes), token);

                if (!string.IsNullOrWhiteSpace(recognizedText))
                {
                    string lowerText = recognizedText.ToLower();

                    // Если надпись "ship stopping" или ее фрагменты все еще на экране — корабль в процессе торможения
                    if (lowerText.Contains("ship") || lowerText.Contains("stop"))
                    {
                        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Зафиксирован выход из варпа: '{recognizedText.Trim()}'. Корабль гасит скорость, ожидаю остановку...", LogType.Info);

                        // Удерживаем узел в состоянии Running, RunLoop вернется сюда на следующем тике
                        return NodeStatus.Running;
                    }
                }
            }

            // 7. Если текст "SHIP STOPPING" больше не обнаружен — значит, корабль полностью остановился на белте!
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Корабль успешно вышел из варпа и остановился. Синхронизирую стейт добычи.", LogType.Success);

            // Потокобезопасно обновляем полетные флаги и цель внутри инстанса бота под локом
            lock (bot)
            {
                bot._isinminingzone = true; // Мы прибыли на белт
                bot._iswarping = false;      // Варп полностью окончен
                bot._currenttarget = null;   // Очищаем полетную OpenCV цель
            }

            // Возвращаем Success, чтобы последовательность "Active Mining Sequence" перешла к следующему узлу (Захвату астероидов)
            return NodeStatus.Success;
        }
        catch (Exception ex)
        {
            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Исключение при очистке полетного стейта по прибытии: {ex.Message}", LogType.Error);

            // Фолбек предохранитель: в случае непредвиденного сбоя графики принудительно выводим бота в рабочую зону
            lock (bot)
            {
                bot._isinminingzone = true;
                bot._iswarping = false;
                bot._currenttarget = null;
            }
            return NodeStatus.Success;
        }
    }




    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIfMiningIsActiveAsync

    /// <summary>
    /// Проверяет, захвачен ли астероид в цель и активны ли буровые лазеры.
    /// </summary>
    private static Task<NodeStatus> CheckIfMiningIsActiveAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // TODO: [Заглушка] Реализовать метод проверки захвата
        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Проверяем захват цели (ЗАГЛУШКА).", LogType.Warning);
        // Исправлено: если лазеры уже горят на экране, возвращаем Success. 
        // Это защитит интерфейс эмулятора от попыток захватить новую цель при включенном оружии.
        return Task.FromResult(bot._weaponryactive ? NodeStatus.Success : NodeStatus.Failure);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region TargetAsteroidAsync

    /// <summary>
    /// Пытается захватить доступный астероид в качестве мишени.
    /// </summary>
    private static async Task<NodeStatus> TargetAsteroidAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._hastarget) return NodeStatus.Success;

        // TODO: [Заглушка] Реализовать выбор астероида в овервью и клик по кнопке "Захват цели" (Lock)
        await Task.Delay(1500, token); // Имитируем время на клики в интерфейсе

        // Взводим флаг успешного захвата цели
        bot._hastarget = true;

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Астероид успешно взят в лок (ЗАГЛУШКА).", LogType.Info);
        return NodeStatus.Success;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ActivateLasersAsync

    /// <summary>
    /// Активирует буровые лазеры на захваченную цель.
    /// </summary>
    private static async Task<NodeStatus> ActivateLasersAsync(ActiveBotAccount bot, CancellationToken token)
    {
        if (bot._weaponryactive) return NodeStatus.Success;

        // TODO: [Заглушка] Реализовать клики по иконкам майнинг-лазеров на панели корабля
        await Task.Delay(1000, token); // Имитируем время на прокликивание модулей

        // Взводим флаг успешного включения лазеров
        bot._weaponryactive = true;

        Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Буровые лазеры успешно активированы (ЗАГЛУШКА).", LogType.Success);
        return NodeStatus.Success;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region MiningMonitorStateAsync

    /// <summary>
    /// Точка удержания тика дерева во время активного процесса добычи руды.
    /// </summary>
    private static Task<NodeStatus> MiningMonitorStateAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Просто успешно завершаем узел, отдавая управление наверх для нового полного цикла проверок
        return Task.FromResult(NodeStatus.Success);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsPanicStateActiveAsync

    /// <summary>
    /// Проверяет, находится ли бот в состоянии экстренной паники/эвакуации.
    /// </summary>
    private static Task<NodeStatus> CheckIsPanicStateActiveAsync(ActiveBotAccount bot, CancellationToken _)
    {
        // ИСПРАВЛЕНО HIGH: Защита Наблюдателя от заклинивания в режиме эвакуации!
        // Если скрипт бота — LocalWatcher (Наблюдатель), и его CurrentTask по какой-то причине 
        // равен GoToStation, мы принудительно сбрасываем его в CheckSecurity прямо здесь.
        // Наблюдателю не нужно никуда лететь, он уже на месте!
        if (bot.Settings.Script?.ToLower() == "localwatcher" && bot.CurrentTask == AccountTask.GoToStation)
        {
            lock (bot._taskLock)
            {
                Logger.Log($"[{bot.Settings.Name}] Наблюдатель обнаружил заклинивший стейт паники на станции. Сбрасываю в CheckSecurity.", LogType.Info);
                bot.CurrentTask = AccountTask.CheckSecurity;
            }
        }

        // Штатная проверка флага эвакуации для остальных полетных кораблей (майнеров)
        bool isPanic = bot.CurrentTask == AccountTask.GoToStation;
        return Task.FromResult(isPanic ? NodeStatus.Success : NodeStatus.Failure);
    }



    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckIsNotUndockingAsync

    /// <summary>
    /// Проверяет, что бот в данный момент НЕ находится в процессе анимации андока (вылета со станции).
    /// Защищает интерфейс эмулятора от бешеного спама кликами по кнопке вылета.
    /// </summary>
    private static Task<NodeStatus> CheckIsNotUndockingAsync(ActiveBotAccount bot, CancellationToken token)
    {
        // Исправлено: корректно прерываем задачу дерева при отзыве токена из веб-панели
        token.ThrowIfCancellationRequested();

        // Если флаг _isUndocking равен true, значит кнопка уже нажата. 
        // Возвращаем Failure, чтобы дерево не пошло дальше по ветке андока в этот тик.
        return Task.FromResult(bot._isUndocking ? NodeStatus.Failure : NodeStatus.Success);
    }

    #endregion

}

// Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] ???", LogType.Test);