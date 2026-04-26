; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer                                                                                       #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.25                                                                                                 #
; # FILENAME.......: libImageSearch.au3                                                                                         #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Автоматизированный комплекс управления добычей руды.                                                       #
; #                  - Поддержка многопоточности графического интерфейса (OnEventMode)                                          #
; #                  - Интеллектуальное распознавание образов (ImageSearch)                                                     #
; #                  - Сохранение прогресса в INI-конфигурацию                                                                  #
; #                                                                                                                             #
; ###############################################################################################################################
; #                                                                                                                             #
; # ТРЕБОВАНИЯ:                                                                                                                 #
; # - Разрешение игры : 1280 x 720                                                                                              #
; # - Android Эмулятор: Memu                                                                                                    #
; # -                 :                                                                                                         #
; #                                                                                                                             #
; ###############################################################################################################################
; #                                                                                                                             #
; # ПЕРСОНАЖ / КОРАБЛЬ:                                                                                                         #
; # -                                                                                                                           #
; #                                                                                                                             #
; #                                                                                                                             #                                                                                                                            #
; #                                                                                                                             #
; ###############################################################################################################################

#include <GUIConstantsEx.au3>    ; Содержит $GUI_EVENT_CLOSE
#include <WindowsConstants.au3>  ; Содержит $WS_EX_TOPMOST
#include <StaticConstants.au3>   ; Содержит константы для стилей текста (Label)
#include <libImageSearch.au3>    ; Содержит функции поиска изображений на экране
#include <libUtility.au3>        ; Содержит общий и служебные функции 

; ===============================================================================================================================
; 1. РЕСУРСЫ И ИНИЦИАЛИЗАЦИЯ ПАПОК
; ===============================================================================================================================

; Создаем временную папку для ресурсов во временном каталоге системы
Global $sResourceDir = @TempDir & "\MyBotResources\"
DirCreate($sResourceDir)

; Вшиваем необходимые файлы внутрь EXE (распаковываются при запуске)
FileInstall("JetBrainsMono-Bold.ttf", $sResourceDir & "JetBrainsMono-Bold.ttf", 1)
FileInstall("ImageSearchDLL.dll", @SystemDir & "\ImageSearchDLL.dll", 1) 
FileInstall("imgUnDock.bmp", $sResourceDir & "imgUnDock.bmp", 1)
FileInstall("100PCargo.png", $sResourceDir & "100PCargo.png", 1)
FileInstall("EyeIcon.png", $sResourceDir & "EyeIcon.png", 1)

; !!! Сюда добавьте остальные FileInstall для всех ваших BMP/PNG файлов !!!

; Регистрация шрифта JetBrains Mono в системе на время работы скрипта
Global $hFontRes = DllCall("gdi32.dll", "int", "AddFontResourceEx", "str", $sResourceDir & "JetBrainsMono-Bold.ttf", "dword", 0x10, "int", 0)

; ===============================================================================================================================
; 2. НАСТРОЙКИ СКРИПТА (OPTIONS)
; ===============================================================================================================================

Opt("MouseCoordMode", 2)       ; Координаты мыши относительно клиентской области окна
Opt("PixelCoordMode", 2)       ; Координаты пикселей относительно клиентской области окна
Opt("SendKeyDownDelay", 50)    ; Задержка удержания клавиш (мс) для защиты от "проглатывания" игрой
Opt("GUIOnEventMode", 1)       ; Включение режима событий для работы интерфейса

; ===============================================================================================================================
; 3. ГЛОБАЛЬНЫЕ ПЕРЕМЕННЫЕ
; ===============================================================================================================================

; --- Пути и файлы ---
Global $sIniPath = @ScriptDir & "\settings.ini" ; Файл настроек рядом с исполняемым файлом

; --- Данные окна ---
Global $ClientName = "(Client W.01)"            ; Заголовок окна игры
Global $Client     = 0                          ; Хэндл окна (будет заполнен позже)

; --- Игровые параметры ---
Global $InSpace    = False                      ; Статус: в космосе или нет
Global $IsSave     = False                      ; Статус: безопасность (враги/щит)

; --- Статистика ---
; Загружаем сохраненный счетчик из INI (по умолчанию "0" если файл не найден)
Global $DeliveredCount = Int(IniRead($sIniPath, "Statistics", "OreCount", "0"))

; --- Оформление ---
Global $sFontFace = "JetBrains Mono"            ; Основной шрифт интерфейса

; --- Режим отладки ---
Global $Debug = false

; ===============================================================================================================================
; 4. ИНТЕРФЕЙС УПРАВЛЕНИЯ (GUI)
; ===============================================================================================================================

Global $hStatusGUI = GUICreate("Mining Control", 320, 160, 20, 20, -1, $WS_EX_TOPMOST)
GUISetOnEvent($GUI_EVENT_CLOSE, "_Terminate")

; Текстовое поле статуса
Global $hStatusLabel = GUICtrlCreateLabel("Бот готов", 10, 15, 300, 45)
GUICtrlSetFont(-1, 9, 400, 0, $sFontFace)

; Текстовое поле счетчика руды
Global $hCountLabel = GUICtrlCreateLabel("Выгружено: " & $DeliveredCount, 10, 65, 300, 30)
GUICtrlSetFont(-1, 11, 800, 0, $sFontFace)
GUICtrlSetColor(-1, 0x008800) ; Зеленый цвет для прогресса

; Кнопка принудительной остановки
Global $btnStop = GUICtrlCreateButton("STOP BOT", 60, 105, 200, 40)
GUICtrlSetFont(-1, 10, 800, 0, $sFontFace)
GUICtrlSetBkColor(-1, 0xFFCCCC) ; Светло-красный фон кнопки
GUICtrlSetOnEvent($btnStop, "_Terminate")

; Показываем окно, не делая его активным (чтобы не мешать игре)
GUISetState(@SW_SHOWNOACTIVATE, $hStatusGUI)

; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

; - + - + - + - + - | Функция выхода из дока | - + - + - + - + - + - + - + - + - + - + - + - + - + - + - 

Func _Undock()

    ; 1. Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("_Undock: Ошибка - Клиент не найден или не активен")
        Return False
    EndIf

    ; 2. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($ClientName)
    If @error Then 
        _Log("_Undock: Ошибка - Не удалось получить координаты клиента")
        Return False
    EndIf

    ; 3. Вычисляем область поиска
    Local $aArea[4] ; Явно объявляем массив на 4 элемента
    $aArea[0] = $aCPos[0] + 1060 ; X1
    $aArea[1] = $aCPos[1] + 230  ; Y1
    $aArea[2] = $aCPos[0] + 1280 ; X2
    $aArea[3] = $aCPos[1] + 300  ; Y2
    
    Local $x, $y

    ; 4. Поиск изображения кнопки Undock
    If _MyImageSearch("imgUnDock.bmp", $sResourceDir, $aArea, $x, $y, 100) = 1 Then
        _Log("_Undock: Кнопка 'Undock' найдена. Выходим в космос")
        _HumanSleep()      
        Send("{SC016}")
        Return True
    EndIf


    _Log("_Undock: Ошибка - Кнопка не найдена")
    Return False

EndFunc

; - + - + - + - + - | Функция проверки карго | - + - + - + - + - + - + - + - + - + - + - + - + - + - + - 

Func IsCargoFull()

    ; 1. Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("IsCargoFull: Ошибка - Клиент не найден")
        Return False
    EndIf

    ; 2. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($ClientName)
    If @error Then 
        _Log("IsCargoFull: Ошибка - Не удалось получить координаты клиента")
        Return False
    EndIf

    ; 3. Вычисляем область поиска
    Local $aArea
    $aArea[0] = $aCPos[0] + 0   ; X1
    $aArea[1] = $aCPos[1] + 80  ; Y1
    $aArea[2] = $aCPos[0] + 60  ; X2
    $aArea[3] = $aCPos[1] + 170 ; Y2

    _Log("IsCargoFull: Проверяем грузовой отсек...")

    Local $x, $y
    ; 4. Поиск изображения 100% карго с использованием обновленной функции
    If _MyImageSearch("imgCargoFull.bmp", $sResourceDir, $aArea, $x, $y, 100) = 1 Then
        _Log("IsCargoFull: Грузовой отсек полон")
        Return True
    EndIf


    _Log("IsCargoFull: Грузовой отсек еще не полон")
    Return False

EndFunc

; - + - + - + - + - | Функция перемещения добытой руды на станцию | - + - + - + - + - + - + - + - + - + - 

Func _MoveCargo()

    ; Шаг 1: Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("_MoveCargo: Ошибка - Клиент не найден")
        Return False
    EndIf

    ; Шаг 2: Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($ClientName)
    If @error Then 
        _Log("_MoveCargo: Ошибка - Не удалось получить координаты клиента")
        Return False
    EndIf

    _Log("_MoveCargo: Перемещаем из грузового отсека...")

    ; Шаг 4: Нажимаем "1", чтобы открыть инвентарь
    _HumanSleep()
    Send("{SC002}") 

    ; Шаг 5: Нажимаем "1" ещё раз, чтобы свернуть раздел "Станция"
    _HumanSleep()
    Send("{SC002}") 

    ; Шаг 6: Нажимаем "0", чтобы выбрать трюм для руды
    _HumanSleep()
    Send("{SC00B}") 

    ; Шаг 7: Нажимаем "D", чтобы выделить всю руду
    _HumanSleep()
    Send("{SC020}") 

    ; Шаг 8: Нажимаем "5", чтобы начать перемещение руды
    _HumanSleep()
    Send("{SC006}") 

    ; Шаг 9: Нажимаем "7", чтобы подтвердить цель перемещения
    _HumanSleep()
    Send("{SC007}") 

    ; Шаг 10: Ожидаем завершения анимации перемещения всей руды
    _HumanSleep(500, 999) 

    ; Шаг 11: Нажимаем "Esc", чтобы закрыть окно инвентаря
    _HumanSleep()
    Send("{SC001}") 

    ; Шаг 12: Финальная проверка пустого трюма с повторными попытками
    Local $x, $y
    Local $iMaxRetries = 3 ; Количество дополнительных проверок
    Local $aArea[4] ; Массив для хранения координа поиска относительно окна

    $aArea[0] = $aCPos[0] + 0
    $aArea[1] = $aCPos[1] + 80
    $aArea[2] = $aCPos[0] + 60
    $aArea[3] = $aCPos[1] + 170

    For $i = 1 To $iMaxRetries
        ; Используем обновленный вызов с путем к ресурсам и массивом координат
        If _MyImageSearch("imgCargoEmpty.bmp", $sResourceDir, $aArea, $x, $y, 100) = 1 Then
            $DeliveredCount += 1
            _Log("_MoveCargo: Успешно. Выгрузка #" & $DeliveredCount)
            Return True
        EndIf


        
        ; Если не нашли, ждем немного и пробуем снова (постепенно увеличивая ожидание)
        _Log("_MoveCargo: Карго пока не пустое, ожидание... (Попытка " & $i & " из " & $iMaxRetries & ")")
        _HumanSleep(500, 999) 
    Next

    ; Если после всех попыток пустое карго не найдено
    _Log("_MoveCargo: ВНИМАНИЕ - После ожидания карго всё еще полное. Ошибка выгрузки.")
    Return False

EndFunc

; - + - + - + - + - | Функция открытия меню грида | - + - + - + - + - + - + - + - + - + - + - + - + - + -

Func _OpenMenuIfNeed()

    ; Шаг 1: Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("_OpenMenuIfNeed: Ошибка - Клиент не найден")
        Return False
    EndIf

    ; Шаг 2: Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($ClientName)
    If @error Then 
        _Log("_OpenMenuIfNeed: Ошибка - Не удалось получить координаты клиента")
        Return False
    EndIf

    ; Шаг 3: Вычисляем область поиска
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 1190 ; X1
    $aArea[1] = $aCPos[1] + 370  ; Y1
    $aArea[2] = $aCPos[0] + 1260 ; X2
    $aArea[3] = $aCPos[1] + 430  ; Y2

    _Log("_OpenMenuIfNeed: Проверяем состояние меню...")

    Local $x, $y
    Local $iMaxRetries = 3

    ; Шаг 4: Цикл попыток открытия меню
    For $i = 1 To $iMaxRetries
        ; Проверяем, видна ли иконка "глаза" (значит меню закрыто)
        ; Теперь передаем $sResourceDir и массив $aArea
        If _MyImageSearch("imgEyeIcon.bmp", $sResourceDir, $aArea, $x, $y, 100) = 0 Then
            _Log("_OpenMenuIfNeed: Меню открыто (иконка глаза не найдена)")
            Return True
        EndIf


        _Log("_OpenMenuIfNeed: Меню закрыто. Попытка открытия " & $i & " из " & $iMaxRetries)
        
        ; Шаг 5: Нажимаем клавишу открытия (SC032)
        _HumanSleep()
        Send("{SC032}") 

        ; Шаг 6: Ожидание анимации появления меню перед следующей проверкой
        _HumanSleep(800, 1200) 
    Next

    ; Шаг 7: Финальный вердикт после всех попыток
    _Log("_OpenMenuIfNeed: Ошибка - Не удалось открыть меню за " & $iMaxRetries & " попыток")
    Return False

EndFunc

; - + - + - + - + - | Функция открытия списка добычи | - + - + - + - + - + - + - + - + - + - + - + - + - 

Func _OpenBeltsList($bNeedToGo)
    ; Шаг 1: Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("_OpenBeltsList: Ошибка - Клиент не найден")
        Return False
    EndIf

    ; Шаг 2: Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($ClientName)
    If @error Then 
        _Log("_OpenBeltsList: Ошибка - Не удалось получить координаты клиента")
        Return False
    EndIf

    _Log("_OpenBeltsList: Проверяем наличие списка астероидов...")

    ; Шаг 3: Проверяем, не открыт ли список уже
    Local $x, $y
    Local $aArea[4] ; Объявляем массив для области

    ; --- Поиск текущей добычи (Mining Current) ---
    $aArea[0] = $aCPos[0] + 970  ; X1
    $aArea[1] = $aCPos[1] + 1    ; Y1
    $aArea[2] = $aCPos[0] + 1100 ; X2
    $aArea[3] = $aCPos[1] + 50   ; Y2

    Local $bMiningCurrent = _MyImageSearch("imgMiningCurrent.bmp", $sResourceDir, $aArea, $x, $y, 100)


    ; --- Поиск выбора руды (Select Ore) ---
    $aArea[0] = $aCPos[0] + 970  ; X1
    $aArea[1] = $aCPos[1] + 55   ; Y1
    $aArea[2] = $aCPos[0] + 1000 ; X2
    $aArea[3] = $aCPos[1] + 720  ; Y2

    Local $bSelectOre = _MyImageSearch("imgSelectOreToMine.bmp", $sResourceDir, $aArea, $x, $y, 100)


    If $bMiningCurrent = 1 And $bSelectOre = 1 Then
        _Log("_OpenBeltsList: Список добычи уже открыт")
        If $bNeedToGo Then Return _GoToRandomBelt() ; Добавлен Return для проброса результата
        Return True
    EndIf

    ; Шаг 4: Открываем выпадающее меню
    _Log("_OpenBeltsList: Список не найден, открываем меню...")
    If _FindAndClick("imgShowDropdownMy.bmp", $aCPos[0] + 970, $aCPos[1] + 1, $aCPos[0] + 1010, $aCPos[1] + 40) Then
        _HumanSleep()

        ; Шаг 5: Выбираем пункт добычи
        If _FindAndClick("imgMinigScreen.bmp", $aCPos[0] + 970, $aCPos[1] + 50, $aCPos[0] + 1220, $aCPos[1] + 720) Then
            _Log("_OpenBeltsList: Грид добычи выбран")
            _HumanSleep()

            ; Шаг 6: Дополнительное действие (клик по области управления списком)
            _FindAndClick("imgMinigScreen.bmp", $aCPos[0] + 1220, $aCPos[1] + 60, $aCPos[0] + 1270, $aCPos[1] + 530)
            _HumanSleep()

            ; Шаг 7: Если нужно лететь — вызываем функцию полета
            If $bNeedToGo Then Return _GoToRandomBelt()
            Return True
        Else
            _Log("_OpenBeltsList: Ошибка - Не удалось нажать на 'imgMinigScreen.bmp'")
            Return False
        EndIf
    Else
        _Log("_OpenBeltsList: Ошибка - Не удалось нажать на 'imgShowDropdownMy.bmp'")
        Return False
    EndIf
EndFunc

; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

; !!! Продолжить тут !!!

Func _WarpTo($sTargetName)
    ; Шаг 1: Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("_WarpTo: Ошибка - Клиент не найден")
        Return False
    EndIf

    ; Шаг 2: Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($ClientName)
    If @error Then 
        _Log("_WarpTo: Ошибка - Не удалось получить координаты клиента")
        Return False
    EndIf

    ; Шаг 3: Вычисляем область поиска кнопки варпа (Смещение: 704, 40, 268, 522)
    Local $iX1 = $aCPos[0] + 704
    Local $iY1 = $aCPos[1] + 40
    Local $iX2 = $aCPos[0] + 972 ; 704 + 268
    Local $iY2 = $aCPos[1] + 562 ; 40 + 522

    _Log("_WarpTo: Попытка варпа к объекту: " & $sTargetName)

    ; Шаг 4: Поиск и клик по кнопке варпа (warp.png)
    If _FindAndClick("warp.png", $iX1, $iY1, $iX2, $iY2) Then
        _Log("_WarpTo: Кнопка найдена. Начинаем разгон к " & $sTargetName)
        
        ; Шаг 5: Имитируем человеческую паузу после клика
        _HumanSleep()
        
        ; Шаг 6: Ожидание 10 секунд (базовое время на вход в варп)
        ; Sleep(10000) 
        
        Return True
    Else
        _Log("_WarpTo: Ошибка - Не удалось найти кнопку варпа")
        Return False
    EndIf
    
EndFunc


; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

; Вспомогательная функция: ищет картинку в области и, если нашла, кликает по ней
Func _FindAndClick($sImg, $iX1, $iY1, $iX2, $iY2)
    Local $x, $y
    ; Ищем изображение с допуском (100)
    If _ImageSearchArea($sImg, 1, $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
        _Log("_FindAndClick: Найдено '" & $sImg & "', кликаем.")
        _HumanSleep(100, 300)      ; Небольшая пауза перед кликом
        MouseClick("left", $x, $y, 1, 1) ; Кликаем левой кнопкой мыши
        Return True
    EndIf
    
    _Log("_FindAndClick: Изображение '" & $sImg & "' не найдено.")
    Return False
EndFunc


; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

Func _HumanSleep($iMin = 100, $iMax = 800)
    ; Если при вызове параметры не указаны, используются 100 и 800
    Local $iWait = Random($iMin, $iMax, 1)
    
    ; Логируем только значительные паузы
    If $iWait > 1000 Then 
        _Log("Пауза: " & StringFormat("%.2f", $iWait / 1000) & " сек.")
    EndIf
    
    Sleep($iWait)
EndFunc


Func _CheckAndActivateClient($title)
    $Client = WinGetHandle($title) ; Записываем в глобальную переменную

    If @error Then Return False ; Окно не существует

    WinActivate($Client)

    ; Ждем активации окна 3 секунды
    If WinWaitActive($Client, "", 1) Then
        Return True ; Все ок
    Else
		_Log("Не смог отобразить окно клиента :(")
        Return False ; Окно есть, но не смогло стать активным
    EndIf
EndFunc

Func _CW($sText)
    ConsoleWrite(BinaryToString(StringToBinary($sText & @CRLF, 4), 1))
EndFunc



; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +


; #FUNCTION# ====================================================================================================================
; Name...........: _Terminate
; Description....: Корректно завершает работу скрипта, очищая временные ресурсы и удаляя следы в системе.
; Syntax.........: _Terminate()
; Parameters ....: Нет
; Return values .: Нет (завершает процесс Exit)
; Date ..........: 2026.04.25
; Version .......: 1.0
; Author ........: [Ваше Имя / Ник]
; Remarks .......: Удаляет папку $sResourceDir и выгружает временный шрифт JetBrains Mono.
; ===============================================================================================================================
Func _Terminate()
    
    _Log("!!! Скрипт остановлен пользователем !!!")
    
    ; 1. Выгружаем временный шрифт из памяти системы
    If IsDeclared("sResourceDir") Then
        DllCall("gdi32.dll", "int", "RemoveFontResourceEx", "str", $sResourceDir & "JetBrainsMono-Bold.ttf", "dword", 0x10, "int", 0)
    EndIf

    ; 2. Если была создана временная папка для картинок — удаляем её со всем содержимым
    If IsDeclared("sResourceDir") Then 
        DirRemove($sResourceDir, 1)
    EndIf
    
    ; Даем небольшую паузу для записи последнего лога
    _HumanSleep(300, 500)
    
    Exit ; Полный выход из программы

EndFunc   ;==>_Terminate


