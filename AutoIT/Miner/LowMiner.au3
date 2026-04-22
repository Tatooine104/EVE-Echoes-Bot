; ----------------------------------------------------------------------------------------------------
;
; AutoIT Version: 5.5.6
; Language      : Russia
; Description   : A bot that fully automates ore mining in Low
;
; Contents:  
; - _CW - функция корректного отображения русского в консоли (для дебага)
; - 
; - 
; - 
; - 
; - 
; - 
; ----------------------------------------------------------------------------------------------------

Opt("MouseCoordMode", 2) ; Опция для позиционирования курсора относительно указанного окна
Opt("PixelCoordMode", 2) ; Опция для работы с координатами относительно указанного окна

Global $Client = 0 ; Глобальная переменная для хранения ссылки на окно

If _CheckAndActivateClient("(Client W.01)") Then
    ; Если функция вернула True, работаем дальше
    Local $aPos = WinGetPos($Client)

    Local $absX1 = $aPos[0] + 1060
    Local $absY1 = $aPos[1] + 230
    Local $absX2 = $aPos[0] + 1280
    Local $absY2 = $aPos[1] + 300

    Local $x, $y
    If _ImageSearchArea("imgUnDock.bmp", 1, $absX1, $absY1, $absX2, $absY2, $x, $y, 100) = 1 Then
        ;MouseClick("left", $x - $aPos[0], $y - $aPos[1], 1, 0)
		_CW("Всё отлично!" & @CRLF)
    Else
        _Log("Кнопка не найдена" & @CRLF)
    EndIf
Else
    _Log("Эмулятор не нейден!")
EndIf

; 
Func _CheckAndActivateClient($title)
    $Client = WinGetHandle($title) ; Записываем в глобальную переменную

    If @error Then Return False ; Окно не существует

    WinActivate($Client)

    ; Ждем активации окна 3 секунды
    If WinWaitActive($Client, "", 1) Then
        Return True ; Все ок
    Else
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

	If Not IsArray($Size) Then
		Return SetError(1, 0, 0)
	EndIf

	Local $tPOINT = DllStructCreate($tagPOINT)

	For $i = 1 To 2
		DllStructSetData($tPOINT, $i, 0)
	Next
	_WinAPI_ClientToScreen($hwnd, $tPOINT)
	If @error Then
		Return SetError(1, 0, 0)
	EndIf

	Local $Pos[4]

	For $i = 0 To 1
		$Pos[$i] = DllStructGetData($tPOINT, $i + 1)
	Next
	For $i = 2 To 3
		$Pos[$i] = $Size[$i - 2]
	Next
	Return $Pos
EndFunc