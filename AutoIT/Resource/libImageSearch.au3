; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer (Image Library)                                                                       #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.25                                                                                                 #
; # FILENAME.......: libImageSearch.au3                                                                                         #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Библиотека функций для работы с графическими образами.                                                     #
; #                                                                                                                             #
; # FUNCTIONS......: _MyImageSearch         - Поиск в массиве области с логированием и скриншотом.                              #
; #                  _ImageSearch           - Поиск изображения по всему рабочему столу.                                        #
; #                  _ImageSearchClientArea - Поиск строго внутри клиентской части окна (без рамок).                            #
; #                  _ImageSearchArea       - Базовая обертка для прямого вызова ImageSearchDLL.dll.                            #
; #                  _WaitForImageSearch    - Цикличное ожидание появления одной картинки (тайм-аут).                           #
; #                  _WaitForImagesSearch   - Ожидание появления любой картинки из списка (массива).                            #
; #                  _WinGetClientPos       - Получение экранных координат клиентской области (WinAPI).                         #
; #                  _FindAndClick          - Найти изображение инажать на него.                                                #
; #                                                                                                                             #
; ###############################################################################################################################

#include-once ; Добавить в первую строку файла библиотеки

#include <ScreenCapture.au3>
#include <libUtility.au3>

; #FUNCTION# ====================================================================================================================
; Name...........: _MyWaitForImageSearch
; Description....: Ожидает появления изображения в конкретной области (массиве) в течение заданного времени.
; Syntax.........: _MyWaitForImageSearch($sImgName, $sResDir, $aRect, $iWaitSecs, ByRef $x, ByRef $y, $iTolerance)
; Parameters ....: $sImgName   - Имя файла изображения.
;                  $sResDir    - Путь к папке с изображениями.
;                  $aRect      - Массив координат области поиска [X1, Y1, X2, Y2].
;                  $iWaitSecs  - Время ожидания в секундах.
;                  $x          - [ByRef] Переменная для записи найденной координаты X.
;                  $y          - [ByRef] Переменная для записи найденной координаты Y.
;                  $iTolerance - Допуск поиска (0-255).
; Return values .: 1 - Изображение найдено.
;                  0 - Время ожидания истекло.
; Updated .......: 2026.04.26
; Version .......: 1.00
; Remarks .......: Проверка выполняется каждые 100 мс. Использует TimerInit для точности.
; ===============================================================================================================================
Func _MyWaitForImageSearch($sImgName, $sResDir, $aRect, $iWaitSecs, ByRef $x, ByRef $y, $iTolerance)
    ; Проверка входных данных
    If Not IsArray($aRect) Or UBound($aRect) < 4 Then Return 0
    
    Local $iWaitMs = $iWaitSecs * 1000
    Local $hTimer = TimerInit()
    
    _Log("Ожидание появления '" & $sImgName & "' (" & $iWaitSecs & " сек.)")

    While TimerDiff($hTimer) < $iWaitMs
        ; Используем нашу основную функцию поиска (флаг возврата центра уже вшит в неё)
        If _MyImageSearch($sImgName, $sResDir, $aRect, $x, $y, $iTolerance) Then
            Return 1
        EndIf
        
        Sleep(100) ; Пауза между проверками
    WEnd

    _Log("Тайм-аут: '" & $sImgName & "' не появилось.")
    Return 0
EndFunc   ;==>_MyWaitForImageSearch



; #FUNCTION# ====================================================================================================================
; Name...........: _FindAndClick
; Description....: Ищет изображение в заданной области (массиве) и выполняет клик при обнаружении.
; Syntax.........: _FindAndClick($sImg, $sResDir, $aRect)
; Parameters ....: $sImg        - Имя файла изображения (например, "button.png").
;                  $sResDir     - Путь к папке с изображениями.
;                  $aRect       - Массив координат области поиска [X1, Y1, X2, Y2].
; Return values .: True         - Изображение найдено и клик выполнен.
;                  False        - Изображение не найдено.
; Updated .......: 2026.04.25
; Version .......: 1.01
; Remarks .......: Использует _MyImageSearch для поиска и _HumanSleep перед кликом.
; ===============================================================================================================================
Func _FindAndClick($sImg, $sResDir, $aRect)

    Local $x, $y
    
    ; Используем нашу основную функцию поиска (она уже умеет работать с массивом и делать скриншот ошибки)
    If _MyImageSearch($sImg, $sResDir, $aRect, $x, $y, 100) Then
        _Log("_FindAndClick: Найдено '" & $sImg & "', кликаем.")
        
        _HumanSleep(100, 300)      ; Небольшая пауза перед кликом
        MouseClick("left", $x, $y, 1, 1) 
        
        Return True
    EndIf
    
    _Log("_FindAndClick: Изображение '" & $sImg & "' не найдено.")
    Return False

EndFunc   ;==>_FindAndClick



; #FUNCTION# ====================================================================================================================
; Name...........: _MyImageSearch
; Description....: Ищет изображение в заданной области (массиве) с автоматической склейкой пути к ресурсам.
; Syntax.........: _MyImageSearch($sImgName, $sResDir, $aRect, ByRef $x, ByRef $y, $iTolerance)
; Parameters ....: $sImgName   - Имя файла (например, "imgMining.bmp").
;                  $sResDir    - Путь к папке (Global $sResourceDir).
;                  $aRect      - Массив [X1, Y1, X2, Y2].
;                  $x, $y      - [ByRef] Переменные для координат результата.
;                  $iTolerance - Допуск (0-255).
; Return values .: 1 - Найдено, 0 - Не найдено.
; Updated .......: 2026.04.26
; Version .......: 1.05
; Remarks .......: Если картинка не найдена, сохраняет скриншот области в папку \Logs\.
; ===============================================================================================================================
Func _MyImageSearch($sImgName, $sResDir, $aRect, ByRef $x, ByRef $y, $iTolerance)
    ; 1. Проверка массива координат
    If Not IsArray($aRect) Or UBound($aRect) < 4 Then 
        _Log("_MyImageSearch: Ошибка - передан неверный массив координат для " & $sImgName)
        Return 0
    EndIf

    ; 2. Корректировка пути (добавляем слэш, если его нет)
    If $sResDir <> "" And StringRight($sResDir, 1) <> "\" Then $sResDir &= "\"
    Local $sFullPath = $sResDir & $sImgName

    ; 3. Вызов базовой функции поиска (1 - поиск центра)
    ; Передаем элементы массива явно: [0], [1], [2], [3]
    Local $iResult = _ImageSearchArea($sFullPath, 1, $aRect[0], $aRect[1], $aRect[2], $aRect[3], $x, $y, $iTolerance)
    
    ; 4. Если НЕ нашли — делаем скриншот для отладки
    If $iResult = 0 Then
        Local $sLogDir = @ScriptDir & "\Logs"
        If Not FileExists($sLogDir) Then DirCreate($sLogDir)
        
        ; Формируем имя скриншота: ИмяКартинки_Время.jpg
        Local $sFileErr = $sLogDir & "\" & StringReplace($sImgName, ".", "_") & "_" & @HOUR & @MIN & @SEC & "_err.jpg"
        
        ; Снимаем именно ту область, где искали
        _ScreenCapture_Capture($sFileErr, $aRect[0], $aRect[1], $aRect[2], $aRect[3])
    EndIf

    Return $iResult
EndFunc   ;==>_MyImageSearch




; #FUNCTION# ====================================================================================================================
; Name...........: _ImageSearch
; Description....: Ищет изображение на всем экране (от 0,0 до разрешения рабочего стола).
; Syntax.........: _ImageSearch($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
; Parameters ....: $findImage      - Путь к файлу изображения.
;                  $resultPosition - Где установить координаты: 0 - левый верхний угол, 1 - центр найденного объекта.
;                  $x              - [ByRef] Переменная для записи найденной координаты X.
;                  $y              - [ByRef] Переменная для записи найденной координаты Y.
;                  $tolerance      - Допуск несовпадения цветов (0-255).
;                  $hwnd           - [Optional] Дескриптор окна для поиска (по умолчанию 0 - весь рабочий стол).
; Return values .: 1 - Найдено, 0 - Не найдено.
; Updated .......: 2026.04.25
; Version .......: 1.00
; Remarks .......: Является оберткой для _ImageSearchArea, передавая в неё границы всего экрана.
; ===============================================================================================================================
Func _ImageSearch($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)

	Return _ImageSearchArea($findImage, $resultPosition, 0, 0, @DesktopWidth, @DesktopHeight, $x, $y, $tolerance, $hwnd)

EndFunc   ;==>_ImageSearch



; #FUNCTION# ====================================================================================================================
; Name...........: _ImageSearchClientArea
; Description....: Ищет изображение только внутри клиентской области указанного окна.
; Syntax.........: _ImageSearchClientArea($findImage, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd)
; Parameters ....: $findImage      - Путь к файлу изображения.
;                  $resultPosition - Где установить координаты: 0 - левый верхний угол, 1 - центр найденного объекта.
;                  $x              - [ByRef] Переменная для записи найденной координаты X.
;                  $y              - [ByRef] Переменная для записи найденной координаты Y.
;                  $tolerance      - Допуск несовпадения цветов (0-255).
;                  $hwnd           - Дескриптор окна, в клиентской области которого производится поиск.
; Return values .: 1 - Изображение найдено
;                  0 - Изображение не найдено или произошла ошибка получения координат окна
; Updated .......: 2026.04.25
; Version .......: 1.00
; Remarks .......: Игнорирует заголовки и рамки окна. Требует наличия функции _WinGetClientPos.
; ===============================================================================================================================
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



; #FUNCTION# ====================================================================================================================
; Name...........: _ImageSearchArea
; Description....: Базовая функция поиска изображения в заданных координатах через ImageSearchDLL.dll.
; Syntax.........: _ImageSearchArea($findImage, $resultPosition, $x1, $y1, $right, $bottom, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
; Parameters ....: $findImage      - Полный путь к файлу изображения.
;                  $resultPosition - Где установить координаты: 0 - левый верхний угол, 1 - центр найденного объекта.
;                  $x1             - Координата X левого верхнего угла области.
;                  $y1             - Координата Y левого верхнего угла области.
;                  $right          - Координата X правого нижнего угла области.
;                  $bottom         - Координата Y правого нижнего угла области.
;                  $x              - [ByRef] Переменная для записи найденной координаты X.
;                  $y              - [ByRef] Переменная для записи найденной координаты Y.
;                  $tolerance      - Допуск несовпадения цветов (0-255).
;                  $hwnd           - [Optional] Дескриптор окна для коррекции координат (по умолчанию 0).
; Return values .: 1 - Изображение успешно найдено.
;                  0 - Изображение не найдено или ошибка вызова DLL.
; Updated .......: 2026.04.26
; Version .......: 1.18
; Remarks .......: Динамически определяет путь к DLL, используя Global $sResourceDir или папку скрипта.
; ===============================================================================================================================
Func _ImageSearchArea($findImage, $resultPosition, $x1, $y1, $right, $bottom, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)

    ; 1. Пытаемся получить путь к папке ресурсов из глобальной переменной основного скрипта
    Local $sResPath = IsDeclared("sResourceDir") ? Eval("sResourceDir") : @ScriptDir & "\"
    Local $sDllPath = $sResPath & "ImageSearchDLL.dll"
    
    ; 2. Проверка наличия DLL по указанному пути
    If Not FileExists($sDllPath) Then
        _Log("КРИТИЧЕСКАЯ ОШИБКА: DLL не найдена по пути: " & $sDllPath)
        Return 0
    EndIf

    ; Подготовка строки поиска с допуском
    If $tolerance > 0 Then $findImage = "*" & $tolerance & " " & $findImage
    
    ; 3. Вызов внешней DLL
    Local $result = DllCall($sDllPath, "str", "ImageSearch", "int", $x1, "int", $y1, "int", $right, "int", $bottom, "str", $findImage)

    ; Проверка на ошибки системного вызова
    If @error Or Not IsArray($result) Then Return 0
    
    ; Если DLL вернула "0" (строка), значит изображение не найдено
    If $result[0] = "0" Then Return 0

    ; 4. Разбор результата (формат строки: результат|x|y|ширина|высота)
    Local $array = StringSplit($result[0], "|")
    If $array[0] < 3 Then Return 0

    ; Получаем координаты (индексы 2 и 3 в массиве StringSplit)
    $x = Int(Number($array[2]))
    $y = Int(Number($array[3]))
    
    ; Если запрошен центр (resultPosition = 1), прибавляем половину размеров объекта
    If $resultPosition = 1 Then
        $x = $x + Int(Number($array[4]) / 2)
        $y = $y + Int(Number($array[5]) / 2)
    EndIf
    
    ; Если передан дескриптор окна, пересчитываем экранные координаты в клиентские
    If $hwnd <> 0 Then
        Local $aWPos = _WinGetClientPos($hwnd)
        If IsArray($aWPos) Then
            $x = $x - $aWPos[0]
            $y = $y - $aWPos[1]
        EndIf
    EndIf

    Return 1
EndFunc   ;==>_ImageSearchArea





; #FUNCTION# ====================================================================================================================
; Name...........: _WaitForImageSearch
; Description....: Ожидает появления изображения в течение заданного времени.
; Syntax.........: _WaitForImageSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
; Parameters ....: $findImage      - Путь к файлу изображения.
;                  $waitSecs       - Время ожидания в секундах.
;                  $resultPosition - Где установить координаты: 0 - левый верхний угол, 1 - центр найденного объекта.
;                  $x              - [ByRef] Переменная для записи найденной координаты X.
;                  $y              - [ByRef] Переменная для записи найденной координаты Y.
;                  $tolerance      - Допуск несовпадения цветов (0-255).
;                  $hwnd           - [Optional] Дескриптор окна для поиска (по умолчанию 0).
; Return values .: 1 - Изображение найдено в течение заданного времени
;                  0 - Время ожидания истекло, изображение не найдено
; Updated .......: 2026.04.25
; Version .......: 1.00
; Remarks .......: Проверка выполняется каждые 100 мс. Использует TimerInit и TimerDiff для точности.
; ===============================================================================================================================
Func _WaitForImageSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)

	Local $iWaitMs = $waitSecs * 1000
	Local $hTimer = TimerInit()
    
	While TimerDiff($hTimer) < $iWaitMs
		Local $iResult = _ImageSearch($findImage, $resultPosition, $x, $y, $tolerance, $hwnd)
		If $iResult > 0 Then Return 1
		Sleep(100)
	WEnd

	Return 0
    
EndFunc   ;==>_WaitForImageSearch



; #FUNCTION# ====================================================================================================================
; Name...........: _WaitForImagesSearch
; Description....: Ожидает появления одного из нескольких изображений (массива) в течение заданного времени.
; Syntax.........: _WaitForImagesSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
; Parameters ....: $findImage      - Массив путей к файлам изображений (индекс [0] должен содержать количество элементов).
;                  $waitSecs       - Время ожидания в секундах.
;                  $resultPosition - Где установить координаты: 0 - левый верхний угол, 1 - центр найденного объекта.
;                  $x              - [ByRef] Переменная для записи найденной координаты X.
;                  $y              - [ByRef] Переменная для записи найденной координаты Y.
;                  $tolerance      - Допуск несовпадения цветов (0-255).
;                  $hwnd           - [Optional] Дескриптор окна для поиска (по умолчанию 0).
; Return values .: Индекс найденного изображения (1, 2, 3...)
;                  0 - Время ожидания истекло, ни одно изображение не найдено
; Updated .......: 2026.04.25
; Version .......: 1.00
; Remarks .......: Полезно, когда одна и та же кнопка может иметь разные состояния или скины.
; ===============================================================================================================================
Func _WaitForImagesSearch($findImage, $waitSecs, $resultPosition, ByRef $x, ByRef $y, $tolerance, $hwnd = 0)
    
	Local $iWaitMs = $waitSecs * 1000
	Local $hTimer = TimerInit()
	
	While TimerDiff($hTimer) < $iWaitMs
		For $i = 1 To $findImage[0]
			Local $iResult = _ImageSearch($findImage[$i], $resultPosition, $x, $y, $tolerance, $hwnd)
			If $iResult > 0 Then
				Return $i
			EndIf
		Next
		Sleep(100) ; Пауза между полными циклами проверки всех картинок
	WEnd

	Return 0

EndFunc   ;==>_WaitForImagesSearch



; #FUNCTION# ====================================================================================================================
; Name...........: _WinGetClientPos
; Description....: Получает экранные координаты и размеры клиентской области окна (без рамок и заголовка).
; Syntax.........: _WinGetClientPos($hwnd)
; Parameters ....: $hwnd - Дескриптор окна (HWND).
; Return values .: Массив: [0] = X, [1] = Y (экранные), [2] = Ширина, [3] = Высота.
;                  При ошибке возвращает 0 и устанавливает @error = 1.
; Updated .......: 2026.04.25
; Version .......: 1.00
; Remarks .......: Использует WinAPI функцию ClientToScreen через user32.dll для точности.
; ===============================================================================================================================
Func _WinGetClientPos($hwnd)
    
    Local $aSize = WinGetClientSize($hwnd)
    If Not IsArray($aSize) Then Return SetError(1, 0, 0)

    ; Описываем структуру напрямую (POINT)
    Local $tPoint = DllStructCreate("long X;long Y")
    
    ; Устанавливаем начальную точку (0,0) — это левый верхний угол клиентской области относительно самого окна
    DllStructSetData($tPoint, "X", 0)
    DllStructSetData($tPoint, "Y", 0)

    ; Вызываем системную функцию для перевода клиентских 0,0 в экранные координаты
    Local $aRet = DllCall("user32.dll", "bool", "ClientToScreen", "hwnd", $hwnd, "ptr", DllStructGetPtr($tPoint))
    
    If @error Or Not $aRet[0] Then
        Return SetError(1, 0, 0)
    EndIf

    ; Формируем массив: [0]=X, [1]=Y, [2]=Ширина, [3]=Высота
    Local $aPos[4]
    $aPos[0] = DllStructGetData($tPoint, "X") ; Экранный X начала клиентской области
    $aPos[1] = DllStructGetData($tPoint, "Y") ; Экранный Y начала клиентской области
    $aPos[2] = $aSize[0]                      ; Ширина области
    $aPos[3] = $aSize[1]                      ; Высота области

    Return $aPos

EndFunc   ;==>_WinGetClientPos
