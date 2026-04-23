Settings:setCompareDimension(true, 1280)

Settings:setScriptDimension(true, 1280)

defaultWait = math.random(1, 5)        -- Время задержки действий в секундах. Рандом, чтобы имитировать действите человека

notificationTime = 2                   -- Время отображения уведомления

imageSearchWait = 2                    -- Время распознания изображений в секундах

statusRegion = Region(440,335,400,50)  -- Область, где отображаются уведомления

cargoCountDelivered = 0                -- Переменная для счётчика циклов

-- string concatenation example: 'hello' .. name;


-- Проверка в доке или в космосе, проверка по наличию кнопки выход из дока.
function isInDock() 

    existsClick("XCloseCargoMovement.png", imageSearchWait)

    statusRegion:highlight('Проверяем в доке или в космосе', notificationTime);

    undock = Region(1050,175,230,125):exists("Undock.png", imageSearchWait);

    if undock then 

        statusRegion:highlight('В доке (' .. tostring(undock) .. ')', notificationTime);

        return true;

    else

        statusRegion:highlight('В космосе (' .. tostring(undock) .. ')', notificationTime);

        return false;

    end

end

-- Проверяем полон ли грузовой отсек или нет
function isCargoFull() 

    statusRegion:highlight('Проверяем грузовой отсек', notificationTime);

    cargo = Region(0, 70, 122, 55):exists("100PCargo.png", imageSearchWait);

    if cargo then 

        statusRegion:highlight('Грузовой отсек полон', notificationTime);

        return true;

    else

        statusRegion:highlight('Грузовой отсек пуст', notificationTime);

        return false;

    end

end



function activateNewShip()

    statusRegion:highlight('Открываем ангар', notificationTime);

    cargo = Region(3, 68, 120, 58):existsClick("Cargo.png", imageSearchWait);

    if cargo then

        Region(10, 244, 256, 103):existsClick("ShipHangar.png", imageSearchWait);

        newShip = existsClick("Covetor.png", imageSearchWait); -- Добавить другие типы 

        if newShip then

            statusRegion:highlight('Берём новый корабль', notificationTime);

            activate = existsClick("SetActive.png", imageSearchWait);

            if activate then 

                wait(5);

                existsClick("XCloseCargoMovement.png", imageSearchWait);

                return true;

            else

                return false;

            end

        else

            statusRegion:highlight('Нет свободных кораблей', notificationTime); 

        end

    else

        return false;

    end

end



function isCapsule(space)

    statusRegion:highlight('Проверяем наличие корабля', notificationTime);

    fitting = Region(145, 75, 115, 45):existsClick("FitIcon.png", imageSearchWait);

    wait(defaultWait);

    if fitting then

        capsule = Region(8, 166, 213, 80):exists("Capsule.png", imageSearchWait);

        if capsule then

            if not Region(1206, 12, 54, 53):existsClick("XCloseCargoMovement.png", imageSearchWait) then

                statusRegion:highlight('Что-то пошло не так...', notificationTime);

            end

            statusRegion:highlight('У нас капсула, видимо нас убили..', notificationTime);

            if space then

                goToStation();       

            end

            activated = activateNewShip();

            if activated then

                statusRegion:highlight('Переключили корабль', notificationTime);

            else

                scriptExit('Корабль уничтожен, остановка скрипта')

            end

        else

            statusRegion:highlight('Все в порядке, продолжаем добывать', notificationTime);

            if not Region(1206, 12, 54, 53):existsClick("XCloseCargoMovement.png", imageSearchWait) then

                statusRegion:highlight('Что-то пошло не так...', notificationTime);

            end

        end

    else

        statusRegion:highlight('Не удалось проверить наличие корабля', notificationTime);

    end

end



function moveCargo() 

    statusRegion:highlight('Перемещаем из грузового отсека', notificationTime);

    if Region(0, 70, 122, 55):existsClick("100PCargo.png", imageSearchWait) then

        wait(defaultWait)
        
        -- [ ] Добавить коллапс меню станции

        if Region(14, 513, 248, 108):existsClick("OptOreHold.png", imageSearchWait) then

            wait(defaultWait)

            if Region(926, 599, 116, 106):existsClick("SelectAll.png", imageSearchWait) then

                wait(defaultWait)

                if Region(16, 103, 251, 87):existsClick("MoveOreTo.png", imageSearchWait) then

                    wait(defaultWait)

                    if Region(181, 119, 272, 89):existsClick("ItemHanger.png", imageSearchWait) then

                        wait(defaultWait)

                        if Region(1206, 12, 54, 53):existsClick("XCloseCargoMovement.png", imageSearchWait) then

                            cargoCountDelivered = cargoCountDelivered + 1
                            statusRegion:highlight('Выгрузили руду #' .. cargoCountDelivered, notificationTime);

                        else

                            statusRegion:highlight('Что-то пошло не так...', notificationTime); -- [ ] Расписать ошибки подробнее 

                        end

                    else

                        statusRegion:highlight('Что-то пошло не так...', notificationTime);

                    end

                else

                    statusRegion:highlight('Что-то пошло не так...', notificationTime);

                end

            else

                statusRegion:highlight('Что-то пошло не так...', notificationTime);

            end

        else

            statusRegion:highlight('Что-то пошло не так...', notificationTime);

        end

    else

        statusRegion:highlight('Что-то пошло не так...', notificationTime);

    end

end



function openMenuIfNeed()

    statusRegion:highlight('Проверяем открыто ли меню', notificationTime);

    menu = Region(1207, 378, 60, 53):exists("EyeIcon.png", imageSearchWait);

    if menu then 

        statusRegion:highlight('Меню закрыто, открываем', notificationTime);

        if not Region(1207, 378, 60, 53):existsClick("EyeIcon.png", imageSearchWait) then

            statusRegion:highlight('Не удалось открыть меню', notificationTime);

        end

    else 

        statusRegion:highlight('Меню открыто', notificationTime);

    end

end



function openBeltsList(needToGo)

    if not Region(969,1,194,58):exists("MiningCurrent.png") or not Region(966,55,28,662):exists("SelectOreToMine.png") then

            dropdownMenu = Region(1104,19,48,23):existsClick("ShowDropdownMy.png", imageSearchWait)

            if dropdownMenu then

                belts = Region(970,515,248,89):existsClick("MinigScreenRu.png", imageSearchWait)

                if belts then

                    if needToGo then

                        return goToRandomBelt();

                    else

                        return true;

                    end;

                else

                    statusRegion:highlight('Не удалось выбрать пункт добычи', notificationTime);

                    return false

                end

            else

                statusRegion:highlight('Не удалось открыть меню', notificationTime);

                return false

            end

        else

            return true;

    end

end



function goToRandomBelt() 

    belts = listToTable(Region(967,51,28,382):findAllNoFindException("MiningLocation.png"));

    while not belts do

        wait(defaultWait)

    end

    statusRegion:highlight('Найдено ' .. table.getn(belts) .. ' полей астероидов', notificationTime);

    if typeOf(table.getn(belts)) == "number" and not table.getn(belts) == 0 then

        number = table.getn(belts);

    else

        number = 2;

    end

    wait(imageSearchWait);

    randomBeltNumber = math.random(number - 1);

    statusRegion:highlight('Выбран ' .. randomBeltNumber .. ' пояс', notificationTime);

    for i, m in ipairs(belts) do

        if(i == randomBeltNumber) then

            click(m);

        end

    end

    wait(defaultWait)

    if Region(704,40,268,522):existsClick("warp.png") then 

        statusRegion:highlight('Варп к поясу', notificationTime);

        wait(10)

        return true;

    else

        statusRegion:highlight('Не удалось отправиться к поясу', notificationTime);

        return false

    end

end



function mining()

    if not Region(966,55,28,662):exists("SelectOreToMine.png") then

        onTheBelt = openBeltsList(true);

    else

        statusRegion:highlight('Уже на поле астероидов', notificationTime);

        onTheBelt = true; 

    end

    if onTheBelt then

        while not isCargoFull() or Region(1050,200,220,65):exists("Undock.png", imageSearchWait) do

            if not Region(428,32,532,132):exists("MineProc.png") then

                openBeltsList(false);

                Region(1222,52,53,64):existsClick("filterOre.png", imageSearchWait)

                --wait(defaultWait)

                Region(966,50,25,596):existsClick("SelectOreToMine.png", imageSearchWait)

                --wait(1)

                selectedOre = selectOre();

                if not selectedOre then

                    break;

                end

                -- wait(defaultWait)

                ore = Pattern("ore.png"):similar(0.8)

                if Region(500,19,463,107):existsClick(ore, imageSearchWait) then

                   -- wait(defaultWait)
                    
                    if existsClick("Approach.png") then
                    --if existsClick("Orbit.png") then

                        --wait(defaultWait)

                        while not Region(428,32,532,132):exists("MineProc.png", imageSearchWait) do

                            statusRegion:highlight('Пытаемся начать добычу', notificationTime); 

                            miner = findAllNoFindException("StartMine.png", imageSearchWait)

                            for i, m in ipairs(miner) do

                                click(m)

                            end

                        end

                    end

                end

            else

                stopMining = hasAnotherShips();

                if stopMining then

                    break;

                end

                statusRegion:highlight('Идёт добыча #' .. cargoCountDelivered + 1, notificationTime); 

            end

        end

        goToStation()

    else

        statusRegion:highlight('Не удалось отправиться к поясу', notificationTime); 

    end

end



function hasAnotherShips() 

   -- statusRegion:highlight('Проверяем есть ли корабли', notificationTime);

   -- openShipMenuIfNeed();

    if not Region(0,400,400,320):exists("NoShipFound.png", imageSearchWait) then -- and not Region(1221,55,56,575):exists("Barge.png") then

        statusRegion:highlight('ТРЕВОГА!!! Отправляюсь на станцию', notificationTime);

        goToStation();

        AlliChatMessage()

        wait(60);

        return true;

    else

       statusRegion:highlight('Мы одни, продолжаем добывать', notificationTime);

       return false;        

    end

end

function AlliChatMessage()

    click(25, 6225)

    if Region(0,20,261,700):exists("AllianceChat.png", imageSearchWait) or Region(0,20,261,700):exists("AllianceChatActive.png", imageSearchWait) then

        Region(300,630,100,90):existsClick("AllianceChatActive.png", imageSearchWait)

        Region(1050,630,230,90):existsClick("InformButton.png", imageSearchWait)

        Region(5,295,195,300):existsClick("Intelligence.png", imageSearchWait)

        Region(190,400,300,200):existsClick("WarningMessage.png", imageSearchWait)

        Region(360,200,200,100):existsClick("SendButton.png", imageSearchWait)

    else

        statusRegion:highlight('Что-то пошло не так...', notificationTime);

    end

end

--[[

function openShipMenuIfNeed()

    if Region(969,1,312,620):exists("ShipMenuCurrent.png", imageSearchWait) then

        statusRegion:highlight('Пункт корабль уже открыт', notificationTime);

    else    

        dropdownMenu = Region(1104,19,48,23):existsClick("ShowDropdownMy.png", imageSearchWait)

        if dropdownMenu then

            ships = Region(970,1,312,620):existsClick("ShipMenu.png", imageSearchWait)

            if ships then

                statusRegion:highlight('Открыл меню', notificationTime);

                return true

            else

                statusRegion:highlight('Не удалось выбрать пункт Корабли', notificationTime);

                return false

            end

        else

            statusRegion:highlight('Не удалось открыть меню', notificationTime);

            return false

        end

    end

end

--]]

-- [ ] Продолжить

function goToStation() 

    dropdownMenu = Region(1104,19,48,23):existsClick("ShowDropdownMy.png", imageSearchWait)

    if dropdownMenu then

        stationList = Region(520,4,738,702):existsClick("MenuStation.png", imageSearchWait)

        if stationList then

            if Region(520,4,738,702):existsClick("StationLocation.png", imageSearchWait) then

                wait(defaultWait)

                if Region(520,4,738,702):existsClick("OptDock.png", imageSearchWait) then

                    -- [ ] Добавить прожимку ежей

                    statusRegion:highlight('Отправляемся к станции', notificationTime); 

                end

            end

            while not exists("Undock.png", imageSearchWait) do

                wait(defaultWait)

            end

        else

            statusRegion:highlight('Не удалось выбрать станцию', notificationTime);

            return false

        end

    else

        statusRegion:highlight('Не удалось открыть меню', notificationTime);

        return false

    end

end



function selectOre() 

    moveToRandom = Region(966,50,25,596):existsClick("SelectOreToMine.png");

    if moveToRandom then

        existsClick("Orbit.png");

    end

    selected = Region(963,52,312,656):existsClick("oreSpodumain.png")

    if selected then

        wait(defaultWait)

        if not existsClick("MineLock.png") then

            statusRegion:highlight('Не удалось заблокировать астероид', notificationTime); 

            goToStation(); 

            return false;

        end

    else

        selected2 = Region(963,52,312,656):existsClick("oreDarkOchre.png")

        if selected2 then

            wait(defaultWait)

            if not existsClick("MineLock.png") then

                statusRegion:highlight('Не удалось заблокировать астероид', notificationTime); 

                goToStation();

                return false; 

            end

        else

            selected3 = Region(966,50,25,596):existsClick("SelectOreToMine.png")

            if selected2 then

                wait(defaultWait)

                if not existsClick("MineLock.png") then

                    statusRegion:highlight('Не удалось заблокировать астероид', notificationTime);

                    goToStation(); 

                    return false;

                end

            end

        end

    end

    Region(1222,52,53,64):existsClick("filterOre.png", imageSearchWait)

    wait(defaultWait)

    ore = Pattern("ore.png"):similar(0.8)

    if Region(500,19,463,107):existsClick(ore, imageSearchWait) then 

        statusRegion:highlight('Заблокирован астероид', notificationTime); 

    else

        isCapsule(true)

    end

    return true;

end



while true do   

    if isInDock() then

        isCapsule(false);

        if isCargoFull() then 

            moveCargo()

        end

        wait(defaultWait)

        Region(1050,200,220,65):existsClick("Undock.png", imageSearchWait)

        statusRegion:highlight('Выходим из дока..', notificationTime);

        wait(10)

    else

        openMenuIfNeed()

        wait(defaultWait)

        mining()

    end

    wait(defaultWait);

end