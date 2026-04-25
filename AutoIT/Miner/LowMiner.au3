; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer                                                                                       #
; # VERSION........: 1.0.0 Build 2026.04.25                                                                                     #
; # AUTHOR.........: Tatooine104 / primalevil@gmail.com                                                                         #
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

#include <GUIConstantsEx.au3>    ; Содержит $GUI_EVENT_CLOSE
#include <WindowsConstants.au3>  ; Содержит $WS_EX_TOPMOST
#include <StaticConstants.au3>   ; Содержит константы для стилей текста (Label)

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

    ; 3. Вычисляем область поиска (Координаты из вашего примера)
    Local $iX1 = $aCPos[0] + 1060 
    Local $iY1 = $aCPos[1] + 230  
    Local $iX2 = $aCPos[0] + 1280
    Local $iY2 = $aCPos[1] + 300
    Local $x, $y

    ; 4. Поиск изображения кнопки Undock
    If _MyImageSearch("imgUnDock.bmp", $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
        _Log("_Undock: Кнопка 'Undock' найдена. Выходим в космос")
        _HumanSleep()      
        Send("{SC016}")    ; Нажимаем на клавиатуре клавишу "U", настроенную на кнопке "Undock"
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

    ; 3. Вычисляем область поиска (Смещение: 0, 70, 122, 55)
    Local $iX1 = $aCPos[0] + 0
    Local $iY1 = $aCPos[1] + 80
    Local $iX2 = $aCPos[0] + 60
    Local $iY2 = $aCPos[1] + 170

    _Log("IsCargoFull: Проверяем грузовой отсек...")

    Local $x, $y
    ; 4. Поиск изображения 100% карго
    If _MyImageSearch("imgCargoFull.bmp", $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
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

    ; Шаг 3: Вычисляем область поиска (Смещение: 0, 80, 60, 170)
    Local $iX1 = $aCPos[0] + 0
    Local $iY1 = $aCPos[1] + 80
    Local $iX2 = $aCPos[0] + 60
    Local $iY2 = $aCPos[1] + 170

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

    For $i = 1 To $iMaxRetries
        If _MyImageSearch("imgCargoEmpty.bmp", $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
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
    Local $iX1 = $aCPos[0] + 1190
    Local $iY1 = $aCPos[1] + 370
    Local $iX2 = $aCPos[0] + 1260
    Local $iY2 = $aCPos[1] + 430

    _Log("_OpenMenuIfNeed: Проверяем состояние меню...")

    Local $x, $y
    Local $iMaxRetries = 3

    ; Шаг 4: Цикл попыток открытия меню
    For $i = 1 To $iMaxRetries
        ; Проверяем, видна ли иконка "глаза" (значит меню закрыто)
        If _MyImageSearch("imgEyeIcon.bmp", $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 0 Then
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
    Local $bMiningCurrent = _MyImageSearch("imgMiningCurrent.bmp", $aCPos[0] + 970, $aCPos[1] + 1, $aCPos[0] + 1100, $aCPos[1] + 50, $x, $y, 100)
    Local $bSelectOre = _MyImageSearch("imgSelectOreToMine.bmp", $aCPos[0] + 970, $aCPos[1] + 55, $aCPos[0] + 1000, $aCPos[1] + 720, $x, $y, 100)

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

Func _Log($sText)

    ; 1. Пишем в консоль (для отладки)
    ; ConsoleWrite($sText & @CRLF)
    
    ; 2. Обновляем текст в нашем окошке
    GUICtrlSetData($hStatusLabel, $sText)
    
    ; 3. Можно также писать в текстовый файл, если нужно
    Local $hFile = FileOpen("bot_log.txt", 1)
    FileWriteLine($hFile, @MDAY & "." & @MON & " " & @HOUR & ":" & @MIN & ":" & @SEC & " -> " & $sText)
    FileClose($hFile)

EndFunc


; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

;===============================================================================
;
; Description:      Find the position of an image on the desktop
; Syntax:           _ImageSearchArea, _ImageSearch
; Parameter(s):
;                   $findImage - the image to locate on the desktop
;                   $tolerance - 0 for no tolerance (0-255). Needed when colors of
;                                image differ from desktop. e.g GIF
;                   $resultPosition - Set where the returned x,y location of the image is.
;                                     1 for centre of image, 0 for top left of image
;                   $x $y - Return the x and y location of the image
;
; Return Value(s):  On Success - Returns 1
;                   On Failure - Returns 0
;
; Note: Use _ImageSearch to search the entire desktop, _ImageSearchArea to specify
;       a desktop region to search
;
;===============================================================================
Func _ImageSearch($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
	Return _ImageSearchArea($findImage, $resultPosition, 0, 0, @DesktopWidth, @DesktopHeight, $x, $y, $tolerance, $hwnd)
EndFunc   

Func _ImageSearchClientArea($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd)

	If $hwnd <> 0 Then
		Local $clientWindowPos = _WinGetClientPos($hwnd)
		If @error Then Return 0
		Local $clientWindowSize = WinGetClientSize($hwnd)
		If @error Then Return 0
	Else
		Return 0
	EndIf

	If $clientWindowSize[0] = 0 Or $clientWindowSize[1] = 0 Then
		Return 0
	EndIf

	Return _ImageSearchArea($findImage, $resultPosition, $clientWindowPos[0], $clientWindowPos[1], $clientWindowPos[0] + $clientWindowSize[0], $clientWindowPos[1] + $clientWindowSize[1], $x, $y, $tolerance, $hwnd)

EndFunc   ;==>_ImageSearchClientArea

Func _ImageSearchArea($findImage, $resultPosition, $x1, $y1, $right, $bottom, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
	;MsgBox(0,"asd","" & $x1 & " " & $y1 & " " & $right & " " & $bottom)
	If $tolerance > 0 Then $findImage = "*" & $tolerance & " " & $findImage
	Local $result = DllCall("ImageSearchDLL.dll", "str", "ImageSearch", "int", $x1, "int", $y1, "int", $right, "int", $bottom, "str", $findImage)

	; If error exit
	If $result[0] = "0" Then Return 0

	; Otherwise get the x,y location of the match and the size of the image to
	; compute the centre of search
	Dim $array = StringSplit($result[0], "|")

	$x = Int(Number($array[2]))
	$y = Int(Number($array[3]))
	If $resultPosition = 1 Then
		$x = $x + Int(Number($array[4]) / 2)
		$y = $y + Int(Number($array[5]) / 2)
	EndIf
	If $hwnd <> 0 Then
		Local $wpos = _WinGetClientPos($hwnd)
		$x = $x - $wpos[0]
		$y = $y - $wpos[1]
	EndIf

	Return 1
EndFunc   

;===============================================================================
;
; Description:      Wait for a specified number of seconds for an image to appear
;
; Syntax:           _WaitForImageSearch, _WaitForImagesSearch
; Parameter(s):
;					$waitSecs  - seconds to try and find the image
;                   $findImage - the image to locate on the desktop
;                   $tolerance - 0 for no tolerance (0-255). Needed when colors of
;                                image differ from desktop. e.g GIF
;                   $resultPosition - Set where the returned x,y location of the image is.
;                                     1 for centre of image, 0 for top left of image
;                   $x $y - Return the x and y location of the image
;
; Return Value(s):  On Success - Returns 1
;                   On Failure - Returns 0
;
;
;===============================================================================
Func _WaitForImageSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
	$waitSecs = $waitSecs * 1000
	Local $startTime = TimerInit()
	While TimerDiff($startTime) < $waitSecs
		Sleep(100)
		Local $result = _ImageSearch($findImage, $resultPosition, $x, $y, $tolerance, $hwnd)
		If $result > 0 Then
			Return 1
		EndIf
	WEnd
	Return 0
EndFunc   ;==>_WaitForImageSearch

;===============================================================================
;
; Description:      Wait for a specified number of seconds for any of a set of
;                   images to appear
;
; Syntax:           _WaitForImagesSearch
; Parameter(s):
;					$waitSecs  - seconds to try and find the image
;                   $findImage - the ARRAY of images to locate on the desktop
;                              - ARRAY[0] is set to the number of images to loop through
;								 ARRAY[1] is the first image
;                   $tolerance - 0 for no tolerance (0-255). Needed when colors of
;                                image differ from desktop. e.g GIF
;                   $resultPosition - Set where the returned x,y location of the image is.
;                                     1 for centre of image, 0 for top left of image
;                   $x $y - Return the x and y location of the image
;
; Return Value(s):  On Success - Returns the index of the successful find
;                   On Failure - Returns 0
;
;
;===============================================================================
Func _WaitForImagesSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
	$waitSecs = $waitSecs * 1000
	$startTime = TimerInit()
	While TimerDiff($startTime) < $waitSecs
		For $i = 1 To $findImage[0]
			Sleep(100)
			$result = _ImageSearch($findImage[$i], $resultPosition, $x, $y, $tolerance, $hwnd)
			If $result > 0 Then
				Return $i
			EndIf
		Next
	WEnd
	Return 0
EndFunc   ;==>_WaitForImagesSearch

Func _WinGetClientPos($hwnd)
    Local $Size = WinGetClientSize($hwnd)
    If Not IsArray($Size) Then Return SetError(1, 0, 0)

    ; Описываем структуру напрямую
    Local $tPoint = DllStructCreate("long X;long Y")
    
    ; Устанавливаем начальную точку (0,0) — это левый верхний угол клиентской области
    DllStructSetData($tPoint, "X", 0)
    DllStructSetData($tPoint, "Y", 0)

    ; Вызываем системную функцию напрямую через DllCall
    Local $aRet = DllCall("user32.dll", "bool", "ClientToScreen", "hwnd", $hwnd, "ptr", DllStructGetPtr($tPoint))
    
    If @error Or Not $aRet[0] Then
        Return SetError(1, 0, 0)
    EndIf

    ; Формируем массив: [0]=X, [1]=Y, [2]=Ширина, [3]=Высота
    Local $Pos[4]
    $Pos[0] = DllStructGetData($tPoint, "X") ; Экранный X начала клиентской области
    $Pos[1] = DllStructGetData($tPoint, "Y") ; Экранный Y начала клиентской области
    $Pos[2] = $Size[0]                       ; Ширина области
    $Pos[3] = $Size[1]                       ; Высота области

    Return $Pos
EndFunc

Func _Terminate()
    _Log("!!! Скрипт остановлен пользователем !!!")
    
    ; Если мы использовали временную папку для картинок, удаляем её
    If IsDeclared("sResourceDir") Then DirRemove($sResourceDir, 1)
    
    Exit ; Полный выход из программы
EndFunc

Func _SaveToIni($sSection, $sKey, $vValue)
    IniWrite($sIniPath, $sSection, $sKey, $vValue)
EndFunc

; #FUNCTION# ====================================================================================================================
; Name...........: _MyImageSearch
; Description....: Ищет изображение в заданной области с автоматической подстановкой пути к папке ресурсов.
; Syntax.........: _MyImageSearch($sImgName, $iX1, $iY1, $iX2, $iY2, ByRef $x, ByRef $y, $iTolerance)
; Parameters ....: $sImgName   - Имя файла изображения (например, "button.png").
;                  $iX1        - Координата X левого верхнего угла области поиска.
;                  $iY1        - Координата Y левого верхнего угла области поиска.
;                  $iX2        - Координата X правого нижнего угла области поиска.
;                  $iY2        - Координата Y правого нижнего угла области поиска.
;                  $x          - [ByRef] Переменная для записи найденной координаты X (центр изображения).
;                  $y          - [ByRef] Переменная для записи найденной координаты Y (центр изображения).
;                  $iTolerance - Допуск поиска (0-255). Чем выше, тем больше цветовых отличий допускается.
; Return values .: 1 - Изображение успешно найдено
;                  0 - Изображение не найдено
; Date ..........: 2024-05-15
; Version .......: v1.1
; Author ........: [Ваше Имя / Ник]
; Remarks .......: Автоматически склеивает имя файла с глобальным путем $sResourceDir.
; ===============================================================================================================================
Func _MyImageSearch($sImgName, $iX1, $iY1, $iX2, $iY2, ByRef $x, ByRef $y, $iTolerance)
    ; Формируем полный путь: временная папка ресурсов + имя файла
    Local $sFullPath = $sResourceDir & $sImgName
    
    ; Вызываем базовую функцию ImageSearchArea (флаг 1 — возврат центра объекта)
    Local $iResult = _ImageSearchArea($sFullPath, 1, $iX1, $iY1, $iX2, $iY2, $x, $y, $iTolerance)
    
    Return $iResult
EndFunc   ;==>_MyImageSearch
