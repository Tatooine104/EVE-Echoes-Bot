
; #FUNCTION# ====================================================================================================================
; Name...........: _MyImageSearch
; Description....: Ищет изображение в заданной области (массиве) с подстановкой пути к папке ресурсов.
; Syntax.........: _MyImageSearch($sImgName, $sResDir, $aRect, ByRef $x, ByRef $y, $iTolerance)
; Parameters ....: $sImgName   - Имя файла изображения (например, "button.png").
;                  $sResDir    - Путь к папке с изображениями.
;                  $aRect      - Массив координат области поиска [X1, Y1, X2, Y2].
;                  $x          - [ByRef] Переменная для записи найденной координаты X (центр изображения).
;                  $y          - [ByRef] Переменная для записи найденной координаты Y (центр изображения).
;                  $iTolerance - Допуск поиска (0-255).
; Return values .: 1 - Изображение успешно найдено
;                  0 - Изображение не найдено или передан неверный массив координат
; Date ..........: 2026.04.25
; Version .......: 1.02
; Remarks .......: Склеивает $sResDir и $sImgName. Автоматически добавляет "\" к пути, если он отсутствует.
; ===============================================================================================================================
Func _MyImageSearch($sImgName, $sResDir, $aRect, ByRef $x, ByRef $y, $iTolerance)
    ; Проверка входных данных: должен быть массив минимум с 4 элементами
    If Not IsArray($aRect) Or UBound($aRect) < 4 Then Return 0

    ; Гарантируем наличие обратного слэша в конце пути
    If $sResDir <> "" And StringRight($sResDir, 1) <> "\" Then $sResDir &= "\"

    ; Формируем полный путь
    Local $sFullPath = $sResDir & $sImgName
    
    ; Вызываем базовую функцию ImageSearchArea
    Local $iResult = _ImageSearchArea($sFullPath, 1, $aRect[0], $aRect[1], $aRect[2], $aRect[3], $x, $y, $iTolerance)
    
    Return $iResult
EndFunc   ;==>_MyImageSearch






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