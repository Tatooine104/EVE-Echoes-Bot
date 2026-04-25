; ----------------------------------------------------------------------------------------------------
;
; AutoIT Version: 5.5.6
; Language      : Russia
; Description   : A bot that fully automates ore mining in Low
;
; ----------------------------------------------------------------------------------------------------
;
; EVE Echoes Fuctions:  
; - _Undock - выход из дока
; - _IsCargoFull - 
; - _TakeNewShip
; - _IsCapsule
; - _MoveCargo
; - _OpenEyeMenu
; - _OpenBeltList
; - _GoToRandomBelt
; - _Mining
; - _IsSave
; - _AliChatMessage
; - _GoToStation
; - _SelectOre
; - _
; ----------------------------------------------------------------------------------------------------
;
; Special functions: 
; - _CW - корректное отображения русского в консоли (для дебага)
; - _Log - логирование действий в txt файл
; - _CheckAndActivateClient - проверка что клиент игры активен
; - _HumanSleep - задержка перед выполнением действий, имитирующее действие человека
; - 
; - 
; - 
; ----------------------------------------------------------------------------------------------------
;
; Additional Functions:
; - _ImageSearch - 
; - _ImageSearchClientArea - 
; - _ImageSearchArea - 
; - _WaitForImageSearch - 
; - _WaitForImagesSearch - 
; - _WinGetClientPos - 
; ----------------------------------------------------------------------------------------------------

Opt("MouseCoordMode", 2) ; Опция для позиционирования курсора относительно указанного окна
Opt("PixelCoordMode", 2) ; Опция для работы с координатами относительно указанного окна
Opt("SendKeyDownDelay", 50) ; Удерживать клавишу 50мс (стандарт 5мс)

Global $Client = 0                   ; Глобальная переменная для хранения ссылки на окно
Global $InSpace = false              ; 
Global $IsSave = false               ; 
Global $ClientName = "(Client W.01)" ;
Global $DeliveredCount = 0           ;

; - + - + - + - + - | Функция выхода из дока | - + - + - + - + - + - + - + - + - + - + - + - + - + - + - 

Func _Undock()

    ; 1. Проверяем и активируем окно
    If Not _CheckAndActivateClient($ClientName) Then
        _Log("_Undock: Ошибка - Клиент не найден или не активен")
        Return False
    EndIf

    ; 2. Получаем координаты клиентской области
    Local $aCPos = WinGetClientPos($ClientName)
    If @error Then 
        _Log("_Undock: Ошибка - Не удалось получить координаты")
        Return False
    EndIf

    ; 3. Вычисляем область поиска (Координаты из вашего примера)
    Local $iX1 = $aCPos[0] + 1060 
    Local $iY1 = $aCPos[1] + 230  
    Local $iX2 = $aCPos[0] + 1280
    Local $iY2 = $aCPos[1] + 300

    Local $x, $y
    ; 4. Поиск изображения кнопки Undock
    If _ImageSearchArea("imgUnDock.bmp", 1, $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
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
    Local $aCPos = WinGetClientPos($ClientName)
    If @error Then 
        _Log("IsCargoFull: Ошибка - Не удалось получить координаты")
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
    If _ImageSearchArea("imgCargoFull.bmp", 1, $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
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
    Local $aCPos = WinGetClientPos($ClientName)
    If @error Then 
        _Log("_MoveCargo: Ошибка - Не удалось получить координаты")
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
        If _ImageSearchArea("imgCargoEmpty.bmp", 1, $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 1 Then
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
    Local $aCPos = WinGetClientPos($ClientName)
    If @error Then 
        _Log("_OpenMenuIfNeed: Ошибка - Не удалось получить координаты")
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
        If _ImageSearchArea("EyeIcon.png", 1, $iX1, $iY1, $iX2, $iY2, $x, $y, 100) = 0 Then
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


; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +



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
    Local $sLogFile = @ScriptDir & "\bot_log.txt"
    
    ; 1. Вывод в консоль (твой рабочий способ для русского языка)
    ConsoleWrite(BinaryToString(StringToBinary($sText & @CRLF, 4), 1))
    
    ; 2. Запись в файл
    ; Режим 1 = Открыть для записи с добавлением в конец файла
    ; Режим 128 = Принудительное использование кодировки UTF-8 (чтобы русский читался)
    Local $hFile = FileOpen($sLogFile, 1 + 128)
    
    If $hFile <> -1 Then
        ; Формируем строку: Дата Время : Текст
        Local $sTimeStamp = @YEAR & "-" & @MON & "-" & @MDAY & " " & @HOUR & ":" & @MIN & ":" & @SEC
        FileWriteLine($hFile, $sTimeStamp & " : " & $sText)
        FileClose($hFile)
    EndIf
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
EndFunc   ;==>_ImageSearch

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
EndFunc   ;==>_ImageSearchArea

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