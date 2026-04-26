#include-once
#include <WinAPI.au3>
; ------------------------------------------------------------------------------
;
; AutoIt Version: 3.0
; Language:       English
; Description:    Functions that assist with Image Search
;                 Require that the ImageSearchDLL.dll be loadable
;
; ------------------------------------------------------------------------------

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
EndFunc   ;==>_WinGetClientPos
